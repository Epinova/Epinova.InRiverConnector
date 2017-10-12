using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Xml.Linq;
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
using inRiver.EPiServerCommerce.Importer.EventHandling;
using inRiver.EPiServerCommerce.Importer.ResourceModels;
using inRiver.EPiServerCommerce.Interfaces;
using log4net;
using Mediachase.Commerce.Assets;
using Mediachase.Commerce.Catalog;
using Mediachase.Commerce.Catalog.Dto;
using Mediachase.Commerce.Catalog.ImportExport;
using Mediachase.Commerce.Catalog.Managers;
using Mediachase.Commerce.Catalog.Objects;
using LogManager = log4net.LogManager;

namespace inRiver.EPiServerCommerce.Importer
{
    public class InriverDataImportController : SecuredApiController
    {
        private readonly ICatalogSystem _catalogSystem;
        private readonly IContentRepository _contentRepository;
        private readonly ReferenceConverter _referenceConverter;
        private readonly ICatalogImporter _catalogImporter;
        private readonly ILogger _logger;

        public InriverDataImportController(ICatalogSystem catalogSystem, 
                                           IContentRepository contentRepository, 
                                           ReferenceConverter referenceConverter, 
                                           ICatalogImporter catalogImporter,
                                           ILogger logger)
        {
            _catalogSystem = catalogSystem;
            _contentRepository = contentRepository;
            _referenceConverter = referenceConverter;
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

            Task importTask = Task.Run(
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
                           Log.Error("Catalog Import Failed", ex);
                           ImportStatusContainer.Instance.Message = "ERROR: " + ex.Message;
                       }

                       ImportStatusContainer.Instance.IsImporting = false;
                       ImportStatusContainer.Instance.Message = "Import Sucessful";
                   });

            if (importTask.Status != TaskStatus.RanToCompletion)
            {
                return "importing";
            }

