using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Utilities
{
    public class CvlUtility
    {
        private readonly EpiApi _epiAPi;
        private Configuration Config { get; set; }

        public CvlUtility(Configuration config)
        {
            Config = config;
            _epiAPi = new EpiApi(config);
        }

        public void AddCvl(string cvlId, string folderDateTime)
        {
            List<XElement> metafields = new List<XElement>();
            List<FieldType> affectedFieldTypes = BusinessHelper.GetFieldTypesWithCVL(cvlId);

            foreach (FieldType fieldType in affectedFieldTypes)
            {
                if (EpiMappingHelper.SkipField(fieldType, Config))
                {
                    continue;
                }

                XElement metaField = EpiElement.InRiverFieldTypeToMetaField(fieldType, Config);

                if (fieldType.DataType.Equals(DataType.CVL))
                {
                    metaField.Add(EpiMappingHelper.GetDictionaryValues(fieldType, Config));
                }

                if (metafields.Any(
                    mf =>
                    {
                        XElement nameElement = mf.Element("Name");
                        return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, Config));
                    }))
                {
                    XElement existingMetaField =
                        metafields.FirstOrDefault(
                            mf =>
                            {
                                XElement nameElement = mf.Element("Name");
                                return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, Config));
                            });

                    if (existingMetaField == null)
                    {
                        continue;
                    }

                    var movefields = metaField.Elements("OwnerMetaClass");
                    existingMetaField.Add(movefields);
                }
                else
                {
                    metafields.Add(metaField);
                }
            }

            XElement metaData = new XElement("MetaDataPlusBackup", new XAttribute("version", "1.0"), metafields.ToArray());
            XDocument doc = _epiDocumentFactory.CreateDocument(null, metaData, null, Config);

            Entity channelEntity = RemoteManager.DataService.GetEntity(Config.ChannelId, LoadLevel.DataOnly);
            if (channelEntity == null)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not find channel {0} for cvl add", Config.ChannelId));
                return;
            }

            string channelIdentifier = ChannelHelper.GetChannelIdentifier(channelEntity);

            string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, Config);
            IntegrationLogger.Write(LogLevel.Debug, string.Format("catalog {0} saved", channelIdentifier));

            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

            if (_epiAPi.Import(Path.Combine(Config.PublicationsRootPath, folderDateTime, Configuration.ExportFileName), ChannelHelper.GetChannelGuid(channelEntity, Config), Config))
            {
                _epiAPi.SendHttpPost(Config, Path.Combine(Config.PublicationsRootPath, folderDateTime, zippedfileName));
            }
        }
    }
}
