using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
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
        private readonly IConfiguration _config;
        private readonly EpiDocumentFactory _epiDocumentFactory;
        private readonly EpiApi _epiApi;
        private readonly DocumentFileHelper _documentFileHelper;

        public CvlUpdater(IConfiguration config, 
                          EpiDocumentFactory epiDocumentFactory, 
                          EpiApi epiApi,
                          DocumentFileHelper documentFileHelper)
        {
            _config = config;
            _epiDocumentFactory = epiDocumentFactory;
            _epiApi = epiApi;
            _documentFileHelper = documentFileHelper;
        }

        public ConnectorEvent CVLValueUpdated(Entity channel, string cvlId, string cvlValueKey)
        {
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, 
                                        ConnectorEventType.CVLValueUpdated, 
                                        $"CVL value updated, updating values in channel: {channel.DisplayName.Data}", 0);

            var cvlFieldTypes = RemoteManager.ModelService.GetAllFieldTypes().Where(x => x.CVLId == cvlId);
            
            var criterias = cvlFieldTypes.Select(cvlFieldType => new Criteria
            {
                FieldTypeId = cvlFieldType.Id,
                Value = cvlValueKey,
                Operator = Operator.Equal
            }).ToList();

            var query = new Query { Criteria = criterias, Join = Join.Or };
            var entities = RemoteManager.DataService.Search(query, LoadLevel.DataOnly);
            IntegrationLogger.Write(LogLevel.Debug, $"Found {entities.Count} entities with the CVL {cvlId} to update. Value-key to update: {cvlValueKey}.");

            var updateDocument = _epiDocumentFactory.CreateUpdateDocument(channel, entities);
            var folderDateTime = DateTime.Now.ToString(Constants.PublicationFolderNameTimeComponent);

            var savedCatalogDocument = _documentFileHelper.SaveCatalogDocument(channel, updateDocument, folderDateTime);

            _epiApi.ImportCatalog(savedCatalogDocument);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);

            _epiApi.NotifyEpiserverPostImport(Path.Combine(_config.PublicationsRootPath, folderDateTime, savedCatalogDocument));

            return connectorEvent;
        }
    }
}