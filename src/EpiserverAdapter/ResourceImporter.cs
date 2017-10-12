using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using inRiver.EPiServerCommerce.Interfaces;
using inRiver.EPiServerCommerce.MediaPublisher.Poco;
using inRiver.Remoting;
using inRiver.Remoting.Log;

namespace inRiver.EPiServerCommerce.CommerceAdapter
{
    public class ResourceImporter
    {
        private readonly Configuration _config;

        public ResourceImporter(Configuration config)
        {
            _config = config;
        }

        
        public bool ImportResources(string manifest, string baseResourcePath)
        {
            Integration.Logging.IntegrationLogger.Write(LogLevel.Information,$"Starting Resource Import. Manifest: {manifest} BaseResourcePath: {baseResourcePath}");

            var timeout = _config.EpiRestTimeout;
            var apikey = _config.EpiApiKey;
            var endpointAddress = _config.EpiEndpoint;
            
            // Name of resource import controller method
            endpointAddress = endpointAddress + "ImportResources";

            return ImportResourcesToEPiServerCommerce(manifest, baseResourcePath, endpointAddress, apikey, timeout);
        }

        public bool ImportResourcesToEPiServerCommerce(string manifest, string baseResourcePath, string endpointAddress, string apikey, int timeout)
        {
            var serializer = new XmlSerializer(typeof(Resources));
            Resources resources;
            using (var reader = XmlReader.Create(manifest))
            {
                resources = (Resources)serializer.Deserialize(reader);
            }

            var resourcesForImport = new List<InRiverImportResource>();
            foreach (var resource in resources.ResourceFiles.Resource)
            {
                var newRes = new InRiverImportResource();
                newRes.Action = resource.action;
                newRes.Codes = new List<string>();
                if (resource.ParentEntries != null && resource.ParentEntries.EntryCode != null)
                {
                    foreach (MediaPublisher.Poco.EntryCode entryCode in resource.ParentEntries.EntryCode)
                    {
                        if (!string.IsNullOrEmpty(entryCode.Value))
                        {
                            newRes.Codes = new List<string>();

                            newRes.Codes.Add(entryCode.Value);
                            newRes.EntryCodes.Add(new Interfaces.EntryCode()
                            {
                                Code = entryCode.Value,
                                IsMainPicture = entryCode.IsMainPicture
                            });
                        }
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
                Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, string.Format("Nothing to tell server about."));
                return true;
            }

            Uri importEndpoint = new Uri(endpointAddress);
            return PostResourceDataToImporterEndPoint(manifest, importEndpoint, resourcesForImport, apikey, timeout);
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

        /// <summary>
        /// </summary>
        /// <param name="manifest"></param>
        /// <param name="importEndpoint">// http://server:port/inriverapi/InriverDataImport/ImportImages</param>
        /// <param name="resourcesForImport"></param>
        /// <param name="apikey"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        private bool PostResourceDataToImporterEndPoint(string manifest, Uri importEndpoint, List<InRiverImportResource> resourcesForImport, string apikey, int timeout)
        {
            List<List<InRiverImportResource>> listofLists = new List<List<InRiverImportResource>>();
            int maxSize = 1000;
            for (int i = 0; i < resourcesForImport.Count; i += maxSize)
            {
                listofLists.Add(resourcesForImport.GetRange(i, Math.Min(maxSize, resourcesForImport.Count - i)));
            }

            foreach (List<InRiverImportResource> resources in listofLists)
            {
                HttpClient client = new HttpClient();
                string baseUrl = importEndpoint.Scheme + "://" + importEndpoint.Authority;

                Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, string.Format("Sending {2} of {3} resources from {0} to {1}", manifest, importEndpoint, resources.Count, resourcesForImport.Count));
                client.BaseAddress = new Uri(baseUrl);

                // Add an Accept header for JSON format.
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("apikey", apikey);

                client.Timeout = new TimeSpan(timeout, 0, 0);
                HttpResponseMessage response = client.PostAsJsonAsync(importEndpoint.PathAndQuery, resources).Result;
                if (response.IsSuccessStatusCode)
                {
                    // Parse the response body. Blocking!
                    var result = response.Content.ReadAsAsync<bool>().Result;
                    if (result)
                    {
                        string resp = Get(apikey, timeout);

                        int tries = 0;
                        while (resp == "importing")
                        {
                            tries++;
                            if (tries < 10)
                            {
                                Thread.Sleep(5000);
                            }
                            else if (tries < 30)
                            {
                                Thread.Sleep(60000);
                            }
                            else
                            {
                                Thread.Sleep(600000);
                            }

                            resp = Get(apikey, timeout);
                        }

                        if (resp.StartsWith("ERROR"))
                        {
                            Integration.Logging.IntegrationLogger.Write(LogLevel.Error, resp);
                            return false;
                        }
                    }
                }
                else
                {
                    Integration.Logging.IntegrationLogger.Write(
                        LogLevel.Error,
                        string.Format("Import failed: {0} ({1})", (int)response.StatusCode, response.ReasonPhrase));
                    return false;
                }
            }

            return true;
        }

        private string Get(string apikey, int timeout)
        {
            string endpointAddress = _settings["EPI_ENDPOINT_URL"];
            if (string.IsNullOrEmpty(endpointAddress))
            {
                throw new ConfigurationErrorsException("Missing EPI_ENDPOINT_URL setting on connector. It should point to the import end point on the EPiServer Commerce web site. ");
            }

            if (endpointAddress.EndsWith("/") == false)
            {
                endpointAddress = endpointAddress + "/";
            }

            // Name of resource import controller method
            endpointAddress = endpointAddress + "IsImporting";

            Uri uri = new Uri(endpointAddress);

            HttpClient client = new HttpClient();
            string baseUrl = uri.Scheme + "://" + uri.Authority;

            client.BaseAddress = new Uri(baseUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apikey", apikey);

            // HttpResponseMessage response = client.GetAsync("").Result;  // Blocking call!
            client.Timeout = new TimeSpan(timeout, 0, 0);
            HttpResponseMessage response = client.GetAsync(uri.PathAndQuery).Result;

            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                string resp = response.Content.ReadAsAsync<string>().Result;

                return resp;
            }
            
            string errorMsg = string.Format("Import failed: {0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);
            throw new HttpRequestException(errorMsg);
        }
    }
}
