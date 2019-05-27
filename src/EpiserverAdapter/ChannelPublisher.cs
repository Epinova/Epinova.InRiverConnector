using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.EpiserverAdapter.XmlFactories;
using Epinova.InRiverConnector.Interfaces;
using Epinova.InRiverConnector.Interfaces.Enums;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Connect;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class ChannelPublisher
    {
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly CatalogDocumentFactory _catalogDocumentFactory;
        private readonly CatalogElementFactory _catalogElementFactory;
        private readonly IConfiguration _config;
        private readonly DocumentFileHelper _documentFileHelper;
        private readonly IEntityService _entityService;
        private readonly EpiApi _epiApi;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly PimFieldAdapter _pimFieldAdapter;
        private readonly ResourceElementFactory _resourceElementFactory;

        public ChannelPublisher(IConfiguration config,
            CatalogDocumentFactory catalogDocumentFactory,
            CatalogElementFactory catalogElementFactory,
            ResourceElementFactory resourceElementFactory,
            EpiApi epiApi,
            EpiMappingHelper mappingHelper,
            DocumentFileHelper documentFileHelper,
            PimFieldAdapter pimFieldAdapter,
            IEntityService entityService,
            CatalogCodeGenerator catalogCodeGenerator)
        {
            _config = config;
            _catalogDocumentFactory = catalogDocumentFactory;
            _catalogElementFactory = catalogElementFactory;
            _resourceElementFactory = resourceElementFactory;
            _epiApi = epiApi;
            _mappingHelper = mappingHelper;
            _documentFileHelper = documentFileHelper;
            _pimFieldAdapter = pimFieldAdapter;
            _entityService = entityService;
            _catalogCodeGenerator = catalogCodeGenerator;
        }

        public async Task<ConnectorEvent> ChannelEntityAddedAsync(Entity channel, int entityId)
        {
            ConnectorEvent connectorEvent =
                ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityAdded, $"Received entity added for entity {entityId} in channel {channel.DisplayName}", 0);

            var structureEntities = new List<StructureEntity>();
            List<StructureEntity> addedStructureEntities = _entityService.GetStructureEntitiesForEntityInChannel(_config.ChannelId, entityId);

            foreach (StructureEntity addedEntity in addedStructureEntities)
            {
                StructureEntity parentEntity = _entityService.GetParentStructureEntity(_config.ChannelId, addedEntity.ParentId, addedEntity.EntityId, addedStructureEntities);
                structureEntities.Add(parentEntity);
            }

            structureEntities.AddRange(addedStructureEntities);

            string targetEntityPath = _entityService.GetTargetEntityPath(entityId, addedStructureEntities);
            List<StructureEntity> childLinks = _entityService.GetChildrenEntitiesInChannel(entityId, targetEntityPath);

            foreach (StructureEntity linkStructureEntity in childLinks)
            {
                List<StructureEntity> childLinkedEntities = _entityService.GetChildrenEntitiesInChannel(linkStructureEntity.EntityId, linkStructureEntity.Path);
                structureEntities.AddRange(childLinkedEntities);
            }

            structureEntities.AddRange(childLinks);

            await PublishEntitiesAsync(channel, connectorEvent, structureEntities);

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            await _epiApi.ImportUpdateCompletedAsync(channelName, ImportUpdateCompletedEventType.EntityAdded, true);
            return connectorEvent;
        }

        public async Task<ConnectorEvent> ChannelEntityDeletedAsync(Entity channel, Entity deletedEntity)
        {
            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config,
                ConnectorEventType.ChannelEntityDeleted,
                $"Received entity deleted for entity {deletedEntity.Id} in channel {channelName}.", 0);

            await DeleteAsync(channel, deletedEntity);

            await _epiApi.DeleteCompletedAsync(channelName, DeleteCompletedEventType.EntitiyDeleted);

            return connectorEvent;
        }

        public async Task<ConnectorEvent> ChannelEntityUpdatedAsync(Entity channel, int entityId, string data)
        {
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityUpdated,
                $"Received entity update for entity {entityId} in channel {channel.DisplayName}", 0);


            Entity updatedEntity = _entityService.GetEntity(entityId, LoadLevel.DataAndLinks);

            if (updatedEntity.EntityType.IsLinkEntityType)
                return connectorEvent;

            string folderDateTime = DateTime.Now.ToString(Constants.PublicationFolderNameTimeComponent);
            var resourceIncluded = false;
            List<StructureEntity> structureEntities = _entityService.GetStructureEntitiesForEntityInChannel(_config.ChannelId, entityId);

            if (updatedEntity.EntityType.Id.Equals("Resource"))
            {
                resourceIncluded = await HandleResourceUpdateAsync(updatedEntity, folderDateTime);
            }
            else
            {
                IntegrationLogger.Write(LogLevel.Debug, $"Updated entity found. Type: {updatedEntity.EntityType.Id}, id: {updatedEntity.Id}");

                if (updatedEntity.EntityType.Id.Equals("Item") && data != null && data.Split(',').Contains("SKUs"))
                {
                    resourceIncluded = await HandleSkuUpdateAsync(entityId, channel, connectorEvent, structureEntities);
                }
                else if (updatedEntity.EntityType.Id.Equals("ChannelNode"))
                {
                    await HandleChannelNodeUpdateAsync(channel, structureEntities, connectorEvent);
                    return connectorEvent;
                }

                XDocument doc = _catalogDocumentFactory.CreateUpdateDocument(channel, updatedEntity);

                string catalogDocumentName = _documentFileHelper.SaveCatalogDocument(channel, doc, folderDateTime);

                IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

                await _epiApi.ImportCatalogAsync(catalogDocumentName);
                await _epiApi.NotifyEpiserverPostImportAsync(catalogDocumentName);
            }

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            await _epiApi.ImportUpdateCompletedAsync(channelName, ImportUpdateCompletedEventType.EntityUpdated, resourceIncluded);
            return connectorEvent;
        }

        public async Task<ConnectorEvent> ChannelLinkAddedAsync(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkAdded,
                $"Received link added for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}", 0);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Fetching channel entities...", 1);

            List<StructureEntity> existingEntitiesInChannel = _entityService.GetStructureEntitiesForEntityInChannel(_config.ChannelId, targetEntityId);

            var structureEntities = new List<StructureEntity>();

            foreach (StructureEntity existingEntity in existingEntitiesInChannel)
            {
                List<string> parentIds = existingEntity.Path.Split('/').ToList();
                parentIds.Reverse();
                parentIds.RemoveAt(0);

                for (var i = 0; i < parentIds.Count - 1; i++)
                {
                    int entityId = Int32.Parse(parentIds[i]);
                    int parentId = Int32.Parse(parentIds[i + 1]);

                    structureEntities.AddRange(RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channel.Id, entityId, parentId));
                }
            }

            foreach (StructureEntity existingEntity in existingEntitiesInChannel)
            {
                string targetEntityPath = _entityService.GetTargetEntityPath(existingEntity.EntityId, existingEntitiesInChannel, existingEntity.ParentId);
                structureEntities.AddRange(RemoteManager.ChannelService.GetAllChannelStructureEntitiesFromPath(targetEntityPath));
            }

            // Remove duplicates
            structureEntities = structureEntities.GroupBy(x => x.EntityId).Select(x => x.First()).ToList();

            //Adding existing Entities. If it occurs more than one time in channel. We can not remove duplicates.
            structureEntities.AddRange(existingEntitiesInChannel);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done fetching channel entities", 10);

            await PublishEntitiesAsync(channel, connectorEvent, structureEntities);

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            await _epiApi.ImportUpdateCompletedAsync(channelName, ImportUpdateCompletedEventType.LinkAdded, true);

            return connectorEvent;
        }

        public async Task<ConnectorEvent> ChannelLinkDeletedAsync(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkDeleted,
                $"Received link deleted for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName.Data}", 0);

            Entity removalTarget = _entityService.GetEntity(targetEntityId, LoadLevel.DataAndLinks);
            Entity removalSource = _entityService.GetEntity(sourceEntityId, LoadLevel.DataAndLinks);

            if (removalTarget.EntityType.Id == "Resource")
            {
                await DeleteResourceLinkAsync(removalTarget, removalSource);
            }
            else
            {
                await DeleteLinkAsync(removalSource, removalTarget, linkTypeId);
            }

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            await _epiApi.DeleteCompletedAsync(channelName, DeleteCompletedEventType.LinkDeleted);

            return connectorEvent;
        }

        public async Task<ConnectorEvent> ChannelLinkUpdatedAsync(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            List<StructureEntity> targetEntityStructure = _entityService.GetEntityInChannelWithParent(_config.ChannelId, targetEntityId, sourceEntityId);

            StructureEntity parentStructureEntity = _entityService.GetParentStructureEntity(_config.ChannelId, sourceEntityId, targetEntityId, targetEntityStructure);

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            if (parentStructureEntity == null)
            {
                ConnectorEvent deleteEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkDeleted,
                    $"Received link deleted for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName.Data}", 0);
                Entity removalTarget = _entityService.GetEntity(targetEntityId, LoadLevel.DataAndLinks);
                Entity removalSource = _entityService.GetEntity(sourceEntityId, LoadLevel.DataAndLinks);
                await DeleteLinkAsync(removalSource, removalTarget, linkTypeId, true);
                await _epiApi.DeleteCompletedAsync(channelName, DeleteCompletedEventType.LinkDeleted);
                return deleteEvent;
            }

            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkAdded,
                $"Received link update for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}.", 0);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Fetching channel entities...", 1);

            var structureEntities = new List<StructureEntity>
            {
                parentStructureEntity
            };

            List<StructureEntity> entities = _entityService.GetChildrenEntitiesInChannel(parentStructureEntity.EntityId, parentStructureEntity.Path);
            structureEntities.AddRange(entities);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done fetching channel entities", 10);

            await PublishEntitiesAsync(channel, connectorEvent, structureEntities);

            await _epiApi.ImportUpdateCompletedAsync(channelName, ImportUpdateCompletedEventType.LinkUpdated, true);

            return connectorEvent;
        }

        public async Task<ConnectorEvent> PublishAsync(Entity channel)
        {
            ConnectorEvent publishEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Publish, $"Publish started for channel: {channel.DisplayName.Data}", 0);
            ConnectorEventHelper.UpdateEvent(publishEvent, "Fetching all channel entities...", 1);

            List<StructureEntity> channelStructureEntities = _entityService.GetAllStructureEntitiesInChannel(_config.ExportEnabledEntityTypes);

            ConnectorEventHelper.UpdateEvent(publishEvent, "Fetched all channel entities. Generating catalog.xml...", 10);

            CatalogElementContainer epiElements = await _catalogDocumentFactory.GetEPiElementsAsync(channelStructureEntities);
            XElement metaClasses = _catalogElementFactory.GetMetaClassesFromFieldSets();
            XElement associationTypes = _catalogDocumentFactory.GetAssociationTypes();

            XDocument catalogDocument = _catalogDocumentFactory.CreateImportDocument(channel, metaClasses, associationTypes, epiElements);

            LogCatalogProperties(epiElements);

            List<StructureEntity> resourceEntities = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeFromPath(channel.Id.ToString(), "Resource");

            await PublishToEpiserverAsync(publishEvent, catalogDocument, resourceEntities, channel);

            return publishEvent;
        }

        internal async Task PublishEntitiesAsync(Entity channel, ConnectorEvent connectorEvent, List<StructureEntity> structureEntities)
        {
            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating catalog.xml...", 11);
            CatalogElementContainer epiElements = await _catalogDocumentFactory.GetEPiElementsAsync(structureEntities);

            XDocument catalogDocument = _catalogDocumentFactory.CreateImportDocument(channel, null, null, epiElements);

            LogCatalogProperties(epiElements);

            await PublishToEpiserverAsync(connectorEvent, catalogDocument, structureEntities, channel);
        }

        private static void LogCatalogProperties(CatalogElementContainer epiElements)
        {
            IntegrationLogger.Write(LogLevel.Information, "Catalog saved with the following: " +
                                                          $"Nodes: {epiElements.Nodes.Count}. " +
                                                          $"Entries: {epiElements.Entries.Count}. " +
                                                          $"Relations: {epiElements.Relations.Count}. " +
                                                          $"Associations: {epiElements.Associations.Count}. ");
        }

        private async Task DeleteAsync(Entity channelEntity, Entity deletedEntity)
        {
            switch (deletedEntity.EntityType.Id)
            {
                case "Resource":
                    Guid resourceGuid = EpiserverEntryIdentifier.EntityIdToGuid(deletedEntity.Id);
                    await _epiApi.DeleteResourceAsync(resourceGuid);

                    break;
                case "Channel":
                    await _epiApi.DeleteCatalogAsync(deletedEntity.Id);
                    break;
                case "ChannelNode":
                    await _epiApi.DeleteCatalogNodeAsync(deletedEntity, channelEntity.Id);
                    break;
                case "Item":
                    if ((_config.ItemsToSkus && _config.UseThreeLevelsInCommerce) || !_config.ItemsToSkus)
                    {
                        await _epiApi.DeleteCatalogEntryAsync(deletedEntity);
                    }

                    if (_config.ItemsToSkus)
                    {
                        var entitiesToDelete = new List<string>();

                        List<XElement> skuElements = _catalogElementFactory.GenerateSkuItemElemetsFromItem(deletedEntity);

                        foreach (XElement sku in skuElements)
                        {
                            XElement skuCodElement = sku.Element("Code");
                            if (skuCodElement != null)
                            {
                                entitiesToDelete.Add(skuCodElement.Value);
                            }
                        }

                        await _epiApi.DeleteSkusAsync(entitiesToDelete);
                    }

                    break;
                default:
                    await _epiApi.DeleteCatalogEntryAsync(deletedEntity);
                    break;
            }
        }

        private async Task DeleteLinkAsync(Entity removalSource, Entity removalTarget, string linkTypeId, bool overrideIsRelation = false)
        {
            bool isRelation = _mappingHelper.IsRelation(linkTypeId) || overrideIsRelation;

            LinkType linktype = _config.LinkTypes.Find(lt => lt.Id == linkTypeId);
            if (linktype.SourceEntityTypeId.Equals("ChannelNode") && linktype.TargetEntityTypeId.Equals("Product"))
            {
                isRelation = true;
            }

            string sourceCode = _catalogCodeGenerator.GetEpiserverCode(removalSource);
            string targetCode = _catalogCodeGenerator.GetEpiserverCode(removalTarget);

            await _epiApi.DeleteLinkAsync(sourceCode, targetCode, isRelation);
        }

        private async Task DeleteResourceLinkAsync(Entity removedResource, Entity removalTarget)
        {
            Guid resourceGuid = EpiserverEntryIdentifier.EntityIdToGuid(removedResource.Id);
            string targetCode = _catalogCodeGenerator.GetEpiserverCode(removalTarget);

            await _epiApi.DeleteLinkAsync(resourceGuid, targetCode);
        }

        private async Task HandleChannelNodeUpdateAsync(Entity channel, List<StructureEntity> structureEntities, ConnectorEvent entityUpdatedConnectorEvent)
        {
            await PublishEntitiesAsync(channel, entityUpdatedConnectorEvent, structureEntities);
            await _epiApi.ImportUpdateCompletedAsync(_pimFieldAdapter.GetDisplayName(channel, 100), ImportUpdateCompletedEventType.EntityUpdated, true);
        }

        private async Task<bool> HandleResourceUpdateAsync(Entity updatedEntity, string folderDateTime)
        {
            XDocument resourceDocument = _resourceElementFactory.HandleResourceUpdate(updatedEntity, folderDateTime);
            string resourcesBasePath = Path.Combine(_config.ResourcesRootPath, folderDateTime);
            _documentFileHelper.SaveDocument(resourceDocument, resourcesBasePath);

            IntegrationLogger.Write(LogLevel.Debug, "Resources saved, Starting automatic resource import!");

            string baseFilePath = Path.Combine(_config.ResourcesRootPath, folderDateTime);
            string resourceXmlPath = Path.Combine(baseFilePath, "Resources.xml");

            await _epiApi.ImportResourcesAsync(resourceXmlPath, baseFilePath);

            await _epiApi.NotifyEpiserverPostImportAsync(resourceXmlPath);

            return true;
        }

        private async Task<bool> HandleSkuUpdateAsync(int entityId,
            Entity channelEntity,
            ConnectorEvent connectorEvent,
            List<StructureEntity> structureEntities)
        {
            var resourceIncluded = false;
            Field currentField = RemoteManager.DataService.GetField(entityId, "SKUs");

            List<Field> fieldHistory = RemoteManager.DataService.GetFieldHistory(entityId, "SKUs");

            Field previousField = fieldHistory.FirstOrDefault(f => f.Revision == currentField.Revision - 1);

            string oldXml = String.Empty;
            if (previousField?.Data != null)
            {
                oldXml = (string) previousField.Data;
            }

            string newXml = String.Empty;
            if (currentField.Data != null)
            {
                newXml = (string) currentField.Data;
            }

            PimFieldAdapter.CompareAndParseSkuXmls(oldXml, newXml, out List<XElement> skusToAdd, out List<XElement> skusToDelete);

            foreach (XElement skuToDelete in skusToDelete)
            {
                string skuId = skuToDelete.Attribute("id")?.Value;
                await _epiApi.DeleteSkuAsync(skuId);
            }

            if (skusToAdd.Count > 0)
            {
                await PublishEntitiesAsync(channelEntity, connectorEvent, structureEntities);
                resourceIncluded = true;
            }

            return resourceIncluded;
        }

        private async Task PublishToEpiserverAsync(ConnectorEvent connectorEvent,
            XDocument catalogDocument,
            List<StructureEntity> structureEntitiesToGetResourcesFor,
            Entity channelEntity)
        {
            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating catalog.xml. Generating Resource.xml and saving files to disk...", 26);

            string folderNameTimestampComponent = DateTime.Now.ToString(Constants.PublicationFolderNameTimeComponent);

            string resourcesBasePath = Path.Combine(_config.ResourcesRootPath, folderNameTimestampComponent);
            XDocument resourceDocument = _resourceElementFactory.GetResourcesNodeForChannelEntities(structureEntitiesToGetResourcesFor, resourcesBasePath);
            string resourceDocumentPath = _documentFileHelper.SaveDocument(resourceDocument, resourcesBasePath);

            string savedCatalogDocument = _documentFileHelper.SaveCatalogDocument(channelEntity, catalogDocument, folderNameTimestampComponent);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating/saving Resource.xml, sending Catalog.xml to EPiServer...", 50);

            await _epiApi.ImportCatalogAsync(savedCatalogDocument);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);

            await _epiApi.NotifyEpiserverPostImportAsync(savedCatalogDocument);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Sending Resources to EPiServer...", 76);

            await _epiApi.ImportResourcesAsync(resourceDocumentPath, resourcesBasePath);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Resources to EPiServer...", 99);

            await _epiApi.NotifyEpiserverPostImportAsync(resourceDocumentPath);
            string channelName = _mappingHelper.GetNameForEntity(channelEntity, 100);

            await _epiApi.ImportUpdateCompletedAsync(channelName, ImportUpdateCompletedEventType.Publish, true);
        }
    }
}