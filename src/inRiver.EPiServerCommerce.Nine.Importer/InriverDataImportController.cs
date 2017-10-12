using System;
using System.Collections.Generic;
using System.Web.Http;
using EPiServer.Logging;
using inRiver.EPiServerCommerce.Interfaces;
using log4net;
using LogManager = log4net.LogManager;

namespace inRiver.EPiServerCommerce.Importer
{
    public class InriverDataImportController : SecuredApiController
    {
        private readonly ICatalogImporter _catalogImporter;
        private readonly ILogger _logger;

        public InriverDataImportController(ICatalogImporter catalogImporter,
                                           ILogger logger)
        {
            _catalogImporter = catalogImporter;
            _logger = logger;
        }

        private static readonly ILog Log = LogManager.GetLogger(typeof(InriverDataImportController));
        
        [HttpGet]
        public string IsImporting()
        {
            Log.Debug("IsImporting");

            if (ImportStatusContainer.Instance.IsImporting)
            {
                return "importing";
            }

            return ImportStatusContainer.Instance.Message;
        }

        // TODO: Global exception logging, ref PIM-78

        [HttpPost]
        public bool DeleteCatalogEntry([FromBody] string catalogEntryId)
        {
            _logger.Debug("DeleteCatalogEntry");

            try
            {
                _catalogImporter.DeleteCatalogEntry(catalogEntryId);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error while deleting catalog entry with code {catalogEntryId}", ex);
                return false;
            }

            return true;
        }

        [HttpPost]
        public bool DeleteCatalog([FromBody] int catalogId)
        {
            _logger.Debug("DeleteCatalog");
            
            try
            {
                _catalogImporter.DeleteCatalog(catalogId);
            }
            catch (Exception ex)
            {
                Log.Error($"Error while deleting catalog with id: {catalogId}", ex);
                return false;
            }

            return true;
        }

        [HttpPost]
        public bool DeleteCatalogNode([FromBody] string catalogNodeId)
        {
            Log.Debug("DeleteCatalogNode");

            try
            {
                _catalogImporter.DeleteCatalogNode(catalogNodeId);
            }
            catch (Exception ex)
            {
                Log.Error($"Error while deleting catalogNode with id: {catalogNodeId}", ex);
                return false;
            }
            
            return true;
        }

        [HttpPost]
        public bool CheckAndMoveNodeIfNeeded([FromBody] string catalogNodeId)
        {
            Log.Debug("CheckAndMoveNodeIfNeeded");

            try
            {
                _catalogImporter.CheckAndMoveNodeIfNeeded(catalogNodeId);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not CheckAndMoveNodeIfNeeded for catalogNode with id: {catalogNodeId}", ex);
                return false;
            }

            return true;
        }

        [HttpPost]
        public bool UpdateLinkEntityData(LinkEntityUpdateData linkEntityUpdateData)
        {
            Log.Debug("UpdateLinkEntityData");

            try
            {
                _catalogImporter.UpdateLinkEntityData(linkEntityUpdateData);
            }
            catch (Exception ex)
            {
                _logger.Error($"Could not update LinkEntityData for entity with id:{linkEntityUpdateData.LinkEntityIdString}", ex);
                return false;
            }

            return true;
        }

        [HttpPost]
        public bool UpdateEntryRelations(UpdateEntryRelationData updateEntryRelationData)
        {
            Log.Debug("UpdateEntryRelations");

            try
            {
                _catalogImporter.UpdateEntryRelations(updateEntryRelationData);
            }
            catch (Exception ex)
            {
                Log.Warn(string.Format("Could not update entry relations catalog with id:{0}", updateEntryRelationData.CatalogEntryIdString), ex);
                return false;
            }

            return true;
        }

        [HttpPost]
        public List<string> GetLinkEntityAssociationsForEntity(GetLinkEntityAssociationsForEntityData data)
        {
            Log.Debug("GetLinkEntityAssociationsForEntity");

            try
            {
                return _catalogImporter.GetLinkEntityAssociationsForEntity(data);
            }
            catch (Exception e)
            {
                Log.Error($"Could not GetLinkEntityAssociationsForEntity for parentIds: {data.ParentIds}", e);
            }

            return new List<string>();
        }

        public string Get()
        {
            Log.Debug("Hello from Episerver import controller!");
            return "Hello from Episerver import controller!";
        }

        [HttpPost]
        public string ImportCatalogXml([FromBody] string path)
        {
            ImportStatusContainer.Instance.Message = "importing";
            try
            {
                _catalogImporter.ImportCatalogXml(path);
            }
            catch (Exception ex)
            {
                _logger.Error("Error while importing catalog XML", ex);
            }

            return ImportStatusContainer.Instance.Message;
        }

        [HttpPost]
        public bool ImportResources(List<InRiverImportResource> resources)
        {
            return _catalogImporter.ImportResources(resources);
        }

        [HttpPost]
        public bool ImportUpdateCompleted(ImportUpdateCompletedData data)
        {
            try
            {
                return _catalogImporter.ImportUpdateCompleted(data);
            }
            catch (Exception ex)
            {
                Log.Error("Error in ImportUpdateCompleted", ex);
                return false;
            }
        }

        [HttpPost]
        public bool DeleteCompleted(DeleteCompletedData data)
        {
            try
            {
                return _catalogImporter.DeleteCompleted(data);
            }
            catch (Exception ex)
            {
                Log.Error("Error in DeleteCompleted", ex);
                return false;
            }
        }
    }
}