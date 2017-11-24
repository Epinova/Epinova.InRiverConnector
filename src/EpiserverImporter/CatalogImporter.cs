using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverImporter.EventHandling;
using Epinova.InRiverConnector.Interfaces;
using EPiServer;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Commerce.Catalog.Linking;
using EPiServer.Logging;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using Mediachase.Commerce.Catalog;
using Mediachase.Commerce.Catalog.Dto;
using Mediachase.Commerce.Catalog.ImportExport;
using Mediachase.Commerce.Catalog.Managers;
using Mediachase.Commerce.Catalog.Objects;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class CatalogImporter : ICatalogImporter
    {
        private readonly ILogger _logger;
        private readonly ReferenceConverter _referenceConverter;
        private readonly IContentRepository _contentRepository;
        private readonly Configuration _config;
        private readonly IRelationRepository _relationRepository;
        private readonly ICatalogService _catalogService;
        private readonly IAssociationRepository _associationRepository;

        public CatalogImporter(ILogger logger, 
                               ReferenceConverter referenceConverter, 
                               IContentRepository contentRepository,
                               Configuration config,
                               IRelationRepository relationRepository, 
                               ICatalogService catalogService,
                               IAssociationRepository associationRepository)
        {
            _logger = logger;
            _referenceConverter = referenceConverter;
            _contentRepository = contentRepository;
            _config = config;
            _relationRepository = relationRepository;
            _catalogService = catalogService;
            _associationRepository = associationRepository;
        }

        
        public void DeleteCatalogEntry(string code)
        {
            List<IDeleteActionsHandler> deleteHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            var contentReference = _referenceConverter.GetContentLink(code);
            var entry = _contentRepository.Get<EntryContentBase>(contentReference);

            if (entry == null)
            {
                _logger.Warning($"Could not find catalog entry with id: {code}. No entry is deleted");
                return;
            }
            if (_config.RunDeleteActionsHandlers)
            {
                foreach (var handler in deleteHandlers)
                {
                    handler.PreDeleteCatalogEntry(entry);
                }
            }

            var relatedChildren = _catalogService.GetChildren(entry);
            foreach (var child in relatedChildren)
            {
                var entryRelations = _catalogService.GetParents(child);
                if (entryRelations.Count() > 1)
                    continue;

                _logger.Debug($"Deleting child with only one parent: {child.Code}.");
                _contentRepository.Delete(child.ContentLink, true, AccessLevel.NoAccess);
            }

            _logger.Debug($"Deleting entry {entry.Code}.");
            _contentRepository.Delete(entry.ContentLink, true, AccessLevel.NoAccess);

            if (_config.RunDeleteActionsHandlers)
            {
                foreach (var handler in deleteHandlers)
                {
                    handler.PostDeleteCatalogEntry(entry);
                }
            }
        }

        public void DeleteCatalog(int catalogId)
        {
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (_config.RunDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteCatalog(catalogId);
                }
            }

            CatalogContext.Current.DeleteCatalog(catalogId);
          
            if (_config.RunDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalog(catalogId);
                }
            }
        }

        public void DeleteCatalogNode(string code)
        {
            var contentReference = _referenceConverter.GetContentLink(code, CatalogContentType.CatalogNode);
            if (!_contentRepository.TryGet(contentReference, out NodeContent nodeToDelete))
            {
                _logger.Error($"DeleteCatalogNode called with a code that doesn't exist or is not a catalog node: {code}");
                return;
            }

            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (_config.RunDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteCatalogNode(nodeToDelete);
                }
            }

            var children = _contentRepository.GetChildren<EntryContentBase>(nodeToDelete.ContentLink);

            foreach (var child in children.Where(ShouldDeleteChild))
            {
                _contentRepository.Delete(child.ContentLink, true, AccessLevel.NoAccess);
            }
            
            _contentRepository.Delete(nodeToDelete.ContentLink, true, AccessLevel.NoAccess);

            if (_config.RunDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalogNode(nodeToDelete);
                }
            }
        }

        public bool ShouldDeleteChild(EntryContentBase child)
        {
            var nodeRelations = _relationRepository.GetParents<NodeRelation>(child.ContentLink);
            return nodeRelations.Count() == 1;
        }

        public void MoveNodeToRootIfNeeded(string catalogNodeId)
        {
            CatalogNodeDto nodeDto = CatalogContext.Current.GetCatalogNodeDto(catalogNodeId);
            if (nodeDto.CatalogNode.Count > 0)
            {
                if (nodeDto.CatalogNode[0].ParentNodeId != 0)
                {
                    MoveNode(nodeDto.CatalogNode[0].Code, 0);
                }
            }
        }

        public void ImportCatalogXml(string path)
        {
            Task.Run(
                () =>
                {
                    try
                    {
                        ImportStatusContainer.Instance.Message = ImportStatus.IsImporting;
                        ImportStatusContainer.Instance.IsImporting = true;

                        _logger.Information($"Importing catalog document from {path}");

                        var catalogImportHandlers = ServiceLocator.Current.GetAllInstances<ICatalogImportHandler>().ToList();
                        if (catalogImportHandlers.Any() && _config.RunCatalogImportHandlers)
                        {
                            _logger.Information("Importing with pre- and post-import handlers.");
                            ImportCatalogWithHandlers(path, catalogImportHandlers);
                        }
                        else
                        {
                            _logger.Information("Importing without handlers.");
                            ImportCatalog(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        ImportStatusContainer.Instance.IsImporting = false;
                        _logger.Error("Catalog Import Failed", ex);
                        ImportStatusContainer.Instance.Message = "ERROR: " + ex.Message;
                    }

                    _logger.Information("Successfully imported Catalog.xml.");

                    ImportStatusContainer.Instance.IsImporting = false;
                    ImportStatusContainer.Instance.Message = "Import Sucessful";
                });
        }


        public bool ImportUpdateCompleted(ImportUpdateCompletedData data)
        {
            if (_config.RunInRiverEventsHandlers)
            {
                IEnumerable<IInRiverEventsHandler> eventsHandlers = ServiceLocator.Current.GetAllInstances<IInRiverEventsHandler>();
                foreach (IInRiverEventsHandler handler in eventsHandlers)
                {
                    handler.ImportUpdateCompleted(data.CatalogName, data.EventType, data.ResourcesIncluded);
                }

                _logger.Debug($"*** ImportUpdateCompleted events with parameters CatalogName={data.CatalogName}, EventType={data.EventType}, ResourcesIncluded={data.ResourcesIncluded}");
            }

            return true;
        }

        public bool DeleteCompleted(DeleteCompletedData data)
        {
            if (_config.RunInRiverEventsHandlers)
            {
                IEnumerable<IInRiverEventsHandler> eventsHandlers = ServiceLocator.Current.GetAllInstances<IInRiverEventsHandler>();
                foreach (IInRiverEventsHandler handler in eventsHandlers)
                {
                    handler.DeleteCompleted(data.CatalogName, data.EventType);
                }

                _logger.Debug("*** DeleteCompleted events with parameters CatalogName={data.CatalogName}, EventType={data.EventType}");
            }

            return true;
        }

        public void DeleteAssociation(string sourceCode, string targetCode)
        {
            _logger.Debug($"Deleting association between {sourceCode} and {targetCode}.");
            var sourceReference = _referenceConverter.GetContentLink(sourceCode);
            var targetReference = _referenceConverter.GetContentLink(targetCode);

            var associations = _associationRepository.GetAssociations(sourceReference);
            var existingAssociation = associations.FirstOrDefault(x => x.Target.Equals(targetReference));
            if (existingAssociation != null)
            {
                _associationRepository.RemoveAssociation(existingAssociation);
            }
        }

        public void DeleteRelation(string sourceCode, string targetCode)
        {
            _logger.Debug($"Deleting relation between {sourceCode} and {targetCode}.");
            var sourceReference = _referenceConverter.GetContentLink(sourceCode);
            var targetReference = _referenceConverter.GetContentLink(targetCode);

            var entryRelations = _relationRepository.GetChildren<EntryRelation>(sourceReference);
            var relation = entryRelations.FirstOrDefault(x => x.Child.Equals(targetReference));
            if (relation != null)
            {
                _relationRepository.RemoveRelation(relation);
            }
        }

        private void ImportCatalog(string path)
        {
            var cie = new CatalogImportExport();
            cie.ImportExportProgressMessage += ProgressHandler;

            var directoryName = Path.GetDirectoryName(path);
            cie.Import(directoryName, true);
        }

        private void ImportCatalogWithHandlers(string filePath, List<ICatalogImportHandler> catalogImportHandlers)
        {
            string originalFileName = Path.GetFileNameWithoutExtension(filePath);
            string filenameBeforePreImport = originalFileName + "-beforePreImport.xml";

            XDocument catalogDoc = XDocument.Load(filePath);
            var directory = Path.GetDirectoryName(filePath);
            var completeFilePathToSave = Path.Combine(directory, filenameBeforePreImport);
            _logger.Debug($"Saving original file to {completeFilePathToSave}.");

            catalogDoc.Save(completeFilePathToSave);

            if (catalogImportHandlers.Any())
            {
                foreach (ICatalogImportHandler handler in catalogImportHandlers)
                {
                    try
                    {
                        _logger.Debug($"Preimport handler: {handler.GetType().FullName}");
                        handler.PreImport(catalogDoc);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("Failed to run PreImport on " + handler.GetType().FullName, e);
                    }
                }
            }
    
            FileStream fs = new FileStream(filePath, FileMode.Create);
            catalogDoc.Save(fs);
            fs.Dispose();

            CatalogImportExport cie = new CatalogImportExport();
            cie.ImportExportProgressMessage += ProgressHandler;

            cie.Import(directory, true);

            catalogDoc = XDocument.Load(filePath);

            if (catalogImportHandlers.Any())
            {
                foreach (ICatalogImportHandler handler in catalogImportHandlers)
                {
                    try
                    {
                        _logger.Debug($"Postimport handler: {handler.GetType().FullName}");
                        handler.PostImport(catalogDoc);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("Failed to run PostImport on " + handler.GetType().FullName, e);
                    }
                }
            }
        }

        private void ProgressHandler(object source, ImportExportEventArgs args)
        {
            _logger.Debug($"{args.Message}");
        }


        private void MoveNode(string nodeCode, int newParent)
        {
            CatalogNodeDto catalogNodeDto = CatalogContext.Current.GetCatalogNodeDto(nodeCode, new CatalogNodeResponseGroup(CatalogNodeResponseGroup.ResponseGroup.CatalogNodeFull));

            // Move node to new parent
            _logger.Debug($"Move {nodeCode} to new parent ({newParent}).");
            catalogNodeDto.CatalogNode[0].ParentNodeId = newParent;
            CatalogContext.Current.SaveCatalogNode(catalogNodeDto);
        }

        private int FindCatalogByName(string name)
        {
            try
            {
                CatalogDto d = CatalogContext.Current.GetCatalogDto();
                foreach (CatalogDto.CatalogRow catalog in d.Catalog)
                {
                    if (name.Equals(catalog.Name))
                    {
                        return catalog.CatalogId;
                    }
                }

                return -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}