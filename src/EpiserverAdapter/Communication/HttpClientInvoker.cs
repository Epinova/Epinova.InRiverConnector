using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using inRiver.Remoting.Log;

namespace Epinova.InRiverConnector.EpiserverAdapter.Communication
{
    public class HttpClientInvoker
    {
        // ReSharper disable once StaticMemberInGenericType
        private static readonly HttpClient _httpClient;
        private readonly EndpointCollection _endpoints;
        private readonly string _isImportingAction;

        static HttpClientInvoker()
        {
            _httpClient = new HttpClient();
        }

        public HttpClientInvoker(Configuration config)
        {
            _isImportingAction = _endpoints.IsImporting;
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("apikey", config.EpiApiKey);
            _httpClient.Timeout = new TimeSpan(config.EpiRestTimeout, 0, 0);
        }

        public string Post<T>(string url, T message)
        {
            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");

            var response = _httpClient.PostAsJsonAsync(url, message).Result;
            if (response.IsSuccessStatusCode)
            {
                var parsedResponse = response.Content.ReadAsAsync<string>().Result;
                int tries = 0;
                
                while (parsedResponse == "importing")
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
                    
                    parsedResponse = Get(_isImportingAction);
                }

                if (parsedResponse.StartsWith("ERROR"))
                {
                    inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error, parsedResponse);
                }

                return parsedResponse;
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

        public List<string> PostWithStringListAsReturn<T>(string url, T message)
        {
            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");

            var uri = new Uri(url);
            HttpResponseMessage response = _httpClient.PostAsJsonAsync<T>(uri.PathAndQuery, message).Result;

            if (response.IsSuccessStatusCode)
            {
                return response.Content.ReadAsAsync<List<string>>().Result;
            }
            string errorMsg = $"Import failed: {(int) response.StatusCode} ({response.ReasonPhrase})";
            inRiver.Integration.Logging.IntegrationLogger.Write(LogLevel.Error, errorMsg);
            throw new HttpRequestException(errorMsg);
        }
    }
}