using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public static class Resources
    {
        public static XDocument GetDocumentAndSaveFilesToDisk(List<StructureEntity> channelEntities, Configuration config, string folderDateTime)
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
                XElement resourceMetaClasses = EpiElement.CreateResourceMetaFieldsElement(reourceType, config);
                resourceDocument = CreateResourceDocument(resourceMetaClasses, resources, resources, "added", config);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not add resources"), ex);
            }

            return resourceDocument;
        }
        
        internal static XDocument HandleResourceUpdate(Entity updatedResource, Configuration config, string folderDateTime)
        {
            SaveFileToDisk(updatedResource, config, folderDateTime);
            List<Entity> channelResources = new List<Entity>();
            channelResources.Add(updatedResource);
            return CreateResourceDocument(null, channelResources, new List<Entity> { updatedResource }, "updated", config);
        }

        internal static XDocument HandleResourceDelete(List<string> deletedResources)
        {
            return
                new XDocument(
                    new XElement(
                        "Resources",
                        new XElement("ResourceMetaFields"),
                        new XElement(
                            "ResourceFiles",
                            from id in deletedResources select new XElement("Resource", new XAttribute("id", id), new XAttribute("action", "deleted")))));
        }

        internal static XDocument HandleResourceUnlink(Entity resource, Entity parent, Configuration config)
        {
            Dictionary<int, Entity> parentEntities = config.ChannelEntities;
            XElement resourceElement = EpiElement.CreateResourceElement(resource, "unlinked", config, parentEntities);
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
                    new XElement(
                        "Resources",
                        new XElement("ResourceMetaFields"),
                        new XElement("ResourceFiles", resourceElement)));
        }

        internal static XDocument CreateResourceDocument(XElement resourceMetaClasses, List<Entity> channelResources, List<Entity> resources, string action, Configuration config)
        {

            Dictionary<int, Entity> parentEntities = config.ChannelEntities;

            return
                new XDocument(
                    new XElement(
                        "Resources",
                        resourceMetaClasses,
                        new XElement(
                            "ResourceFiles",
                            resources.Select(res => EpiElement.CreateResourceElement(res, action, config, parentEntities)))));
        }

        internal static bool SaveFileToDisk(Entity resource, Configuration config, string folderDateTime)
        {
            Stopwatch saveFileStopWatch = new Stopwatch();
            string fileName = string.Empty;
            try
            {
                saveFileStopWatch.Start();
                if (resource == null)
                {
                    IntegrationLogger.Write(LogLevel.Error, "Resource is null!");
                    return false;
                }

                int resourceFileId = GetResourceFileId(resource);
                if (resourceFileId < 0)
                {
                    IntegrationLogger.Write(LogLevel.Information, string.Format("Resource with id:{0} has no value for ResourceFileId", resource.Id));
                    return false;
                }

                foreach (string displayConfiguration in GetDisplayConfigurations(resource, config))
                {
                    byte[] resourceData = RemoteManager.UtilityService.GetFile(resourceFileId, displayConfiguration);
                    if (resourceData == null)
                    {
                        IntegrationLogger.Write(LogLevel.Error, string.Format("Resource with id:{0} and ResourceFileId: {1} could not get file", resource.Id, resourceFileId));
                        return false;
                    }

                    fileName = GetResourceFileName(resource, resourceFileId, displayConfiguration, config);

                    string folder = GetFolderFromDisplayConfiguration(displayConfiguration, resource, config);
                    string dir = Path.Combine(config.ResourcesRootPath, folderDateTime, folder);

                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    
                    File.WriteAllBytes(Path.Combine(dir, fileName), resourceData);
                }
            }
            catch (Exception ex)
            {
                if (resource != null)
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Could not save resource! id:{0}, ResourceFileId:{1}", resource.Id, resource.GetField("ResourceFileId")), ex);
                }

                return false;
            }
            
            saveFileStopWatch.Stop();
            IntegrationLogger.Write(LogLevel.Debug, string.Format("Saving Resource {0} to {1} took {2}", resource.Id, fileName, BusinessHelper.GetElapsedTimeFormated(saveFileStopWatch)));
            return true;
        }

        internal static XElement GetInternalPathsInZip(Entity resource, Configuration config)
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
                string folder = GetFolderFromDisplayConfiguration(displayConfiguration, resource, config);
                paths.Add(new XElement("Path", string.Format("./{0}/{1}", folder, fileName)));
            }

            return paths;
        }

        private static int GetResourceFileId(Entity resource)
        {
            Field resourceFileIdField = resource.GetField("ResourceFileId");
            if (resourceFileIdField == null || resourceFileIdField.IsEmpty())
            {
                return -1;
            }

            return (int)resourceFileIdField.Data;
        }

        private static string GetResourceFileName(Entity resource, int resourceFileId, string displayConfiguration, Configuration config)
        {
            Field resourceFileNameField = resource.GetField("ResourceFilename");
            string fileName = string.Format("[{0}].jpg", resourceFileId);
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
                fileName = string.Format("{0}{1}", fileName, fileType);
            }

            return fileName;
        }

        private static IEnumerable<string> GetDisplayConfigurations(Entity resource, Configuration config)
        {
            if (IsImage(resource))
            {
                return config.ResourceConfigurations;
            }

            IntegrationLogger.Write(LogLevel.Debug,
                $"No image configuration found for Resource {resource.Id}. Original will be used");
            return new[] { Configuration.OriginalDisplayConfiguration };
        }

        private static bool IsImage(Entity resource)
        {
            var fileEnding = resource.GetField("ResourceFilename")?.Data?.ToString().Split('.').Last();
            return !string.IsNullOrWhiteSpace(fileEnding) && RemoteManager.UtilityService.GetAllImageServiceConfigurations().Exists(x => string.Compare(x.Extension, fileEnding, StringComparison.OrdinalIgnoreCase) == 0);
        }

        private static string GetFolderFromDisplayConfiguration(string displayConfiguration, Entity resource, Configuration config)
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
