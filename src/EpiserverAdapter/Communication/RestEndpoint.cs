using System;
using System.Collections.Generic;
using System.Configuration;
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

        private readonly Dictionary<string, string> _settingsDictionary;

        private readonly int _timeout;

        public RestEndpoint(Dictionary<string, string> settings, string action)
        {
            _action = action;
            _settingsDictionary = settings;

            _apikey = settings["EPI_APIKEY"];
            _endpointAddress = settings["EPI_ENDPOINT_URL"];
            _timeout = int.Parse(settings["EPI_RESTTIMEOUT"]);
        }

        public string GetUrl()
        {
            return GetUrl(_action);
        }

        public string GetUrl(string action)
        {
            string endpointAddress = _endpointAddress;

            if (string.IsNullOrEmpty(action) == false)
            {
                endpointAddress = endpointAddress + action;
            }

            return endpointAddress;
        }

        public string Post(T message)
        {
            Uri uri = new Uri(GetUrl());
            HttpClient client = new HttpClient();
            string baseUrl = uri.Scheme + "://" + uri.Authority;

            Integration.Logging.IntegrationLogger.Write(LogLevel.Debug, string.Format("Posting to {0}", uri));

            client.BaseAddress = new Uri(baseUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apikey", _apikey);

            // HttpResponseMessage response = client.GetAsync("").Result;  // Blocking call!
            client.Timeout = new TimeSpan(_timeout, 0, 0);
            HttpResponseMessage response = client.PostAsJsonAsync(uri.PathAndQuery, message).Result;
            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                string resp = response.Content.ReadAsAsync<string>().Result;

                int tries = 0;
                RestEndpoint<string> endpoint = new RestEndpoint<string>(_settingsDictionary, "IsImporting");

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
            Uri uri = new Uri(GetUrl());
            HttpClient client = new HttpClient();
            string baseUrl = uri.Scheme + "://" + uri.Authority;

            client.BaseAddress = new Uri(baseUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apikey", _apikey);

            // HttpResponseMessage response = client.GetAsync("").Result;  // Blocking call!
            client.Timeout = new TimeSpan(_timeout, 0, 0);
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

    }
}