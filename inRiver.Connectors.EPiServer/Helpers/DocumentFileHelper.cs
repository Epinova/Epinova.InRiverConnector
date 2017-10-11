using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml.Linq;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace inRiver.EPiServerCommerce.CommerceAdapter.Helpers
{
    public class DocumentFileHelper
    {
        public static void SaveDocument(string channelIdentifier, XDocument doc, Configuration config, string folderDateTime)
        {
            string dirPath = Path.Combine(config.ResourcesRootPath, folderDateTime);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            string filePath = Path.Combine(dirPath, "Resources.xml");
            IntegrationLogger.Write(
                LogLevel.Information,
                string.Format("Saving document to path {0} for channel:{1}", filePath, channelIdentifier));
            doc.Save(filePath);
        }

        public static string SaveAndZipDocument(string channelIdentifier, XDocument doc, string folderDateTime, Configuration config)
        {
            string dirPath = Path.Combine(config.PublicationsRootPath, folderDateTime);
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

            string filePath = Path.Combine(dirPath, Configuration.ExportFileName);
            IntegrationLogger.Write(
                LogLevel.Information,
                string.Format("Saving verified document to path {0} for channel:{1}", filePath, channelIdentifier));
            doc.Save(filePath);
            string fullZippedFileName = string.Format(
                "inRiverExport_{0}_{1}.zip",
                channelIdentifier,
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            ZipFile(filePath, fullZippedFileName);

            return fullZippedFileName;
        }

        public static void ZipFile(Package zip, FileInfo fi, string destFilename)
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

        public static void ZipFile(string fileToZip, string zippedFileName)
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

        private static XDocument VerifyAndCorrectDocument(XDocument doc)
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

        private static List<string> CreateUnwantedEntityTypeList()
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