            return ImportStatusContainer.Instance.Message;
        }

        [HttpPost]
        public bool ImportResources(List<InRiverImportResource> resources)
        {
            if (resources == null)
            {
                Log.DebugFormat("Received resource list that is NULL");
                return false;
            }

            List<IInRiverImportResource> resourcesImport = resources.Cast<IInRiverImportResource>().ToList();

            Log.DebugFormat("Received list of {0} resources to import", resourcesImport.Count());

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
                                            MediaData existingMediaData = _contentRepository.Get<MediaData>(EntityIdToGuid(resource.ResourceId));
                                            if (existingMediaData != null)
                                            {
                                                found = true;
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            Log.DebugFormat("Waiting ({1}/10) for resource {0} to be ready.", resource.ResourceId, count);
                                            Thread.Sleep(500);
                                        }
                                    }

                                    Log.DebugFormat("Working with resource {0} from {1} with action: {2}", resource.ResourceId, resource.Path, resource.Action);

                                    if (resource.Action == "added" || resource.Action == "updated")
                                    {
                                        ImportImageAndAttachToEntry(resource);
                                    }
                                    else if (resource.Action == "deleted")
                                    {
                                        Log.DebugFormat("Got delete action for resource id: {0}.", resource.ResourceId);
                                        HandleDelete(resource);
                                    }
                                    else if (resource.Action == "unlinked")
                                    {
                                        HandleUnlink(resource);
                                    }
                                    else
                                    {
                                        Log.DebugFormat("Got unknown action for resource id: {0}, {1}", resource.ResourceId, resource.Action);
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                ImportStatusContainer.Instance.IsImporting = false;
                                Log.Error("Resource Import Failed", exception);
                                ImportStatusContainer.Instance.Message = "ERROR: " + exception.Message;
                                return;
                            }

                            Log.DebugFormat("Imported {0} resources", resources.Count());

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
                            Log.Error("Resource Import Failed", ex);
                            ImportStatusContainer.Instance.Message = "ERROR: " + ex.Message;
                        }

                        ImportStatusContainer.Instance.Message = "Resource Import successful";
                        ImportStatusContainer.Instance.IsImporting = false;
                    });

            return importTask.Status != TaskStatus.RanToCompletion;
        }

        [HttpPost]
        public bool ImportUpdateCompleted(ImportUpdateCompletedData data)
        {
            try
            {
                if (RunIInRiverEventsHandlers)
                {
                    IEnumerable<IInRiverEventsHandler> eventsHandlers = ServiceLocator.Current.GetAllInstances<IInRiverEventsHandler>();
                    foreach (IInRiverEventsHandler handler in eventsHandlers)
                    {
                        handler.ImportUpdateCompleted(data.CatalogName, data.EventType, data.ResourcesIncluded);
                    }

                    Log.DebugFormat("*** ImportUpdateCompleted events with parameters CatalogName={0}, EventType={1}, ResourcesIncluded={2}", data.CatalogName, data.EventType, data.ResourcesIncluded);
                }

                return true;
            }
            catch (Exception exception)
            {
                Log.Error(exception);
                return false;
            }
        }

        [HttpPost]
        public bool DeleteCompleted(DeleteCompletedData data)
        {
            try
            {
                if (RunIInRiverEventsHandlers)
                {
                    IEnumerable<IInRiverEventsHandler> eventsHandlers = ServiceLocator.Current.GetAllInstances<IInRiverEventsHandler>();
                    foreach (IInRiverEventsHandler handler in eventsHandlers)
                    {
                        handler.DeleteCompleted(data.CatalogName, data.EventType);
                    }

                    Log.DebugFormat("*** DeleteCompleted events with parameters CatalogName={0}, EventType={1}", data.CatalogName, data.EventType);
                }

                return true;
            }
            catch (Exception exception)
            {
                Log.Error(exception);
                return false;
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

        private void ImportCatalogXmlFromPath(string path)
        {
            Log.Info("Starting importing the xml into EPiServer Commerce.");
            CatalogImportExport cie = new CatalogImportExport();
            cie.ImportExportProgressMessage += ProgressHandler;
            cie.Import(path, true);
            Log.Info("Done importing the xml into EPiServer Commerce.");
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
                            Log.DebugFormat("Preimport handler: {0}", handler.GetType().FullName);
                            handler.PreImport(catalogDoc);
                        }
                        catch (Exception e)
                        {
                            Log.Error("Failed to run PreImport on " + handler.GetType().FullName, e);
                        }
                    }
                }
                
                if (!File.Exists(filePath))
                {
                    Log.Error("Catalog.xml for path " + filePath + " does not exist. Importer is not able to continue with this process.");
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
                            Log.DebugFormat("Postimport handler: {0}", handler.GetType().FullName);
                            handler.PostImport(catalogDoc);
                        }
                        catch (Exception e)
                        {
                            Log.Error("Failed to run PostImport on " + handler.GetType().FullName, e);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Error in ImportCatalogXmlWithHandlers", exception);
                throw;
            }
        }

        private void ProgressHandler(object source, ImportExportEventArgs args)
        {
            Log.Debug($"{args.Message}");
        }

        private void HandleUnlink(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = _contentRepository.Get<MediaData>(EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception)
            {
                Log.DebugFormat("Didn't find resource with Resource ID: {0}, can't unlink", inriverResource.ResourceId);
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
                existingMediaData = _contentRepository.Get<MediaData>(EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception)
            {
                Log.DebugFormat("Didn't find resource with Resource ID: {0}, can't Delete", inriverResource.ResourceId);
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

        private void ImportImageAndAttachToEntry(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = _contentRepository.Get<MediaData>(EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception ex)
            {
                Log.Debug($"Didn't find resource with Resource ID: {inriverResource.ResourceId}");
            }

            try
            {
                if (existingMediaData != null)
                {
                    Log.DebugFormat("Found existing resource with Resource ID: {0}", inriverResource.ResourceId);

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
                Log.ErrorFormat("Unable to create/update metadata for Resource ID: {0}.\n{1}", inriverResource.ResourceId, exception.Message);
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
                        Log.DebugFormat("Could not find entry with code: {0}, can't create link", entryCode.Code);
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
                IAssetService assetService = ServiceLocator.Current.GetInstance<IAssetService>();

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

                assetService.CommitAssetsToNode(list, catalogNodeDto);

                // NOTE! Truncates version history
                _catalogSystem.SaveCatalogNode(catalogNodeDto);
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
                    Log.DebugFormat("Adding '{0}' as main picture on {1}", contentMedia.Name, entryCode.Code);
                    // First
                    list.Add(newAssetRow);
                    list.AddRange(catalogEntry.CatalogItemAsset.ToList());
                }
                else
                {
                    Log.DebugFormat("Adding '{0}' at end of list on  {1}", contentMedia.Name, entryCode.Code);
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
                        Log.DebugFormat("Sorting and setting '{0}' as main picture on {1}", contentMedia.Name, entryCode.Code);
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
                        Log.DebugFormat("Setting '{0}' as main picture on {1}", contentMedia.Name, entryCode.Code);

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

        private CatalogEntryDto GetCatalogEntryDto(string code)
        {
            CatalogEntryDto catalogEntry = _catalogSystem.GetCatalogEntryDto(code, new CatalogEntryResponseGroup(CatalogEntryResponseGroup.ResponseGroup.Assets));
            if (catalogEntry == null || catalogEntry.CatalogEntry.Count <= 0)
            {
                return null;
            }

            return catalogEntry;
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
            IContentTypeRepository contentTypeRepository = ServiceLocator.Current.GetInstance<IContentTypeRepository>();

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
                    contentType = contentTypeRepository.Load(type);
                    break;
                }
            }

            if (contentType == null)
            {
                contentType = contentTypeRepository.Load(typeof(InRiverGenericMedia));
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
                
                Log.ErrorFormat("Error when running HandleMetaData for resource {0} with contentType {1}: {2}", inriverResource.ResourceId, contentType.Name, exception.Message);
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

            newFile.ContentGuid = EntityIdToGuid(inriverResource.ResourceId);
            try
            {
                contentReference = _contentRepository.Save(newFile, SaveAction.Publish, AccessLevel.NoAccess);
                return newFile;
            }
            catch (Exception exception)
            {
                Log.ErrorFormat("Error when calling Save: " + exception.Message);
                contentReference = null;
                return newFile;
            }
        }

        private Guid EntityIdToGuid(int entityId)
        {
            return new Guid(string.Format("00000000-0000-0000-0000-00{0:0000000000}", entityId));
        }
    }
}
