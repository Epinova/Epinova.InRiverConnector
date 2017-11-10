using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using Epinova.InRiverConnector.Interfaces;
using EPiServer.Logging;
using log4net;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    [ImporterExceptionFilter]
    public class InriverDataImportController : SecuredApiController
    {
        private readonly ICatalogImporter _catalogImporter;
        private readonly ILogger _logger;
        private readonly MediaImporter _mediaImporter;

        public InriverDataImportController(ICatalogImporter catalogImporter,
                                           ILogger logger,
                                           MediaImporter mediaImporter)
        {
            _catalogImporter = catalogImporter;
            _logger = logger;
            _mediaImporter = mediaImporter;
        }
      
        [HttpGet]
        public string IsImporting()
        {
            _logger.Debug("IsImporting");

            if (ImportStatusContainer.Instance.IsImporting)
            {
                return "importing";
            }

            return ImportStatusContainer.Instance.Message;
        }

        [HttpPost]
        public void DeleteCatalogEntry([FromBody] string catalogEntryId)
        {
            _logger.Debug("DeleteCatalogEntry");

            _catalogImporter.DeleteCatalogEntry(catalogEntryId);
        }

        [HttpPost]
        public void DeleteCatalog([FromBody] int catalogId)
        {
            _logger.Debug("DeleteCatalog");
            
            _catalogImporter.DeleteCatalog(catalogId);
        }

        [HttpPost]
        public void DeleteCatalogNode([FromBody] string catalogNodeId)
        {
            _logger.Debug("DeleteCatalogNode");

            _catalogImporter.DeleteCatalogNode(catalogNodeId);
        }

        [HttpPost]
        public void CheckAndMoveNodeIfNeeded([FromBody] string catalogNodeId)
        {
            _logger.Debug("CheckAndMoveNodeIfNeeded");

            _catalogImporter.CheckAndMoveNodeIfNeeded(catalogNodeId);
        }

        [HttpPost]
        public void UpdateLinkEntityData(LinkEntityUpdateData linkEntityUpdateData)
        {
            _logger.Debug("UpdateLinkEntityData");
            _catalogImporter.UpdateLinkEntityData(linkEntityUpdateData);
        }

        [HttpPost]
        public void UpdateEntryRelations(UpdateRelationData updateRelationData)
        {
            _logger.Debug("UpdateEntryRelations");

            _catalogImporter.UpdateEntryRelations(updateRelationData);
        }

        [HttpPost]
        public List<string> GetLinkEntityAssociationsForEntity(GetLinkEntityAssociationsForEntityData data)
        {
            _logger.Debug("GetLinkEntityAssociationsForEntity");

            return _catalogImporter.GetLinkEntityAssociationsForEntity(data);
        }

        public string Get()
        {
            _logger.Debug("Hello from Episerver import controller!");
            return "Hello from Episerver import controller!";
        }

        [HttpPost]
        public string ImportCatalogXml([FromBody] string path)
        {
            ImportStatusContainer.Instance.Message = "importing";

            _catalogImporter.ImportCatalogXml(path);

            return ImportStatusContainer.Instance.Message;
        }

        [HttpPost]
        public void ImportResources(List<InRiverImportResource> resources)
        {
            Task.Run(
                () =>
                {
                    _logger.Debug($"Received list of {resources.Count} resources to import");

                    _mediaImporter.ImportResources(resources);
                });
        }

        [HttpPost]
        public bool ImportUpdateCompleted(ImportUpdateCompletedData data)
        {
            return _catalogImporter.ImportUpdateCompleted(data);
        }

        [HttpPost]
        public void DeleteCompleted(DeleteCompletedData data)
        {
            _catalogImporter.DeleteCompleted(data);
        }
    }
}