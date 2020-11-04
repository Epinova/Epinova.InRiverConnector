using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Epinova.InRiverConnector.EpiserverImporter.EventHandling;
using Epinova.InRiverConnector.EpiserverImporter.ResourceModels;
using Epinova.InRiverConnector.Interfaces;
using Epinova.InRiverConnector.Interfaces.Poco;
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
using EntryCode = Epinova.InRiverConnector.Interfaces.EntryCode;
using Type = System.Type;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class MediaImporter
    {
        private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
        private readonly IBlobFactory _blobFactory;
        private readonly Configuration _config;
        private readonly ContentFolderCreator _contentFolderCreator;
        private readonly ContentMediaResolver _contentMediaResolver;
        private readonly IContentRepository _contentRepository;
        private readonly IContentTypeRepository _contentTypeRepository;
        private readonly ILogger _logger;
        private readonly ReferenceConverter _referenceConverter;

        public MediaImporter(ILogger logger,
            IContentTypeRepository contentTypeRepository,
            ContentFolderCreator contentFolderCreator,
            IBlobFactory blobFactory,
            ContentMediaResolver contentMediaResolver,
            IContentRepository contentRepository,
            ReferenceConverter referenceConverter,
            Configuration config)
        {
            _logger = logger;
            _contentTypeRepository = contentTypeRepository;
            _contentFolderCreator = contentFolderCreator;
            _blobFactory = blobFactory;
            _contentMediaResolver = contentMediaResolver;
            _contentRepository = contentRepository;
            _referenceConverter = referenceConverter;
            _config = config;
        }

        public void DeleteResource(DeleteResourceRequest request)
        {
            if (!_contentRepository.TryGet(request.ResourceGuid, out MediaData mediaData))
            {
                _logger.Information($"Deleting resource with Guid {request.ResourceGuid}. Resource not found.");
                return;
            }

            List<ReferenceInformation> references =
                _contentRepository.GetReferencesToContent(mediaData.ContentLink, false).ToList();

            if (request.EntryToRemoveFrom == null)
            {
                _logger.Debug($"Deleting resource with GUID {request.ResourceGuid}");
                _logger.Debug($"Found {references.Count} references to mediacontent.");

                foreach (string code in references
                    .Select(reference => _referenceConverter.GetCode(reference.OwnerID)))
                {
                    DeleteMediaLink(mediaData, code);
                }

                _contentRepository.Delete(mediaData.ContentLink, true, AccessLevel.NoAccess);
            }
            else
            {
                foreach (ReferenceInformation reference in references)
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


        public void ImportResources(ImportResourcesRequest request)
        {
            List<InRiverImportResource> resources = DeserializeRequest(request);
            if (!resources.Any())
                return;

            _logger.Debug($"Starting import of {resources.Count} resources.");

            try
            {
                List<IResourceImporterHandler> importerHandlers =
                    ServiceLocator.Current.GetAllInstances<IResourceImporterHandler>().ToList();

                if (_config.RunResourceImporterHandlers)
                {
                    foreach (IResourceImporterHandler handler in importerHandlers)
                    {
                        handler.PreImport(resources);
                    }
                }

                int errors = 0;

                // TODO: Degree of parallelism should be configurable, default to 2.
                Parallel.ForEach(resources, new ParallelOptions { MaxDegreeOfParallelism = _config.DegreesOfParallelism }, resource =>
                {
                    try
                    {
                        ImportResource(resource);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.Error($"Importing resource failed: action: {resource.Action} - id: {resource.ResourceId} - path: {resource.Path} - ", ex);
                    }
                });

                _logger.Information($"Imported/deleted/updated {resources.Count} resources. {errors} errors.");

                if (_config.RunResourceImporterHandlers)
                {
                    foreach (IResourceImporterHandler handler in importerHandlers)
                    {
                        handler.PostImport(resources);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Resource Import Failed", ex);
            }
        }

        /// <summary>
        /// Returns a reference to the inriver resource folder. It will be created if it does not already exist.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="contentType"></param>
        protected ContentReference GetFolder(FileInfo fileInfo, ContentType contentType)
        {
            return ExecuteWithinLock(() =>
            {
                string rootFolderName = ConfigurationManager.AppSettings["InRiverConnector.ResourceFolderName"];
                ContentReference rootFolder =
                    _contentFolderCreator.CreateOrGetFolder(SiteDefinition.Current.GlobalAssetsRoot, rootFolderName ?? "ImportedResources");

                string firstLevelFolderName = fileInfo.Name[0].ToString().ToUpper();
                ContentReference firstLevelFolder = _contentFolderCreator.CreateOrGetFolder(rootFolder, firstLevelFolderName);

                string secondLevelFolderName = contentType.Name.Replace("File", "");
                return _contentFolderCreator.CreateOrGetFolder(firstLevelFolder, secondLevelFolderName);
            });
        }

        private void AddLinksFromMediaToCodes(MediaData contentMedia, IEnumerable<EntryCode> codes)
        {
            var media = new CommerceMedia { AssetLink = contentMedia.ContentLink, GroupName = "default", AssetType = "episerver.core.icontentmedia" };

            foreach (EntryCode entryCode in codes)
            {
                var contentReference = _referenceConverter.GetContentLink(entryCode.Code);

                IAssetContainer writableContent = null;
                if (_contentRepository.TryGet(contentReference, out EntryContentBase entry))
                    writableContent = (EntryContentBase)entry.CreateWritableClone();

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

                //if image been delete the media is linked with status "Media not found" in UI - then need to be removed, else it will throw System.ComponentModel.DataAnnotations.ValidationException: Media is not found. Navigate to Assets tab and remove it in order to publish. at EPiServer.Core.ContentProvider.ThrowValidationException(ICollection`1 errors) at EPiServer.Core.Internal.DefaultContentRepository.Save(IContent content, SaveAction action, AccessLevel access)
                var unlinkedAssets = writableContent.CommerceMediaCollection.Where(x => ContentReference.IsNullOrEmpty(x.AssetLink)).ToList();
                foreach (var asset in unlinkedAssets)
                {
                    writableContent.CommerceMediaCollection.Remove(asset);
                }

                if (entryCode.IsMainPicture)
                {
                    _logger.Debug($"Setting '{contentMedia.Name}' as main media on {entryCode.Code}");
                    media.SortOrder = 0;
                    writableContent.CommerceMediaCollection.Insert(0, media);
                }
                else
                {
                    _logger.Debug($"Adding '{contentMedia.Name}' as media on {entryCode.Code}");
                    media.SortOrder = 1;
                    writableContent.CommerceMediaCollection.Add(media);
                }

                try
                {
                    _contentRepository.Save((IContent)writableContent, SaveAction.Publish | SaveAction.SkipValidation, AccessLevel.NoAccess);
                }
                catch (Exception e)
                {
                    _logger.Error($"Something went wrong connecting assets code: {entryCode.Code} cr: {contentReference.ID}, media: {media.AssetLink.ID} {contentMedia.Name}. Continuing... ", e);
                    continue;
                }
            }
        }

        private MediaData CreateNewFile(InRiverImportResource inriverResource)
        {
            ResourceMetaField resourceFileId = inriverResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceFileId");
            if (String.IsNullOrEmpty(resourceFileId?.Values.FirstOrDefault()?.Data))
            {
                _logger.Debug("ResourceFileId is null, won't do stuff.");
                return null;
            }

            _logger.Debug($"Attempting to create and import file from path: {inriverResource.Path}");

            var fileInfo = new FileInfo(inriverResource.Path);

            IEnumerable<Type> mediaTypes = _contentMediaResolver.ListAllMatching(fileInfo.Extension).ToList();

            _logger.Debug($"Found {mediaTypes.Count()} matching media types for extension {fileInfo.Extension}.");

            Type contentTypeType = mediaTypes.FirstOrDefault(x => x.GetInterfaces().Contains(typeof(IInRiverResource))) ??
                                   _contentMediaResolver.GetFirstMatching(fileInfo.Extension);

            if (contentTypeType == null)
                _logger.Warning($"Can't find suitable content type when trying to import {inriverResource.Path}");

            else
                _logger.Debug($"Chosen content type-type is {contentTypeType.Name}.");

            ContentType contentType = _contentTypeRepository.Load(contentTypeType);

            var newFile = _contentRepository.GetDefault<MediaData>(GetFolder(fileInfo, contentType), contentType.ID);
            newFile.Name = fileInfo.Name;
            newFile.ContentGuid = EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId);

            // ReSharper disable once SuspiciousTypeConversion.Global
            if (newFile is IInRiverResource resource)
            {
                resource.ResourceFileId = Int32.Parse(resourceFileId.Values.First().Data);
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

            Blob blob = _blobFactory.CreateBlob(newFile.BinaryDataContainer, fileInfo.Extension);
            using (Stream stream = blob.OpenWrite())
            {
                FileStream fileStream = File.OpenRead(fileInfo.FullName);
                fileStream.CopyTo(stream);
                fileStream.Dispose();
            }

            newFile.BinaryData = blob;

            _logger.Debug($"New mediadata is ready to be saved: {newFile.Name}, from path {inriverResource.Path}");

            ContentReference contentReference = _contentRepository.Save(newFile, SaveAction.Publish | SaveAction.SkipValidation, AccessLevel.NoAccess);
            var mediaData = _contentRepository.Get<MediaData>(contentReference);

            _logger.Debug($"Saved file {fileInfo.Name} with Content ID {contentReference?.ID}.");

            return mediaData;
        }

        private void DeleteLinksBetweenMediaAndCodes(MediaData media, IEnumerable<string> codes)
        {
            foreach (string code in codes)
            {
                DeleteMediaLink(media, code);
            }
        }

        /// <param name="media">The media to remove as link</param>
        /// <param name="code">The code of the catalog content from which the <paramref name="media"/> should be removed.</param>
        private void DeleteMediaLink(MediaData media, string code)
        {
            ContentReference contentReference = _referenceConverter.GetContentLink(code);
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

            writableContent?.CommerceMediaCollection.CreateWritableClone();
            CommerceMedia mediaToRemove = writableContent?.CommerceMediaCollection?.FirstOrDefault(x => x.AssetLink.Equals(media.ContentLink));
            if (mediaToRemove == null)
                return;

            writableContent.CommerceMediaCollection.Remove(mediaToRemove);
            _contentRepository.Save((IContent)writableContent, SaveAction.Publish | SaveAction.SkipValidation, AccessLevel.NoAccess);
        }

        private List<InRiverImportResource> DeserializeRequest(ImportResourcesRequest request)
        {
            _logger.Debug($"Deserializing and preparing {request.ResourceXmlPath} for import. basepath is: {request.BasePath}");

            var serializer = new XmlSerializer(typeof(Resources));
            Resources resources;
            using (XmlReader reader = XmlReader.Create(request.ResourceXmlPath))
            {
                resources = (Resources)serializer.Deserialize(reader);
            }

            var resourcesForImport = new List<InRiverImportResource>();
            foreach (Resource resource in resources.ResourceFiles.Resource)
            {
                var newRes = new InRiverImportResource
                {
                    Action = resource.action
                };

                if (resource.ParentEntries?.EntryCode != null)
                {
                    foreach (Interfaces.Poco.EntryCode entryCode in resource.ParentEntries.EntryCode)
                    {
                        if (String.IsNullOrEmpty(entryCode.Value))
                            continue;

                        newRes.Codes.Add(entryCode.Value);
                        newRes.EntryCodes.Add(new EntryCode
                        {
                            Code = entryCode.Value,
                            IsMainPicture = entryCode.IsMainPicture
                        });
                    }
                }

                if (resource.action != ImporterActions.Deleted)
                {
                    newRes.MetaFields = GenerateMetaFields(resource);

                    // path is ".\some file.ext"
                    if (resource.Paths?.Path != null)
                    {
                        string filePath = resource.Paths.Path.Value.Remove(0, 1);
                        filePath = filePath.Replace("/", "\\");
                        newRes.Path = request.BasePath + filePath;
                    }
                }

                newRes.ResourceId = resource.id;
                resourcesForImport.Add(newRes);
            }

            return resourcesForImport;
        }

        private T ExecuteWithinLock<T>(Func<T> action, string errorString = null)
        {
            Semaphore.Wait();
            try
            {
                return action();
            }
            catch (Exception exception)
            {
                _logger.Error(errorString, exception);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        private List<ResourceMetaField> GenerateMetaFields(Resource resource)
        {
            var metaFields = new List<ResourceMetaField>();
            if (resource.ResourceFields == null)
                return metaFields;

            foreach (MetaField metaField in resource.ResourceFields.MetaField)
            {
                var resourceMetaField = new ResourceMetaField { Id = metaField.Name.Value };
                var values = new List<Value>();

                foreach (Data data in metaField.Data)
                {
                    var value = new Value { Languagecode = data.language };
                    if (data.Item != null && data.Item.Count > 0)
                    {
                        foreach (Item item in data.Item)
                        {
                            value.Data += item.value + ";";
                        }

                        int lastIndexOf = value.Data.LastIndexOf(';');
                        if (lastIndexOf != -1)
                        {
                            value.Data = value.Data.Remove(lastIndexOf);
                        }
                    }
                    else
                    {
                        value.Data = data.value;
                    }

                    values.Add(value);
                }

                resourceMetaField.Values = values;

                metaFields.Add(resourceMetaField);
            }

            return metaFields;
        }

        private void HandleDelete(InRiverImportResource inriverResource)
        {
            var existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId));

            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (_config.RunDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
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

        private void HandleUnlink(InRiverImportResource inriverResource)
        {
            var existingMediaData = _contentRepository.Get<MediaData>(EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId));

            DeleteLinksBetweenMediaAndCodes(existingMediaData, inriverResource.Codes);
        }

        private void ImportImageAndAttachToEntry(InRiverImportResource inriverResource)
        {
            Guid mediaGuid = EpiserverEntryIdentifier.EntityIdToGuid(inriverResource.ResourceId);
            if (_contentRepository.TryGet(mediaGuid, out MediaData existingMediaData))
            {
                _logger.Debug($"Found existing resource with Resource ID: {inriverResource.ResourceId}");

                // ReSharper disable once SuspiciousTypeConversion.Global
                UpdateMetaData((IInRiverResource)existingMediaData, inriverResource);

                if (inriverResource.Action == ImporterActions.Added)
                {
                    AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
                }
            }
            else
            {
                existingMediaData = CreateNewFile(inriverResource);
                if (existingMediaData == null)
                    return;

                AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
            }
        }


        private void ImportResource(InRiverImportResource resource)
        {
            if (resource.Action == ImporterActions.Added || resource.Action == ImporterActions.Updated)
            {
                ImportImageAndAttachToEntry(resource);
            }
            else if (resource.Action == ImporterActions.Deleted)
            {
                _logger.Debug($"Got delete action for resource id: {resource.ResourceId}.");
                HandleUnlink(resource);//always unlink before delete
                HandleDelete(resource);
            }
            else if (resource.Action == ImporterActions.Unlinked)
            {
                HandleUnlink(resource);
            }
        }

        private void UpdateMetaData(IInRiverResource resource, InRiverImportResource updatedResource)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            var editableMediaData = (MediaData)((MediaData)resource).CreateWritableClone();

            ResourceMetaField resourceFileId = updatedResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceFileId");
            if (resourceFileId != null && !String.IsNullOrEmpty(resourceFileId.Values.First().Data) && resource.ResourceFileId != Int32.Parse(resourceFileId.Values.First().Data))
            {
                var blobFactory = ServiceLocator.Current.GetInstance<IBlobFactory>();

                var fileInfo = new FileInfo(updatedResource.Path);
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
                    fileStream.Dispose();
                }

                editableMediaData.BinaryData = blob;
                editableMediaData.Name = fileInfo.Name;
            }

            // ReSharper disable once SuspiciousTypeConversion.Global
            ((IInRiverResource)editableMediaData).HandleMetaData(updatedResource.MetaFields);

            _contentRepository.Save(editableMediaData, SaveAction.Publish, AccessLevel.NoAccess);
        }
    }
}
