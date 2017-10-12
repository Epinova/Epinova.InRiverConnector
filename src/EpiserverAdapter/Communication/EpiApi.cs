using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using inRiver.EPiServerCommerce.CommerceAdapter.Helpers;
using inRiver.EPiServerCommerce.Interfaces;
using inRiver.EPiServerCommerce.Interfaces.Enums;
using inRiver.Integration.Logging;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace inRiver.EPiServerCommerce.CommerceAdapter.Communication
{
    internal class EpiApi
    {
        internal static void DeleteCatalog(int catalogId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    RestEndpoint<int> endpoint = new RestEndpoint<int>(config.Settings, "DeleteCatalog");
                    endpoint.Post(catalogId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Failed to delete catalog with id: {0}", catalogId), exception);
                }
            }
        }

        internal static void DeleteCatalogNode(int catalogNodeId, int catalogId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string catalogNode = ChannelPrefixHelper.GetEpiserverCode(catalogNodeId, config);
                    RestEndpoint<string> endpoint = new RestEndpoint<string>(config.Settings, "DeleteCatalogNode");
                    endpoint.Post(catalogNode);
                }
                catch (Exception ex)
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Failed to delete catalogNode with id: {0} for channel: {1}", catalogNodeId, catalogId), ex);
                }
            }
        }

        internal static void DeleteCatalogEntry(string entityId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string catalogEntryId = ChannelPrefixHelper.GetEpiserverCode(entityId, config);
                    var endpoint = new RestEndpoint<string>(config.Settings, "DeleteCatalogEntry");
                    endpoint.Post(catalogEntryId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog entry based on entity id: {entityId}", exception);
                }
            }
        }

        internal static void UpdateLinkEntityData(Entity linkEntity, int channelId, Entity channelEntity, Configuration config, string parentId)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string channelName = BusinessHelper.GetDisplayNameFromEntity(channelEntity, config, -1);

                    string parentEntryId = ChannelPrefixHelper.GetEpiserverCode(parentId, config);
                    string linkEntityIdString = ChannelPrefixHelper.GetEpiserverCode(linkEntity.Id, config);

                    string dispName = linkEntity.EntityType.Id + '_' + BusinessHelper.GetDisplayNameFromEntity(linkEntity, config, -1).Replace(' ', '_');

                    LinkEntityUpdateData dataToSend = new LinkEntityUpdateData
                                                          {
                                                              ChannelName = channelName,
                                                              LinkEntityIdString = linkEntityIdString,
                                                              LinkEntryDisplayName = dispName,
                                                              ParentEntryId = parentEntryId
                                                          };

                    RestEndpoint<LinkEntityUpdateData> endpoint = new RestEndpoint<LinkEntityUpdateData>(config.Settings, "UpdateLinkEntityData");
                    endpoint.Post(dataToSend);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to update data for link entity with id:{linkEntity.Id}", exception);
                }
            }
        }

        internal static List<string> GetLinkEntityAssociationsForEntity(string linkType, int channelId, Entity channelEntity, Configuration config, List<string> parentIds, List<string> targetIds)
        {
            lock (EpiLockObject.Instance)
            {
                List<string> ids = new List<string>();
                try
                {
                    string channelName = BusinessHelper.GetDisplayNameFromEntity(channelEntity, config, -1);

                    for (int i = 0; i < targetIds.Count; i++)
                    {
                        targetIds[i] = ChannelPrefixHelper.GetEpiserverCode(targetIds[i], config);
                    }

                    for (int i = 0; i < parentIds.Count; i++)
                    {
                        parentIds[i] = ChannelPrefixHelper.GetEpiserverCode(parentIds[i], config);
                    }

                    GetLinkEntityAssociationsForEntityData dataToSend = new GetLinkEntityAssociationsForEntityData
                                                                            {
                                                                                ChannelName = channelName,
                                                                                LinkTypeId = linkType,
                                                                                ParentIds = parentIds,
                                                                                TargetIds = targetIds
                                                                            };

                    var endpoint = new RestEndpoint<GetLinkEntityAssociationsForEntityData>(config.Settings, "GetLinkEntityAssociationsForEntity");
                    ids = endpoint.PostWithStringListAsReturn(dataToSend);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Warning, string.Format("Failed to get link entity associations for entity"), exception);
                }

                return ids;
            }
        }

        internal static void CheckAndMoveNodeIfNeeded(string nodeId, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string entryNodeId = ChannelPrefixHelper.GetEpiserverCode(nodeId, config);

                    RestEndpoint<string> endpoint = new RestEndpoint<string>(config.Settings, "CheckAndMoveNodeIfNeeded");
                    endpoint.Post(entryNodeId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Warning, "Failed when calling the interface function: CheckAndMoveNodeIfNeeded", exception);
                }
            }
        }

        internal static void UpdateEntryRelations(string catalogEntryId, 
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
                try
                {
                    string channelName = BusinessHelper.GetDisplayNameFromEntity(channelEntity, config, -1);
                    List<string> removeFromChannelNodes = new List<string>();
                    foreach (KeyValuePair<string, bool> shouldExistInChannelNode in shouldExistInChannelNodes)
                    {
                        if (!shouldExistInChannelNode.Value)
                        {
                            removeFromChannelNodes.Add(ChannelPrefixHelper.GetEpiserverCode(shouldExistInChannelNode.Key, config));
                        }
                    }

                    string parentEntryId = ChannelPrefixHelper.GetEpiserverCode(parentId, config);
                    string catalogEntryIdString = ChannelPrefixHelper.GetEpiserverCode(catalogEntryId, config);
                    string channelIdEpified = ChannelPrefixHelper.GetEpiserverCode(channelId, config);
                    string inriverAssociationsEpified = ChannelPrefixHelper.GetEpiserverCode("_inRiverAssociations", config);
                    bool relation = EpiMappingHelper.IsRelation(linkTypeId, config);
                    bool parentExistsInChannelNodes = shouldExistInChannelNodes.Keys.Contains(parentId);

                    var updateEntryRelationData = new UpdateRelationData
                                                {
                                                    ParentEntryId = parentEntryId,
                                                    CatalogEntryIdString = catalogEntryIdString,
                                                    ChannelIdEpified = channelIdEpified,
                                                    ChannelName = channelName,
                                                    RemoveFromChannelNodes = removeFromChannelNodes,
                                                    LinkEntityIdsToRemove = linkEntityIdsToRemove,
                                                    InRiverAssociationsEpified = inriverAssociationsEpified,
                                                    LinkTypeId = linkTypeId,
                                                    IsRelation = relation,
                                                    ParentExistsInChannelNodes = parentExistsInChannelNodes
                                                };

                    var endpoint = new RestEndpoint<UpdateRelationData>(config.Settings, "UpdateEntryRelations");
                    endpoint.Post(updateEntryRelationData);
                }
                catch (Exception exception)
                {
                    string parentEntryId = ChannelPrefixHelper.GetEpiserverCode(parentId, config);
                    string childEntryId = ChannelPrefixHelper.GetEpiserverCode(catalogEntryId, config);
                    IntegrationLogger.Write(
                        LogLevel.Error,
                        $"Failed to update entry relations between parent entry id {parentEntryId} and child entry id {childEntryId} in catalog with id {catalogEntryId}",
                        exception);
                }
            }
        }

        internal static bool Import(string filePath, Guid guid, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var endpoint = new RestEndpoint<string>(config.Settings, "ImportCatalogXml");
                    string result = endpoint.Post(filePath);
                    IntegrationLogger.Write(LogLevel.Debug, $"Import catalog returned: {result}");
                    return true;
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to import catalog xml file {filePath}.", exception);
                    IntegrationLogger.Write(LogLevel.Error, exception.ToString());

                    return false;
                }
            }
        }

        internal static bool StartAssetImportIntoEpiServerCommerce(string manifest, string baseFilePpath, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var importer = new ResourceImporter(config);
                    importer.ImportResources(manifest, baseFilePpath);

                    IntegrationLogger.Write(LogLevel.Information, $"Resource file {manifest} imported to EPi Server Commerce.");
                    return true;
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to import resource file {manifest}.", exception);
                    return false;
                }
            }
        }

        internal static bool ImportUpdateCompleted(string catalogName, ImportUpdateCompletedEventType eventType, bool resourceIncluded, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var endpoint = new RestEndpoint<ImportUpdateCompletedData>(config.Settings, "ImportUpdateCompleted");
                    var data = new ImportUpdateCompletedData
                                {
                                    CatalogName = catalogName,
                                    EventType = eventType,
                                    ResourcesIncluded = resourceIncluded
                                };
                    string result = endpoint.Post(data);
                    IntegrationLogger.Write(LogLevel.Debug, string.Format("ImportUpdateCompleted returned: {0}", result));
                    return true;
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Failed to fire import update completed for catalog {0}.", catalogName), exception);
                    return false;
                }
            }
        }

        internal static bool DeleteCompleted(string catalogName, DeleteCompletedEventType eventType, Configuration config)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var endpoint = new RestEndpoint<DeleteCompletedData>(config.Settings, "DeleteCompleted");
                    var data = new DeleteCompletedData
                                {
                                   CatalogName = catalogName,
                                   EventType = eventType
                               };
                    string result = endpoint.Post(data);
                    IntegrationLogger.Write(LogLevel.Debug, string.Format("DeleteCompleted returned: {0}", result));
                    return true;
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Failed to fire DeleteCompleted for catalog {0}.", catalogName), exception);
                    return false;
                }
            }
        }

        internal static void SendHttpPost(Configuration config, string filepath)
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
            }
        }
    }
}
