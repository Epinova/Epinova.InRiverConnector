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
        private readonly Configuration _config;
        private readonly ChannelHelper _channelHelper;
        private readonly EpiDocumentFactory _epiDocumentFactory;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly ResourceElementFactory _resourceElementFactory;
        private readonly EpiApi _epiApi;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly AddUtility _addUtility;
        private readonly DeleteUtility _deleteUtility;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;

        public ChannelPublisher(Configuration config, 
                                ChannelHelper channelHelper, 
                                EpiDocumentFactory epiDocumentFactory, 
                                EpiElementFactory epiElementFactory,
                                ResourceElementFactory resourceElementFactory,
                                EpiApi epiApi,
                                EpiMappingHelper mappingHelper,
                                AddUtility addUtility,
                                DeleteUtility deleteUtility,
                                CatalogCodeGenerator catalogCodeGenerator)
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
            _catalogCodeGenerator = catalogCodeGenerator;
        }

        public void Publish(int channelId)
        {
            var publishEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Publish, $"Publish started for channel: {channelId}", 0);

            var publishStopWatch = Stopwatch.StartNew();
            var resourceIncluded = false;

            try
            {
                var channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateEvent(publishEvent, "Failed to initial publish. Could not find the channel.", -1, true);
                    return;
                }

                ConnectorEventHelper.UpdateEvent(publishEvent, "Fetching all channel entities...", 1);

                List<StructureEntity> channelStructureEntities = _channelHelper.GetAllEntitiesInChannel(_config.ExportEnabledEntityTypes);

                _channelHelper.BuildEntityIdAndTypeDict(channelStructureEntities);

                ConnectorEventHelper.UpdateEvent(publishEvent, "Done fetching all channel entities. Generating catalog.xml...", 10);

                var epiElements = _epiDocumentFactory.GetEPiElements(channelStructureEntities);
                var metaClasses = _epiElementFactory.GetMetaClassesFromFieldSets(_config);
                var associationTypes = _epiDocumentFactory.GetAssociationTypes();

                var doc = _epiDocumentFactory.CreateImportDocument(channelEntity, metaClasses, associationTypes, epiElements);

                var channelIdentifier = _channelHelper.GetChannelIdentifier(channelEntity);

                var folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

                var zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, _config);

                IntegrationLogger.Write(LogLevel.Information, $"Catalog saved with the following: " +
                                                              $"Nodes: {epiElements["Nodes"].Count}" +
                                                              $"Entries: {epiElements["Entries"].Count}" +
                                                              $"Relations: {epiElements["Relations"].Count}" +
                                                              $"Associations: {epiElements["Associations"].Count}");

                ConnectorEventHelper.UpdateEvent(publishEvent, "Done generating catalog.xml", 25);
                ConnectorEventHelper.UpdateEvent(publishEvent, "Generating Resource.xml and saving files to disk...", 26);

                List<StructureEntity> resources = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeFromPath(channelEntity.Id.ToString(), "Resource");

                channelStructureEntities.AddRange(resources);

                var resourceDocument = _resourceElementFactory.GetDocumentAndSaveFilesToDisk(resources, _config, folderDateTime);

                DocumentFileHelper.SaveDocument(channelIdentifier, resourceDocument, _config, folderDateTime);

                string resourceZipFile = $"resource_{folderDateTime}.zip";

                DocumentFileHelper.ZipFile(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"), resourceZipFile);
                ConnectorEventHelper.UpdateEvent(publishEvent, "Done generating/saving Resource.xml", 50);
                publishStopWatch.Stop();

                IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");
                ConnectorEventHelper.UpdateEvent(publishEvent, "Sending Catalog.xml to EPiServer...", 51);
                if (_epiApi.Import(
                    Path.Combine(_config.PublicationsRootPath, folderDateTime, Configuration.ExportFileName),
                    _channelHelper.GetChannelGuid(channelEntity),
                    _config))
                {
                    ConnectorEventHelper.UpdateEvent(publishEvent, "Done sending Catalog.xml to EPiServer", 75);
                    _epiApi.SendHttpPost(_config, Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedfileName));
                }
                else
                {
                    ConnectorEventHelper.UpdateEvent(publishEvent, "Error while sending Catalog.xml to EPiServer", -1, true);
                    return;
                }

                ConnectorEventHelper.UpdateEvent(publishEvent, "Sending Resources to EPiServer...", 76);

                if (_epiApi.ImportResources(
                    Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"),
                    Path.Combine(_config.ResourcesRootPath, folderDateTime), _config))
                {
                    ConnectorEventHelper.UpdateEvent(publishEvent, "Done sending Resources to EPiServer...", 99);
                    _epiApi.SendHttpPost(_config, Path.Combine(_config.ResourcesRootPath, folderDateTime, resourceZipFile));
                    resourceIncluded = true;
                }
                else
                {
                    ConnectorEventHelper.UpdateEvent(publishEvent, "Error while sending resources to EPiServer", -1, true);
                }

                if (publishEvent.IsError)
                    return;

                ConnectorEventHelper.UpdateEvent(publishEvent, "Publish done!", 100);
                var channelName = _mappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), 100);

                _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.Publish, resourceIncluded, _config);
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in Publish", exception);
                ConnectorEventHelper.UpdateEvent(publishEvent, exception.Message, -1, true);
            }
            finally
            {
                _config.EntityIdAndType = new Dictionary<int, string>();
                _config.ChannelEntities = new Dictionary<int, Entity>();
            }
        }

        public void ChannelEntityAdded(int channelId, int entityId)
        {
            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received entity added for entity {0} in channel {1}", entityId, channelId));
            ConnectorEvent entityAddedConnectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityAdded, string.Format("Received entity added for entity {0} in channel {1}", entityId, channelId), 0);

            bool resourceIncluded = false;
            Stopwatch entityAddedStopWatch = new Stopwatch();

            entityAddedStopWatch.Start();

            try
            {
                Entity channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateEvent(entityAddedConnectorEvent, "Failed to initial ChannelLinkAdded. Could not find the channel.", -1, true);
                    return;
                }

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

                _channelHelper.BuildEntityIdAndTypeDict(structureEntities);
                _addUtility.Add(channelEntity, entityAddedConnectorEvent, structureEntities, out resourceIncluded);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelEntityAdded", ex);
                ConnectorEventHelper.UpdateEvent(entityAddedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                _config.EntityIdAndType = new Dictionary<int, string>();
            }

            entityAddedStopWatch.Stop();

            IntegrationLogger.Write(LogLevel.Information, $"Add done for channel {channelId}, took {entityAddedStopWatch.GetElapsedTimeFormated()}!");
            ConnectorEventHelper.UpdateEvent(entityAddedConnectorEvent, "ChannelEntityAdded complete", 100);

            if (!entityAddedConnectorEvent.IsError)
            {
                string channelName = _mappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), 100);
                _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityAdded, resourceIncluded, _config);
            }
        }

        public void ChannelEntityUpdated(int channelId, int entityId, string data)
        {
            _config.ChannelEntities = new Dictionary<int, Entity>();

            IntegrationLogger.Write(LogLevel.Debug, $"Received entity update for entity {entityId} in channel {channelId}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityUpdated,
                $"Received entity update for entity {entityId} in channel {channelId}", 0);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (channelId == entityId)
                {
                    ConnectorEventHelper.UpdateEvent(connectorEvent, "ChannelEntityUpdated, updated Entity is the Channel, no action required", 100);
                    return;
                }

                Entity channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateEvent(connectorEvent, $"Failed to initial ChannelEntityUpdated. Could not find the channel with id: {channelId}", -1, true);
                    return;
                }

                string channelIdentifier = _channelHelper.GetChannelIdentifier(channelEntity);
                Entity updatedEntity = RemoteManager.DataService.GetEntity(entityId, LoadLevel.DataAndLinks);

                if (updatedEntity == null)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"ChannelEntityUpdated, could not find entity with id: {entityId}");
                    ConnectorEventHelper.UpdateEvent(connectorEvent, $"ChannelEntityUpdated, could not find entity with id: {entityId}", -1, true);

                    return;
                }

                string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

                bool resourceIncluded = false;
                string channelName = _mappingHelper.GetNameForEntity(channelEntity, 100);

                var structureEntities = _channelHelper.GetStructureEntitiesForEntityInChannel(_config.ChannelId, entityId);

                _channelHelper.BuildEntityIdAndTypeDict(structureEntities);

                if (updatedEntity.EntityType.Id.Equals("Resource"))
                {
                    resourceIncluded = HandleResourceUpdate(updatedEntity, folderDateTime, channelIdentifier);
                }
                else
                {
                    IntegrationLogger.Write(LogLevel.Debug, $"Updated entity found. Type: {updatedEntity.EntityType.Id}, id: {updatedEntity.Id}");

                    if (updatedEntity.EntityType.Id.Equals("Item") && data != null && data.Split(',').Contains("SKUs"))
                    {
                        HandleItemUpdate(entityId, channelEntity, connectorEvent, structureEntities, out resourceIncluded);
                    }
                    else if (updatedEntity.EntityType.Id.Equals("ChannelNode"))
                    {
                        HandleChannelNodeUpdate(channelId, channelEntity, structureEntities, connectorEvent, stopwatch, channelName);
                        return;
                    }

                    if (updatedEntity.EntityType.IsLinkEntityType)
                    {
                        if (!_config.ChannelEntities.ContainsKey(updatedEntity.Id))
                        {
                            _config.ChannelEntities.Add(updatedEntity.Id, updatedEntity);
                        }
                    }

                    XDocument doc = _epiDocumentFactory.CreateUpdateDocument(channelEntity, updatedEntity);

                    // If data exist in EPiCodeFields.
                    // Update Associations and relations for XDocument doc.
                    if (_config.EpiCodeMapping.ContainsKey(updatedEntity.EntityType.Id) &&
                        data.Split(',').Contains(_config.EpiCodeMapping[updatedEntity.EntityType.Id]))
                    {
                        _channelHelper.EpiCodeFieldUpdatedAddAssociationAndRelationsToDocument(doc, updatedEntity, channelId);
                    }

                    if (updatedEntity.EntityType.IsLinkEntityType)
                    {
                        List<Link> links = RemoteManager.DataService.GetLinksForLinkEntity(updatedEntity.Id);
                        if (links.Count > 0)
                        {
                            string parentId = _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(links.First().Source.Id);

                            _epiApi.UpdateLinkEntityData(updatedEntity, channelId, channelEntity, _config, parentId);
                        }
                    }

                    string zippedName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, _config);

                    IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");
                    var channelGuid = _channelHelper.GetChannelGuid(channelEntity);
                    if (_epiApi.Import(Path.Combine(_config.PublicationsRootPath, folderDateTime, "Catalog.xml"), channelGuid, _config))
                    {
                        _epiApi.SendHttpPost(_config, Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedName));
                    }
                }

                _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityUpdated, resourceIncluded, _config);
                stopwatch.Stop();

            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelEntityUpdated", ex);
                ConnectorEventHelper.UpdateEvent(connectorEvent, ex.Message, -1, true);
            }
            finally
            {
                _config.EntityIdAndType = new Dictionary<int, string>();
                _config.ChannelEntities = new Dictionary<int, Entity>();
            }

            IntegrationLogger.Write(LogLevel.Information, $"Update done for channel {channelId}, took {stopwatch.GetElapsedTimeFormated()}!");
            ConnectorEventHelper.UpdateEvent(connectorEvent, "ChannelEntityUpdated complete", 100);
        }

        public void ChannelEntityDeleted(int channelId, Entity deletedEntity)
        {
            Stopwatch deleteStopWatch = new Stopwatch();
            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received entity deleted for entity {0} in channel {1}", deletedEntity.Id, channelId));
            ConnectorEvent entityDeletedConnectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityDeleted, string.Format("Received entity deleted for entity {0} in channel {1}", deletedEntity.Id, channelId), 0);

            try
            {
                IntegrationLogger.Write(LogLevel.Debug, string.Format("Received entity deleted for entity {0} in channel {1}", deletedEntity.Id, channelId));
                deleteStopWatch.Start();

                Entity channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateEvent(entityDeletedConnectorEvent, "Failed to initial ChannelEntityDeleted. Could not find the channel.", -1, true);
                    return;
                }

                _deleteUtility.Delete(channelEntity, -1, deletedEntity, string.Empty);
                deleteStopWatch.Stop();
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelEntityDeleted", ex);
                ConnectorEventHelper.UpdateEvent(entityDeletedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                _config.EntityIdAndType = new Dictionary<int, string>();
            }

            IntegrationLogger.Write(LogLevel.Information, string.Format("Delete done for channel {0}, took {1}!", channelId, deleteStopWatch.GetElapsedTimeFormated()));
            ConnectorEventHelper.UpdateEvent(entityDeletedConnectorEvent, "ChannelEntityDeleted complete", 100);

            if (!entityDeletedConnectorEvent.IsError)
            {
                string channelName = _mappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), 100);
                _epiApi.DeleteCompleted(channelName, DeleteCompletedEventType.EntitiyDeleted, _config);
            }
        }

        public void ChannelLinkAdded(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received link added for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId));
            ConnectorEvent linkAddedConnectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkAdded, string.Format("Received link added for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId), 0);

            bool resourceIncluded;
            Stopwatch linkAddedStopWatch = new Stopwatch();
            try
            {
                linkAddedStopWatch.Start();

                // NEW CODE
                Entity channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateEvent(linkAddedConnectorEvent, "Failed to initial ChannelLinkAdded. Could not find the channel.", -1, true);
                    return;
                }

                ConnectorEventHelper.UpdateEvent(linkAddedConnectorEvent, "Fetching channel entities...", 1);

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

                        structureEntities.AddRange(RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channelId, entityId, parentId));
                    }
                }

                List<StructureEntity> children = new List<StructureEntity>();

                foreach (StructureEntity existingEntity in existingEntitiesInChannel)
                {
                    string targetEntityPath = _channelHelper.GetTargetEntityPath(existingEntity.EntityId, existingEntitiesInChannel, existingEntity.ParentId);
                    structureEntities.AddRange(RemoteManager.ChannelService.GetAllChannelStructureEntitiesFromPath(targetEntityPath));
                }

                // Remove duplicates
                structureEntities = structureEntities.GroupBy(x => x.EntityId).Select(x => x.First()).ToList();

                //Adding existing Entities. If it occurs more than one time in channel. We can not remove duplicates.
                structureEntities.AddRange(existingEntitiesInChannel);

                _channelHelper.BuildEntityIdAndTypeDict(structureEntities);

                ConnectorEventHelper.UpdateEvent(linkAddedConnectorEvent, "Done fetching channel entities", 10);

                _addUtility.Add(channelEntity, linkAddedConnectorEvent, structureEntities, out resourceIncluded);

                linkAddedStopWatch.Stop();
            }
            catch (Exception ex)
            {

                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelLinkAdded", ex);
                ConnectorEventHelper.UpdateEvent(linkAddedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                _config.EntityIdAndType = new Dictionary<int, string>();
            }

            linkAddedStopWatch.Stop();

            IntegrationLogger.Write(LogLevel.Information, $"ChannelLinkAdded done for channel {channelId}, took {linkAddedStopWatch.GetElapsedTimeFormated()}!");
            ConnectorEventHelper.UpdateEvent(linkAddedConnectorEvent, "ChannelLinkAdded complete", 100);

            if (!linkAddedConnectorEvent.IsError)
            {
                string channelName = _mappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), 100);
                _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkAdded, resourceIncluded, _config);
            }
        }

        public void ChannelLinkDeleted(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            _config.ChannelEntities = new Dictionary<int, Entity>();

            IntegrationLogger.Write(LogLevel.Debug, $"Received link deleted for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channelId}");
            var c = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkDeleted,
                $"Received link deleted for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channelId}", 0);

            Stopwatch linkDeletedStopWatch = new Stopwatch();

            try
            {
                linkDeletedStopWatch.Start();

                Entity channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateEvent(c, "Failed to initial ChannelLinkDeleted. Could not find the channel.", -1, true);
                    return;
                }

                Entity targetEntity = RemoteManager.DataService.GetEntity(targetEntityId, LoadLevel.DataAndLinks);

                _deleteUtility.Delete(channelEntity, sourceEntityId, targetEntity, linkTypeId);

                linkDeletedStopWatch.Stop();
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelLinkDeleted", ex);
                ConnectorEventHelper.UpdateEvent(c, ex.Message, -1, true);
                return;
            }
            finally
            {
                _config.EntityIdAndType = new Dictionary<int, string>();
                _config.ChannelEntities = new Dictionary<int, Entity>();
            }

            linkDeletedStopWatch.Stop();

            IntegrationLogger.Write(LogLevel.Information,
                $"ChannelLinkDeleted done for channel {channelId}, took {linkDeletedStopWatch.GetElapsedTimeFormated()}!");
            ConnectorEventHelper.UpdateEvent(c, "ChannelLinkDeleted complete", 100);

            if (!c.IsError)
            {
                string channelName = _mappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), 100);
                _epiApi.DeleteCompleted(channelName, DeleteCompletedEventType.LinkDeleted, _config);
            }
        }

        public void ChannelLinkUpdated(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            

            IntegrationLogger.Write(LogLevel.Debug, $"Received link update for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channelId}");
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelLinkAdded,
                $"Received link update for sourceEntityId {sourceEntityId} and targetEntityId {targetEntityId} in channel {channelId}", 0);

            bool resourceIncluded;

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                Entity channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateEvent(connectorEvent, "Failed to initial ChannelLinkUpdated. Could not find the channel.", -1, true);
                    return;
                }

                ConnectorEventHelper.UpdateEvent(connectorEvent, "Fetching channel entities...", 1);

                var targetEntityStructure = _channelHelper.GetEntityInChannelWithParent(_config.ChannelId, targetEntityId, sourceEntityId);
                var parentStructureEntity = _channelHelper.GetParentStructureEntity(_config.ChannelId, sourceEntityId, targetEntityId, targetEntityStructure);

                if (parentStructureEntity != null)
                {
                    var structureEntities = new List<StructureEntity>
                    {
                        parentStructureEntity
                    };

                    var entities = _channelHelper.GetChildrenEntitiesInChannel(parentStructureEntity.EntityId, parentStructureEntity.Path);
                    structureEntities.AddRange(entities);

                    _channelHelper.BuildEntityIdAndTypeDict(structureEntities);

                    ConnectorEventHelper.UpdateEvent(connectorEvent, "Done fetching channel entities", 10);

                    _addUtility.Add(channelEntity, connectorEvent, structureEntities, out resourceIncluded);
                }
                else
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Not possible to located source entity {sourceEntityId} in channel structure for target entity {targetEntityId}");
                    ConnectorEventHelper.UpdateEvent(connectorEvent, $"Not possible to located source entity {sourceEntityId} in channel structure for target entity {targetEntityId}", -1, true);
                    return;
                }
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelLinkUpdated", ex);
                ConnectorEventHelper.UpdateEvent(connectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                _config.EntityIdAndType = new Dictionary<int, string>();
            }

            stopwatch.Stop();

            IntegrationLogger.Write(LogLevel.Information, $"ChannelLinkUpdated done for channel {channelId}, took {stopwatch.GetElapsedTimeFormated()}!");
            ConnectorEventHelper.UpdateEvent(connectorEvent, "ChannelLinkUpdated complete", 100);

            if (!connectorEvent.IsError)
            {
                string channelName = _mappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), 100);
                _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkUpdated, resourceIncluded, _config);
            }
        }


        private void HandleItemUpdate(int entityId, 
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
            BusinessHelper.CompareAndParseSkuXmls(oldXml, newXml, out skusToAdd, out skusToDelete);

            foreach (XElement skuToDelete in skusToDelete)
            {
                _epiApi.DeleteCatalogEntry(skuToDelete.Attribute("id").Value, _config);
            }

            if (skusToAdd.Count > 0)
            {
                _addUtility.Add(channelEntity, entityUpdatedConnectorEvent, structureEntities, out resourceIncluded);
            }
        }

        private void HandleChannelNodeUpdate(int channelId, Entity channelEntity, List<StructureEntity> structureEntities, ConnectorEvent entityUpdatedConnectorEvent, Stopwatch entityUpdatedStopWatch, string channelName)
        {
            bool resourceIncluded;
            _addUtility.Add(channelEntity, entityUpdatedConnectorEvent, structureEntities, out resourceIncluded);

            entityUpdatedStopWatch.Stop();
            IntegrationLogger.Write(LogLevel.Information, $"Update done for channel {channelId}, took {entityUpdatedStopWatch.GetElapsedTimeFormated()}!");

            ConnectorEventHelper.UpdateEvent(entityUpdatedConnectorEvent, "ChannelEntityUpdated complete", 100);
            _epiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityUpdated, resourceIncluded, _config);
        }

        private bool HandleResourceUpdate(Entity updatedEntity, string folderDateTime, string channelIdentifier)
        {
            var resourceIncluded = false;

            var resDoc = _resourceElementFactory.HandleResourceUpdate(updatedEntity, _config, folderDateTime);

            DocumentFileHelper.SaveDocument(channelIdentifier, resDoc, _config, folderDateTime);

            string resourceZipFile = $"resource_{folderDateTime}.zip";

            DocumentFileHelper.ZipFile(
                Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"),
                resourceZipFile);

            IntegrationLogger.Write(LogLevel.Debug, "Resources saved, Starting automatic resource import!");

            if (_epiApi.ImportResources(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"),
                Path.Combine(_config.ResourcesRootPath, folderDateTime), _config))
            {
                _epiApi.SendHttpPost(_config, Path.Combine(_config.ResourcesRootPath, folderDateTime, resourceZipFile));
                resourceIncluded = true;
            }
            return resourceIncluded;
        }
    }
}