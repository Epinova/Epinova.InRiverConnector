using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverImporter.EventHandling;
using Epinova.InRiverConnector.EpiserverImporter.ResourceModels;
using Epinova.InRiverConnector.Interfaces;
using EPiServer;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Commerce.SpecializedProperties;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Framework.Blobs;
using EPiServer.Logging;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using EPiServer.Web;
using EPiServer.Web.Internal;
using Mediachase.Commerce.Assets;
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
        private readonly IAssetService _assetService;
        private readonly ICatalogSystem _catalogSystem;
        private readonly IContentTypeRepository _contentTypeRepository;

        public CatalogImporter(ILogger logger, 
            ReferenceConverter referenceConverter, 
            IContentRepository contentRepository,
            IAssetService assetService,
            ICatalogSystem catalogSystem,
            IContentTypeRepository contentTypeRepository)
        {
            _logger = logger;
            _referenceConverter = referenceConverter;
            _contentRepository = contentRepository;
            _assetService = assetService;
            _catalogSystem = catalogSystem;
            _contentTypeRepository = contentTypeRepository;
        }

        private bool RunICatalogImportHandlers => GetBoolSetting("inRiver.RunICatalogImportHandlers");

        private bool RunIResourceImporterHandlers => GetBoolSetting("inRiver.RunIResourceImporterHandlers");

        private bool RunIDeleteActionsHandlers => GetBoolSetting("inRiver.RunIDeleteActionsHandlers");

        private bool RunIInRiverEventsHandlers => GetBoolSetting("inRiver.RunIInRiverEventsHandlers");

        private bool GetBoolSetting(string key)
        {
            var setting = ConfigurationManager.AppSettings[key];
            return setting != null && setting.Equals(key, StringComparison.CurrentCultureIgnoreCase);
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
            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in deleteHandlers)
                {
                    handler.PreDeleteCatalogEntry(entry);
                }
            }

            _contentRepository.Delete(entry.ContentLink, true);

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in deleteHandlers)
                {
                    handler.PostDeleteCatalogEntry(entry);
                }
            }
        }

        public void DeleteCatalog(int catalogId)
        {
            _logger.Debug("DeleteCatalog");
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteCatalog(catalogId);
                }
            }

            CatalogContext.Current.DeleteCatalog(catalogId);
          
            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalog(catalogId);
                }
            }
        }

        public void DeleteCatalogNode(string catalogNodeId)
        {
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();
            CatalogNode cn = CatalogContext.Current.GetCatalogNode(catalogNodeId);

            if (cn == null || cn.CatalogNodeId == 0)
            {
                _logger.Error($"Could not find catalog node with id: {catalogNodeId}. No node is deleted");
                return;
            }

            var catalogId = cn.CatalogId;
            var nodeId = cn.CatalogNodeId;

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteCatalogNode(nodeId, catalogId);
                }
            }

            CatalogContext.Current.DeleteCatalogNode(cn.CatalogNodeId, cn.CatalogId);
            
            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalogNode(nodeId, catalogId);
                }
            }
        }

        public void CheckAndMoveNodeIfNeeded(string catalogNodeId)
        {
            CatalogNodeDto nodeDto = CatalogContext.Current.GetCatalogNodeDto(catalogNodeId);
            if (nodeDto.CatalogNode.Count > 0)
            {
                // Node exists
                if (nodeDto.CatalogNode[0].ParentNodeId != 0)
                {
                    MoveNode(nodeDto.CatalogNode[0].Code, 0);
                }
            }
        }

        public void UpdateLinkEntityData(LinkEntityUpdateData linkEntityUpdateData)
        {
            int catalogId = FindCatalogByName(linkEntityUpdateData.ChannelName);

            CatalogAssociationDto associationsDto2 = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, linkEntityUpdateData.ParentEntryId);
            foreach (CatalogAssociationDto.CatalogEntryAssociationRow row in associationsDto2.CatalogEntryAssociation)
            {
                if (row.CatalogAssociationRow.AssociationDescription == linkEntityUpdateData.LinkEntityIdString)
                {
                    row.BeginEdit();
                    row.CatalogAssociationRow.AssociationName = linkEntityUpdateData.LinkEntryDisplayName;
                    row.AcceptChanges();
                }
            }

            CatalogContext.Current.SaveCatalogAssociation(associationsDto2);
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

                CatalogNode parentNode = null;
                if (nodeDto.CatalogNode[0].ParentNodeId != 0)
                {
                    parentNode = CatalogContext.Current.GetCatalogNode(nodeDto.CatalogNode[0].ParentNodeId);
                }

                if ((updateRelationData.RemoveFromChannelNodes.Contains(updateRelationData.ChannelIdEpified) && nodeDto.CatalogNode[0].ParentNodeId == 0)
                    || (parentNode != null && updateRelationData.RemoveFromChannelNodes.Contains(parentNode.ID)))
                {
                    CatalogNode associationNode = CatalogContext.Current.GetCatalogNode(updateRelationData.InRiverAssociationsEpified);

                    MoveNode(nodeDto.CatalogNode[0].Code, associationNode.CatalogNodeId);
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

        public List<string> GetLinkEntityAssociationsForEntity(GetLinkEntityAssociationsForEntityData data)
        {
            List<string> ids = new List<string>();
            int catalogId = FindCatalogByName(data.ChannelName);

            foreach (string parentId in data.ParentIds)
            {
                CatalogAssociationDto associationsDto2 = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, parentId);
                foreach (CatalogAssociationDto.CatalogEntryAssociationRow row in associationsDto2.CatalogEntryAssociation)
                {
                    if (row.AssociationTypeId == data.LinkTypeId)
                    {
                        Entry childEntry = CatalogContext.Current.GetCatalogEntry(row.CatalogEntryId);

                        if (data.TargetIds.Contains(childEntry.ID))
                        {
                            if (!ids.Contains(row.CatalogAssociationRow.AssociationDescription))
                            {
                                ids.Add(row.CatalogAssociationRow.AssociationDescription);
                            }
                        }
                    }
                }

                CatalogContext.Current.SaveCatalogAssociation(associationsDto2);
            }
            return ids;
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

                        List<ICatalogImportHandler> catalogImportHandlers = ServiceLocator.Current.GetAllInstances<ICatalogImportHandler>().ToList();
                        if (catalogImportHandlers.Any() && RunICatalogImportHandlers)
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

        public bool ImportResources(List<InRiverImportResource> resources)
        {
            if (resources == null)
            {
                _logger.Debug("Received resource list that is NULL");
                return false;
            }

            List<IInRiverImportResource> resourcesImport = resources.Cast<IInRiverImportResource>().ToList();

            _logger.Debug("Received list of {resourcesImport.Count} resources to import");

            Task importTask = Task.Run(
                () =>
                {
                    try
                    {
                        ImportStatusContainer.Instance.Message = "importing";
                        ImportStatusContainer.Instance.IsImporting = true;

                        List<IResourceImporterHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IResourceImporterHandler>().ToList();

                        if (RunIResourceImporterHandlers)
                        {
                            foreach (IResourceImporterHandler handler in importerHandlers)
                            {
                                handler.PreImport(resourcesImport);
                            }
                        }

                        try
                        {
                            foreach (IInRiverImportResource resource in resources)
                            {
                                bool found = false;
                                int count = 0;
                                while (!found && count < 10 && resource.Action != "added")
                                {
                                    count++;

                                    try
                                    {
                                        MediaData existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(resource.ResourceId));
                                        if (existingMediaData != null)
                                        {
                                            found = true;
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        _logger.Debug($"Waiting ({count}/10) for resource {resource.ResourceId} to be ready.");
                                        Thread.Sleep(500);
                                    }
                                }

                                _logger.Debug($"Working with resource {resource.ResourceId} from {resource.Path} with action: {resource.Action}");

                                if (resource.Action == "added" || resource.Action == "updated")
                                {
                                    ImportImageAndAttachToEntry(resource);
                                }
                                else if (resource.Action == "deleted")
                                {
                                    _logger.Debug($"Got delete action for resource id: {resource.ResourceId}.");
                                    HandleDelete(resource);
                                }
                                else if (resource.Action == "unlinked")
                                {
                                    HandleUnlink(resource);
                                }
                                else
                                {
                                    _logger.Debug($"Got unknown action for resource id: {resource.ResourceId}, {resource.Action}");
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            ImportStatusContainer.Instance.IsImporting = false;
                            _logger.Error("Resource Import Failed", exception);
                            ImportStatusContainer.Instance.Message = "ERROR: " + exception.Message;
                            return;
                        }

                        _logger.Debug($"Imported {resources.Count} resources");

                        if (RunIResourceImporterHandlers)
                        {
                            foreach (IResourceImporterHandler handler in importerHandlers)
                            {
                                handler.PostImport(resourcesImport);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ImportStatusContainer.Instance.IsImporting = false;
                        _logger.Error("Resource Import Failed", ex);
                        ImportStatusContainer.Instance.Message = "ERROR: " + ex.Message;
                    }

                    ImportStatusContainer.Instance.Message = "Resource Import successful";
                    ImportStatusContainer.Instance.IsImporting = false;
                });

            return importTask.Status != TaskStatus.RanToCompletion;
        }

        public bool ImportUpdateCompleted(ImportUpdateCompletedData data)
        {
            if (RunIInRiverEventsHandlers)
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
            if (RunIInRiverEventsHandlers)
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

        private void ImportCatalogXmlFromPath(string path)
        {
            _logger.Information("Starting importing the xml into EPiServer Commerce.");
            CatalogImportExport cie = new CatalogImportExport();
            cie.ImportExportProgressMessage += ProgressHandler;
            cie.Import(path, true);
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

        private void ImportImageAndAttachToEntry(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception ex)
            {
                _logger.Debug($"Didn't find resource with Resource ID: {inriverResource.ResourceId}");
            }

            try
            {
                if (existingMediaData != null)
                {
                    _logger.Debug("Found existing resource with Resource ID: {0}", inriverResource.ResourceId);

                    UpdateMetaData((IInRiverResource)existingMediaData, inriverResource);

                    if (inriverResource.Action == "added")
                    {
                        AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
                    }

                    return;
                }

                ContentReference contentReference;
                existingMediaData = CreateNewFile(out contentReference, inriverResource);

                AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
            }
            catch (Exception exception)
            {
                _logger.Error("Unable to create/update metadata for Resource ID: {0}.\n{1}", inriverResource.ResourceId, exception.Message);
            }
        }

        private void AddLinksFromMediaToCodes(MediaData contentMedia, List<EntryCode> codes)
        {
            // TODO: This way of adding media will add and save media individually. We 
            //       should add all images, and save once instead. Will improve import speed

            int sortOrder = 1;
            CommerceMedia media = new CommerceMedia(contentMedia.ContentLink, "episerver.core.icontentmedia", "default", sortOrder);

            foreach (EntryCode entryCode in codes)
            {
                // AddOrUpdateMediaOnEntry(entryCode, linkToContent, media);

                CatalogEntryDto catalogEntry = GetCatalogEntryDto(entryCode.Code);
                if (catalogEntry != null)
                {
                    AddLinkToCatalogEntry(contentMedia, media, catalogEntry, entryCode);
                }
                else
                {
                    CatalogNodeDto catalogNodeDto = GetCatalogNodeDto(entryCode.Code);
                    if (catalogNodeDto != null)
                    {
                        AddLinkToCatalogNode(contentMedia, media, catalogNodeDto, entryCode);
                    }
                    else
                    {
                        _logger.Debug($"Could not find entry with code: {entryCode.Code}, can't create link");
                    }
                }
            }
        }

        private void AddLinkToCatalogEntry(MediaData contentMedia, CommerceMedia media, CatalogEntryDto catalogEntry, EntryCode entryCode)
        {
            var newAssetRow = media.ToItemAssetRow(catalogEntry);

            var catalogItemAssetRow = catalogEntry.CatalogItemAsset.FirstOrDefault(row => row.AssetKey == newAssetRow.AssetKey);
            if (catalogItemAssetRow == null)
            {
                IAssetService assetService = ServiceLocator.Current.GetInstance<IAssetService>();

                List<CatalogEntryDto.CatalogItemAssetRow> list = new List<CatalogEntryDto.CatalogItemAssetRow>();

                if (entryCode.IsMainPicture)
                {
                    _logger.Debug($"Adding '{contentMedia.Name}' as main picture on {entryCode.Code}");
                    // First
                    list.Add(newAssetRow);
                    list.AddRange(catalogEntry.CatalogItemAsset.ToList());
                }
                else
                {
                    _logger.Debug("Adding '{contentMedia.Name}' at end of list on  {entryCode.Code}");
                    // Last
                    list.AddRange(catalogEntry.CatalogItemAsset.ToList());
                    list.Add(newAssetRow);
                }

                // Set sort order correctly (instead of having them all to 0)
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].SortOrder = i;
                }

                assetService.CommitAssetsToEntry(list, catalogEntry);

                // NOTE! Truncates version history
                _catalogSystem.SaveCatalogEntry(catalogEntry);
            }
            else
            {
                // Already in the list, check and fix sort order
                if (entryCode.IsMainPicture)
                {
                    bool needsSave = false;
                    // If more than one entry have sort order 0, we need to clean it up
                    int count = catalogEntry.CatalogItemAsset.Count(row => row.SortOrder.Equals(0));
                    if (count > 1)
                    {
                        _logger.Debug("Sorting and setting '{contentMedia.Name}' as main picture on {entryCode.Code}");

                        // Clean up
                        List<CatalogEntryDto.CatalogItemAssetRow> assetRows = catalogEntry.CatalogItemAsset.ToList();
                        // Keep existing sort order, but start at pos 1 since we will set the main picture to 0
                        for (int i = 0; i < assetRows.Count; i++)
                        {
                            assetRows[i].SortOrder = i + 1;
                        }
                        // Set the one we found to 0, which will make it main.
                        catalogItemAssetRow.SortOrder = 0;
                        needsSave = true;
                    }
                    else if (catalogItemAssetRow.SortOrder != 0)
                    {
                        // Switch order if it isn't already first
                        _logger.Debug($"Setting '{contentMedia.Name}' as main picture on {entryCode.Code}");

                        int oldOrder = catalogItemAssetRow.SortOrder;
                        catalogItemAssetRow.SortOrder = 0;
                        catalogEntry.CatalogItemAsset[0].SortOrder = oldOrder;
                        needsSave = true;
                    }
                    // else - we have it already, it isn't main picture, and sort seems ok, we won't save anything

                    if (needsSave == true)
                    {
                        // Since we're not adding or deleting anything from the list, we don't have to "CommitAssetsToEntry", just save
                        _catalogSystem.SaveCatalogEntry(catalogEntry);
                    }
                }
            }
        }


        private void AddLinkToCatalogNode(MediaData contentMedia, CommerceMedia media, CatalogNodeDto catalogNodeDto, EntryCode entryCode)
        {
            var newAssetRow = media.ToItemAssetRow(catalogNodeDto);

            if (catalogNodeDto.CatalogItemAsset.FirstOrDefault(row => row.AssetKey == newAssetRow.AssetKey) == null)
            {
                // This asset have not been added previously
                List<CatalogNodeDto.CatalogItemAssetRow> list = new List<CatalogNodeDto.CatalogItemAssetRow>();

                if (entryCode.IsMainPicture)
                {
                    // First
                    list.Add(newAssetRow);
                    list.AddRange(catalogNodeDto.CatalogItemAsset.ToList());
                }
                else
                {
                    // Last
                    list.AddRange(catalogNodeDto.CatalogItemAsset.ToList());
                    list.Add(newAssetRow);
                }

                // Set sort order correctly (instead of having them all to 0)
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].SortOrder = i;
                }

                _assetService.CommitAssetsToNode(list, catalogNodeDto);

                // NOTE! Truncates version history
                _catalogSystem.SaveCatalogNode(catalogNodeDto);
            }
        }

        private CatalogNodeDto GetCatalogNodeDto(string code)
        {
            CatalogNodeDto catalogNodeDto = _catalogSystem.GetCatalogNodeDto(code, new CatalogNodeResponseGroup(CatalogNodeResponseGroup.ResponseGroup.Assets));
            if (catalogNodeDto == null || catalogNodeDto.CatalogNode.Count <= 0)
            {
                return null;
            }

            return catalogNodeDto;
        }

        private CatalogEntryDto GetCatalogEntryDto(string code)
        {
            CatalogEntryDto catalogEntry = _catalogSystem.GetCatalogEntryDto(code, new CatalogEntryResponseGroup(CatalogEntryResponseGroup.ResponseGroup.Assets));
            if (catalogEntry == null || catalogEntry.CatalogEntry.Count <= 0)
            {
                return null;
            }

            return catalogEntry;
        }

        private void UpdateMetaData(IInRiverResource resource, IInRiverImportResource updatedResource)
        {
            MediaData editableMediaData = (MediaData)((MediaData)resource).CreateWritableClone();

            ResourceMetaField resourceFileId = updatedResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceFileId");
            if (resourceFileId != null && !string.IsNullOrEmpty(resourceFileId.Values.First().Data) && resource.ResourceFileId != int.Parse(resourceFileId.Values.First().Data))
            {
                IBlobFactory blobFactory = ServiceLocator.Current.GetInstance<IBlobFactory>();

                FileInfo fileInfo = new FileInfo(updatedResource.Path);
                if (fileInfo.Exists == false)
                {
                    throw new FileNotFoundException("File could not be imported", updatedResource.Path);
                }

                string ext = fileInfo.Extension;

                Blob blob = blobFactory.CreateBlob(editableMediaData.BinaryDataContainer, ext);
                using (Stream s = blob.OpenWrite())
                {
                    FileStream fileStream = File.OpenRead(fileInfo.FullName);
                    fileStream.CopyTo(s);
                }

                editableMediaData.BinaryData = blob;

                string rawFilename = null;
                if (updatedResource.MetaFields.Any(f => f.Id == "ResourceFilename"))
                {
                    rawFilename = updatedResource.MetaFields.First(f => f.Id == "ResourceFilename").Values[0].Data;
                }
                else if (updatedResource.MetaFields.Any(f => f.Id == "ResourceFileId"))
                {
                    rawFilename = updatedResource.MetaFields.First(f => f.Id == "ResourceFileId").Values[0].Data;
                }

                editableMediaData.RouteSegment = UrlSegment.GetUrlFriendlySegment(rawFilename);
            }

            ((IInRiverResource)editableMediaData).HandleMetaData(updatedResource.MetaFields);

            _contentRepository.Save(editableMediaData, SaveAction.Publish, AccessLevel.NoAccess);
        }

        private MediaData CreateNewFile(out ContentReference contentReference, IInRiverImportResource inriverResource)
        {
            IBlobFactory blobFactory = ServiceLocator.Current.GetInstance<IBlobFactory>();
            ContentMediaResolver mediaDataResolver = ServiceLocator.Current.GetInstance<ContentMediaResolver>();
            
            bool resourceWithoutFile = false;

            ResourceMetaField resourceFileId = inriverResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceFileId");
            if (resourceFileId == null || string.IsNullOrEmpty(resourceFileId.Values.First().Data))
            {
                resourceWithoutFile = true;
            }

            string ext;
            FileInfo fileInfo = null;
            if (resourceWithoutFile)
            {
                ext = "url";
            }
            else
            {
                fileInfo = new FileInfo(inriverResource.Path);
                if (fileInfo.Exists == false)
                {
                    throw new FileNotFoundException("File could not be imported", inriverResource.Path);
                }

                ext = fileInfo.Extension;
            }

            ContentType contentType = null;
            IEnumerable<Type> mediaTypes = mediaDataResolver.ListAllMatching(ext);

            foreach (Type type in mediaTypes)
            {
                if (type.GetInterfaces().Contains(typeof(IInRiverResource)))
                {
                    contentType = _contentTypeRepository.Load(type);
                    break;
                }
            }

            if (contentType == null)
            {
                contentType = _contentTypeRepository.Load(typeof(InRiverGenericMedia));
            }

            MediaData newFile = _contentRepository.GetDefault<MediaData>(GetInRiverResourceFolder(), contentType.ID);
            if (resourceWithoutFile)
            {
                ResourceMetaField resourceName = inriverResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceName");
                if (resourceName != null && !string.IsNullOrEmpty(resourceName.Values.First().Data))
                {
                    newFile.Name = resourceName.Values.First().Data;
                }
                else
                {
                    newFile.Name = inriverResource.ResourceId.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                newFile.Name = fileInfo.Name;
            }

            IInRiverResource resource = (IInRiverResource)newFile;

            if (resourceFileId != null && fileInfo != null)
            {
                resource.ResourceFileId = int.Parse(resourceFileId.Values.First().Data);
            }

            resource.EntityId = inriverResource.ResourceId;

            try
            {
                resource.HandleMetaData(inriverResource.MetaFields);
            }
            catch (Exception exception)
            {

                _logger.Error($"Error when running HandleMetaData for resource {inriverResource.ResourceId} with contentType {contentType.Name}: {exception.Message}");
            }

            if (!resourceWithoutFile)
            {
                Blob blob = blobFactory.CreateBlob(newFile.BinaryDataContainer, ext);
                using (Stream s = blob.OpenWrite())
                {
                    FileStream fileStream = File.OpenRead(fileInfo.FullName);
                    fileStream.CopyTo(s);
                }

                newFile.BinaryData = blob;
            }

            newFile.ContentGuid = EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId);
            try
            {
                contentReference = _contentRepository.Save(newFile, SaveAction.Publish, AccessLevel.NoAccess);
                return newFile;
            }
            catch (Exception ex)
            {
                _logger.Error("Error when calling Save", ex);
                contentReference = null;
                return newFile;
            }
        }

        /// <summary>
        /// Returns a reference to the inRiver Resource folder. It will be created if it
        /// does not already exist.
        /// </summary>
        /// <remarks>
        /// The folder structure will be: /globalassets/inRiver/Resources/...
        /// </remarks>
        protected ContentReference GetInRiverResourceFolder()
        {
            ContentReference rootInRiverFolder = ContentFolderCreator.CreateOrGetFolder(SiteDefinition.Current.GlobalAssetsRoot, "inRiver");
            ContentReference resourceRiverFolder = ContentFolderCreator.CreateOrGetFolder(rootInRiverFolder, "Resources");
            return resourceRiverFolder;
        }

        private void HandleUnlink(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception)
            {
                _logger.Debug("Didn't find resource with Resource ID: {inriverResource.ResourceId}, can't unlink");
            }

            if (existingMediaData == null)
            {
                return;
            }

            DeleteLinksBetweenMediaAndCodes(existingMediaData, inriverResource.Codes);
        }

        private void HandleDelete(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception)
            {
                _logger.Debug($"Didn't find resource with Resource ID: {inriverResource.ResourceId}, can't Delete");
            }

            if (existingMediaData == null)
            {
                return;
            }

            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteResource(inriverResource);
                }
            }

            _contentRepository.Delete(existingMediaData.ContentLink, true, AccessLevel.NoAccess);

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteResource(inriverResource);
                }
            }
        }

        private void DeleteLinksBetweenMediaAndCodes(MediaData media, IEnumerable<string> codes)
        {
            foreach (string code in codes)
            {
                var contentReference = _referenceConverter.GetContentLink(code);
                if (ContentReference.IsNullOrEmpty(contentReference))
                    continue;

                EntryContentBase catalogEntry;
                NodeContent nodeContent;
                if (_contentRepository.TryGet(contentReference, out nodeContent))
                {
                    var writableClone = nodeContent.CreateWritableClone<NodeContent>();
                    var mediaToRemove = writableClone.CommerceMediaCollection.FirstOrDefault(x => x.AssetLink.Equals(media.ContentLink));
                    writableClone.CommerceMediaCollection.Remove(mediaToRemove);
                    _contentRepository.Save(writableClone, AccessLevel.NoAccess);
                }
                else if (_contentRepository.TryGet(contentReference, out catalogEntry))
                {
                    var writableClone = nodeContent.CreateWritableClone<EntryContentBase>();
                    var mediaToRemove = writableClone.CommerceMediaCollection.FirstOrDefault(x => x.AssetLink.Equals(media.ContentLink));
                    writableClone.CommerceMediaCollection.Remove(mediaToRemove);
                    _contentRepository.Save(writableClone, AccessLevel.NoAccess);
                }
            }
        }

    }
}