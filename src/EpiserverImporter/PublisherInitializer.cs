using System.Web.Http;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;
using InitializationModule = EPiServer.Web.InitializationModule;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    [InitializableModule]
    [ModuleDependency(typeof(InitializationModule))]
    public class PublisherInitializer : IConfigurableModule
    {
        public void ConfigureContainer(ServiceConfigurationContext context)
        {
            context.StructureMap().Configure(x =>
            {
                x.For<ICatalogImporter>().Use<CatalogImporter>();
                x.For<ICatalogService>().Use<CatalogService>();
            });
        }

        /// <summary>
        /// Initializate the inRiver Web API.
        /// </summary>
        /// <remarks>
        /// This method is called once after CMS has been initialized
        /// </remarks>
        /// <param name="context"></param>
        public void Initialize(InitializationEngine context)
        {
            HttpConfiguration config = GlobalConfiguration.Configuration;

            //// For debug purposes, turn this on. This should be done in the web project
            //// and not in a packaged library.
            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
            config.Routes.MapHttpRoute(
                "inRiverApi",
                "inriverapi/{controller}/{action}/{id}",
                new { id = RouteParameter.Optional });
        }

        public void Uninitialize(InitializationEngine context)
        {
        }
    }
}