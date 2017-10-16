using System.Web.Http.Filters;
using EPiServer.Logging;
using EPiServer.ServiceLocation;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    internal class ImporterExceptionFilterAttribute : ExceptionFilterAttribute
    {
        public override void OnException(HttpActionExecutedContext actionExecutedContext)
        {
            var logger = ServiceLocator.Current.GetInstance<ILogger>();

            logger.Error("Error in inRiver import!", actionExecutedContext.Exception);

            base.OnException(actionExecutedContext);
        }
    }
}