using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.Interfaces;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.XmlFactories
{
    public class ResourceElementFactory
    {
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly CatalogElementFactory _catalogElementFactory;
        private readonly IConfiguration _config;
        private readonly IEntityService _entityService;
        private readonly EpiMappingHelper _mappingHelper;

        private Dictionary<string, bool> _isImageCache;

        public ResourceElementFactory(CatalogElementFactory catalogElementFactory,
            EpiMappingHelper mappingHelper,
            CatalogCodeGenerator catalogCodeGenerator,
            IConfiguration config,
            IEntityService entityService)
        {
            _catalogElementFactory = catalogElementFactory;
            _mappingHelper = mappingHelper;
            _catalogCodeGenerator = catalogCodeGenerator;
            _config = config;
            _entityService = entityService;

            _isImageCache = new Dictionary<string, bool>();
        }

        public XElement CreateResourceElement(Entity resource, string action)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Creating resource element for resource ID {resource.Id} resource entities found.");

            var parents = new Dictionary<string, int?>();

            List<StructureEntity> allResourceLocations = _entityService.GetAllResourceLocations(resource.Id);

            var links = new List<Link>();

            foreach (Link inboundLink in resource.InboundLinks)
            {
                if (allResourceLocations.Any(i => i.ParentId == inboundLink.Source.Id))
                {
                    links.Add(inboundLink);
                }
            }

            foreach (Link link in links)
            {
                Entity linkedEntity = link.Source;

                var ids = new List<string> { _catalogCodeGenerator.GetEpiserverCode(linkedEntity) };

                if (_config.UseThreeLevelsInCommerce)
                {
                    ids.Add(_catalogCodeGenerator.GetEpiserverCode(linkedEntity));
                }

                if (_config.ItemsToSkus && linkedEntity.EntityType.Id == "Item")
                {
                    List<string> skuIds = _catalogElementFactory.SkuItemIds(linkedEntity);
                    foreach (string skuId in skuIds)
                    {
                        string prefixedSkuId = _catalogCodeGenerator.GetPrefixedCode(skuId);
                        ids.Add(prefixedSkuId);
                    }
                }

                foreach (string id in ids)
                {
                    if (!parents.ContainsKey(id))
                    {
                        parents.Add(id, linkedEntity.MainPictureId);
                    }
                }
            }

            string resourceId = _catalogCodeGenerator.GetEpiserverCode(resource);

            string resourceFileId = null;
            Field resourceFileIdField = resource.GetField(FieldNames.ResourceFileId);
            if (resourceFileIdField != null && !resourceFileIdField.IsEmpty())
            {
                resourceFileId = resource.GetField(FieldNames.ResourceFileId).Data.ToString();
            }

            IEnumerable<XElement> metaFields = resource.Fields.Where(field => !_mappingHelper.SkipField(field.FieldType))
                .Select(field => _catalogElementFactory.GetMetaFieldValueElement(field));

            IEnumerable<XElement> parentEntries = parents.Select(parent => new XElement("EntryCode", parent.Key,
                new XAttribute("IsMainPicture", IsMainPicture(parent, resourceFileId))));

            return new XElement("Resource",
                new XAttribute("id", resourceId),
                new XAttribute("action", action),
                new XElement("ResourceFields", metaFields),
                GetInternalPaths(resource),
                new XElement("ParentEntries", parentEntries));
        }

        public void FlushCache()
        {
            _isImageCache = new Dictionary<string, bool>();
        }

        public XDocument GetResourcesNodeForChannelEntities(List<StructureEntity> channelEntities, string resourcesBasePath)
        {
            var resourceDocument = new XDocument();
            try
            {
                if (!Directory.Exists(_config.ResourcesRootPath))
                {
                    Directory.CreateDirectory(_config.ResourcesRootPath);
                }

                var resourceIds = new List<int>();
                foreach (StructureEntity structureEntity in channelEntities)
                {
                    if (structureEntity.Type == "Resource" && !resourceIds.Contains(structureEntity.EntityId))
                    {
                        resourceIds.Add(structureEntity.EntityId);
                    }
                }

                List<Entity> resources = RemoteManager.DataService.GetEntities(resourceIds, LoadLevel.DataAndLinks);
                foreach (Entity res in resources)
                {
                    SaveFileToDisk(res, resourcesBasePath);
                }

                resourceDocument = CreateResourceDocument(resources, ImporterActions.Added, true);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Could not add resources", ex);
            }

            return resourceDocument;
        }

        internal XDocument CreateResourceDocument(List<Entity> resources, string action, bool addMetadata)
        {
            XElement resourceMetaClasses = null;
            if (addMetadata)
            {
                EntityType reourceType = resources.Count > 0 ? resources[0].EntityType : RemoteManager.ModelService.GetEntityType("Resource");
                resourceMetaClasses = _catalogElementFactory.CreateResourceMetaFieldsElement(reourceType);
            }

            return new XDocument(new XElement("Resources",
                resourceMetaClasses,
                new XElement("ResourceFiles",
                    resources.Select(res => CreateResourceElement(res, action)))));
        }

        internal XElement GetInternalPaths(Entity resource)
        {
            int id = GetResourceFileId(resource);

            var paths = new XElement("Paths");

            if (id < 0)
            {
                return paths;
            }

            foreach (string displayConfiguration in GetDisplayConfigurations(resource))
            {
                string fileName = GetResourceFileName(resource, id, displayConfiguration);
                string folder = GetFolderName(displayConfiguration, resource);
                paths.Add(new XElement("Path", $"./{folder}/{fileName}"));
            }

            return paths;
        }

        internal XDocument HandleResourceUpdate(Entity updatedResource, string folderDateTime)
        {
            string folder = Path.Combine(_config.ResourcesRootPath, folderDateTime);
            SaveFileToDisk(updatedResource, folder);
            return CreateResourceDocument(new List<Entity> { updatedResource }, ImporterActions.Updated, false);
        }

        internal bool SaveFileToDisk(Entity resource, string resourcesBasePath)
        {
            try
            {
                int resourceFileId = GetResourceFileId(resource);
                if (resourceFileId < 0)
                {
                    IntegrationLogger.Write(LogLevel.Information, $"Resource with id:{resource.Id} has no value for ResourceFileId");
                    return false;
                }

                foreach (string displayConfiguration in GetDisplayConfigurations(resource))
                {
                    byte[] resourceData = RemoteManager.UtilityService.GetFile(resourceFileId, displayConfiguration);
                    if (resourceData == null)
                    {
                        IntegrationLogger.Write(LogLevel.Error, $"Resource with id:{resource.Id} and ResourceFileId: {resourceFileId} could not get file");
                        return false;
                    }

                    string fileName = GetResourceFileName(resource, resourceFileId, displayConfiguration);

                    string folder = GetFolderName(displayConfiguration, resource);
                    string dir = Path.Combine(resourcesBasePath, folder);

                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string fullFilePath = Path.Combine(dir, fileName);

                    File.WriteAllBytes(fullFilePath, resourceData);
                    IntegrationLogger.Write(LogLevel.Debug, $"Saving Resource {resource.Id} to {fullFilePath}.");
                }
            }
            catch (Exception ex)
            {
                if (resource != null)
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Could not save resource! id:{resource.Id}, ResourceFileId:{resource.GetField("ResourceFileId")}", ex);
                }

                return false;
            }

            return true;
        }

        private static bool IsMainPicture(KeyValuePair<string, int?> parent, string resourceFileId)
        {
            return parent.Value != null && parent.Value.ToString().Equals(resourceFileId);
        }

        private IEnumerable<string> GetDisplayConfigurations(Entity resource)
        {
            if (IsImage(resource))
            {
                return _config.ResourceConfigurations;
            }

            IntegrationLogger.Write(LogLevel.Debug, $"No image configuration found for Resource {resource.Id}. Original will be used");
            return new[] { Configuration.OriginalDisplayConfiguration };
        }

        private string GetFolderName(string displayConfiguration, Entity resource)
        {
            if (!String.IsNullOrEmpty(displayConfiguration) && IsImage(resource))
            {
                return displayConfiguration;
            }

            Field mimeTypeField = resource.GetField(FieldNames.ResourceMimeType);
            if (mimeTypeField != null && !mimeTypeField.IsEmpty() && mimeTypeField.Data.ToString().Contains('/'))
            {
                return mimeTypeField.Data.ToString().Split('/')[1];
            }

            return displayConfiguration;
        }

        private int GetResourceFileId(Entity resource)
        {
            Field resourceFileIdField = resource.GetField("ResourceFileId");
            if (resourceFileIdField == null || resourceFileIdField.IsEmpty())
            {
                return -1;
            }

            return (int) resourceFileIdField.Data;
        }

        private string GetResourceFileName(Entity resource, int resourceFileId, string displayConfiguration)
        {
            Field resourceFileNameField = resource.GetField("ResourceFilename");
            string fileName = $"[{resourceFileId}].jpg";
            if (resourceFileNameField != null && !resourceFileNameField.IsEmpty())
            {
                string fileType = Path.GetExtension(resourceFileNameField.Data.ToString());
                if (displayConfiguration != Configuration.OriginalDisplayConfiguration)
                {
                    string extension = String.Empty;
                    if (_config.ResourceConfiugurationExtensions.ContainsKey(displayConfiguration))
                    {
                        extension = _config.ResourceConfiugurationExtensions[displayConfiguration];
                    }

                    if (String.IsNullOrEmpty(extension))
                    {
                        fileType = ".jpg";
                    }
                    else
                    {
                        fileType = "." + extension;
                    }
                }

                fileName = Path.GetFileNameWithoutExtension(resourceFileNameField.Data.ToString());
                fileName = $"{fileName}{fileType}";
            }

            return fileName;
        }

        private bool IsImage(Entity resource)
        {
            string fileEnding = resource.GetField("ResourceFilename")?.Data?.ToString().Split('.').LastOrDefault();

            if (String.IsNullOrWhiteSpace(fileEnding))
                return false;

            if (_isImageCache.ContainsKey(fileEnding))
                return _isImageCache[fileEnding];

            List<ImageServiceConfiguration> imageServiceConfigs = RemoteManager.UtilityService.GetAllImageServiceConfigurations();
            bool configsHasExtension = imageServiceConfigs.Any(x => x.Extension.Equals(fileEnding, StringComparison.InvariantCultureIgnoreCase));
            _isImageCache.Add(fileEnding, configsHasExtension);

            return configsHasExtension;
        }
    }
}