using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly SemaphoreSlim Semaphore;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly IConfiguration _config;
        private readonly HttpClientInvoker _httpClient;
        private readonly PimFieldAdapter _pimFieldAdapter;

        static EpiApi()
        {
            Semaphore = new SemaphoreSlim(1, 1);
        }

        public EpiApi(IConfiguration config,
            CatalogCodeGenerator catalogCodeGenerator,
            PimFieldAdapter pimFieldAdapter)
        {
            _config = config;
            _catalogCodeGenerator = catalogCodeGenerator;
            _pimFieldAdapter = pimFieldAdapter;
            _httpClient = new HttpClientInvoker(config);
        }

        internal async Task DeleteCatalog(int catalogId)
        {
            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.DeleteCatalog, catalogId);
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog with id: {catalogId}", exception);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task DeleteCatalogNode(Entity catalogNode, int catalogId)
        {
            await Semaphore.WaitAsync();
            try
            {
                var code = _catalogCodeGenerator.GetEpiserverCode(catalogNode);
                await _httpClient.PostAsync(_config.Endpoints.DeleteCatalogNode, code);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error,
                    $"Failed to delete catalogNode with id: {catalogNode.Id} for channel: {catalogId}", ex);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task DeleteSku(string skuId)
        {
            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.DeleteCatalogEntry, skuId);
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog entry based on SKU ID: {skuId}",
                    exception);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task DeleteCatalogEntry(Entity entity)
        {
            var code = _catalogCodeGenerator.GetEpiserverCode(entity);

            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostAsync(_config.Endpoints.DeleteCatalogEntry, new DeleteRequest(code));
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, $"Failed to delete catalog entry with catalog entry ID: {code}",
                    exception);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task DeleteSkus(List<string> skuIds)
        {
            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostAsync(_config.Endpoints.DeleteCatalogEntry, new DeleteRequest(skuIds));
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, $"Failed to delete skus: {string.Join(",", skuIds)}",
                    exception);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task MoveNodeToRootIfNeeded(int entityId)
        {
            await Semaphore.WaitAsync();
            try
            {
                var entryNodeId = _catalogCodeGenerator.GetEpiserverCode(entityId);
                await _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.CheckAndMoveNodeIfNeeded, entryNodeId);
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Warning,
                    "Failed when calling the interface function: CheckAndMoveNodeIfNeeded", exception);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task ImportCatalog(string filePath)
        {
            await Semaphore.WaitAsync();
            try
            {
                var result = await _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.ImportCatalogXml,
                    new ImportCatalogXmlRequest {Path = filePath});

                IntegrationLogger.Write(LogLevel.Debug, $"Import catalog returned: {result}");
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, $"Failed to import catalog xml file {filePath} into Episerver.",
                    exception);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task ImportResources(string resourceDocumentFilePath, string baseFilePath)
        {
            await Semaphore.WaitAsync();
            try
            {
                var importer = new ResourceImporter(_config, _httpClient);
                importer.ImportResources(resourceDocumentFilePath, baseFilePath);

                IntegrationLogger.Write(LogLevel.Information,
                    $"Resource file {resourceDocumentFilePath} imported to Episerver.");
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error,
                    $"Failed to import resource file {resourceDocumentFilePath} to Episerver.", exception);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task ImportUpdateCompleted(string catalogName, ImportUpdateCompletedEventType eventType,
            bool resourceIncluded)
        {
            await Semaphore.WaitAsync();
            try
            {
                var data = new ImportUpdateCompletedData
                {
                    CatalogName = catalogName,
                    EventType = eventType,
                    ResourcesIncluded = resourceIncluded
                };

                var result = await _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.ImportUpdateCompleted, data);
                IntegrationLogger.Write(LogLevel.Debug, $"ImportUpdateCompleted returned: {result}");
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error,
                    $"Failed to fire import update completed for catalog {catalogName}.", exception);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task DeleteCompleted(string catalogName, DeleteCompletedEventType eventType)
        {
            await Semaphore.WaitAsync();
            try
            {
                var data = new DeleteCompletedData
                {
                    CatalogName = catalogName,
                    EventType = eventType
                };

                await _httpClient.PostAsync(_config.Endpoints.DeleteCompleted, data);
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, $"Failed to fire DeleteCompleted for catalog {catalogName}.",
                    exception);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task NotifyEpiserverPostImport(string filepath)
        {
            if (string.IsNullOrEmpty(_config.HttpPostUrl))
                return;

            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostAsync(_config.HttpPostUrl, new {filePath = filepath});
            }

            finally
            {
                Semaphore.Release();
            }
        }

        public async Task DeleteResource(Guid resourceGuid)
        {
            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostAsync(_config.Endpoints.DeleteResource,
                    new DeleteResourceRequest {ResourceGuid = resourceGuid});
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public async Task DeleteLink(string sourceCode, string targetCode, bool isRelation)
        {
            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostAsync(_config.Endpoints.DeleteLink, new DeleteLinkRequest
                {
                    SourceCode = sourceCode,
                    TargetCode = targetCode,
                    IsRelation = isRelation
                });
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public async Task DeleteLink(Guid resourceGuid, string targetCode)
        {
            await Semaphore.WaitAsync();
            try
            {
                await _httpClient.PostAsync(_config.Endpoints.DeleteResource, new DeleteResourceRequest
                {
                    ResourceGuid = resourceGuid,
                    EntryToRemoveFrom = targetCode
                });
            }
            finally
            {
                Semaphore.Release();
            }
        }
    }
}