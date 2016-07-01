namespace inRiver.EPiServerCommerce.CommerceAdapter.Communication
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;

    using inRiver.Remoting.Log;

    public class RestEndpoint<T>
    {
        private readonly string endpointAddress;
        
        private readonly string action;
        
        private readonly string apikey;

        private readonly Dictionary<string, string> settingsDictionary;

        private readonly int timeout;

        public RestEndpoint(string endpointAddress, string action)
        {
            this.action = action;
            this.endpointAddress = this.ValidateEndpointAddress(endpointAddress);
            this.timeout = 1;
        }

        public RestEndpoint(Dictionary<string, string> settings, string action)
        {
            this.action = action;
            this.settingsDictionary = settings;

            if (settings.ContainsKey("EPI_APIKEY"))
            {
                this.apikey = settings["EPI_APIKEY"];
            }
            else
            {
                throw new ConfigurationErrorsException("Missing EPI_APIKEY setting on connector. It needs to be defined to else the calls will fail. Please see the documentation."); 
            }

            if (settings.ContainsKey("EPI_ENDPOINT_URL") == false)
            {
                throw new ConfigurationErrorsException("Missing EPI_ENDPOINT_URL setting on connector. It should point to the import end point on the EPiServer Commerce web site. Please see the documentation.");
            }
            
            this.endpointAddress = this.ValidateEndpointAddress(settings["EPI_ENDPOINT_URL"]);

            if (settings.ContainsKey("EPI_RESTTIMEOUT"))
            {
                string timeoutString = settings["EPI_RESTTIMEOUT"];
                if (!int.TryParse(timeoutString, out this.timeout))
                {
                    throw new ConfigurationErrorsException("Can't parse EPI_RESTTIMEOUT : " + timeoutString);
                }
            }
            else
            {
                throw new ConfigurationErrorsException("Missing EPI_RESTTIMEOUT setting on connector. It needs to be defined to else the calls will fail. Please see the documentation.");
            }
        }

        public string Action
        {
            get { return this.action; }
        }

        public string GetUrl()
        {
            return this.GetUrl(this.action);
        }

        public string GetUrl(string action)
        {
            string endpointAddress = this.endpointAddress;

            if (string.IsNullOrEmpty(action) == false)
            {
                endpointAddress = endpointAddress + action;
            }

            return endpointAddress;
        }

        public string Post(T message)
        {
            Uri uri = new Uri(this.GetUrl());
            HttpClient client = new HttpClient();
            string baseUrl = uri.Scheme + "://" + uri.Authority;

            Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, string.Format("Posting to {0}", uri));

            client.BaseAddress = new Uri(baseUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apikey", this.apikey);

            // HttpResponseMessage response = client.GetAsync("").Result;  // Blocking call!
            client.Timeout = new TimeSpan(this.timeout, 0, 0);
            HttpResponseMessage response = client.PostAsJsonAsync(uri.PathAndQuery, message).Result;
            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                string resp = response.Content.ReadAsAsync<string>().Result;

                int tries = 0;
                RestEndpoint<string> endpoint = new RestEndpoint<string>(this.settingsDictionary, "IsImporting");

                while (resp == "importing")
                {
                    tries++;
                    if (tries < 10)
                    {
                        Thread.Sleep(2000);
                    }
                    else if (tries < 30)
                    {
                        Thread.Sleep(30000);
                    }
                    else
                    {
                        Thread.Sleep(300000);
                    }

                    resp = endpoint.Get();
                }

                if (resp.StartsWith("ERROR"))
                {
                    Integration.Logging.IntegrationLogger.Write(LogLevel.Error, resp);
                }

                return resp;
            }
            
            string errorMsg = string.Format("Import failed: {0} ({1})", (int) response.StatusCode, response.ReasonPhrase);
            Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);
            throw new HttpRequestException(errorMsg);
        }

        public string Get()
        {
            Uri uri = new Uri(this.GetUrl());
            HttpClient client = new HttpClient();
            string baseUrl = uri.Scheme + "://" + uri.Authority;

            client.BaseAddress = new Uri(baseUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apikey", this.apikey);

            // HttpResponseMessage response = client.GetAsync("").Result;  // Blocking call!
            client.Timeout = new TimeSpan(this.timeout, 0, 0);
            HttpResponseMessage response = client.GetAsync(uri.PathAndQuery).Result;

            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                string resp = response.Content.ReadAsAsync<string>().Result;

                return resp;

            }
            else
            {
                string errorMsg = string.Format("Import failed: {0} ({1})", (int)response.StatusCode,
                    response.ReasonPhrase);
                inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error,
                    errorMsg);
                throw new HttpRequestException(errorMsg);
            }
        }

        public List<string> PostWithStringListAsReturn(T message)
        {
            Uri uri = new Uri(this.GetUrl());
            HttpClient client = new HttpClient();
            string baseUrl = uri.Scheme + "://" + uri.Authority;

            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Debug,
                string.Format("Posting to {0}", uri.ToString()));

            client.BaseAddress = new Uri(baseUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apikey", this.apikey);

            // HttpResponseMessage response = client.GetAsync("").Result;  // Blocking call!
            client.Timeout = new TimeSpan(this.timeout, 0, 0);
            HttpResponseMessage response = client.PostAsJsonAsync<T>(uri.PathAndQuery, message).Result;
            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                return response.Content.ReadAsAsync<List<string>>().Result;
            }
            else
            {
                string errorMsg = string.Format("Import failed: {0} ({1})", (int)response.StatusCode,
                    response.ReasonPhrase);
                inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error,
                    errorMsg);
                throw new HttpRequestException(errorMsg);
            }
        }

        private string ValidateEndpointAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ConfigurationErrorsException("Missing ImportEndPointAddress setting on connector. It should point to the import end point on the EPiServer Commerce web site. Please see the documentation.");
            }

            if (address.EndsWith("/") == false)
            {
                return address + "/";
            }

            return address;
        }
    }
}