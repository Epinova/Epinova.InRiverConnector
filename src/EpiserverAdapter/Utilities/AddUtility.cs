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
        private Configuration ConnectorConfig { get; set; }

        public AddUtility(Configuration connectorConfig)
        {
            this.ConnectorConfig = connectorConfig;
        }

        internal void Add(Entity channelEntity, ConnectorEvent connectorEvent, out bool resourceIncluded)
        {
            resourceIncluded = false;

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating catalog.xml...", 11);
            Dictionary<string, List<XElement>> epiElements = EpiDocument.GetEPiElements(this.ConnectorConfig);

            XDocument doc = EpiDocument.CreateImportDocument(
                channelEntity,
                null,
                null,
                epiElements,
                this.ConnectorConfig);
            string channelIdentifier = ChannelHelper.GetChannelIdentifier(channelEntity);

            string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

            string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, this.ConnectorConfig);

            IntegrationLogger.Write(LogLevel.Information, "Catalog saved with the following:");
            IntegrationLogger.Write(LogLevel.Information, $"Nodes: {epiElements["Nodes"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Entries: {epiElements["Entries"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Relations: {epiElements["Relations"].Count}");
            IntegrationLogger.Write(LogLevel.Information, $"Associations: {epiElements["Associations"].Count}");

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating catalog.xml", 25);

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Generating Resource.xml and saving files to disk...", 26);

            var resourceDocument = Resources.GetDocumentAndSaveFilesToDisk(this.ConnectorConfig.ChannelStructureEntities, this.ConnectorConfig, folderDateTime);
            DocumentFileHelper.SaveDocument(channelIdentifier, resourceDocument, this.ConnectorConfig, folderDateTime);

            string resourceZipFile = string.Format("resource_{0}.zip", folderDateTime);
            DocumentFileHelper.ZipFile(Path.Combine(this.ConnectorConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), resourceZipFile);
            ConnectorEventHelper.UpdateEvent(connectorEvent, "Done generating/saving Resource.xml", 50);
            
            if (this.ConnectorConfig.ActivePublicationMode.Equals(PublicationMode.Automatic))
            {
                IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");
                ConnectorEventHelper.UpdateEvent(connectorEvent, "Sending Catalog.xml to EPiServer...", 51);
                if (EpiApi.Import(Path.Combine(this.ConnectorConfig.PublicationsRootPath, folderDateTime, Configuration.ExportFileName), ChannelHelper.GetChannelGuid(channelEntity, this.ConnectorConfig), this.ConnectorConfig))
                {
                    ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Catalog.xml to EPiServer", 75);
                    EpiApi.SendHttpPost(this.ConnectorConfig, Path.Combine(this.ConnectorConfig.PublicationsRootPath, folderDateTime, zippedfileName));
                }
                else
                {
                    ConnectorEventHelper.UpdateEvent(connectorEvent, "Error while sending Catalog.xml to EPiServer", -1, true);

                    return;
                }

                ConnectorEventHelper.UpdateEvent(connectorEvent, "Sending Resources to EPiServer...", 76);
                if (EpiApi.StartAssetImportIntoEpiServerCommerce(Path.Combine(this.ConnectorConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(this.ConnectorConfig.ResourcesRootPath, folderDateTime), this.ConnectorConfig))
                {
                    ConnectorEventHelper.UpdateEvent(connectorEvent, "Done sending Resources to EPiServer...", 99);
                    EpiApi.SendHttpPost(this.ConnectorConfig, Path.Combine(this.ConnectorConfig.ResourcesRootPath, folderDateTime, resourceZipFile));
                    resourceIncluded = true;
                }
                else
                {
                    ConnectorEventHelper.UpdateEvent(connectorEvent, "Error while sending resources to EPiServer", -1, true);
                }
            }
        }

    }
}
