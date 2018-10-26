using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public ConnectorEvent ChannelEntityAdded(Entity channel, int entityId)
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

            PublishEntities(channel, connectorEvent, structureEntities);

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityAdded, true);
            return connectorEvent;
        }

        public ConnectorEvent ChannelEntityDeleted(Entity channel, Entity deletedEntity)
        {
            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config,
                ConnectorEventType.ChannelEntityDeleted,
                $"Received entity deleted for entity {deletedEntity.Id} in channel {channelName}.", 0);

            Delete(channel, deletedEntity);

            _epiApi.DeleteCompleted(channelName, DeleteCompletedEventType.EntitiyDeleted);

            return connectorEvent;
        }

        public ConnectorEvent ChannelEntityUpdated(Entity channel, int entityId, string data)
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
                resourceIncluded = HandleResourceUpdate(updatedEntity, folderDateTime);
            }
            else
            {
                IntegrationLogger.Write(LogLevel.Debug, $"Updated entity found. Type: {updatedEntity.EntityType.Id}, id: {updatedEntity.Id}");

                if (updatedEntity.EntityType.Id.Equals("Item") && data != null && data.Split(',').Contains("SKUs"))
                {
                    HandleSkuUpdate(entityId, channel, connectorEvent, structureEntities, out resourceIncluded);
                }
                else if (updatedEntity.EntityType.Id.Equals("ChannelNode"))
                {
                    HandleChannelNodeUpdate(channel, structureEntities, connectorEvent);
                    return connectorEvent;
                }

                XDocument doc = _catalogDocumentFactory.CreateUpdateDocument(channel, updatedEntity);

                string catalogDocumentName = _documentFileHelper.SaveCatalogDocument(channel, doc, folderDateTime);

                IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

                _epiApi.ImportCatalog(catalogDocumentName);
                _epiApi.NotifyEpiserverPostImport(catalogDocumentName);
            }

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityUpdated, resourceIncluded);
            return connectorEvent;
        }

        public ConnectorEvent ChannelLinkAdded(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
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
                    int entityId = int.Parse(parentIds[i]);
                    int parentId = int.Parse(parentIds[i + 1]);

                    structureEntities.AddRange(RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channel.Id, entityId, parentId));
                }
            }

            foreach (StructureEntity existingEntity in existingEntitiesInChannel)
            {
                string targetEntityPath = _entityService.GetTargetEntityPath(existingEntity.EntityId, existingEntitiesInChannel, existingEntity.ParentId);
                structureEntities.AddRange(RemoteManager.ChannelService.GetAllChannelStructureEntitiesFromPath(targetEntityPath));
            }

            //Adding existing Entities. If it occurs more than one time in channel. We can not remove duplicates.
            structureEntities.AddRange(existingEntitiesInChannel);

            // Remove duplicates
            structureEntities = structureEntities.GroupBy(x => x.EntityId).Select(x => x.First()).ToList();

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done fetching channel entities", 10);

            PublishEntities(channel, connectorEvent, structureEntities);

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkAdded, true);

            return connectorEvent;
        }

        public ConnectorEvent ChannelLinkDeleted(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkDeleted,
                $"Received link deleted for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName.Data}", 0);

            Entity removalTarget = _entityService.GetEntity(targetEntityId, LoadLevel.DataAndLinks);
            Entity removalSource = _entityService.GetEntity(sourceEntityId, LoadLevel.DataAndLinks);

            if (removalTarget.EntityType.Id == "Resource")
            {
                DeleteResourceLink(removalTarget, removalSource);
            }
            else
            {
                DeleteLink(removalSource, removalTarget, linkTypeId);
            }

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.DeleteCompleted(channelName, DeleteCompletedEventType.LinkDeleted);

            return connectorEvent;
        }

        public ConnectorEvent ChannelLinkUpdated(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
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
                DeleteLink(removalSource, removalTarget, linkTypeId, true);
                _epiApi.DeleteCompleted(channelName, DeleteCompletedEventType.LinkDeleted);
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

            PublishEntities(channel, connectorEvent, structureEntities);


            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkUpdated, true);

            return connectorEvent;
        }

        public ConnectorEvent Publish(Entity channel)
        {
            ConnectorEvent publishEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Publish, $"Publish started for channel: {channel.DisplayName.Data}", 0);
            ConnectorEventHelper.UpdateEvent(publishEvent, "Fetching all channel entities...", 1);

            List<StructureEntity> channelStructureEntities = _entityService.GetAllStructureEntitiesInChannel(_config.ExportEnabledEntityTypes);

            ConnectorEventHelper.UpdateEvent(publishEvent, "Fetched all channel entities. Generating catalog.xml...", 10);

            CatalogElementContainer epiElements = _catalogDocumentFactory.GetEPiElements(channelStructureEntities);
            XElement metaClasses = _catalogElementFactory.GetMetaClassesFromFieldSets();
            XElement associationTypes = _catalogDocumentFactory.GetAssociationTypes();

            XDocument catalogDocument = _catalogDocumentFactory.CreateImportDocument(channel, metaClasses, associationTypes, epiElements);

            LogCatalogProperties(epiElements);

            List<StructureEntity> resourceEntities = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeFromPath(channel.Id.ToString(), "Resource");

            PubilshToEpiserver(publishEvent, catalogDocument, resourceEntities, channel);

            return publishEvent;
        }

        internal void PublishEntities(Entity channel, ConnectorEvent connectorEvent, List<StructureEntity> structureEntities)
        {
            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating catalog.xml...", 11);
            CatalogElementContainer epiElements = _catalogDocumentFactory.GetEPiElements(structureEntities);

            XDocument catalogDocument = _catalogDocumentFactory.CreateImportDocument(channel, null, null, epiElements);

            LogCatalogProperties(epiElements);

            PubilshToEpiserver(connectorEvent, catalogDocument, structureEntities, channel);
        }

        private static void LogCatalogProperties(CatalogElementContainer epiElements)
        {
            IntegrationLogger.Write(LogLevel.Information, $"Catalog saved with the following: " +
                                                          $"Nodes: {epiElements.Nodes.Count}. " +
                                                          $"Entries: {epiElements.Entries.Count}. " +
                                                          $"Relations: {epiElements.Relations.Count}. " +
                                                          $"Associations: {epiElements.Associations.Count}. ");
        }

        private void Delete(Entity channelEntity, Entity deletedEntity)
        {
            switch (deletedEntity.EntityType.Id)
            {
                case "Resource":
                    Guid resourceGuid = EpiserverEntryIdentifier.EntityIdToGuid(deletedEntity.Id);
                    _epiApi.DeleteResource(resourceGuid);

                    break;
                case "Channel":
                    _epiApi.DeleteCatalog(deletedEntity.Id);
                    break;
                case "ChannelNode":
                    _epiApi.DeleteCatalogNode(deletedEntity, channelEntity.Id);
                    break;
                case "Item":
                    if ((_config.ItemsToSkus && _config.UseThreeLevelsInCommerce) || !_config.ItemsToSkus)
                    {
                        _epiApi.DeleteCatalogEntry(deletedEntity);
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

                        _epiApi.DeleteSkus(entitiesToDelete);
                    }

                    break;
                default:
                    _epiApi.DeleteCatalogEntry(deletedEntity);
                    break;
            }
        }

        private void DeleteLink(Entity removalSource, Entity removalTarget, string linkTypeId, bool overrideIsRelation = false)
        {
            bool isRelation = _mappingHelper.IsRelation(linkTypeId) || overrideIsRelation;

            LinkType linktype = _config.LinkTypes.Find(lt => lt.Id == linkTypeId);
            if (linktype.SourceEntityTypeId.Equals("ChannelNode") && linktype.TargetEntityTypeId.Equals("Product"))
            {
                isRelation = true;
            }

            string sourceCode = _catalogCodeGenerator.GetEpiserverCode(removalSource);
            string targetCode = _catalogCodeGenerator.GetEpiserverCode(removalTarget);

            _epiApi.DeleteLink(sourceCode, targetCode, isRelation);
        }

        private void DeleteResourceLink(Entity removedResource, Entity removalTarget)
        {
            Guid resourceGuid = EpiserverEntryIdentifier.EntityIdToGuid(removedResource.Id);
            string targetCode = _catalogCodeGenerator.GetEpiserverCode(removalTarget);

            _epiApi.DeleteLink(resourceGuid, targetCode);
        }

        private void HandleChannelNodeUpdate(Entity channel, List<StructureEntity> structureEntities, ConnectorEvent entityUpdatedConnectorEvent)
        {
            PublishEntities(channel, entityUpdatedConnectorEvent, structureEntities);
            _epiApi.ImportUpdateCompleted(_pimFieldAdapter.GetDisplayName(channel, 100), ImportUpdateCompletedEventType.EntityUpdated, true);
        }

        private bool HandleResourceUpdate(Entity updatedEntity, string folderDateTime)
        {
            var resourceIncluded = false;
            XDocument resourceDocument = _resourceElementFactory.HandleResourceUpdate(updatedEntity, folderDateTime);
            string resourcesBasePath = Path.Combine(_config.ResourcesRootPath, folderDateTime);
            _documentFileHelper.SaveDocument(resourceDocument, resourcesBasePath);

            IntegrationLogger.Write(LogLevel.Debug, "Resources saved, Starting automatic resource import!");

            string baseFilePath = Path.Combine(_config.ResourcesRootPath, folderDateTime);
            string resourceXmlPath = Path.Combine(baseFilePath, "Resources.xml");

            _epiApi.ImportResources(resourceXmlPath, baseFilePath);

            _epiApi.NotifyEpiserverPostImport(resourceXmlPath);
            resourceIncluded = true;

            return resourceIncluded;
        }

        private void HandleSkuUpdate(int entityId,
            Entity channelEntity,
            ConnectorEvent connectorEvent,
            List<StructureEntity> structureEntities,
            out bool resourceIncluded)
        {
            resourceIncluded = false;
            Field currentField = RemoteManager.DataService.GetField(entityId, "SKUs");

            List<Field> fieldHistory = RemoteManager.DataService.GetFieldHistory(entityId, "SKUs");

            Field previousField = fieldHistory.FirstOrDefault(f => f.Revision == currentField.Revision - 1);

            string oldXml = string.Empty;
            if (previousField != null && previousField.Data != null)
            {
                oldXml = (string) previousField.Data;
            }

            string newXml = string.Empty;
            if (currentField.Data != null)
            {
                newXml = (string) currentField.Data;
            }

            List<XElement> skusToDelete, skusToAdd;
            PimFieldAdapter.CompareAndParseSkuXmls(oldXml, newXml, out skusToAdd, out skusToDelete);

            foreach (XElement skuToDelete in skusToDelete)
            {
                string skuId = skuToDelete.Attribute("id").Value;
                _epiApi.DeleteSku(skuId);
            }

            if (skusToAdd.Count > 0)
            {
                PublishEntities(channelEntity, connectorEvent, structureEntities);
                resourceIncluded = true;
            }
        }

        private void PubilshToEpiserver(ConnectorEvent connectorEvent,
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

            _epiApi.ImportCatalog(savedCatalogDocument);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);

            _epiApi.NotifyEpiserverPostImport(savedCatalogDocument);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Sending Resources to EPiServer...", 76);

            _epiApi.ImportResources(resourceDocumentPath, resourcesBasePath);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Resources to EPiServer...", 99);

            _epiApi.NotifyEpiserverPostImport(resourceDocumentPath);
            string channelName = _mappingHelper.GetNameForEntity(channelEntity, 100);

            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.Publish, true);
        }
    }
}