using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using EPiServer;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using inRiver.EPiServerCommerce.Importer.EventHandling;
using Mediachase.Commerce.Catalog;

namespace inRiver.EPiServerCommerce.Importer
{
    public class CatalogImporter : ICatalogImporter
    {
        private readonly ILogger _logger;
        private readonly ReferenceConverter _referenceConverter;
        private readonly IContentRepository _contentRepository;

        public CatalogImporter(ILogger logger, ReferenceConverter referenceConverter, IContentRepository contentRepository)
        {
            _logger = logger;
            _referenceConverter = referenceConverter;
            _contentRepository = contentRepository;
        }

        private bool RunICatalogImportHandlers => GetBoolSetting("inRiver.RunICatalogImportHandlers");

        private bool RunIResourceImporterHandlers => GetBoolSetting("inRiver.RunIResourceImporterHandlers");

        private bool RunIDeleteActionsHandlers => GetBoolSetting("inRiver.RunIDeleteActionsHandlers");

        private bool RunIInRiverEventsHandlers => GetBoolSetting("inRiver.RunIInRiverEventsHandlers");

        private bool GetBoolSetting(string key)
        {
            var setting = ConfigurationManager.AppSettings[key];
            return setting != null && setting.Equals(key, StringComparison.CurrentCultureIgnoreCase);
        }

        public void DeleteCatalogEntry(string code)
        {
            List<IDeleteActionsHandler> deleteHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            try
            {
                var contentReference = _referenceConverter.GetContentLink(code);
                var entry = _contentRepository.Get<EntryContentBase>(contentReference);

                if (entry == null)
                {
                    _logger.Warning($"Could not find catalog entry with id: {code}. No entry is deleted");
                    return;
                }
                if (RunIDeleteActionsHandlers)
                {
                    foreach (IDeleteActionsHandler handler in deleteHandlers)
                    {
                        handler.PreDeleteCatalogEntry(entry);
                    }
                }

                _contentRepository.Delete(entry.ContentLink, true);

                if (RunIDeleteActionsHandlers)
                {
                    foreach (IDeleteActionsHandler handler in deleteHandlers)
                    {
                        handler.PostDeleteCatalogEntry(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Could not delete catalog entry with code {code}", ex);
                throw;
            }
        }
    }
}