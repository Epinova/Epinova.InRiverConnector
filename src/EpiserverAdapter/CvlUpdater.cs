using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.EpiserverAdapter.XmlFactories;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Connect;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;
using inRiver.Remoting.Query;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class CvlUpdater
    {
        private readonly CatalogDocumentFactory _catalogDocumentFactory;
        private readonly IConfiguration _config;
        private readonly DocumentFileHelper _documentFileHelper;
        private readonly EpiApi _epiApi;

        public CvlUpdater(IConfiguration config, CatalogDocumentFactory catalogDocumentFactory, EpiApi epiApi, DocumentFileHelper documentFileHelper)
        {
            _config = config;
            _catalogDocumentFactory = catalogDocumentFactory;
            _epiApi = epiApi;
            _documentFileHelper = documentFileHelper;
        }

        public async Task<ConnectorEvent> CVLValueUpdatedAsync(Entity channel, string cvlId, string cvlValueKey)
        {
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config,
                ConnectorEventType.CVLValueUpdated,
                $"CVL value updated, updating values in channel: {channel.DisplayName.Data}", 0);

            IEnumerable<FieldType> cvlFieldTypes = RemoteManager.ModelService.GetAllFieldTypes().Where(x => x.CVLId == cvlId);

            List<Criteria> criteria = cvlFieldTypes.Select(cvlFieldType => new Criteria
            {
                FieldTypeId = cvlFieldType.Id,
                Value = cvlValueKey,
                Operator = Operator.Equal
            }).ToList();

            var query = new Query { Criteria = criteria, Join = Join.Or };
            List<Entity> entities = RemoteManager.DataService.Search(query, LoadLevel.DataOnly);
            IntegrationLogger.Write(LogLevel.Debug, $"Found {entities.Count} entities with the CVL {cvlId} to update. Value-key to update: {cvlValueKey}.");

            XDocument updateDocument = _catalogDocumentFactory.CreateUpdateDocument(channel, entities);
            string folderDateTime = DateTime.Now.ToString(Constants.PublicationFolderNameTimeComponent);

            string savedCatalogDocument = _documentFileHelper.SaveCatalogDocument(channel, updateDocument, folderDateTime);

            await _epiApi.ImportCatalogAsync(savedCatalogDocument);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);

            await _epiApi.NotifyEpiserverPostImportAsync(Path.Combine(_config.PublicationsRootPath, folderDateTime, savedCatalogDocument));

            return connectorEvent;
        }
    }
}