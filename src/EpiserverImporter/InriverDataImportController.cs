using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Xml;
using System.Xml.Serialization;
using Epinova.InRiverConnector.Interfaces;
using Epinova.InRiverConnector.Interfaces.Poco;
using EPiServer.Logging;
using EPiServer.ServiceLocation;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class InriverDataImportController : SecuredApiController
    {
        private readonly ICatalogImporter _catalogImporter;
        private readonly ILogger _logger;
        private readonly MediaImporter _mediaImporter;

        public InriverDataImportController()
        {
            // In case you haven't got your own WebAPI dependency resolver, do it this old fashioned silly way.

            _catalogImporter = ServiceLocator.Current.GetInstance<ICatalogImporter>();
            _logger = ServiceLocator.Current.GetInstance<ILogger>();
            _mediaImporter = ServiceLocator.Current.GetInstance<MediaImporter>();
        }

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
                return ImportStatus.IsImporting;
            }

            return ImportStatusContainer.Instance.Message;
        }

        [HttpPost]
        public void DeleteCatalogEntry(DeleteRequest request)
        {
            _logger.Debug("DeleteCatalogEntry");

            foreach (var code in request.Codes)
            {
                _catalogImporter.DeleteCatalogEntry(code);
            }
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
        public void MoveNodeToRootIfNeeded([FromBody] string catalogNodeId)
        {
            _logger.Debug("MoveNodeToRootIfNeeded");

            _catalogImporter.MoveNodeToRootIfNeeded(catalogNodeId);
        }

        [HttpPost]
        public void DeleteResource(DeleteResourceRequest request)
        {
            _logger.Debug($"DeleteResource with ID {request.ResourceGuid}");
            DoWithErrorHandling(() => _mediaImporter.DeleteResource(request));
        }

        [HttpPost]
        public void DeleteLink(DeleteLinkRequest request)
        {
            _logger.Debug($"Deleting link between {request.SourceCode} and {request.TargetCode}.");
            DoWithErrorHandling(() =>
                {
                    if (request.IsRelation)
                        _catalogImporter.DeleteRelation(request.SourceCode, request.TargetCode);
                    else
                        _catalogImporter.DeleteAssociation(request.SourceCode, request.TargetCode);
                }
            );
        }

        public string Get()
        {
            _logger.Debug("Hello from Episerver import controller!");
            return "Hello from Episerver import controller!";
        }

        [HttpPost]
        public string ImportCatalogXml(ImportCatalogXmlRequest request)
        {
            ImportStatusContainer.Instance.Message = ImportStatus.IsImporting;

            _catalogImporter.ImportCatalogXml(request.Path);

            return ImportStatusContainer.Instance.Message;
        }

        [HttpPost]
        public string ImportResources(ImportResourcesRequest request)
        {
            DoWithErrorHandling(() =>
            {
                Task.Run(
                    () =>
                    {
                        ImportStatusContainer.Instance.IsImporting = true;

                        _mediaImporter.ImportResources(request);

                        ImportStatusContainer.Instance.IsImporting = false;
                        ImportStatusContainer.Instance.Message = "Import Sucessful";
                    });
            });
            return ImportStatusContainer.Instance.Message;
        }

        [HttpPost]
        public bool ImportUpdateCompleted(ImportUpdateCompletedData data)
        {
            return _catalogImporter.ImportUpdateCompleted(data);
        }

        [HttpPost]
        public void DeleteCompleted(DeleteCompletedData data)
        {
            DoWithErrorHandling(() => _catalogImporter.DeleteCompleted(data));
        }

        private void DoWithErrorHandling(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.Error("Error when receiving import/deletion data from PIM.", ex);
            }
        }
    }
}