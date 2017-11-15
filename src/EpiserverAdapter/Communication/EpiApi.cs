using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly IConfiguration _config;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly PimFieldAdapter _pimFieldAdapter;
        private readonly HttpClientInvoker _httpClient; 

        public EpiApi(IConfiguration config, 
                      CatalogCodeGenerator catalogCodeGenerator, 
                      PimFieldAdapter pimFieldAdapter)
        {
            _config = config;
            _catalogCodeGenerator = catalogCodeGenerator;
            _pimFieldAdapter = pimFieldAdapter;
            _httpClient = new HttpClientInvoker(config);
        }

        internal void DeleteCatalog(int catalogId)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.DeleteCatalog, catalogId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog with id: {catalogId}", exception);
                }
            }
        }

        internal void DeleteCatalogNode(Entity catalogNode, int catalogId)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var code = _catalogCodeGenerator.GetEpiserverCode(catalogNode);
                    _httpClient.Post(_config.Endpoints.DeleteCatalogNode, code);
                }
                catch (Exception ex)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalogNode with id: {catalogNode.Id} for channel: {catalogId}", ex);
                }
            }
        }

        internal void DeleteSku(string skuId)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.DeleteCatalogEntry, skuId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog entry based on SKU ID: {skuId}", exception);
                }
            }
        }

        internal void DeleteCatalogEntry(Entity entity)
        {
            var code = _catalogCodeGenerator.GetEpiserverCode(entity);

            lock (EpiLockObject.Instance)
            {
                try
                {
                    _httpClient.Post(_config.Endpoints.DeleteCatalogEntry, new DeleteRequest(code));
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog entry with catalog entry ID: {code}", exception);
                }
            }
        }

        internal void DeleteSkus(List<string> skuIds)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    _httpClient.Post(_config.Endpoints.DeleteCatalogEntry, new DeleteRequest(skuIds));
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to delete skus: {string.Join(",", skuIds)}", exception);
                }
            }
        }

        internal void UpdateLinkEntityData(Entity linkEntity, Entity channel, int parentId)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string channelName = _pimFieldAdapter.GetDisplayName(channel, -1);

                    string parentEntryId = _catalogCodeGenerator.GetEpiserverCode(parentId);
                    string linkEntityIdString = _catalogCodeGenerator.GetEpiserverCode(linkEntity);

                    string dispName = linkEntity.EntityType.Id + '_' + _pimFieldAdapter.GetDisplayName(linkEntity, -1).Replace(' ', '_');

                    LinkEntityUpdateData dataToSend = new LinkEntityUpdateData
                                                          {
                                                              ChannelName = channelName,
                                                              LinkEntityIdString = linkEntityIdString,
                                                              LinkEntryDisplayName = dispName,
                                                              ParentEntryId = parentEntryId
                                                          };

                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.UpdateLinkEntityData, dataToSend);
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
                    string channelName = _pimFieldAdapter.GetDisplayName(channelEntity, -1);

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

        internal void CheckAndMoveNodeIfNeeded(int entityId)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    string entryNodeId = _catalogCodeGenerator.GetEpiserverCode(entityId);
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.CheckAndMoveNodeIfNeeded, entryNodeId);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Warning, "Failed when calling the interface function: CheckAndMoveNodeIfNeeded", exception);
                }
            }
        }

        internal void ImportCatalog(string filePath)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var result = _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.ImportCatalogXml, filePath);

                    IntegrationLogger.Write(LogLevel.Debug, $"Import catalog returned: {result}");
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to import catalog xml file {filePath} into Episerver.", exception);
                    throw;
                }
            }
        }

        internal void ImportResources(string resourceDocumentFilePath, string baseFilePath)
        {
            lock (EpiLockObject.Instance)
            {
                try
                {
                    var importer = new ResourceImporter(_config, _httpClient);
                    importer.ImportResources(resourceDocumentFilePath, baseFilePath);

                    IntegrationLogger.Write(LogLevel.Information, $"Resource file {resourceDocumentFilePath} imported to Episerver.");
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to import resource file {resourceDocumentFilePath} to Episerver.", exception);
                    throw;
                }
            }
        }

        internal void ImportUpdateCompleted(string catalogName, ImportUpdateCompletedEventType eventType, bool resourceIncluded)
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

                    string result = _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.ImportUpdateCompleted, data);
                    IntegrationLogger.Write(LogLevel.Debug, $"ImportUpdateCompleted returned: {result}");
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to fire import update completed for catalog {catalogName}.", exception);
                    throw;
                }
            }
        }

        internal void DeleteCompleted(string catalogName, DeleteCompletedEventType eventType)
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

                    _httpClient.Post(_config.Endpoints.DeleteCompleted, data);
                }
                catch (Exception exception)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Failed to fire DeleteCompleted for catalog {catalogName}.", exception);
                    throw;
                }
            }
        }

        internal void NotifyEpiserverPostImport(string filepath)
        {
            if (string.IsNullOrEmpty(_config.HttpPostUrl))
                return;

            lock (EpiLockObject.Instance)
            {
                _httpClient.Post(_config.HttpPostUrl, new { filePath = filepath });
            }
        }

        public void DeleteResource(Guid resourceGuid)
        {
            lock (EpiLockObject.Instance)
            {
                _httpClient.Post(_config.Endpoints.DeleteResource, new DeleteResourceRequest { ResourceGuid = resourceGuid });
            }
        }

        public void DeleteLink(string sourceCode, string targetCode, bool isRelation)
        {
            lock (EpiLockObject.Instance)
            {
                _httpClient.Post(_config.Endpoints.DeleteLink, new DeleteLinkRequest
                {
                    SourceCode = sourceCode,
                    TargetCode = targetCode,
                    IsRelation = isRelation
                });
            }
        }

        public void DeleteLink(Guid resourceGuid, string targetCode)
        {
            lock (EpiLockObject.Instance)
            {
                _httpClient.Post(_config.Endpoints.DeleteResource, new DeleteResourceRequest
                {
                    ResourceGuid = resourceGuid,
                    EntryToRemoveFrom = targetCode
                });
            }
        }
    }
}
