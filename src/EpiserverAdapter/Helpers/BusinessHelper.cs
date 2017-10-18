using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public class BusinessHelper
    {
        private static List<CVLValue> cvlValues;

        private static List<CVL> cvls;

        public static List<CVLValue> CVLValues
        {
            get => cvlValues ?? (cvlValues = RemoteManager.ModelService.GetAllCVLValues());
            set => cvlValues = value;
        }

        public static List<CVL> CvLs
        {
            get => cvls ?? (cvls = RemoteManager.ModelService.GetAllCVLs());
            set => cvls = value;
        }

        public static bool FieldTypeIsMultiLanguage(FieldType fieldType)
        {
            if (fieldType.DataType.Equals(DataType.LocaleString))
            {
                return true;
            }

            if (fieldType.DataType.Equals(DataType.CVL))
            {
                CVL cvl = RemoteManager.ModelService.GetCVL(fieldType.CVLId);

                if (cvl == null)
                {
                    return false;
                }

                return cvl.DataType.Equals(DataType.LocaleString);
            }

            return false;
        }
        
        public static string GetAllowSearch(FieldType fieldType)
        {
            if (fieldType.Settings.ContainsKey("AllowsSearch"))
            {
                return fieldType.Settings["AllowsSearch"];
            }

            return "true";
        }

        public static string GetDisplayTemplateEntity(Entity entity)
        {
            Field displayTemplateField =
                entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("displaytemplate"));

            if (displayTemplateField == null || displayTemplateField.IsEmpty())
            {
                return null;
            }

            return displayTemplateField.Data.ToString();
        }

        public static IEnumerable<string> CultureInfosToStringArray(CultureInfo[] cultureInfo)
        {
            return cultureInfo.Select(ci => ci.Name.ToLower()).ToArray();
        }

        public static string GetStartDateFromEntity(Entity entity)
        {
            Field startDateField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("startdate"));

            if (startDateField == null || startDateField.IsEmpty())
            {
                return DateTime.UtcNow.ToString("u");
            }

            return ((DateTime)startDateField.Data).ToUniversalTime().ToString("u");
        }

        public static string GetEndDateFromEntity(Entity entity)
        {
            Field endDateField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("enddate"));

            if (endDateField == null || endDateField.IsEmpty())
            {
                return DateTime.UtcNow.AddYears(100).ToString("u");
            }

            return ((DateTime)endDateField.Data).ToUniversalTime().ToString("u");
        }

        public static string FieldIsUseInCompare(FieldType fieldType)
        {
            string value = "False";

            if (fieldType.Settings.ContainsKey("UseInComparing"))
            {
                value = fieldType.Settings["UseInComparing"];
                if (!(value.ToLower().Equals("false") || value.ToLower().Equals("true")))
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Fieldtype with id {0} has invalid UseInComparing setting", fieldType.Id));
                }
            }

            return value;
        }

        public static string GetDisplayNameFromEntity(Entity entity, Configuration config, int maxLength)
        {
            Field displayNameField = entity.DisplayName;

            string returnString;

            if (displayNameField == null || displayNameField.IsEmpty())
            {
                returnString = string.Format("[{0}]", entity.Id);
            }
            else if (displayNameField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                LocaleString ls = (LocaleString)displayNameField.Data;
                if (string.IsNullOrEmpty(ls[config.LanguageMapping[config.ChannelDefaultLanguage]]))
                {
                    returnString = string.Format("[{0}]", entity.Id);
                }
                else
                {
                    returnString = ls[config.LanguageMapping[config.ChannelDefaultLanguage]];
                }
            }
            else
            {
                returnString = displayNameField.Data.ToString();
            }

            if (maxLength > 0)
            {
                int lenght = returnString.Length;
                if (lenght > maxLength)
                {
                    returnString = returnString.Substring(0, maxLength - 1);
                }
            }

            return returnString;
        }

        public static string GetSeoUriFromEntity(Entity entity, CultureInfo ci, Configuration configuration)
        {
            Field seoUriField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("seouri"));

            if (seoUriField == null || seoUriField.IsEmpty())
            {
                return string.Empty;
            }

            if (seoUriField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                return configuration.ChannelIdPrefix + ((LocaleString)seoUriField.Data)[ci];
            }

            return configuration.ChannelIdPrefix + seoUriField.Data;
        }

        public static string GetSeoUriSegmentFromEntity(Entity entity, CultureInfo ci, Configuration config)
        {
            Field seoUriSegmentField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("seourisegment"));

            if (seoUriSegmentField == null || seoUriSegmentField.IsEmpty())
            {
                return string.Empty;
            }

            if (seoUriSegmentField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                return config.ChannelIdPrefix + ((LocaleString)seoUriSegmentField.Data)[ci];
            }

            return config.ChannelIdPrefix + seoUriSegmentField.Data;
        }

        public static string GetSeoTitleFromEntity(Entity entity, CultureInfo ci)
        {
            Field seoTitleField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("seotitle"));

            if (seoTitleField == null || seoTitleField.IsEmpty())
            {
                return string.Empty;
            }

            if (seoTitleField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                return ((LocaleString)seoTitleField.Data)[ci];
            }

            return seoTitleField.Data.ToString();
        }

        public static string GetSeoDescriptionFromEntity(Entity entity, CultureInfo ci)
        {
            Field seoDescriptionField =
                entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("seodescription"));

            if (seoDescriptionField == null || seoDescriptionField.IsEmpty())
            {
                return string.Empty;
            }

            if (seoDescriptionField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                return ((LocaleString)seoDescriptionField.Data)[ci];
            }

            return seoDescriptionField.Data.ToString();
        }

        public static string GetSeoKeywordsFromEntity(Entity entity, CultureInfo ci)
        {
            Field seoKeywordsField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("seokeywords"));

            if (seoKeywordsField == null || seoKeywordsField.IsEmpty())
            {
                return string.Empty;
            }

            if (seoKeywordsField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                return ((LocaleString)seoKeywordsField.Data)[ci];
            }

            return seoKeywordsField.Data.ToString();
        }

        public static List<XElement> GetCVLValues(Field field, Configuration configuration)
        {
            var dataElements = new List<XElement>();
            if (field == null || field.IsEmpty())
                return dataElements;

            var cvl = CvLs.FirstOrDefault(c => c.Id.Equals(field.FieldType.CVLId));
            if (cvl == null)
                return dataElements;

            if (cvl.DataType == DataType.LocaleString)
            {
                foreach (var language in configuration.LanguageMapping)
                {
                    var dataElement = GetCvlDataElement(field, configuration, language.Key);
                    dataElements.Add(dataElement);
                }
            }
            else
            {
                var dataElement = GetCvlDataElement(field, configuration, configuration.ChannelDefaultLanguage);
                dataElements.Add(dataElement);
            }
                
            return dataElements;
        }

        private static XElement GetCvlDataElement(Field field, Configuration configuration, CultureInfo language)
        {
            var dataElement = new XElement(
                "Data",
                new XAttribute("language", language.Name.ToLower()),
                new XAttribute("value", GetCvlFieldValue(field, language, configuration)));

            return dataElement;
        }

        private static string GetCvlFieldValue(Field field, CultureInfo language, Configuration config)
        {
            if (config.ActiveCVLDataMode.Equals(CVLDataMode.Keys) ||
                FieldIsExcludedCatalogEntryMarkets(field))
            {
                return field.Data.ToString();
            }
           
            string[] keys = field.Data.ToString().Split(';');
            var cvlId = field.FieldType.CVLId;

            var returnValues = new List<string>();
                
            foreach (var key in keys)
            {
                var cvlValue = CVLValues.FirstOrDefault(cv => cv.CVLId.Equals(cvlId) && cv.Key.Equals(key));
                if (cvlValue?.Value == null)
                    continue;

                string finalizedValue;

                if (field.FieldType.DataType.Equals(DataType.LocaleString))
                {
                    LocaleString ls = (LocaleString)cvlValue.Value;
                        
                    if (!ls.ContainsCulture(language))
                        return null;

                    var value = ls[language];
                    finalizedValue = GetFinalizedValue(config, value, key);
                }
                else
                {
                    var value = cvlValue.Value.ToString();
                    finalizedValue = GetFinalizedValue(config, value, key);
                }

                returnValues.Add(finalizedValue);
            }

            return string.Join(";", returnValues);
        }

        private static bool FieldIsExcludedCatalogEntryMarkets(Field field)
        {
            return field.FieldType.Settings.ContainsKey("EPiMetaFieldName") &&
                   field.FieldType.Settings["EPiMetaFieldName"].Equals("_ExcludedCatalogEntryMarkets");
        }

        private static string GetFinalizedValue(Configuration config, string value, string key)
        {
            if (config.ActiveCVLDataMode.Equals(CVLDataMode.KeysAndValues))
            {
                value = key + Configuration.CVLKeyDelimiter + value;
            }
            return value;
        }

        public static string GetFlatFieldData(Field field, Configuration configuration)
        {
            if (field == null || field.IsEmpty())
            {
                return string.Empty;
            }

            if (field.FieldType.DataType.Equals(DataType.Boolean))
            {
                return ((bool)field.Data).ToString();
            }
            if (field.FieldType.DataType.Equals(DataType.DateTime))
            {
                return ((DateTime)field.Data).ToString("O");
            }
            if (field.FieldType.DataType.Equals(DataType.Double))
            {
                return ((double)field.Data).ToString(CultureInfo.InvariantCulture);
            }
            if (field.FieldType.DataType.Equals(DataType.File))
            {
                return field.Data.ToString();
            }
            if (field.FieldType.DataType.Equals(DataType.Integer))
            {
                return field.Data.ToString();
            }
            if (field.FieldType.DataType.Equals(DataType.String))
            {
                return field.Data.ToString();
            }
            if (field.FieldType.DataType.Equals(DataType.Xml))
            {
                return field.Data.ToString();
            }

            return string.Empty;
        }

        internal static void CompareAndParseSkuXmls(string oldXml, string newXml, out List<XElement> skusToAdd, out List<XElement> skusToDelete)
        {
            XDocument oldDoc = XDocument.Parse(oldXml);
            XDocument newDoc = XDocument.Parse(newXml);

            List<XElement> oldSkus = oldDoc.Descendants().Elements("SKU").ToList();
            List<XElement> newSkus = newDoc.Descendants().Elements("SKU").ToList();

            List<string> removables = new List<string>();

            foreach (XElement elem in oldSkus)
            {
                XAttribute id = elem.Attribute("id");
                if (newSkus.Exists(e => e.Attribute("id").Value == id.Value))
                {
                    if (!removables.Exists(y => y == id.Value))
                    {
                        removables.Add(id.Value);
                    }
                }
            }

            foreach (string id in removables)
            {
                oldSkus.RemoveAll(e => e.Attribute("id").Value == id);
                newSkus.RemoveAll(e => e.Attribute("id").Value == id);
            }

            skusToAdd = newSkus;
            skusToDelete = oldSkus;
        }
    }
}
