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
        private readonly DocumentFileHelper _documentFileHelper;
        private readonly Configuration _connectorConfig;

        public AddUtility(Configuration config, 
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

        internal void Add(Entity channel, ConnectorEvent connectorEvent, List<StructureEntity> structureEntities, out bool resourceIncluded)
        {
            resourceIncluded = false;

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating catalog.xml...", 11);
            var epiElements = _epiDocumentFactory.GetEPiElements(structureEntities);

            var doc = _epiDocumentFactory.CreateImportDocument(channel, null, null, epiElements);

            string channelIdentifier = _channelHelper.GetChannelIdentifier(channel);

            string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

            string zippedfileName = _documentFileHelper.SaveAndZipDocument(channel, doc, folderDateTime);

            LogSaveStatement(epiElements);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating catalog.xml. Generating Resource.xml and saving files to disk...", 26);

            var resourceDocument = _resourceElementFactory.GetDocumentAndSaveFilesToDisk(structureEntities, _connectorConfig, folderDateTime);
            _documentFileHelper.SaveDocument(channelIdentifier, resourceDocument, _connectorConfig, folderDateTime);

            string zipFileName = $"resource_{folderDateTime}.zip";
            var fileToZip = Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime, "Resources.xml");

            _documentFileHelper.ZipFile(fileToZip, zipFileName);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating/saving Resource.xml, sending Catalog.xml to EPiServer...", 50);
            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

            var filePath = Path.Combine(_connectorConfig.PublicationsRootPath, folderDateTime, Configuration.ExportFileName);

            var channelGuid = _channelHelper.GetChannelGuid(channel);

            _epiApi.Import(filePath, channelGuid, _connectorConfig);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);

            _epiApi.SendHttpPost(_connectorConfig, Path.Combine(_connectorConfig.PublicationsRootPath, folderDateTime, zippedfileName));

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Sending Resources to EPiServer...", 76);

            _epiApi.ImportResources(fileToZip, Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime), _connectorConfig);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Resources to EPiServer...", 99);

            _epiApi.SendHttpPost(_connectorConfig, Path.Combine(_connectorConfig.ResourcesRootPath, folderDateTime, zipFileName));

            resourceIncluded = true;
        }

        private static void LogSaveStatement(Dictionary<string, List<XElement>> epiElements)
        {
            IntegrationLogger.Write(LogLevel.Information, "Catalog saved with the following:");
            IntegrationLogger.Write(LogLevel.Information, $"Nodes: {epiElements["Nodes"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Entries: {epiElements["Entries"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Relations: {epiElements["Relations"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Associations: {epiElements["Associations"].Count}");
        }
    }
}
