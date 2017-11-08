using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Poco;
using Epinova.InRiverConnector.Interfaces;
using inRiver.Integration.Logging;
using inRiver.Remoting.Log;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class ResourceImporter
    {
        private readonly Configuration _config;
        private readonly HttpClientInvoker _httpClient;

        public ResourceImporter(Configuration config, HttpClientInvoker httpClient)
        {
            _config = config;
            _httpClient = httpClient;
        }
        
        public bool ImportResources(string manifest, string baseResourcePath)
        {
            IntegrationLogger.Write(LogLevel.Information, $"Starting Resource Import. Manifest: {manifest} BaseResourcePath: {baseResourcePath}");
            var serializer = new XmlSerializer(typeof(Resources));
            Resources resources;
            using (var reader = XmlReader.Create(manifest))
            {
                resources = (Resources)serializer.Deserialize(reader);
            }

            var resourcesForImport = new List<InRiverImportResource>();
            foreach (var resource in resources.ResourceFiles.Resource)
            {
                var newRes = new InRiverImportResource
                {
                    Action = resource.action
                };

                if (resource.ParentEntries != null && resource.ParentEntries.EntryCode != null)
                {
                    foreach (var entryCode in resource.ParentEntries.EntryCode)
                    {
                        if (string.IsNullOrEmpty(entryCode.Value))
                            continue;

                        newRes.Codes.Add(entryCode.Value);
                        newRes.EntryCodes.Add(new Interfaces.EntryCode
                        {
                            Code = entryCode.Value,
                            IsMainPicture = entryCode.IsMainPicture
                        });
                    }
                }

                if (resource.action != "deleted")
                {
                    newRes.MetaFields = GenerateMetaFields(resource);

                    // path is ".\some file.ext"
                    if (resource.Paths != null && resource.Paths.Path != null)
                    {
                        string filePath = resource.Paths.Path.Value.Remove(0, 1);
                        filePath = filePath.Replace("/", "\\");
                        newRes.Path = baseResourcePath + filePath;
                    }
                }

                newRes.ResourceId = resource.id;
                resourcesForImport.Add(newRes);
            }

            if (resourcesForImport.Count == 0)
            {
                IntegrationLogger.Write(LogLevel.Debug, "Nothing to tell server about.");
                return true;
            }

            return PostToEpiserver(resourcesForImport);
        }

        private List<ResourceMetaField> GenerateMetaFields(Resource resource)
        {
            List<ResourceMetaField> metaFields = new List<ResourceMetaField>();
            if (resource.ResourceFields != null)
            {
                foreach (MetaField metaField in resource.ResourceFields.MetaField)
                {
                    ResourceMetaField resourceMetaField = new ResourceMetaField { Id = metaField.Name.Value };
                    List<Value> values = new List<Value>();
                    foreach (Data data in metaField.Data)
                    {
                        Value value = new Value { Languagecode = data.language };
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
            }

            return metaFields;
        }

        private bool PostToEpiserver(List<InRiverImportResource> resourcesForImport)
        {
            var listofLists = new List<List<InRiverImportResource>>();
            var maxSize = 100;
            for (var i = 0; i < resourcesForImport.Count; i += maxSize)
            {
                listofLists.Add(resourcesForImport.GetRange(i, Math.Min(maxSize, resourcesForImport.Count - i)));
            }

            foreach (var resourceList in listofLists)
            {
               IntegrationLogger.Write(LogLevel.Debug, $"Sending {resourceList.Count} of {resourcesForImport.Count} resources to Episerver");

                var response = _httpClient.PostAsJsonAsync(_config.Endpoints.ImportResources, resourceList).Result;
                response.EnsureSuccessStatusCode();
                
                var result = response.Content.ReadAsAsync<bool>().Result;
                if (!result)
                    continue;

                var resp = GetImportStatus();
                while (resp == "importing")
                {
                    Thread.Sleep(10000);
                    resp = GetImportStatus();
                }

                if (resp.StartsWith("ERROR"))
                {
                    IntegrationLogger.Write(LogLevel.Error, resp);
                    return false;
                }
            }

            return true;
        }

        private string GetImportStatus()
        {
            return _httpClient.Get(_config.Endpoints.IsImporting);
        }
    }
}
