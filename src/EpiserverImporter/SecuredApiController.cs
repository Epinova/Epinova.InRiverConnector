using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class SecuredApiController : ApiController
    {
        private const string ApiKeyName = "apikey";

        private static string ApiKeyValue => ConfigurationManager.AppSettings["InRiverPimConnector.APIKey"];

        public override Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            if (!ValidateApiKey(controllerContext.Request))
            {
                HttpResponseMessage resp = new HttpResponseMessage(HttpStatusCode.Forbidden);
                TaskCompletionSource<HttpResponseMessage> tsc = new TaskCompletionSource<HttpResponseMessage>();
                tsc.SetResult(resp);
                return tsc.Task;
            }

            return base.ExecuteAsync(controllerContext, cancellationToken);
        }

        protected virtual bool ValidateApiKey(HttpRequestMessage request)
        {
            if (string.IsNullOrEmpty(ApiKeyValue))
                return false;

            return request.Headers.GetValues(ApiKeyName).FirstOrDefault() == ApiKeyValue;
        }
    }
}
