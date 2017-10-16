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
        private Configuration CvlUtilConfig { get; set; }

        public CvlUtility(Configuration cvlUtilConfig)
        {
            CvlUtilConfig = cvlUtilConfig;
        }

        public void AddCvl(string cvlId, string folderDateTime)
        {
            List<XElement> metafields = new List<XElement>();
            List<FieldType> affectedFieldTypes = BusinessHelper.GetFieldTypesWithCVL(cvlId);

            foreach (FieldType fieldType in affectedFieldTypes)
            {
                if (EpiMappingHelper.SkipField(fieldType, CvlUtilConfig))
                {
                    continue;
                }

                XElement metaField = EpiElement.InRiverFieldTypeToMetaField(fieldType, CvlUtilConfig);

                if (fieldType.DataType.Equals(DataType.CVL))
                {
                    metaField.Add(EpiMappingHelper.GetDictionaryValues(fieldType, CvlUtilConfig));
                }

                if (metafields.Any(
                    mf =>
                    {
                        XElement nameElement = mf.Element("Name");
                        return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, CvlUtilConfig));
                    }))
                {
                    XElement existingMetaField =
                        metafields.FirstOrDefault(
                            mf =>
                            {
                                XElement nameElement = mf.Element("Name");
                                return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, CvlUtilConfig));
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
            XDocument doc = EpiDocument.CreateDocument(null, metaData, null, CvlUtilConfig);

            Entity channelEntity = RemoteManager.DataService.GetEntity(CvlUtilConfig.ChannelId, LoadLevel.DataOnly);
            if (channelEntity == null)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not find channel {0} for cvl add", CvlUtilConfig.ChannelId));
                return;
            }

            string channelIdentifier = ChannelHelper.GetChannelIdentifier(channelEntity);

            string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, CvlUtilConfig);
            IntegrationLogger.Write(LogLevel.Debug, string.Format("catalog {0} saved", channelIdentifier));

            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

            if (EpiApi.Import(Path.Combine(CvlUtilConfig.PublicationsRootPath, folderDateTime, Configuration.ExportFileName), ChannelHelper.GetChannelGuid(channelEntity, CvlUtilConfig), CvlUtilConfig))
            {
                EpiApi.SendHttpPost(CvlUtilConfig, Path.Combine(CvlUtilConfig.PublicationsRootPath, folderDateTime, zippedfileName));
            }
        }
    }
}
