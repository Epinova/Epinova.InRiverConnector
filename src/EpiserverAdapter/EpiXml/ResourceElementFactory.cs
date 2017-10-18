using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.EpiXml
{
    public class ResourceElementFactory
    {
        private readonly EpiElementFactory _epiElementFactory;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly ChannelPrefixHelper _channelPrefixHelper;

        public ResourceElementFactory(EpiElementFactory epiElementFactory, EpiMappingHelper mappingHelper, ChannelPrefixHelper channelPrefixHelper)
        {
            _epiElementFactory = epiElementFactory;
            _mappingHelper = mappingHelper;
            _channelPrefixHelper = channelPrefixHelper;
        }

        public XElement CreateResourceElement(Entity resource, string action, Configuration config, Dictionary<int, Entity> parentEntities = null)
        {
            string resourceFileId = "-1";
            Field resourceFileIdField = resource.GetField("ResourceFileId");
            if (resourceFileIdField != null && !resourceFileIdField.IsEmpty())
            {
                resourceFileId = resource.GetField("ResourceFileId").Data.ToString();
            }

            Dictionary<string, int?> parents = new Dictionary<string, int?>();

            string resourceId = _channelPrefixHelper.GetEpiserverCode(resource.Id);
            resourceId = resourceId.Replace("_", string.Empty);

            if (action == "unlinked")
            {
                var resourceParents = config.ChannelEntities.Where(i => !i.Key.Equals(resource.Id));

                foreach (KeyValuePair<int, Entity> resourceParent in resourceParents)
                {
                    List<string> ids = new List<string> { resourceParent.Value.Id.ToString(CultureInfo.InvariantCulture) };

                    if (config.ItemsToSkus && resourceParent.Value.EntityType.Id == "Item")
                    {
                        List<string> skuIds = _epiElementFactory.SkuItemIds(resourceParent.Value, config);

                        foreach (string skuId in skuIds)
                        {
                            ids.Add(skuId);
                        }

                        if (config.UseThreeLevelsInCommerce == false)
                        {
                            ids.Remove(resourceParent.Value.Id.ToString(CultureInfo.InvariantCulture));
                        }
                    }

                    foreach (string id in ids)
                    {
                        if (!parents.ContainsKey(id))
                        {
                            parents.Add(id, resourceParent.Value.MainPictureId);
                        }
                    }
                }
            }
            else
            {
                List<StructureEntity> allResourceLocations = config.ChannelStructureEntities.FindAll(i => i.EntityId.Equals(resource.Id));

                List<Link> links = new List<Link>();

                foreach (Link inboundLink in resource.InboundLinks)
                {
                    if (allResourceLocations.Exists(i => i.ParentId.Equals(inboundLink.Source.Id)))
                    {
                        links.Add(inboundLink);
                    }
                }

                foreach (Link link in links)
                {
                    Entity linkedEntity = link.Source;
                    List<string> ids = new List<string> { linkedEntity.Id.ToString(CultureInfo.InvariantCulture) };
                    if (config.ItemsToSkus && linkedEntity.EntityType.Id == "Item")
                    {
                        List<string> skuIds = _epiElementFactory.SkuItemIds(linkedEntity, config);
                        foreach (string skuId in skuIds)
                        {
                            ids.Add(skuId);
                        }

                        if (config.UseThreeLevelsInCommerce == false)
                        {
                            ids.Remove(linkedEntity.Id.ToString(CultureInfo.InvariantCulture));
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

                if (parents.Any() && parentEntities != null)
                {
                    List<int> nonExistingIds =
                        (from id in parents.Keys where !parentEntities.ContainsKey(int.Parse(id)) select int.Parse(id))
                        .ToList();

                    if (nonExistingIds.Any())
                    {
                        foreach (Entity entity in RemoteManager.DataService.GetEntities(nonExistingIds, LoadLevel.DataOnly))
                        {
                            if (!parentEntities.ContainsKey(entity.Id))
                            {
                                parentEntities.Add(entity.Id, entity);
                            }
                        }
                    }
                }
            }

            return new XElement(
                "Resource",
                new XAttribute("id", resourceId),
                new XAttribute("action", action),
                new XElement(
                    "ResourceFields",
                    resource.Fields.Where(field => !_mappingHelper.SkipField(field.FieldType))
                        .Select(field => _epiElementFactory.GetMetaFieldValueElement(field, config))),
                GetInternalPathsInZip(resource, config),
                new XElement(
                    "ParentEntries",
                    parents.Select(parent =>
                            new XElement("EntryCode", _channelPrefixHelper.GetEpiserverCode(parent.Key), 
                                new XAttribute("IsMainPicture", parent.Value != null && parent.Value.ToString().Equals(resourceFileId))))));
        }


        public XDocument GetDocumentAndSaveFilesToDisk(List<StructureEntity> channelEntities, Configuration config, string folderDateTime)
        {
            XDocument resourceDocument = new XDocument();
            try
            {
                if (!Directory.Exists(config.ResourcesRootPath))
                {
                    Directory.CreateDirectory(config.ResourcesRootPath);
                }

                List<int> resourceIds = new List<int>();
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
                    SaveFileToDisk(res, config, folderDateTime);
                }

                EntityType reourceType = resources.Count > 0 ? resources[0].EntityType : RemoteManager.ModelService.GetEntityType("Resource");
                XElement resourceMetaClasses = _epiElementFactory.CreateResourceMetaFieldsElement(reourceType);
                resourceDocument = CreateResourceDocument(resourceMetaClasses, resources, resources, "added", config);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not add resources"), ex);
            }

            return resourceDocument;
        }
        
        internal XDocument HandleResourceUpdate(Entity updatedResource, Configuration config, string folderDateTime)
        {
            SaveFileToDisk(updatedResource, config, folderDateTime);
            List<Entity> channelResources = new List<Entity>();
            channelResources.Add(updatedResource);
            return CreateResourceDocument(null, channelResources, new List<Entity> { updatedResource }, "updated", config);
        }

        internal XDocument HandleResourceDelete(List<string> deletedResources)
        {
            return
                new XDocument(
                    new XElement(
                        "Resources",
                        new XElement("ResourceMetaFields"),
                        new XElement("ResourceFiles",
                            from id in deletedResources select new XElement("Resource", new XAttribute("id", id), new XAttribute("action", "deleted")))));
        }

        internal XDocument HandleResourceUnlink(Entity resource, Entity parent, Configuration config)
        {
            Dictionary<int, Entity> parentEntities = config.ChannelEntities;
            XElement resourceElement = CreateResourceElement(resource, "unlinked", config, parentEntities);
            XElement resourceFieldsElement = resourceElement.Element("ResourceFields");
            if (resourceFieldsElement != null)
            {
                resourceFieldsElement.Remove();
            }

            XElement pathsElement = resourceElement.Element("Paths");
            if (pathsElement != null)
            {
                pathsElement.Remove();
            }

            return
                new XDocument(
                    new XElement("Resources",
                        new XElement("ResourceMetaFields"),
                        new XElement("ResourceFiles", resourceElement)));
        }

        internal XDocument CreateResourceDocument(XElement resourceMetaClasses, List<Entity> channelResources, List<Entity> resources, string action, Configuration config)
        {

            Dictionary<int, Entity> parentEntities = config.ChannelEntities;

            return
                new XDocument(
                    new XElement(
                        "Resources",
                        resourceMetaClasses,
                        new XElement(
                            "ResourceFiles",
                            resources.Select(res => CreateResourceElement(res, action, config, parentEntities)))));
        }

        internal bool SaveFileToDisk(Entity resource, Configuration config, string folderDateTime)
        {
            string fileName = string.Empty;
            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                int resourceFileId = GetResourceFileId(resource);
                if (resourceFileId < 0)
                {
                    IntegrationLogger.Write(LogLevel.Information, $"Resource with id:{resource.Id} has no value for ResourceFileId");
                    return false;
                }

                foreach (string displayConfiguration in GetDisplayConfigurations(resource, config))
                {
                    byte[] resourceData = RemoteManager.UtilityService.GetFile(resourceFileId, displayConfiguration);
                    if (resourceData == null)
                    {
                        IntegrationLogger.Write(LogLevel.Error, $"Resource with id:{resource.Id} and ResourceFileId: {resourceFileId} could not get file");
                        return false;
                    }

                    fileName = GetResourceFileName(resource, resourceFileId, displayConfiguration, config);

                    string folder = GetFolderName(displayConfiguration, resource, config);
                    string dir = Path.Combine(config.ResourcesRootPath, folderDateTime, folder);

                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    
                    File.WriteAllBytes(Path.Combine(dir, fileName), resourceData);
                }
                stopwatch.Stop();
                IntegrationLogger.Write(LogLevel.Debug, $"Saving Resource {resource.Id} to {fileName} took {stopwatch.GetElapsedTimeFormated()}");
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

        internal XElement GetInternalPathsInZip(Entity resource, Configuration config)
        {
            int id = GetResourceFileId(resource);

            XElement paths = new XElement("Paths");

            if (id < 0)
            {
                return paths;
            }

            foreach (string displayConfiguration in GetDisplayConfigurations(resource, config))
            {
                string fileName = GetResourceFileName(resource, id, displayConfiguration, config);
                string folder = GetFolderName(displayConfiguration, resource, config);
                paths.Add(new XElement("Path", string.Format("./{0}/{1}", folder, fileName)));
            }

            return paths;
        }

        private int GetResourceFileId(Entity resource)
        {
            Field resourceFileIdField = resource.GetField("ResourceFileId");
            if (resourceFileIdField == null || resourceFileIdField.IsEmpty())
            {
                return -1;
            }

            return (int)resourceFileIdField.Data;
        }

        private string GetResourceFileName(Entity resource, int resourceFileId, string displayConfiguration, Configuration config)
        {
            Field resourceFileNameField = resource.GetField("ResourceFilename");
            string fileName = $"[{resourceFileId}].jpg";
            if (resourceFileNameField != null && !resourceFileNameField.IsEmpty())
            {
                string fileType = Path.GetExtension(resourceFileNameField.Data.ToString());
                if (displayConfiguration != Configuration.OriginalDisplayConfiguration)
                {
                    string extension = string.Empty;
                    if (config.ResourceConfiugurationExtensions.ContainsKey(displayConfiguration))
                    {
                        extension = config.ResourceConfiugurationExtensions[displayConfiguration];
                    }
                    
                    if (string.IsNullOrEmpty(extension))
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

        private IEnumerable<string> GetDisplayConfigurations(Entity resource, Configuration config)
        {
            if (IsImage(resource))
            {
                return config.ResourceConfigurations;
            }

            IntegrationLogger.Write(LogLevel.Debug, $"No image configuration found for Resource {resource.Id}. Original will be used");
            return new[] { Configuration.OriginalDisplayConfiguration };
        }

        private bool IsImage(Entity resource)
        {
            var fileEnding = resource.GetField("ResourceFilename")?.Data?.ToString().Split('.').Last();
            var imageServiceConfigs = RemoteManager.UtilityService.GetAllImageServiceConfigurations();
            var configsHasExtension = imageServiceConfigs.Exists(x => string.Compare(x.Extension, fileEnding, StringComparison.OrdinalIgnoreCase) == 0);

            return !string.IsNullOrWhiteSpace(fileEnding) && configsHasExtension;
        }

        private string GetFolderName(string displayConfiguration, Entity resource, Configuration config)
        {
            if (!string.IsNullOrEmpty(displayConfiguration) && config.ChannelMimeTypeMappings.ContainsKey(displayConfiguration))
            {
                return config.ChannelMimeTypeMappings[displayConfiguration];
            }

            if (!string.IsNullOrEmpty(displayConfiguration) && IsImage(resource))
            {
                return displayConfiguration;
            }

            Field mimeTypeField = resource.GetField(Configuration.MimeType);
            if (mimeTypeField != null && !mimeTypeField.IsEmpty() && mimeTypeField.Data.ToString().Contains('/'))
            {
                if (config.ChannelMimeTypeMappings.ContainsKey(mimeTypeField.Data.ToString()))
                {
                    return config.ChannelMimeTypeMappings[mimeTypeField.Data.ToString()];
                }

                return mimeTypeField.Data.ToString().Split('/')[1];
            }

            return displayConfiguration;
        }
    }
}
