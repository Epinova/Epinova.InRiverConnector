using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public class DocumentFileHelper
    {
        private readonly ChannelHelper _channelHelper;
        private readonly IConfiguration _config;

        public DocumentFileHelper(IConfiguration config, ChannelHelper channelHelper)
        {
            _config = config;
            _channelHelper = channelHelper;
        }

        public static void CopyStream(FileStream inputStream, Stream outputStream)
        {
            const long maxBufferSize = 4096;
            long bufferSize = inputStream.Length < maxBufferSize ? inputStream.Length : maxBufferSize;
            var buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) != 0)
            {
                outputStream.Write(buffer, 0, bytesRead);
            }
        }

        public string SaveCatalogDocument(Entity channel, XDocument doc, string folderNameTimestampComponent)
        {
            string dirPath = Path.Combine(_config.PublicationsRootPath, folderNameTimestampComponent);

            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            try
            {
                XDocument verified = VerifyAndCorrectDocument(doc);
                doc = verified;
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, "Fail to verify the document: ", exception);
            }

            string filePath = Path.Combine(dirPath, Constants.CatalogExportFilename);

            string channelIdentifier = _channelHelper.GetChannelIdentifier(channel);
            IntegrationLogger.Write(LogLevel.Information, $"Saving verified document to path {filePath} for channel: {channelIdentifier}");

            doc.Save(filePath);
            return filePath;
        }

        public string SaveDocument(XDocument doc, string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string filePath = Path.Combine(path, Constants.ResourceExportFilename);

            IntegrationLogger.Write(LogLevel.Information, $"Saving document to path {filePath}.");
            doc.Save(filePath);
            return filePath;
        }

        private List<string> CreateUnwantedEntityTypeList()
        {
            var typeIds = new List<string>
            {
                "Channel",
                "Assortment",
                "Resource",
                "Task",
                "Section",
                "Publication"
            };

            var result = new List<string>();
            foreach (string typeId in typeIds)
            {
                List<FieldSet> fieldSets = RemoteManager.ModelService.GetFieldSetsForEntityType(typeId);
                if (!fieldSets.Any())
                {
                    if (!result.Contains(typeId))
                    {
                        result.Add(typeId);
                    }

                    continue;
                }

                foreach (FieldSet fieldSet in fieldSets)
                {
                    string value = $"{typeId}_{fieldSet.Id}";
                    if (!result.Contains(value))
                    {
                        result.Add(value);
                    }
                }
            }

            return result;
        }

        private XDocument VerifyAndCorrectDocument(XDocument doc)
        {
            List<string> unwantedEntityTypes = CreateUnwantedEntityTypeList();
            var result = new XDocument(doc);
            XElement root = result.Root;
            if (root == null)
            {
                throw new Exception("Can't verify the Catalog.cml as it's empty.");
            }

            IEnumerable<XElement> entryElements = root.Descendants("Entry");
            var codesToBeRemoved = new List<string>();
            foreach (XElement entryElement in entryElements)
            {
                string code = entryElement.Elements("Code").First().Value;
                string metaClassName = entryElement.Elements("MetaData")
                    .Elements("MetaClass")
                    .Elements("Name")
                    .First().Value;

                if (!unwantedEntityTypes.Contains(metaClassName))
                    continue;

                IntegrationLogger.Write(LogLevel.Debug, $"Code {code} will be removed as it has wrong metaclass name ({metaClassName})");
                codesToBeRemoved.Add(code);
            }

            foreach (string code in codesToBeRemoved)
            {
                root.Descendants("Entry").Where(x => x.Element("Code")?.Value == code).Remove();
            }

            return result;
        }
    }
}