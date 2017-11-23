using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
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

        public HttpClientInvoker(IConfiguration config)
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

        public void Post<T>(string url, T message)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");
            var timer = Stopwatch.StartNew();

            var response = HttpClient.PostAsJsonAsync(url, message).Result;
            response.EnsureSuccessStatusCode();
            
            IntegrationLogger.Write(LogLevel.Debug, $"Posted to {url}, took {timer.ElapsedMilliseconds}.");
        }

        public string PostWithAsyncStatusCheck<T>(string url, T message)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");

            var response = HttpClient.PostAsJsonAsync(url, message).Result;
            if (response.IsSuccessStatusCode)
            {
                var parsedResponse = response.Content.ReadAsAsync<string>().Result;
                
                while (parsedResponse == "importing")
                {
                    Thread.Sleep(10000);
                    parsedResponse = Get(_isImportingAction);
                }

                if (parsedResponse.StartsWith("ERROR"))
                {
                    IntegrationLogger.Write(LogLevel.Error, parsedResponse);
                }

                return parsedResponse;
            }
            
            string errorMsg = $"Import failed: {(int) response.StatusCode} ({response.ReasonPhrase})";
            IntegrationLogger.Write(LogLevel.Error, errorMsg);
            throw new HttpRequestException(errorMsg);
        }

        public string Get(string uri)
        {
            HttpResponseMessage response = HttpClient.GetAsync(uri).Result;

            response.EnsureSuccessStatusCode();

            return response.Content.ReadAsAsync<string>().Result;
        }
        
        public List<string> PostWithStringListAsReturn<T>(string url, T message)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"Posting to {url}");

            var uri = new Uri(url);
            HttpResponseMessage response = HttpClient.PostAsJsonAsync<T>(uri.PathAndQuery, message).Result;

            if (response.IsSuccessStatusCode)
            {
                return response.Content.ReadAsAsync<List<string>>().Result;
            }
            string errorMsg = $"Import failed: {(int) response.StatusCode} ({response.ReasonPhrase})";
            IntegrationLogger.Write(LogLevel.Error, errorMsg);
            throw new HttpRequestException(errorMsg);
        }
    }
}