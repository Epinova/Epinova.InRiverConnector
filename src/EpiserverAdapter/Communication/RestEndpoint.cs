using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using inRiver.Remoting.Log;

namespace Epinova.InRiverConnector.EpiserverAdapter.Communication
{
    public class RestEndpoint<T>
    {
        private readonly string _endpointAddress;       
        private readonly string _action;       

        // ReSharper disable once StaticMemberInGenericType
        private static readonly HttpClient _httpClient;

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

            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");

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
                        Thread.Sleep(15000);
                    }
                    else
                    {
                        Thread.Sleep(150000);
                    }
                    
                    resp = Get(isImportingAction);
                }

                if (resp.StartsWith("ERROR"))
                {
                    inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error, resp);
                }

                return resp;
            }
            
            string errorMsg = $"Import failed: {(int) response.StatusCode} ({response.ReasonPhrase})";
            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);
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
            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);

            throw new HttpRequestException(errorMsg);
        }

        public List<string> PostWithStringListAsReturn(T message)
        {
            Uri uri = new Uri(GetUrl());

            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, $"Posting to {uri}");

            HttpResponseMessage response = _httpClient.PostAsJsonAsync<T>(uri.PathAndQuery, message).Result;

            if (response.IsSuccessStatusCode)
            {
                return response.Content.ReadAsAsync<List<string>>().Result;
            }
            string errorMsg = $"Import failed: {(int) response.StatusCode} ({response.ReasonPhrase})";
            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);
            throw new HttpRequestException(errorMsg);
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