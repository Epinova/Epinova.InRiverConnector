using System.Web.Http.Filters;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using Newtonsoft.Json;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    internal class ImporterExceptionFilterAttribute : ExceptionFilterAttribute
    {
        public override void OnException(HttpActionExecutedContext actionExecutedContext)
        {
            var logger = ServiceLocator.Current.GetInstance<ILogger>();

            var uri = actionExecutedContext.Request.RequestUri;
            var actionName = actionExecutedContext.ActionContext.ActionDescriptor.ActionName;
            var arguments = JsonConvert.SerializeObject(actionExecutedContext.ActionContext.ActionArguments);

            logger.Error($"Error when importing data from inRiver. Request URI: {uri}. Action: {actionName}. Arguments: {arguments}", actionExecutedContext.Exception);

            base.OnException(actionExecutedContext);
        }
    }
}