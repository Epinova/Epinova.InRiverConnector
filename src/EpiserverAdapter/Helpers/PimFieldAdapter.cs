using System;
using System.Collections.Generic;
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
    public class PimFieldAdapter : IPimFieldAdapter
    {
        private static List<CVL> _cvls;

        private static List<CVLValue> _cvlValues;
        private readonly IConfiguration _config;

        public PimFieldAdapter(IConfiguration config)
        {
            _config = config;
        }

        public static List<CVL> CVLs
        {
            get => _cvls ?? (_cvls = RemoteManager.ModelService.GetAllCVLs());
            set => _cvls = value;
        }

        public static List<CVLValue> CVLValues
        {
            get => _cvlValues ?? (_cvlValues = RemoteManager.ModelService.GetAllCVLValues());
            set => _cvlValues = value;
        }

        public IEnumerable<string> CultureInfosToStringArray(CultureInfo[] cultureInfo)
        {
            return cultureInfo.Select(ci => ci.Name.ToLower()).ToArray();
        }

        public string FieldIsUseInCompare(FieldType fieldType)
        {
            var value = "False";

            if (fieldType.Settings.ContainsKey("UseInComparing"))
            {
                value = fieldType.Settings["UseInComparing"];
                if (!(value.ToLower().Equals("false") || value.ToLower().Equals("true")))
                {
                    IntegrationLogger.Write(LogLevel.Error, $"Fieldtype with id {fieldType.Id} has invalid UseInComparing setting");
                }
            }

            return value;
        }

        public bool FieldTypeIsMultiLanguage(FieldType fieldType)
        {
            if (fieldType.DataType.Equals(DataType.LocaleString))
            {
                return true;
            }

            if (!fieldType.DataType.Equals(DataType.CVL))
                return false;

            CVL cvl = RemoteManager.ModelService.GetCVL(fieldType.CVLId);

            return cvl != null && cvl.DataType.Equals(DataType.LocaleString);
        }

        public string GetAllowSearch(FieldType fieldType)
        {
            if (fieldType.Settings.ContainsKey("AllowsSearch"))
            {
                return fieldType.Settings["AllowsSearch"];
            }

            return "true";
        }

        public List<XElement> GetCVLValues(Field field)
        {
            var dataElements = new List<XElement>();
            if (field == null)
               return dataElements;

            if (field.IsEmpty())
            {
                XElement dataElement = GetEmptyCvlDataElement(_config.ChannelDefaultLanguage);
                dataElements.Add(dataElement);
                return dataElements;
            }

            CVL cvl = CVLs.FirstOrDefault(c => c.Id.Equals(field.FieldType.CVLId));
            if (cvl == null)
                return dataElements;

            if (cvl.DataType == DataType.LocaleString)
            {
                foreach (KeyValuePair<CultureInfo, CultureInfo> language in _config.LanguageMapping)
                {
                    XElement dataElement = GetCvlDataElement(field, language.Key);
                    dataElements.Add(dataElement);
                }
            }
            else
            {
                XElement dataElement = GetCvlDataElement(field, _config.ChannelDefaultLanguage);
                dataElements.Add(dataElement);
            }

            return dataElements;
        }

        public string GetDisplayName(Entity entity, int maxLength)
        {
            Field displayNameField = entity.DisplayName;

            string returnString;

            if (displayNameField == null || displayNameField.IsEmpty())
            {
                returnString = $"[{entity.Id}]";
            }
            else if (displayNameField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                var ls = (LocaleString) displayNameField.Data;
                if (String.IsNullOrEmpty(ls[_config.LanguageMapping[_config.ChannelDefaultLanguage]]))
                {
                    returnString = $"[{entity.Id}]";
                }
                else
                {
                    returnString = ls[_config.LanguageMapping[_config.ChannelDefaultLanguage]];
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

        public string GetEndDate(Entity entity)
        {
            Field endDateField = entity?.Fields?.FirstOrDefault(f => f.FieldType?.Id.ToLower().Contains("enddate") ?? false);

            if (endDateField == null || endDateField.IsEmpty())
            {
                return DateTime.UtcNow.AddYears(100).ToString("u");
            }

            return ((DateTime) endDateField.Data).ToUniversalTime().ToString("u");
        }

        public string GetFieldValue(Entity entity, string fieldName, CultureInfo ci)
        {
            Field field = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains(fieldName));

            if (field == null || field.IsEmpty())
            {
                return String.Empty;
            }

            if (field.FieldType.DataType.Equals(DataType.LocaleString))
            {
                return _config.ChannelIdPrefix + ((LocaleString) field.Data)[ci];
            }

            return field.Data.ToString();
        }

        public string GetFlatFieldData(Field field)
        {
            if (field == null || field.IsEmpty())
            {
                return String.Empty;
            }

            string dataType = field.FieldType.DataType;
            if (dataType == DataType.Boolean)
            {
                return ((bool) field.Data).ToString();
            }

            if (dataType == DataType.DateTime)
            {
                return ((DateTime) field.Data).ToString("O");
            }

            if (dataType == DataType.Double)
            {
                return ((double) field.Data).ToString(CultureInfo.InvariantCulture);
            }

            if (dataType == DataType.File ||
                dataType == DataType.Integer ||
                dataType == DataType.String ||
                dataType == DataType.Xml)
            {
                return field.Data.ToString();
            }

            return String.Empty;
        }

        public string GetStartDate(Entity entity)
        {
            Field startDateField = entity?.Fields?.FirstOrDefault(f => f.FieldType?.Id.ToLower().Contains("startdate") ?? false);

            if (startDateField == null || startDateField.IsEmpty())
            {
                return DateTime.UtcNow.AddMinutes(-5).ToString("u");
            }

            return ((DateTime) startDateField.Data).ToUniversalTime().ToString("u");
        }

        public string GetSingleCvlValue(string key, CultureInfo language, List<CVLValue> currentCvlValues, CVL cvl)
        {
            CVLValue cvlValue = currentCvlValues.FirstOrDefault(cv => cv.Key.Equals(key));
            if (cvlValue?.Value == null)
                return null;

            string finalizedValue;

            if (cvl.DataType.Equals(DataType.LocaleString))
            {
                var ls = (LocaleString) cvlValue.Value;

                if (!ls.ContainsCulture(language))
                    return null;

                string value = ls[language];
                finalizedValue = GetFinalizedValue(value, key);
            }
            else
            {
                string value = cvlValue.Value.ToString();
                finalizedValue = GetFinalizedValue(value, key);
            }

            return finalizedValue;
        }

        internal static void CompareAndParseSkuXmls(string oldXml, string newXml, out List<XElement> skusToAdd, out List<XElement> skusToDelete)
        {
            XDocument oldDoc = XDocument.Parse(oldXml);
            XDocument newDoc = XDocument.Parse(newXml);

            List<XElement> oldSkus = oldDoc.Descendants().Elements("SKU").ToList();
            List<XElement> newSkus = newDoc.Descendants().Elements("SKU").ToList();

            var removables = new List<string>();

            foreach (XElement elem in oldSkus)
            {
                var idValue = elem.Attribute("id")?.Value;
                if (newSkus.Exists(e => e.Attribute("id")?.Value == idValue))
                {
                    if (!removables.Exists(y => y == idValue))
                    {
                        removables.Add(idValue);
                    }
                }
            }

            foreach (string id in removables)
            {
                oldSkus.RemoveAll(e => e.Attribute("id")?.Value == id);
                newSkus.RemoveAll(e => e.Attribute("id")?.Value == id);
            }

            skusToAdd = newSkus;
            skusToDelete = oldSkus;
        }

        private static bool FieldIsExcludedCatalogEntryMarkets(Field field)
        {
            return field.FieldType.Settings.ContainsKey("EPiMetaFieldName") &&
                   field.FieldType.Settings["EPiMetaFieldName"].Equals("_ExcludedCatalogEntryMarkets");
        }

        private XElement GetCvlDataElement(Field field, CultureInfo language)
        {
            var dataElement = new XElement("Data",
                new XAttribute("language", language.Name.ToLower()),
                new XAttribute("value", GetCvlFieldValue(field, language)));

            return dataElement;
        }

        private XElement GetEmptyCvlDataElement(CultureInfo language)
        {
            var dataElement = new XElement("Data",
                new XAttribute("language", language.Name.ToLower()),
                new XAttribute("value", ""));

            return dataElement;
        }

        private string GetCvlFieldValue(Field field, CultureInfo language)
        {
            if (FieldIsExcludedCatalogEntryMarkets(field))
                return field.Data.ToString();

            string[] keys = field.Data.ToString().Split(';');
            string cvlId = field.FieldType.CVLId;
            List<CVLValue> currentCvlValues = CVLValues.Where(cv => cv.CVLId.Equals(cvlId)).ToList();

            CVL cvl = CVLs.FirstOrDefault(x => x.Id == cvlId);
            if (cvl == null)
                return null;

            var returnValues = new List<string>();

            IntegrationLogger.Write(LogLevel.Debug, $"Fetching CVL Value for CVL {cvlId} and Field {field.FieldType.Id}.");

            foreach (string key in keys)
            {
                string finalizedValue = GetSingleCvlValue(key, language, currentCvlValues, cvl);

                returnValues.Add(finalizedValue);
            }

            return string.Join(";", returnValues);
        }

        private string GetFinalizedValue(string value, string key)
        {
            if (_config.ActiveCVLDataMode.Equals(CVLDataMode.Keys))
            {
                return key;
            }

            if (_config.ActiveCVLDataMode.Equals(CVLDataMode.KeysAndValues))
            {
                value = key + Configuration.CVLKeyDelimiter + value;
            }

            return value;
        }
    }
}