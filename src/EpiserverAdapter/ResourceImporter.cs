using System.Threading.Tasks;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.Interfaces;
using inRiver.Integration.Logging;
using inRiver.Remoting.Log;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class ResourceImporter
    {
        private readonly IConfiguration _config;
        private readonly HttpClientInvoker _httpClient;

        public ResourceImporter(IConfiguration config, HttpClientInvoker httpClient)
        {
            _config = config;
            _httpClient = httpClient;
        }

        public async Task ImportResources(string resourceXmlFilePath, string baseResourcePath)
        {
            IntegrationLogger.Write(LogLevel.Information, $"Starting Resource Import. Manifest: {resourceXmlFilePath} BaseResourcePath: {baseResourcePath}");

            var importResourcesRequest = new ImportResourcesRequest { BasePath = baseResourcePath, ResourceXmlPath = resourceXmlFilePath };

            await _httpClient.PostWithAsyncStatusCheck(_config.Endpoints.ImportResources, importResourcesRequest);
        }
    }
}
