using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting.Connect;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Utilities
{
    public class AddUtility
    {
        private readonly EpiApi _epiApi;
        private readonly EpiDocumentFactory _epiDocumentFactory;
        private readonly ResourceElementFactory _resourceElementFactory;
        private readonly ChannelHelper _channelHelper;
        private readonly DocumentFileHelper _documentFileHelper;
        private readonly IConfiguration _connectorConfig;

        public AddUtility(IConfiguration config, 
                          EpiApi epiApi, 
                          EpiDocumentFactory epiDocumentFactory, 
                          ResourceElementFactory resourceElementFactory, 
                          ChannelHelper channelHelper,
                          DocumentFileHelper documentFileHelper)
        {
            _connectorConfig = config;
            _epiApi = epiApi;
            _epiDocumentFactory = epiDocumentFactory;
            _resourceElementFactory = resourceElementFactory;
            _channelHelper = channelHelper;
            _documentFileHelper = documentFileHelper;
        }

        internal void Add(Entity channel, ConnectorEvent connectorEvent, List<StructureEntity> structureEntities)
        {
            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating catalog.xml...", 11);
            var epiElements = _epiDocumentFactory.GetEPiElements(structureEntities);
            
            var channelIdentifier = _channelHelper.GetChannelIdentifier(channel);
            var folderDateTime = DateTime.Now.ToString(Constants.PublicationFolderNameTimeComponent);

            var doc = _epiDocumentFactory.CreateImportDocument(channel, null, null, epiElements);
            var savedCatalogDocument = _documentFileHelper.SaveCatalogDocument(channel, doc, folderDateTime);

            LogSaveStatement(epiElements);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating catalog.xml. Generating Resource.xml and saving files to disk...", 26);

            var resourcesBasePath = Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime);
            var resourceDocument = _resourceElementFactory.GetResourcesNodeForChannelEntities(structureEntities, resourcesBasePath);
            var resourceDocumentPath = _documentFileHelper.SaveDocument(channelIdentifier, resourceDocument, resourcesBasePath);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating/saving Resource.xml, sending Catalog.xml to EPiServer...", 50);

            _epiApi.ImportCatalog(savedCatalogDocument);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);

            _epiApi.NotifyEpiserverPostImport(Path.Combine(_connectorConfig.PublicationsRootPath, folderDateTime, savedCatalogDocument));

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Sending Resources to EPiServer...", 76);

            _epiApi.ImportResources(resourceDocumentPath, resourcesBasePath);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Resources to EPiServer...", 99);

            _epiApi.NotifyEpiserverPostImport(resourceDocumentPath);
        }

        private static void LogSaveStatement(CatalogElementContainer epiElements)
        {
            IntegrationLogger.Write(LogLevel.Information, "Catalog saved with the following:");
            IntegrationLogger.Write(LogLevel.Information, $"Nodes: {epiElements.Nodes.Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Entries: {epiElements.Entries.Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Relations: {epiElements.Relations.Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Associations: {epiElements.Associations.Count}");
        }
    }
}
