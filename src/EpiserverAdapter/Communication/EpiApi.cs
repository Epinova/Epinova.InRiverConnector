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
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.DeleteCatalog, catalogId), $"Failed to delete catalog with id: {catalogId}"
            );
        }

        internal async Task DeleteCatalogNode(Entity catalogNode, int catalogId)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.Endpoints.DeleteCatalogNode, _catalogCodeGenerator.GetEpiserverCode(catalogNode)), $"Failed to delete catalogNode with id: {catalogNode.Id} for channel: {catalogId}"
            );
        }

        internal async Task DeleteSku(string skuId)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.DeleteCatalogEntry, skuId), $"Failed to delete catalog entry based on SKU ID: {skuId}"
            );
        }

        internal async Task DeleteCatalogEntry(Entity entity)
        {
            var code = _catalogCodeGenerator.GetEpiserverCode(entity);
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.Endpoints.DeleteCatalogEntry, new DeleteRequest(code)), $"Failed to delete catalog entry with catalog entry ID: {code}"
            );
        }

        internal async Task DeleteSkus(List<string> skuIds)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.Endpoints.DeleteCatalogEntry, new DeleteRequest(skuIds)), $"Failed to delete skus: {string.Join(",", skuIds)}"
            );
        }

        internal async Task MoveNodeToRootIfNeeded(int entityId)
        {
            var entryNodeId = _catalogCodeGenerator.GetEpiserverCode(entityId);
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.CheckAndMoveNodeIfNeeded, entryNodeId), "Failed when calling the interface function : CheckAndMoveNodeIfNeeded"
            );
        }

        internal async Task ImportCatalog(string filePath)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.ImportCatalogXml,
                        new ImportCatalogXmlRequest { Path = filePath }), $"Failed to import catalog xml file {filePath} into Episerver."
            );
        }

        internal async Task ImportResources(string resourceDocumentFilePath, string baseFilePath)
        {
            var importer = new ResourceImporter(_config, _httpClient);
            await ExecuteWithinLockAsync(
                () =>
                    importer.ImportResources(resourceDocumentFilePath, baseFilePath), $"Failed to import resource file {resourceDocumentFilePath} to Episerver."
            );
        }

        internal async Task ImportUpdateCompleted(string catalogName, ImportUpdateCompletedEventType eventType,
            bool resourceIncluded)
        {
            var data = new ImportUpdateCompletedData
            {
                CatalogName = catalogName,
                EventType = eventType,
                ResourcesIncluded = resourceIncluded
            };
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.ImportUpdateCompleted, data)
            );
        }

        internal async Task DeleteCompleted(string catalogName, DeleteCompletedEventType eventType)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.Endpoints.DeleteCompleted, new DeleteCompletedData
                    {
                        CatalogName = catalogName,
                        EventType = eventType
                    }), $"Failed to fire DeleteCompleted for catalog {catalogName}."
            );
        }

        internal async Task NotifyEpiserverPostImport(string filepath)
        {
            if (string.IsNullOrEmpty(_config.HttpPostUrl))
                return;

            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.HttpPostUrl, new {filePath = filepath})
            );
        }

        public async Task DeleteResource(Guid resourceGuid)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.Endpoints.DeleteResource,
                        new DeleteResourceRequest {ResourceGuid = resourceGuid})
            );
        }

        public async Task DeleteLink(string sourceCode, string targetCode, bool isRelation)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.Endpoints.DeleteLink, new DeleteLinkRequest
                    {
                        SourceCode = sourceCode,
                        TargetCode = targetCode,
                        IsRelation = isRelation
                    })
            );
        }

        public async Task DeleteLink(Guid resourceGuid, string targetCode)
        {
            await ExecuteWithinLockAsync(
                () =>
                    _httpClient.PostAsync(_config.Endpoints.DeleteResource, new DeleteResourceRequest
                    {
                        ResourceGuid = resourceGuid,
                        EntryToRemoveFrom = targetCode
                    })
            );
        }

        private async Task ExecuteWithinLockAsync(Func<Task> action,
            string errorString = null)
        {
            await Semaphore.WaitAsync();
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error,
                    errorString,
                    exception);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        }
    }
}