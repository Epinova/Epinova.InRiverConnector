using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using Epinova.InRiverConnector.EpiserverImporter.EventHandling;
using Epinova.InRiverConnector.EpiserverImporter.ResourceModels;
using Epinova.InRiverConnector.Interfaces;
using EPiServer;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Commerce.Catalog.Linking;
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
using Mediachase.Commerce.Catalog.Managers;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class MediaImporter
    {
        private readonly ILogger _logger;
        private readonly IAssetService _assetService;
        private readonly ICatalogSystem _catalogSystem;
        private readonly IContentTypeRepository _contentTypeRepository;
        private readonly ContentFolderCreator _contentFolderCreator;
        private readonly IBlobFactory _blobFactory;
        private readonly ContentMediaResolver _contentMediaResolver;
        private readonly IContentRepository _contentRepository;
        private readonly ReferenceConverter _referenceConverter;
        private readonly Configuration _config;

        public MediaImporter(ILogger logger,
                             IAssetService assetService,
                             ICatalogSystem catalogSystem,
                             IContentTypeRepository contentTypeRepository,
                             ContentFolderCreator contentFolderCreator,
                             IBlobFactory blobFactory,
                             ContentMediaResolver contentMediaResolver,
                             IContentRepository contentRepository,
                             ReferenceConverter referenceConverter,
                             Configuration config)
        {
            _logger = logger;
            _assetService = assetService;
            _catalogSystem = catalogSystem;
            _contentTypeRepository = contentTypeRepository;
            _contentFolderCreator = contentFolderCreator;
            _blobFactory = blobFactory;
            _contentMediaResolver = contentMediaResolver;
            _contentRepository = contentRepository;
            _referenceConverter = referenceConverter;
            _config = config;
        }
        

        public void ImportResources(List<InRiverImportResource> resources)
        {
            if (resources == null || !resources.Any())
            {
                _logger.Debug("Received empty resource list.");
                return;
            }

            var importResources = resources.Cast<IInRiverImportResource>().ToList();

            _logger.Debug($"Received list of {importResources.Count} resources to import");

            ImportStatusContainer.Instance.Message = "importing";
            ImportStatusContainer.Instance.IsImporting = true;

            try
            {
                var importerHandlers = ServiceLocator.Current.GetAllInstances<IResourceImporterHandler>().ToList();

                if (_config.RunIResourceImporterHandlers)
                {
                    foreach (IResourceImporterHandler handler in importerHandlers)
                    {
                        handler.PreImport(importResources);
                    }
                }

                foreach (IInRiverImportResource resource in resources)
                {
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
                }

                _logger.Debug($"Imported/deleted/updated {resources.Count} resources");

                if (_config.RunIResourceImporterHandlers)
                {
                    foreach (IResourceImporterHandler handler in importerHandlers)
                    {
                        handler.PostImport(importResources);
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
        }

        private void ImportImageAndAttachToEntry(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            
            if (_contentRepository.TryGet(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId), out existingMediaData))
            {
                _logger.Debug("Found existing resource with Resource ID: {0}", inriverResource.ResourceId);

                UpdateMetaData((IInRiverResource) existingMediaData, inriverResource);

                if (inriverResource.Action == "added")
                {
                    AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
                }
            }
            else
            {
                existingMediaData = CreateNewFile(inriverResource);

                AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
            }
        }

        private void AddLinksFromMediaToCodes(MediaData contentMedia, List<EntryCode> codes)
        {
            int sortOrder = 1;
            CommerceMedia media = new CommerceMedia(contentMedia.ContentLink, "episerver.core.icontentmedia", "default", sortOrder);

            foreach (EntryCode entryCode in codes)
            {
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
                        AddLinkToCatalogNode(media, catalogNodeDto, entryCode);
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
                var list = new List<CatalogEntryDto.CatalogItemAssetRow>();

                if (entryCode.IsMainPicture)
                {
                    _logger.Debug($"Adding '{contentMedia.Name}' as main picture on {entryCode.Code}");
                    
                    list.Add(newAssetRow);
                    list.AddRange(catalogEntry.CatalogItemAsset.ToList());
                }
                else
                {
                    _logger.Debug($"Adding '{contentMedia.Name}' at end of list on  {entryCode.Code}");

                    list.AddRange(catalogEntry.CatalogItemAsset.ToList());
                    list.Add(newAssetRow);
                }

                // Set sort order correctly
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].SortOrder = i;
                }

                _assetService.CommitAssetsToEntry(list, catalogEntry);
                _catalogSystem.SaveCatalogEntry(catalogEntry);
            }
            else
            {
                // Already in the list. Fix sort order if needed.
                if (!entryCode.IsMainPicture)
                    return;

                bool needsSave = false;
                // If more than one entry have sort order 0, we need to clean it up
                int count = catalogEntry.CatalogItemAsset.Count(row => row.SortOrder.Equals(0));
                if (count > 1)
                {
                    _logger.Debug($"Sorting and setting '{contentMedia.Name}' as main picture on {entryCode.Code}");

                    var assetRows = catalogEntry.CatalogItemAsset.ToList();
                    
                    // Keep existing sort order, but start at pos 1 since we will set the main picture to 0
                    for (int i = 0; i < assetRows.Count; i++)
                    {
                        assetRows[i].SortOrder = i + 1;
                    }
                    
                    catalogItemAssetRow.SortOrder = 0;
                    needsSave = true;
                }
                else if (catalogItemAssetRow.SortOrder != 0)
                {
                    _logger.Debug($"Setting '{contentMedia.Name}' as main picture on {entryCode.Code}");

                    int oldOrder = catalogItemAssetRow.SortOrder;
                    catalogItemAssetRow.SortOrder = 0;
                    catalogEntry.CatalogItemAsset[0].SortOrder = oldOrder;
                    needsSave = true;
                }

                if (needsSave)
                {
                    _catalogSystem.SaveCatalogEntry(catalogEntry);
                }
            }
        }

        private void AddLinkToCatalogNode(CommerceMedia media, CatalogNodeDto catalogNodeDto, EntryCode entryCode)
        {
            var newAssetRow = media.ToItemAssetRow(catalogNodeDto);
            
            if (catalogNodeDto.CatalogItemAsset.FirstOrDefault(row => row.AssetKey == newAssetRow.AssetKey) == null)
            {
                var list = new List<CatalogNodeDto.CatalogItemAssetRow>();

                if (entryCode.IsMainPicture)
                {
                    list.Add(newAssetRow);
                    list.AddRange(catalogNodeDto.CatalogItemAsset.ToList());
                }
                else
                {
                    list.AddRange(catalogNodeDto.CatalogItemAsset.ToList());
                    list.Add(newAssetRow);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    list[i].SortOrder = i;
                }
                
                _assetService.CommitAssetsToNode(list, catalogNodeDto);
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

        private MediaData CreateNewFile(IInRiverImportResource inriverResource)
        {
            ResourceMetaField resourceFileId = inriverResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceFileId");
            if (resourceFileId == null || string.IsNullOrEmpty(resourceFileId.Values.First().Data))
            {
                return null;
            }

            var fileInfo = new FileInfo(inriverResource.Path);

            IEnumerable<Type> mediaTypes = _contentMediaResolver.ListAllMatching(fileInfo.Extension);

            _logger.Debug($"Found {mediaTypes.Count()} matching media types for extension {fileInfo.Extension}.");

            var contentTypeType = mediaTypes.FirstOrDefault(x => x.GetInterfaces().Contains(typeof(IInRiverResource))) ??
                                  _contentMediaResolver.GetFirstMatching(fileInfo.Extension);

            _logger.Debug($"Chosen content type-type is {contentTypeType.Name}.");

            var contentType = _contentTypeRepository.Load(contentTypeType);

            var newFile = _contentRepository.GetDefault<MediaData>(GetFolder(fileInfo, contentType), contentType.ID);
            newFile.Name = fileInfo.Name;
            newFile.ContentGuid = EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId);

            if (newFile is IInRiverResource resource)
            {
                resource.ResourceFileId = int.Parse(resourceFileId.Values.First().Data);
                resource.EntityId = inriverResource.ResourceId;

                try
                {
                    resource.HandleMetaData(inriverResource.MetaFields);
                }
                catch (Exception exception)
                {
                    _logger.Error($"Error when running HandleMetaData for resource {inriverResource.ResourceId} with contentType {contentType.Name}: {exception.Message}");
                }
            }

            var blob = _blobFactory.CreateBlob(newFile.BinaryDataContainer, fileInfo.Extension);
            using (var stream = blob.OpenWrite())
            {
                var fileStream = File.OpenRead(fileInfo.FullName);
                fileStream.CopyTo(stream);
            }

            newFile.BinaryData = blob;

            var contentReference = _contentRepository.Save(newFile, SaveAction.Publish, AccessLevel.NoAccess);
            _contentRepository.Get<MediaData>(contentReference);
            return newFile;
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

            if (_config.RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteResource(inriverResource);
                }
            }

            _contentRepository.Delete(existingMediaData.ContentLink, true, AccessLevel.NoAccess);

            if (_config.RunIDeleteActionsHandlers)
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

        private static readonly object LockObject = new object();
       
        /// <summary>
        /// Returns a reference to the inriver resource folder. It will be created if it does not already exist.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="contentType"></param>
        protected ContentReference GetFolder(FileInfo fileInfo, ContentType contentType)
        {
            lock(LockObject) { 
                var rootFolderName = ConfigurationManager.AppSettings["InRiverPimConnector.ResourceFolderName"];
                var rootFolder = _contentFolderCreator.CreateOrGetFolder(SiteDefinition.Current.GlobalAssetsRoot, rootFolderName ?? "ImportedResources");

                var firstLevelFolderName = fileInfo.Name[0].ToString().ToUpper();
                var firstLevelFolder = _contentFolderCreator.CreateOrGetFolder(rootFolder, firstLevelFolderName);

                var secondLevelFolderName = contentType.Name.Replace("File", "");
                return _contentFolderCreator.CreateOrGetFolder(firstLevelFolder, secondLevelFolderName);
            }
        }
    }
}