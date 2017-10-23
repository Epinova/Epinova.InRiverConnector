using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.Interfaces;
using Epinova.InRiverConnector.Interfaces.Enums;
using inRiver.Integration.Logging;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Communication
{
    public class EpiApi
    {
        private readonly Configuration _config;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly HttpClientInvoker _httpClient; 

        public EpiApi(Configuration config, EpiMappingHelper mappingHelper, CatalogCodeGenerator catalogCodeGenerator)
        {
            _config = config;
            _mappingHelper = mappingHelper;
            _catalogCodeGenerator = catalogCodeGenerator;
            _httpClient = new HttpClientInvoker(config);
        }

        internal void DeleteCatalog(int catalogId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    _httpClient.Post(config.Endpoints.DeleteCatalog, catalogId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog with id: {catalogId}", exception);
                }
            }
        }

        internal void DeleteCatalogNode(int catalogNodeId, int catalogId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string catalogNode = _catalogCodeGenerator.GetEpiserverCode(catalogNodeId);
                    _httpClient.Post(config.Endpoints.DeleteCatalogNode, catalogNode);
                }
                catch (Exception ex)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalogNode with id: {catalogNodeId} for channel: {catalogId}", ex);
                }
            }
        }

        internal void DeleteSku(string skuId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    _httpClient.Post(config.Endpoints.DeleteCatalogEntry, skuId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog entry based on SKU ID: {skuId}", exception);
                }
            }
        }

        internal void DeleteCatalogEntry(Entity entity, Configuration config)
        {
            string catalogEntryId = _catalogCodeGenerator.GetEpiserverCode(entity);

            lock (EpiLockObject.Instance)
            {
                try
                {
                    _httpClient.Post(config.Endpoints.DeleteCatalogEntry, catalogEntryId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog entry with catalog entry ID: {catalogEntryId}", exception);
                }
            }
        }

        internal void UpdateLinkEntityData(Entity linkEntity, Entity channel, Configuration config, int parentId)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string channelName = BusinessHelper.GetDisplayNameFromEntity(channel, config, -1);

                    string parentEntryId = _catalogCodeGenerator.GetEpiserverCode(parentId);
                    string linkEntityIdString = _catalogCodeGenerator.GetEpiserverCode(linkEntity);

                    string dispName = linkEntity.EntityType.Id + '_' + BusinessHelper.GetDisplayNameFromEntity(linkEntity, config, -1).Replace(' ', '_');

                    LinkEntityUpdateData dataToSend = new LinkEntityUpdateData
                                                          {
                                                              ChannelName = channelName,
                                                              LinkEntityIdString = linkEntityIdString,
                                                              LinkEntryDisplayName = dispName,
                                                              ParentEntryId = parentEntryId
                                                          };

                    _httpClient.Post(config.Endpoints.UpdateLinkEntityData, dataToSend);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to update data for link entity with id:{linkEntity.Id}", exception);
                }
            }
        }

        internal List<string> GetLinkEntityAssociationsForEntity(string linkType, 
                                                                 Entity channelEntity, 
                                                                 List<string> parentCodes, 
                                                                 List<string> targetCodes)
        {
            lock (EpiLockObject.Instance)
            {
                List<string> ids = new List<string>();
                try
                {
                    string channelName = BusinessHelper.GetDisplayNameFromEntity(channelEntity, _config, -1);

                    GetLinkEntityAssociationsForEntityData dataToSend = new GetLinkEntityAssociationsForEntityData
                                                                            {
                                                                                ChannelName = channelName,
                                                                                LinkTypeId = linkType,
                                                                                ParentIds = parentCodes,
                                                                                TargetIds = targetCodes
                                                                            };

                    ids = _httpClient.PostWithStringListAsReturn(_config.Endpoints.GetLinkEntityAssociationsForEntity, dataToSend);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Warning, "Failed to get link entity associations for entity", exception);
                }

                return ids;
            }
        }

        internal void CheckAndMoveNodeIfNeeded(int entityId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string entryNodeId = _catalogCodeGenerator.GetEpiserverCode(entityId);
                    _httpClient.Post(config.Endpoints.CheckAndMoveNodeIfNeeded, entryNodeId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Warning, "Failed when calling the interface function: CheckAndMoveNodeIfNeeded", exception);
                }
            }
        }

        internal void UpdateEntryRelations(string catalogEntryId, 
                                           int channelId,
                                           Entity channelEntity,
                                           Configuration config, 
                                           string parentId,
                                           Dictionary<string, bool> shouldExistInChannelNodes,
                                           string linkTypeId, 
                                           List<string> linkEntityIdsToRemove)
        {
            lock (EpiLockObject.Instance)
            {
                string parentEntryId = parentId;

                try
                {
                    string channelName = BusinessHelper.GetDisplayNameFromEntity(channelEntity, _config, -1);
                    List<string> removeFromChannelNodes = new List<string>();
                    foreach (KeyValuePair<string, bool> shouldExistInChannelNode in shouldExistInChannelNodes)
                    {
                        if (!shouldExistInChannelNode.Value)
                        {
                            removeFromChannelNodes.Add(shouldExistInChannelNode.Key);
                        }
                    }

                    string channelIdEpified = _catalogCodeGenerator.GetEpiserverCode(channelId);
                    bool relation = _mappingHelper.IsRelation(linkTypeId);
                    bool parentExistsInChannelNodes = shouldExistInChannelNodes.Keys.Contains(parentId);

                    var updateEntryRelationData = new UpdateRelationData
                                                {
                                                    ParentEntryId = parentEntryId,
                                                    CatalogEntryIdString = catalogEntryId,
                                                    ChannelIdEpified = channelIdEpified,
                                                    ChannelName = channelName,
                                                    RemoveFromChannelNodes = removeFromChannelNodes,
                                                    LinkEntityIdsToRemove = linkEntityIdsToRemove,
                                                    LinkTypeId = linkTypeId,
                                                    IsRelation = relation,
                                                    ParentExistsInChannelNodes = parentExistsInChannelNodes
                                                };

                    _httpClient.Post(config.Endpoints.UpdateEntryRelations, updateEntryRelationData);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to update entry relations between parent entry id {parentEntryId} and child entry id {catalogEntryId}.", exception);
                    throw;
                }
            }
        }

        internal void Import(string filePath, Guid guid, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string result = _httpClient.Post(config.Endpoints.ImportCatalogXml, filePath);

                    IntegrationLogger.Write(LogLevel.Debug, $"Import catalog returned: {result}");
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to import catalog xml file {filePath} into Episerver.", exception);
                    throw;
                }
            }
        }

        internal void ImportResources(string manifest, string baseFilePpath, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var importer = new ResourceImporter(config);
                    importer.ImportResources(manifest, baseFilePpath);

                    IntegrationLogger.Write(LogLevel.Information, $"Resource file {manifest} imported to EPi Server Commerce.");
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to import resource file {baseFilePpath} to Episerver. Manifest: {manifest}.", exception);
                    throw;
                }
            }
        }

        internal void ImportUpdateCompleted(string catalogName, ImportUpdateCompletedEventType eventType, bool resourceIncluded, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var data = new ImportUpdateCompletedData
                                {
                                    CatalogName = catalogName,
                                    EventType = eventType,
                                    ResourcesIncluded = resourceIncluded
                                };

                    string result = _httpClient.Post(config.Endpoints.ImportUpdateCompleted, data);
                    IntegrationLogger.Write(LogLevel.Debug, $"ImportUpdateCompleted returned: {result}");
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to fire import update completed for catalog {catalogName}.", exception);
                    throw;
                }
            }
        }

        internal bool DeleteCompleted(string catalogName, DeleteCompletedEventType eventType, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var data = new DeleteCompletedData
                                {
                                   CatalogName = catalogName,
                                   EventType = eventType
                               };

                    string result = _httpClient.Post(config.Endpoints.DeleteCompleted, data);
                    IntegrationLogger.Write(LogLevel.Debug, $"DeleteCompleted returned: {result}");
                    return true;
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error,
                        $"Failed to fire DeleteCompleted for catalog {catalogName}.", exception);
                    return false;
                }
            }
        }

        internal void SendHttpPost(Configuration config, string filepath)
        {
            if (string.IsNullOrEmpty(config.HttpPostUrl))
            {
                return;
            }

            try
            {
                string uri = config.HttpPostUrl;
                using (WebClient client = new WebClient())
                {
                    client.UploadFileAsync(new Uri(uri), "POST", @filepath);
                }
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in SendHttpPost", ex);
                throw;
            }
        }
    }
}
