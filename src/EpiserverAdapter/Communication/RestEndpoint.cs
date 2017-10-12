using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using inRiver.Remoting.Log;

namespace inRiver.EPiServerCommerce.CommerceAdapter.Communication
{
    public class RestEndpoint<T>
    {
        private readonly string _endpointAddress;       
        private readonly string _action;       
        private readonly string _apikey;
        
        // ReSharper disable once StaticMemberInGenericType
        private static HttpClient _httpClient;

        static RestEndpoint()
        {
            _httpClient = new HttpClient();
        }

        public RestEndpoint(Configuration config, string action)
        {
            _action = action;
            _endpointAddress = config.EpiEndpoint;

            Uri uri = new Uri(GetUrl());
            _httpClient.BaseAddress = new Uri(uri.Scheme + "://" + uri.Authority);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("apikey", config.EpiApiKey);
            _httpClient.Timeout = new TimeSpan(config.EpiRestTimeout, 0, 0);

        }

        public string Post(T message)
        {
            var url = GetUrl();

            Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");

            var response = _httpClient.PostAsJsonAsync(url, message).Result;
            if (response.IsSuccessStatusCode)
            {
                var resp = response.Content.ReadAsAsync<string>().Result;

                int tries = 0;
                var isImportingAction = GetUrl("IsImporting");

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
                    
                    resp = Get(isImportingAction);
                }

                if (resp.StartsWith("ERROR"))
                {
                    Integration.Logging.IntegrationLogger.Write(LogLevel.Error, resp);
                }

                return resp;
            }
            
            string errorMsg = $"Import failed: {(int) response.StatusCode} ({response.ReasonPhrase})";
            Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);
            throw new HttpRequestException(errorMsg);
        }

        private string Get(string uri)
        {
            HttpResponseMessage response = _httpClient.GetAsync(uri).Result;

            if (response.IsSuccessStatusCode)
            {
                return response.Content.ReadAsAsync<string>().Result;
            }
            string errorMsg = string.Format("Import failed: {0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);

            throw new HttpRequestException(errorMsg);
        }

        public List<string> PostWithStringListAsReturn(T message)
        {
            Uri uri = new Uri(GetUrl());
            HttpClient client = new HttpClient();
            string baseUrl = uri.Scheme + "://" + uri.Authority;

            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Debug,
                string.Format("Posting to {0}", uri.ToString()));

            client.BaseAddress = new Uri(baseUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apikey", _apikey);

            // HttpResponseMessage response = client.GetAsync("").Result;  // Blocking call!
            client.Timeout = new TimeSpan(_timeout, 0, 0);
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

        private string GetUrl(string action)
        {
            return _endpointAddress + action;
        }

        private string GetUrl()
        {
            return GetUrl(_action);
        }
    }
}