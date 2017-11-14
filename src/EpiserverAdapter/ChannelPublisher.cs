using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.EpiserverAdapter.Utilities;
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
        private readonly IConfiguration _config;
        private readonly ChannelHelper _channelHelper;
        private readonly EpiDocumentFactory _epiDocumentFactory;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly ResourceElementFactory _resourceElementFactory;
        private readonly EpiApi _epiApi;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly AddUtility _addUtility;
        private readonly DeleteUtility _deleteUtility;
        private readonly DocumentFileHelper _documentFileHelper;
        private readonly PimFieldAdapter _pimFieldAdapter;

        public ChannelPublisher(IConfiguration config, 
                                ChannelHelper channelHelper, 
                                EpiDocumentFactory epiDocumentFactory, 
                                EpiElementFactory epiElementFactory,
                                ResourceElementFactory resourceElementFactory,
                                EpiApi epiApi,
                                EpiMappingHelper mappingHelper,
                                AddUtility addUtility,
                                DeleteUtility deleteUtility,
                                DocumentFileHelper documentFileHelper,
                                PimFieldAdapter pimFieldAdapter)
        {
            _config = config;
            _channelHelper = channelHelper;
            _epiDocumentFactory = epiDocumentFactory;
            _epiElementFactory = epiElementFactory;
            _resourceElementFactory = resourceElementFactory;
            _epiApi = epiApi;
            _mappingHelper = mappingHelper;
            _addUtility = addUtility;
            _deleteUtility = deleteUtility;
            _documentFileHelper = documentFileHelper;
            _pimFieldAdapter = pimFieldAdapter;
        }

        public ConnectorEvent Publish(Entity channel)
        {
            var publishEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Publish, $"Publish started for channel: {channel.DisplayName.Data}", 0);
            ConnectorEventHelper.UpdateEvent(publishEvent, "Fetching all channel entities...", 1);

            var channelStructureEntities = _channelHelper.GetAllStructureEntitiesInChannel(_config.ExportEnabledEntityTypes);
            ConnectorEventHelper.UpdateEvent(publishEvent, "Done fetching all channel entities. Generating catalog.xml...", 10);

            var epiElements = _epiDocumentFactory.GetEPiElements(channelStructureEntities);
            var metaClasses = _epiElementFactory.GetMetaClassesFromFieldSets();
            var associationTypes = _epiDocumentFactory.GetAssociationTypes();

            var doc = _epiDocumentFactory.CreateImportDocument(channel, metaClasses, associationTypes, epiElements);
            
            var folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

            var zippedfileName = _documentFileHelper.SaveAndZipDocument(channel, doc, folderDateTime);

            IntegrationLogger.Write(LogLevel.Information, $"Catalog saved with the following: " +
                                                          $"Nodes: {epiElements["Nodes"].Count}. " +
                                                          $"Entries: {epiElements["Entries"].Count}. " +
                                                          $"Relations: {epiElements["Relations"].Count}. " +
                                                          $"Associations: {epiElements["Associations"].Count}. ");

            ConnectorEventHelper.UpdateEvent(publishEvent, "Done generating catalog.xml. Generating Resource.xml and saving files to disk...", 26);

            var resources = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeFromPath(channel.Id.ToString(), "Resource");
            var resourceDocument = _resourceElementFactory.GetResourcesNodeForChannelEntities(resources, folderDateTime);

            var channelIdentifier = _channelHelper.GetChannelIdentifier(channel);
            _documentFileHelper.SaveDocument(channelIdentifier, resourceDocument, folderDateTime);

            string resourceZipFile = $"resource_{folderDateTime}.zip";

            var resourceXml = Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml");
            _documentFileHelper.ZipFile(resourceXml, resourceZipFile);

            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");
            ConnectorEventHelper.UpdateEvent(publishEvent, "Done generating/saving Resource.xml. Sending Catalog.xml to EPiServer...", 51);

            var filePath = Path.Combine(_config.PublicationsRootPath, folderDateTime, Constants.ExportFilename);

            _epiApi.Import(filePath, _channelHelper.GetChannelGuid(channel));

            ConnectorEventHelper.UpdateEvent(publishEvent, "Done sending Catalog.xml to EPiServer", 75);

            _epiApi.PostFilePath(Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedfileName));

            ConnectorEventHelper.UpdateEvent(publishEvent, "Sending Resources to EPiServer...", 76);

            var baseFilePpath = Path.Combine(_config.ResourcesRootPath, folderDateTime);
            _epiApi.ImportResources(resourceXml, baseFilePpath);

            ConnectorEventHelper.UpdateEvent(publishEvent, "Done sending resources to EPiServer...", 99);

            var resourceZipFilePath = Path.Combine(_config.ResourcesRootPath, folderDateTime, resourceZipFile);
            
            _epiApi.PostFilePath(resourceZipFilePath);

            var channelName = _mappingHelper.GetNameForEntity(channel, 100);

            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.Publish, true);

            return publishEvent;
        }

        public ConnectorEvent ChannelEntityAdded(Entity channel, int entityId)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Received entity added for entity {entityId} in channel {channel.Id}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityAdded, $"Received entity added for entity {entityId} in channel {channel.DisplayName}", 0);

            var structureEntities = new List<StructureEntity>();
            var addedStructureEntities = _channelHelper.GetStructureEntitiesForEntityInChannel(_config.ChannelId, entityId);

            foreach (var addedEntity in addedStructureEntities)
            {
                var parentEntity = _channelHelper.GetParentStructureEntity(_config.ChannelId, addedEntity.ParentId, addedEntity.EntityId, addedStructureEntities);
                structureEntities.Add(parentEntity);
            }

            structureEntities.AddRange(addedStructureEntities);

            var targetEntityPath = _channelHelper.GetTargetEntityPath(entityId, addedStructureEntities);
            var childLinkEntities = _channelHelper.GetChildrenEntitiesInChannel(entityId, targetEntityPath);

            foreach (var linkEntity in childLinkEntities)
            {
                var childLinkedEntities = _channelHelper.GetChildrenEntitiesInChannel(linkEntity.EntityId, linkEntity.Path);
                structureEntities.AddRange(childLinkedEntities);
            }

            structureEntities.AddRange(childLinkEntities);

            _addUtility.Add(channel, connectorEvent, structureEntities);
          
            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityAdded, true);
            return connectorEvent;
        }

        public ConnectorEvent ChannelEntityUpdated(Entity channel, int entityId, string data)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Received entity update for entity {entityId} in channel {channel.DisplayName}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityUpdated, $"Received entity update for entity {entityId} in channel {channel.DisplayName}", 0);

            Entity updatedEntity = RemoteManager.DataService.GetEntity(entityId, LoadLevel.DataAndLinks);

            string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

            bool resourceIncluded = false;
            
            var structureEntities = _channelHelper.GetStructureEntitiesForEntityInChannel(_config.ChannelId, entityId);

            if (updatedEntity.EntityType.Id.Equals("Resource"))
            {
                resourceIncluded = HandleResourceUpdate(updatedEntity, folderDateTime, channel);
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

                XDocument doc = _epiDocumentFactory.CreateUpdateDocument(channel, updatedEntity);
                
                if (updatedEntity.EntityType.IsLinkEntityType)
                {
                    List<Link> links = RemoteManager.DataService.GetLinksForLinkEntity(updatedEntity.Id);
                    if (links.Count > 0)
                        _epiApi.UpdateLinkEntityData(updatedEntity, channel, links.First().Source.Id);
                }

                string zippedName = _documentFileHelper.SaveAndZipDocument(channel, doc, folderDateTime);

                IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");
                var channelGuid = _channelHelper.GetChannelGuid(channel);
                _epiApi.Import(Path.Combine(_config.PublicationsRootPath, folderDateTime, "Catalog.xml"), channelGuid);
                _epiApi.PostFilePath(Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedName));
            }

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityUpdated, resourceIncluded);
            return connectorEvent;
        }

        public ConnectorEvent ChannelEntityDeleted(Entity channel, Entity deletedEntity)
        {
            var channelName = _mappingHelper.GetNameForEntity(channel, 100);
            IntegrationLogger.Write(LogLevel.Debug, $"Received entity deleted for entity {deletedEntity.Id} in channel {channelName}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, 
                                            ConnectorEventType.ChannelEntityDeleted, 
                                            $"Received entity deleted for entity {deletedEntity.Id} in channel {channelName}", 0);

            _deleteUtility.Delete(channel, deletedEntity);
            
            _epiApi.DeleteCompleted(channelName, DeleteCompletedEventType.EntitiyDeleted);

            return connectorEvent;
        }

        public ConnectorEvent ChannelLinkAdded(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Received link added for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkAdded,
                    $"Received link added for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}", 0);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Fetching channel entities...", 1);

            var existingEntitiesInChannel = _channelHelper.GetStructureEntitiesForEntityInChannel(_config.ChannelId, targetEntityId);

            List<StructureEntity> structureEntities = new List<StructureEntity>();

            foreach (StructureEntity existingEntity in existingEntitiesInChannel)
            {
                List<string> parentIds = existingEntity.Path.Split('/').ToList();
                parentIds.Reverse();
                parentIds.RemoveAt(0);

                for (int i = 0; i < parentIds.Count - 1; i++)
                {
                    int entityId = int.Parse(parentIds[i]);
                    int parentId = int.Parse(parentIds[i + 1]);

                    structureEntities.AddRange(RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channel.Id, entityId, parentId));
                }
            }

            foreach (StructureEntity existingEntity in existingEntitiesInChannel)
            {
                string targetEntityPath = _channelHelper.GetTargetEntityPath(existingEntity.EntityId, existingEntitiesInChannel, existingEntity.ParentId);
                structureEntities.AddRange(RemoteManager.ChannelService.GetAllChannelStructureEntitiesFromPath(targetEntityPath));
            }

            // Remove duplicates
            structureEntities = structureEntities.GroupBy(x => x.EntityId).Select(x => x.First()).ToList();

            //Adding existing Entities. If it occurs more than one time in channel. We can not remove duplicates.
            structureEntities.AddRange(existingEntitiesInChannel);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done fetching channel entities", 10);

            _addUtility.Add(channel, connectorEvent, structureEntities);

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkAdded, true);

            return connectorEvent;
        }

        public ConnectorEvent ChannelLinkDeleted(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Received link deleted for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkDeleted,
                $"Received link deleted for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}", 0);

            Entity removalTarget = RemoteManager.DataService.GetEntity(targetEntityId, LoadLevel.DataAndLinks);
            Entity removalSource = RemoteManager.DataService.GetEntity(sourceEntityId, LoadLevel.DataAndLinks);

            if (removalTarget.EntityType.Id == "Resource")
            {
                _deleteUtility.DeleteResourceLink(removalTarget, removalSource);
            }
            else
            {
                
                _deleteUtility.DeleteLink(removalSource, removalTarget, linkTypeId);
            }

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.DeleteCompleted(channelName, DeleteCompletedEventType.LinkDeleted);

            return connectorEvent;
        }

        public ConnectorEvent ChannelLinkUpdated(Entity channel, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Received link update for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkAdded,
                $"Received link update for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channel.DisplayName}", 0);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Fetching channel entities...", 1);

            var targetEntityStructure = _channelHelper.GetEntityInChannelWithParent(_config.ChannelId, targetEntityId, sourceEntityId);
            var parentStructureEntity = _channelHelper.GetParentStructureEntity(_config.ChannelId, sourceEntityId, targetEntityId, targetEntityStructure);

            if (parentStructureEntity == null)
                throw new Exception($"Can't find parent structure entity {sourceEntityId} with target entity id {targetEntityId}");

            var structureEntities = new List<StructureEntity>
            {
                parentStructureEntity
            };

            var entities = _channelHelper.GetChildrenEntitiesInChannel(parentStructureEntity.EntityId, parentStructureEntity.Path);
            structureEntities.AddRange(entities);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done fetching channel entities", 10);

            _addUtility.Add(channel, connectorEvent, structureEntities);

            string channelName = _mappingHelper.GetNameForEntity(channel, 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkUpdated, true);

            return connectorEvent;
        }


        private void HandleSkuUpdate(int entityId, 
                                      Entity channelEntity,
                                      ConnectorEvent entityUpdatedConnectorEvent, 
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
                oldXml = (string)previousField.Data;
            }

            string newXml = string.Empty;
            if (currentField.Data != null)
            {
                newXml = (string)currentField.Data;
            }

            List<XElement> skusToDelete, skusToAdd;
            PimFieldAdapter.CompareAndParseSkuXmls(oldXml, newXml, out skusToAdd, out skusToDelete);

            foreach (XElement skuToDelete in skusToDelete)
            {
                var skuId = skuToDelete.Attribute("id").Value;
                _epiApi.DeleteSku(skuId);
            }

            if (skusToAdd.Count > 0)
            {
                _addUtility.Add(channelEntity, entityUpdatedConnectorEvent, structureEntities);
                resourceIncluded = true;
            }
        }

        private void HandleChannelNodeUpdate(Entity channel, List<StructureEntity> structureEntities, ConnectorEvent entityUpdatedConnectorEvent)
        {
            _addUtility.Add(channel, entityUpdatedConnectorEvent, structureEntities);
            _epiApi.ImportUpdateCompleted(_pimFieldAdapter.GetDisplayNameFromEntity(channel, 100), ImportUpdateCompletedEventType.EntityUpdated, true);
        }

        private bool HandleResourceUpdate(Entity updatedEntity, string folderDateTime, Entity channel)
        {
            var resourceIncluded = false;
            string channelIdentifier = _channelHelper.GetChannelIdentifier(channel);

            var resDoc = _resourceElementFactory.HandleResourceUpdate(updatedEntity, folderDateTime);
            _documentFileHelper.SaveDocument(channelIdentifier, resDoc, folderDateTime);

            string resourceZipFile = $"resource_{folderDateTime}.zip";

            _documentFileHelper.ZipFile(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"), resourceZipFile);

            IntegrationLogger.Write(LogLevel.Debug, "Resources saved, Starting automatic resource import!");

            _epiApi.ImportResources(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(_config.ResourcesRootPath, folderDateTime));
            _epiApi.PostFilePath(Path.Combine(_config.ResourcesRootPath, folderDateTime, resourceZipFile));
            resourceIncluded = true;

            return resourceIncluded;
        }
    }
}