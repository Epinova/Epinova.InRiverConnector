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

        public void UpdateEntryRelations(UpdateRelationData updateRelationData)
        {
            int catalogId = FindCatalogByName(updateRelationData.ChannelName);
            CatalogEntryDto ced = CatalogContext.Current.GetCatalogEntryDto(updateRelationData.CatalogEntryIdString);
            CatalogEntryDto ced2 = CatalogContext.Current.GetCatalogEntryDto(updateRelationData.ParentEntryId);
            _logger.Debug($"UpdateEntryRelations called for catalog {catalogId} between {updateRelationData.ParentEntryId} and {updateRelationData.CatalogEntryIdString}");


            CatalogNodeDto nodeDto = CatalogContext.Current.GetCatalogNodeDto(updateRelationData.CatalogEntryIdString);

            if (nodeDto.CatalogNode.Count > 0)
            {
                _logger.Debug($"found {updateRelationData.CatalogEntryIdString} as a catalog node");
                CatalogRelationDto rels = CatalogContext.Current.GetCatalogRelationDto(
                    catalogId,
                    nodeDto.CatalogNode[0].CatalogNodeId,
                    0,
                    string.Empty,
                    new CatalogRelationResponseGroup(CatalogRelationResponseGroup.ResponseGroup.CatalogNode));

                foreach (CatalogRelationDto.CatalogNodeRelationRow row in rels.CatalogNodeRelation)
                {
                    CatalogNode parentCatalogNode = CatalogContext.Current.GetCatalogNode(row.ParentNodeId);
                    if (updateRelationData.RemoveFromChannelNodes.Contains(parentCatalogNode.ID))
                    {
                        row.Delete();
                        updateRelationData.RemoveFromChannelNodes.Remove(parentCatalogNode.ID);
                    }
                }

                if (rels.HasChanges())
                {
                    _logger.Debug("Relations between nodes has been changed, saving new catalog releations");
                    CatalogContext.Current.SaveCatalogRelationDto(rels);
                }
            }

            if (ced.CatalogEntry.Count <= 0)
            {
                _logger.Debug($"No catalog entry with id {updateRelationData.CatalogEntryIdString} found, will not continue.");
                return;
            }

            if (updateRelationData.RemoveFromChannelNodes.Count > 0)
            {
                _logger.Debug($"Look for removal from channel nodes, nr of possible nodes: {updateRelationData.RemoveFromChannelNodes.Count}");
                var rel = CatalogContext.Current.GetCatalogRelationDto(catalogId, 0, ced.CatalogEntry[0].CatalogEntryId, string.Empty, new CatalogRelationResponseGroup(CatalogRelationResponseGroup.ResponseGroup.NodeEntry));

                foreach (CatalogRelationDto.NodeEntryRelationRow row in rel.NodeEntryRelation)
                {
                    CatalogNode catalogNode = CatalogContext.Current.GetCatalogNode(row.CatalogNodeId);
                    if (updateRelationData.RemoveFromChannelNodes.Contains(catalogNode.ID))
                    {
                        row.Delete();
                    }
                }

                if (rel.HasChanges())
                {
                    _logger.Debug("Relations between entries has been changed, saving new catalog releations");
                    CatalogContext.Current.SaveCatalogRelationDto(rel);
                }
            }
            else
            {
                _logger.Debug($"{updateRelationData.CatalogEntryIdString} shall not be removed from node {updateRelationData.ParentEntryId}");
            }

            if (ced2.CatalogEntry.Count <= 0)
            {
                return;
            }

            if (!updateRelationData.ParentExistsInChannelNodes)
            {
                if (updateRelationData.IsRelation)
                {
                    _logger.Debug("Checking other relations");
                    CatalogRelationDto rel3 = CatalogContext.Current.GetCatalogRelationDto(catalogId, 0, ced2.CatalogEntry[0].CatalogEntryId, string.Empty, new CatalogRelationResponseGroup(CatalogRelationResponseGroup.ResponseGroup.CatalogEntry));
                    foreach (CatalogRelationDto.CatalogEntryRelationRow row in rel3.CatalogEntryRelation)
                    {
                        Entry childEntry = CatalogContext.Current.GetCatalogEntry(row.ChildEntryId);
                        if (childEntry.ID == updateRelationData.CatalogEntryIdString)
                        {
                            _logger.Debug(string.Format("Relations between entries {0} and {1} has been removed, saving new catalog releations", row.ParentEntryId, row.ChildEntryId));
                            row.Delete();
                            CatalogContext.Current.SaveCatalogRelationDto(rel3);
                            break;
                        }
                    }
                }
                else
                {
                    List<int> catalogAssociationIds = new List<int>();
                    _logger.Debug("Checking other associations");

                    CatalogAssociationDto associationsDto = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, updateRelationData.ParentEntryId);
                    foreach (CatalogAssociationDto.CatalogEntryAssociationRow row in associationsDto.CatalogEntryAssociation)
                    {
                        if (row.AssociationTypeId == updateRelationData.LinkTypeId)
                        {
                            Entry childEntry = CatalogContext.Current.GetCatalogEntry(row.CatalogEntryId);
                            if (childEntry.ID == updateRelationData.CatalogEntryIdString)
                            {
                                if (updateRelationData.LinkEntityIdsToRemove.Count == 0 || updateRelationData.LinkEntityIdsToRemove.Contains(row.CatalogAssociationRow.AssociationDescription))
                                {
                                    catalogAssociationIds.Add(row.CatalogAssociationId);
                                    _logger.Debug(string.Format("Removing association for {0}", row.CatalogEntryId));
                                    row.Delete();
                                }
                            }
                        }
                    }

                    if (associationsDto.HasChanges())
                    {
                        _logger.Debug("Saving updated associations");
                        CatalogContext.Current.SaveCatalogAssociation(associationsDto);
                    }

                    if (catalogAssociationIds.Count > 0)
                    {
                        foreach (int catalogAssociationId in catalogAssociationIds)
                        {
                            associationsDto = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, updateRelationData.ParentEntryId);
                            if (associationsDto.CatalogEntryAssociation.Count(r => r.CatalogAssociationId == catalogAssociationId) == 0)
                            {
                                foreach (CatalogAssociationDto.CatalogAssociationRow assRow in associationsDto.CatalogAssociation)
                                {
                                    if (assRow.CatalogAssociationId == catalogAssociationId)
                                    {
                                        assRow.Delete();
                                        _logger.Debug($"Removing association with id {catalogAssociationId} and sending update.");
                                        CatalogContext.Current.SaveCatalogAssociation(associationsDto);
                                        break;
                                    }
                                }
                            }
                        }
                    }
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
                        ImportStatusContainer.Instance.Message = "importing";
                        ImportStatusContainer.Instance.IsImporting = true;

                        _logger.Information($"Importing catalog document from {path}");
                        List<ICatalogImportHandler> catalogImportHandlers = ServiceLocator.Current.GetAllInstances<ICatalogImportHandler>().ToList();
                        if (catalogImportHandlers.Any() && _config.RunCatalogImportHandlers)
                        {
                            ImportCatalogXmlWithHandlers(path, catalogImportHandlers);
                        }
                        else
                        {
                            ImportCatalogXmlFromPath(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        ImportStatusContainer.Instance.IsImporting = false;
                        _logger.Error("Catalog Import Failed", ex);
                        ImportStatusContainer.Instance.Message = "ERROR: " + ex.Message;
                    }

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

        private void ImportCatalogXmlFromPath(string path)
        {
            _logger.Information("Starting importing the xml into EPiServer Commerce.");

            var cie = new CatalogImportExport();
            cie.ImportExportProgressMessage += ProgressHandler;

            var directoryName = Path.GetDirectoryName(path);
            cie.Import(directoryName, true);

            _logger.Information("Done importing the xml into EPiServer Commerce.");
        }

        private void ImportCatalogXmlWithHandlers(string filePath, List<ICatalogImportHandler> catalogImportHandlers)
        {
            try
            {
                string originalFileName = Path.GetFileNameWithoutExtension(filePath);
                string filenameBeforePreImport = originalFileName + "-beforePreImport.xml";

                XDocument catalogDoc = XDocument.Load(filePath);
                catalogDoc.Save(filenameBeforePreImport);

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

                if (!File.Exists(filePath))
                {
                    _logger.Error("Cata_logger.xml for path " + filePath + " does not exist. Importer is not able to continue with this process.");
                    return;
                }
                var directoryPath = Path.GetDirectoryName(filePath);

                FileStream fs = new FileStream(filePath, FileMode.Create);
                catalogDoc.Save(fs);
                fs.Dispose();

                CatalogImportExport cie = new CatalogImportExport();
                cie.ImportExportProgressMessage += ProgressHandler;

                cie.Import(directoryPath, true);

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
            catch (Exception exception)
            {
                _logger.Error("Error in ImportCatalogXmlWithHandlers", exception);
                throw;
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