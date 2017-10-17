using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using inRiver.Integration.Logging;
using inRiver.Remoting.Log;

namespace Epinova.InRiverConnector.EpiserverAdapter.Communication
{
    public class HttpClientInvoker
    {
        private static readonly HttpClient HttpClient;
        private readonly string _isImportingAction;
        private static bool _clientPropsSet = false;

        static HttpClientInvoker()
        {
            HttpClient = new HttpClient();
            IntegrationLogger.Write(LogLevel.Debug, $"Static constructor running.");
        }

        public HttpClientInvoker(Configuration config)
        {
            _isImportingAction = config.Endpoints.IsImporting;
            IntegrationLogger.Write(LogLevel.Debug, $"Initializing HttpClientInvoker. clientPropsSet: {_clientPropsSet}");
            
            // INFO: Allows multiple HttpClientInvoker classes to be created while keeping one static HttpClient.
            if (!_clientPropsSet) 
            {
                IntegrationLogger.Write(LogLevel.Debug, $"Initing clientPropsSet. ApiKey => {config.EpiApiKey}");

                HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpClient.DefaultRequestHeaders.Add("apikey", config.EpiApiKey);
                HttpClient.Timeout = new TimeSpan(config.EpiRestTimeout, 0, 0);
                _clientPropsSet = true;
            }
        }

        public string Post<T>(string url, T message)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");

            var response = HttpClient.PostAsJsonAsync(url, message).Result;
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
            HttpResponseMessage response = HttpClient.GetAsync(uri).Result;

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
            HttpResponseMessage response = HttpClient.PostAsJsonAsync<T>(uri.PathAndQuery, message).Result;

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