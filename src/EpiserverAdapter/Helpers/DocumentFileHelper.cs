using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
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
        private readonly IConfiguration _config;
        private readonly ChannelHelper _channelHelper;

        public DocumentFileHelper(IConfiguration config, ChannelHelper channelHelper)
        {
            _config = config;
            _channelHelper = channelHelper;
        }

        public void SaveDocument(string channelIdentifier, XDocument doc, string folderDateTime)
        {
            string dirPath = Path.Combine(_config.ResourcesRootPath, folderDateTime);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            string filePath = Path.Combine(dirPath, "Resources.xml");

            IntegrationLogger.Write(LogLevel.Information, $"Saving document to path {filePath} for channel:{channelIdentifier}");
            doc.Save(filePath);
        }

        public string SaveAndZipDocument(Entity channel, XDocument doc, string folderName)
        {
            string dirPath = Path.Combine(_config.PublicationsRootPath, folderName);
            
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

            string filePath = Path.Combine(dirPath, Constants.ExportFilename);

            var channelIdentifier = _channelHelper.GetChannelIdentifier(channel);
            IntegrationLogger.Write(LogLevel.Information, $"Saving verified document to path {filePath} for channel: {channelIdentifier}");
            
            doc.Save(filePath);
            string fullZippedFileName = $"inRiverExport_{channelIdentifier}_{DateTime.Now.ToString("yyyyMMdd-HHmmss")}.zip";

            ZipFile(filePath, fullZippedFileName);

            return fullZippedFileName;
        }

        public void ZipFile(Package zip, FileInfo fi, string destFilename)
        {
            Uri uri = PackUriHelper.CreatePartUri(new Uri(destFilename, UriKind.Relative));
            if (zip.PartExists(uri))
            {
                zip.DeletePart(uri);
            }

            PackagePart part = zip.CreatePart(uri, string.Empty, CompressionOption.Normal);
            using (FileStream fileStream = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read))
            {
                if (part != null)
                {
                    using (Stream dest = part.GetStream())
                    {
                        CopyStream(fileStream, dest);
                    }
                }
            }
        }

        public static void CopyStream(FileStream inputStream, Stream outputStream)
        {
            const long MaxbuffertSize = 4096;
            long bufferSize = inputStream.Length < MaxbuffertSize ? inputStream.Length : MaxbuffertSize;
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) != 0)
            {
                outputStream.Write(buffer, 0, bytesRead);
            }
        }

        public void ZipFile(string fileToZip, string zippedFileName)
        {
            string path = Path.GetDirectoryName(fileToZip);
            if (path != null)
            {
                using (Package zip = Package.Open(Path.Combine(path, zippedFileName), FileMode.Create))
                {
                    string destFilename = ".\\" + Path.GetFileName(fileToZip);
                    ZipFile(zip, new FileInfo(fileToZip), destFilename);
                }
            }
        }

        private XDocument VerifyAndCorrectDocument(XDocument doc)
        {
            var unwantedEntityTypes = CreateUnwantedEntityTypeList();
            XDocument result = new XDocument(doc);
            XElement root = result.Root;
            if (root == null)
            {
                throw new Exception("Can't verify the Catalog.cml as it's empty.");
            }

            var entryElements = root.Descendants("Entry");
            List<string> codesToBeRemoved = new List<string>();
            foreach (XElement entryElement in entryElements)
            {
                string code = entryElement.Elements("Code").First().Value;
                string metaClassName =
                    entryElement.Elements("MetaData").Elements("MetaClass").Elements("Name").First().Value;

                if (unwantedEntityTypes.Contains(metaClassName))
                {
                    IntegrationLogger.Write(
                        LogLevel.Debug,
                        string.Format("Code {0} will be removed as it has wrong metaclass name ({1})", code, metaClassName));
                    codesToBeRemoved.Add(code);
                }
            }

            foreach (string code in codesToBeRemoved)
            {
                string theCode = code;
                root.Descendants("Entry").Where(
                    e =>
                        {
                            XElement codeElement = e.Element("Code");
                            return codeElement != null && codeElement.Value == theCode;
                        }).Remove();
            }

            return result;
        }

        private List<string> CreateUnwantedEntityTypeList()
        {
            List<string> typeIds = new List<string>
                                       {
                                           "Channel",
                                           "Assortment",
                                           "Resource",
                                           "Task",
                                           "Section",
                                           "Publication"
                                       };
            List<string> result = new List<string>();
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
                    string value = string.Format("{0}_{1}", typeId, fieldSet.Id);
                    if (!result.Contains(value))
                    {
                        result.Add(value);
                    }
                }
            }

            return result;
        }
    }
}