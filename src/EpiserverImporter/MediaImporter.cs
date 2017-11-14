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
using EPiServer.Commerce.SpecializedProperties;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Framework.Blobs;
using EPiServer.Logging;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using EPiServer.Web;
using Mediachase.Commerce.Catalog;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class MediaImporter
    {
        private readonly ILogger _logger;
        private readonly IContentTypeRepository _contentTypeRepository;
        private readonly ContentFolderCreator _contentFolderCreator;
        private readonly IBlobFactory _blobFactory;
        private readonly ContentMediaResolver _contentMediaResolver;
        private readonly IContentRepository _contentRepository;
        private readonly ReferenceConverter _referenceConverter;
        private readonly Configuration _config;
        private readonly IUrlSegmentGenerator _urlSegmentGenerator;

        public MediaImporter(ILogger logger,
                             IContentTypeRepository contentTypeRepository,
                             ContentFolderCreator contentFolderCreator,
                             IBlobFactory blobFactory,
                             ContentMediaResolver contentMediaResolver,
                             IContentRepository contentRepository,
                             ReferenceConverter referenceConverter,
                             Configuration config,
                             IUrlSegmentGenerator urlSegmentGenerator)
        {
            _logger = logger;
            _contentTypeRepository = contentTypeRepository;
            _contentFolderCreator = contentFolderCreator;
            _blobFactory = blobFactory;
            _contentMediaResolver = contentMediaResolver;
            _contentRepository = contentRepository;
            _referenceConverter = referenceConverter;
            _config = config;
            _urlSegmentGenerator = urlSegmentGenerator;
        }
        

        public void ImportResources(List<InRiverImportResource> resources)
        {
            if (resources == null || !resources.Any())
            {
                return;
            }

            var importResources = resources.Cast<IInRiverImportResource>().ToList();
            
            ImportStatusContainer.Instance.Message = "importing";
            ImportStatusContainer.Instance.IsImporting = true;

            try
            {
                var importerHandlers = ServiceLocator.Current.GetAllInstances<IResourceImporterHandler>().ToList();

                if (_config.RunResourceImporterHandlers)
                {
                    foreach (IResourceImporterHandler handler in importerHandlers)
                    {
                        handler.PreImport(importResources);
                    }
                }

                foreach (IInRiverImportResource resource in resources)
                {
                    if (resource.Action == ImporterActions.Added || resource.Action == ImporterActions.Updated)
                    {
                        ImportImageAndAttachToEntry(resource);
                    }
                    else if (resource.Action == ImporterActions.Deleted)
                    {
                        _logger.Debug($"Got delete action for resource id: {resource.ResourceId}.");
                        HandleDelete(resource);
                    }
                    else if (resource.Action == ImporterActions.Unlinked)
                    {
                        HandleUnlink(resource);
                    }
                }

                _logger.Debug($"Imported/deleted/updated {resources.Count} resources");

                if (_config.RunResourceImporterHandlers)
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

        public void DeleteResource(DeleteResourceRequest request)
        {
            var mediaData = _contentRepository.Get<MediaData>(request.ResourceGuid);
            var references = _contentRepository.GetReferencesToContent(mediaData.ContentLink, false).ToList();

            if (request.EntryToRemoveFrom == null)
            {
                _logger.Debug($"Deleting resource with GUID {request.ResourceGuid}");
                _logger.Debug($"Found {references.Count} references to mediacontent.");

                foreach (var reference in references)
                {
                    var code = _referenceConverter.GetCode(reference.OwnerID);
                    DeleteMediaLink(mediaData, code);
                }
                _contentRepository.Delete(mediaData.ContentLink, true, AccessLevel.NoAccess);
            }
            else
            {
                foreach (var reference in references)
                {
                    if (_contentRepository.TryGet(reference.OwnerID, out EntryContentBase content))
                    {
                        if (content.Code != request.EntryToRemoveFrom)
                            continue;
                    }
                    _logger.Debug($"Removing resource {request.ResourceGuid} from entry with code {content.Code}.");

                    DeleteMediaLink(mediaData, content.Code);
                }
            }
        }

        private void ImportImageAndAttachToEntry(IInRiverImportResource inriverResource)
        {
            var mediaGuid = EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId);
            if (_contentRepository.TryGet(mediaGuid, out MediaData existingMediaData))
            {
                _logger.Debug($"Found existing resource with Resource ID: {inriverResource.ResourceId}");

                UpdateMetaData((IInRiverResource) existingMediaData, inriverResource);

                if (inriverResource.Action == ImporterActions.Added)
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
            var media = new CommerceMedia { AssetLink = contentMedia.ContentLink };
            
            foreach (EntryCode entryCode in codes)
            {
                var contentReference = _referenceConverter.GetContentLink(entryCode.Code);
                
                IAssetContainer writableContent = null;
                if (_contentRepository.TryGet(contentReference, out EntryContentBase entry))
                    writableContent = (EntryContentBase) entry.CreateWritableClone();

                if (_contentRepository.TryGet(contentReference, out NodeContent node))
                    writableContent = (NodeContent)node.CreateWritableClone();

                if (writableContent == null)
                {
                    _logger.Error($"Can't get a suitable content (with code {entryCode.Code} to add CommerceMedia to, meaning it's neither EntryContentBase nor NodeContent.");
                    continue;
                }

                var existingMedia = writableContent.CommerceMediaCollection.FirstOrDefault(x => x.AssetLink.Equals(media.AssetLink));
                if (existingMedia != null)
                    writableContent.CommerceMediaCollection.Remove(existingMedia);

                if (entryCode.IsMainPicture)
                {
                    _logger.Debug($"Setting '{contentMedia.Name}' as main media on {entryCode.Code}");
                    writableContent.CommerceMediaCollection.Insert(0, media);
                }
                else
                {
                    _logger.Debug($"Adding '{contentMedia.Name}' as media on {entryCode.Code}");
                    writableContent.CommerceMediaCollection.Add(media);
                }
                
                _contentRepository.Save((IContent) writableContent);
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

                editableMediaData.RouteSegment = _urlSegmentGenerator.Create(rawFilename);
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
            var existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId));

            DeleteLinksBetweenMediaAndCodes(existingMediaData, inriverResource.Codes);
        }

        private void HandleDelete(IInRiverImportResource inriverResource)
        {
            var existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId));

            var importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (_config.RunDeleteActionsHandlers)
            {
                foreach (var handler in importerHandlers)
                {
                    handler.PreDeleteResource(inriverResource);
                }
            }

            _contentRepository.Delete(existingMediaData.ContentLink, true, AccessLevel.NoAccess);

            if (_config.RunDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteResource(inriverResource);
                }
            }
        }

        private void DeleteLinksBetweenMediaAndCodes(MediaData media, IEnumerable<string> codes)
        {
            foreach (var code in codes)
            {
                DeleteMediaLink(media, code);
            }
        }

        /// <param name="media">The media to remove as link</param>
        /// <param name="code">The code of the catalog content from which the <paramref name="media"/> should be removed.</param>
        private void DeleteMediaLink(MediaData media, string code)
        {
            var contentReference = _referenceConverter.GetContentLink(code);
            if (ContentReference.IsNullOrEmpty(contentReference))
                return;

            IAssetContainer writableContent = null;
            if (_contentRepository.TryGet(contentReference, out NodeContent nodeContent))
            {
                writableContent = nodeContent.CreateWritableClone<NodeContent>();
            }
            else if (_contentRepository.TryGet(contentReference, out EntryContentBase catalogEntry))
            {
                writableContent = catalogEntry.CreateWritableClone<EntryContentBase>();
            }

            var writableMediaCollection = writableContent.CommerceMediaCollection.CreateWritableClone();
            var mediaToRemove = writableMediaCollection.FirstOrDefault(x => x.AssetLink.Equals(media.ContentLink));
            if (mediaToRemove == null)
                return;

            writableMediaCollection.Remove(mediaToRemove);
            _contentRepository.Save((IContent) writableContent, AccessLevel.NoAccess);
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