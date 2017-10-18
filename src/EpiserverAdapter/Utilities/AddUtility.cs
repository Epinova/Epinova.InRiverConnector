using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
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
        private readonly Configuration _connectorConfig;

        public AddUtility(Configuration config, 
                          EpiApi epiApi, 
                          EpiDocumentFactory epiDocumentFactory, 
                          ResourceElementFactory resourceElementFactory, 
                          ChannelHelper channelHelper)
        {
            _connectorConfig = config;
            _epiApi = epiApi;
            _epiDocumentFactory = epiDocumentFactory;
            _resourceElementFactory = resourceElementFactory;
            _channelHelper = channelHelper;
        }

        internal void Add(Entity channelEntity, ConnectorEvent connectorEvent, out bool resourceIncluded)
        {
            resourceIncluded = false;

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating catalog.xml...", 11);
            Dictionary<string, List<XElement>> epiElements = _epiDocumentFactory.GetEPiElements();

            XDocument doc = _epiDocumentFactory.CreateImportDocument(channelEntity, null, null, epiElements);

            string channelIdentifier = _channelHelper.GetChannelIdentifier(channelEntity);

            string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

            string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, _connectorConfig);

            IntegrationLogger.Write(LogLevel.Information, "Catalog saved with the following:");
            IntegrationLogger.Write(LogLevel.Information, $"Nodes: {epiElements["Nodes"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Entries: {epiElements["Entries"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Relations: {epiElements["Relations"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Associations: {epiElements["Associations"].Count}");

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating catalog.xml", 25);
            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating Resource.xml and saving files to disk...", 26);

            var resourceDocument = _resourceElementFactory.GetDocumentAndSaveFilesToDisk(_connectorConfig.ChannelStructureEntities, _connectorConfig, folderDateTime);
            DocumentFileHelper.SaveDocument(channelIdentifier, resourceDocument, _connectorConfig, folderDateTime);

            string resourceZipFile = string.Format("resource_{0}.zip", folderDateTime);
            DocumentFileHelper.ZipFile(Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), resourceZipFile);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating/saving Resource.xml, sending Catalog.xml to EPiServer...", 50);
            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

            var filePath = Path.Combine(_connectorConfig.PublicationsRootPath, folderDateTime, Configuration.ExportFileName);
            var channelGuid = _channelHelper.GetChannelGuid(channelEntity);

            if (_epiApi.Import(filePath, channelGuid, _connectorConfig))
            {
                ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);
                _epiApi.SendHttpPost(_connectorConfig, Path.Combine(_connectorConfig.PublicationsRootPath, folderDateTime, zippedfileName));
            }
            else
            {
                ConnectorEventHelper.UpdateEvent(connectorEvent, "Error while sending Catalog.xml to EPiServer", -1, true);
                return;
            }

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Sending Resources to EPiServer...", 76);
            if (_epiApi.ImportResources(Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime), _connectorConfig))
            {
                ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Resources to EPiServer...", 99);
                _epiApi.SendHttpPost(_connectorConfig, Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime, resourceZipFile));
                resourceIncluded = true;
            }
            else
            {
                ConnectorEventHelper.UpdateEvent(connectorEvent, "Error while sending resources to EPiServer", -1, true);
            }
        }

    }
}
