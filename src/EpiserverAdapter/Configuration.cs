using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class Configuration : IConfiguration
    {
        private readonly Dictionary<string, string> _settings;

        private readonly List<string> _epiFieldsIninRiver;

        private Dictionary<CultureInfo, CultureInfo> _languageMapping;

        private Dictionary<string, string> _epiNameMapping;

        private bool? _useThreeLevelsInCommerce;

        private CultureInfo _channelDefaultLanguage;

        private string _channelDefaultCurrency;

       private Dictionary<string, string> _epiCodeMapping;

        private string _channelWeightBase;

        private Dictionary<string, string> _resourceConfiugurationExtensions;

        private List<LinkType> _exportEnabledLinkTypes;

        private bool _itemsToSkus;

        private HashSet<string> _excludedFields;

        private int _batchsize;

        public Configuration(string id)
        {
            _settings = RemoteManager.UtilityService.GetConnector(id).Settings;
            var settingsValidator = new SettingsValidator(_settings);
            settingsValidator.ValidateSettings();
            
            Id = id;
            Endpoints = new EndpointCollection(EpiEndpoint);
            LinkTypes = new List<LinkType>(RemoteManager.ModelService.GetAllLinkTypes());

            ChannelEntities = new Dictionary<int, Entity>();

            _epiFieldsIninRiver = new List<string> { "startdate", "enddate", "displaytemplate", "seodescription", "seokeywords", "seotitle", "seouri", "skus" };
        }

        public int EpiRestTimeout => int.Parse(_settings[ConfigKeys.EpiTimeout]);
        public string EpiApiKey => _settings[ConfigKeys.EpiApiKey];
        public string EpiEndpoint => _settings[ConfigKeys.EpiEndpoint];
        public EndpointCollection Endpoints { get; set; }

        public static string ExportFileName => "Catalog.xml";

        public static string MimeType => "ResourceMimeType";

        public static string OriginalDisplayConfiguration => "Original";

        public static string CVLKeyDelimiter => "||";

        public static string EPiCommonField => "EPiMetaFieldName";

        public static string SKUFieldName => "SKUs";

        public static string SKUData => "Data";

        public string Id { get; private set; }

        public List<LinkType> LinkTypes { get; set; }

        public int ChannelId
        {
            get
            {
                if (!_settings.ContainsKey("CHANNEL_ID"))
                {
                    return 0;
                }

                return int.Parse(_settings["CHANNEL_ID"]);
            }
        }
        
        public string PublicationsRootPath
        {
            get
            {
                if (!_settings.ContainsKey("PUBLISH_FOLDER"))
                {
                    return @"C:\temp\Publish\Epi";
                }

                return _settings["PUBLISH_FOLDER"];
            }
        }

        private List<EntityType> _exportEnabledEntityTypes;
        public List<EntityType> ExportEnabledEntityTypes
        {
            get
            {
                if (_exportEnabledEntityTypes != null)
                    return _exportEnabledEntityTypes;

                if (!_settings.ContainsKey(ConfigKeys.ExportEntities))
                    throw new Exception($"Need to add exportable entities (config value {ConfigKeys.ExportEntities}. Default value is: {ConfigDefaults.ExportEntities}.");

                var list = _settings[ConfigKeys.ExportEntities].Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries).ToList();
                var allEntityTypes = RemoteManager.ModelService.GetAllEntityTypes();
                _exportEnabledEntityTypes = allEntityTypes.Where(x => list.Contains(x.Id)).ToList();

                return _exportEnabledEntityTypes;
            }
        }

        public string HttpPostUrl
        {
            get
            {
                if (!_settings.ContainsKey("HTTP_POST_URL"))
                {
                    return null;
                }

                return _settings["HTTP_POST_URL"];
            }
        }

        public Dictionary<CultureInfo, CultureInfo> LanguageMapping
        {
            get
            {
                if (_languageMapping == null)
                {
                    if (!_settings.ContainsKey("LANGUAGE_MAPPING"))
                    {
                        return new Dictionary<CultureInfo, CultureInfo>();
                    }

                    string mappingXml = _settings["LANGUAGE_MAPPING"];

                    Dictionary<CultureInfo, CultureInfo> languageMapping2 = new Dictionary<CultureInfo, CultureInfo>();

                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(mappingXml);

                    List<CultureInfo> allLanguages = RemoteManager.UtilityService.GetAllLanguages();

                    if (doc.DocumentElement != null)
                    {
                        foreach (XmlNode languageNode in doc.DocumentElement)
                        {
                            XmlElement epiLanguage = languageNode["epi"];
                            XmlElement inriverLanguage = languageNode["inriver"];
                            if (epiLanguage != null && inriverLanguage != null)
                            {
                                CultureInfo epiCi = new CultureInfo(epiLanguage.InnerText);
                                CultureInfo pimCi = new CultureInfo(inriverLanguage.InnerText);

                                if (!allLanguages.Exists(ci => ci.LCID == pimCi.LCID))
                                {
                                    throw new Exception(
                                        string.Format(
                                            "ERROR: Mapping Language incorrect, {0} is not a valid pim culture info",
                                            inriverLanguage.InnerText));
                                }

                                languageMapping2.Add(epiCi, pimCi);
                            }
                            else
                            {
                                throw new Exception("ERROR: Mapping language is missing.");
                            }
                        }
                    }

                    _languageMapping = languageMapping2;
                }

                return _languageMapping;
            }

            set
            {
                _languageMapping = value;
            }
        }

        public Dictionary<string, string> EpiNameMapping
        {
            get
            {
                if (_epiNameMapping == null)
                {
                    if (!_settings.ContainsKey("EPI_NAME_FIELDS"))
                    {
                        _epiNameMapping = new Dictionary<string, string>();

                        return _epiNameMapping;
                    }

                    string value = _settings["EPI_NAME_FIELDS"];

                    _epiNameMapping = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(value))
                    {
                        List<FieldType> fieldTypes = RemoteManager.ModelService.GetAllFieldTypes();

                        string[] values = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string val in values)
                        {
                            if (string.IsNullOrEmpty(val))
                            {
                                continue;
                            }

                            FieldType fieldType = fieldTypes.FirstOrDefault(fT => fT.Id.ToLower() == val.ToLower());
                            if (fieldType != null && !_epiNameMapping.ContainsKey(fieldType.EntityTypeId))
                            {
                                _epiNameMapping.Add(fieldType.EntityTypeId, fieldType.Id);
                            }
                        }
                    }
                }

                return _epiNameMapping;
            }
        }

        public string ResourcesRootPath
        {
            get
            {
                if (!_settings.ContainsKey("PUBLISH_FOLDER_RESOURCES"))
                {
                    return @"C:\temp\Publish\Epi\Resources";
                }

                return _settings["PUBLISH_FOLDER_RESOURCES"];
            }
        }

        public bool UseThreeLevelsInCommerce
        {
            get
            {
                if (_useThreeLevelsInCommerce == null)
                {
                    if (!_settings.ContainsKey("USE_THREE_LEVELS_IN_COMMERCE"))
                    {
                        _useThreeLevelsInCommerce = false;
                        return false;
                    }

                    string value = _settings["USE_THREE_LEVELS_IN_COMMERCE"];

                    if (!string.IsNullOrEmpty(value))
                    {
                        _useThreeLevelsInCommerce = bool.Parse(value);
                    }
                    else
                    {
                        _useThreeLevelsInCommerce = false;
                    }
                }

                return (bool)_useThreeLevelsInCommerce;
            }
        }

        public CultureInfo ChannelDefaultLanguage
        {
            get => _channelDefaultLanguage ?? (_channelDefaultLanguage = new CultureInfo("en-us"));
            set => _channelDefaultLanguage = value;
        }

        public string ChannelDefaultCurrency
        {
            get
            {
                if (string.IsNullOrEmpty(_channelDefaultCurrency))
                {
                    _channelDefaultCurrency = "usd";
                }
                
                return _channelDefaultCurrency;
            }

            set => _channelDefaultCurrency = value;
        }

        public Dictionary<string, string> EpiCodeMapping
        {
            get
            {
                if (_epiCodeMapping != null)
                    return _epiCodeMapping;

                if (!_settings.ContainsKey("EPI_CODE_FIELDS"))
                {
                    _epiCodeMapping = new Dictionary<string, string>();
                    return _epiCodeMapping;
                }

                var rawValue = _settings["EPI_CODE_FIELDS"];

                _epiCodeMapping = new Dictionary<string, string>();
                if (string.IsNullOrEmpty(rawValue))
                    return _epiCodeMapping;

                List<FieldType> fieldTypes = RemoteManager.ModelService.GetAllFieldTypes();

                var values = rawValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var val in values)
                {
                    FieldType fieldType = fieldTypes.FirstOrDefault(x => x.Id.Equals(val, StringComparison.InvariantCultureIgnoreCase));
                    if (fieldType != null && !_epiCodeMapping.ContainsKey(fieldType.EntityTypeId))
                    {
                        _epiCodeMapping.Add(fieldType.EntityTypeId, fieldType.Id);
                    }
                }

                return _epiCodeMapping;
            }
        }

        public Dictionary<int, Entity> ChannelEntities { get; set; }

        public string ChannelDefaultWeightBase
        {
            get
            {
                if (string.IsNullOrEmpty(_channelWeightBase))
                {
                    _channelWeightBase = "kg";
                }

                return _channelWeightBase;
            }
            set => _channelWeightBase = value;
        }

        public string ChannelIdPrefix { get; set; } = string.Empty;

        public Dictionary<string, string> ChannelMimeTypeMappings { get; set; } = new Dictionary<string, string>();


        public string[] ResourceConfigurations
        {
            get
            {
                if (!_settings.ContainsKey("RESOURCE_CONFIGURATION"))
                {
                    return new string[0];
                }

                Dictionary<string, string> resourceConfWithExt = ParseResourceConfig(_settings["RESOURCE_CONFIGURATION"]);
                return resourceConfWithExt.Keys.ToArray();
            }
        }

        public Dictionary<string, string> ResourceConfiugurationExtensions
        {
            get
            {
                return _resourceConfiugurationExtensions
                       ?? (_resourceConfiugurationExtensions =
                           ParseResourceConfig(_settings["RESOURCE_CONFIGURATION"]));
            }
        }

        public LinkType[] ExportEnabledLinkTypes
        {
            get
            {
                if (_exportEnabledLinkTypes != null)
                    return _exportEnabledLinkTypes.ToArray();

                _exportEnabledLinkTypes = new List<LinkType>();
                List<LinkType> allLinkTypes = RemoteManager.ModelService.GetAllLinkTypes();

                LinkType firstProdItemLink = allLinkTypes.Where(
                        lt => lt.SourceEntityTypeId.Equals("Product") && lt.TargetEntityTypeId.Equals("Item"))
                    .OrderBy(l => l.Index)
                    .FirstOrDefault();

                foreach (LinkType linkType in allLinkTypes)
                {
                    // ChannelNode links and  Product to item links are not associations
                    if (linkType.LinkEntityTypeId == null &&
                        (linkType.SourceEntityTypeId.Equals("ChannelNode")
                         || (BundleEntityTypes.Contains(linkType.SourceEntityTypeId) && !BundleEntityTypes.Contains(linkType.TargetEntityTypeId))
                         || (PackageEntityTypes.Contains(linkType.SourceEntityTypeId) && !PackageEntityTypes.Contains(linkType.TargetEntityTypeId))
                         || (DynamicPackageEntityTypes.Contains(linkType.SourceEntityTypeId) && !DynamicPackageEntityTypes.Contains(linkType.TargetEntityTypeId))
                         || (linkType.SourceEntityTypeId.Equals("Product") && linkType.TargetEntityTypeId.Equals("Item") && firstProdItemLink != null && linkType.Id == firstProdItemLink.Id)))
                    {
                        continue;
                    }

                    if (ExportEnabledEntityTypes.Any(eee => eee.Id.Equals(linkType.SourceEntityTypeId))
                        && ExportEnabledEntityTypes.Any(eee => eee.Id.Equals(linkType.TargetEntityTypeId)))
                    {
                        _exportEnabledLinkTypes.Add(linkType);
                    }
                }

                return _exportEnabledLinkTypes.ToArray();
            }
        }

        public bool ItemsToSkus
        {
            get
            {
                string value = _settings["ITEM_TO_SKUs"];
                if (!bool.TryParse(value, out _itemsToSkus))
                {
                    _itemsToSkus = false;
                }

                return _itemsToSkus;
            }
        }

        public int BatchSize
        {
            get
            {
                if (_settings.ContainsKey("BATCH_SIZE"))
                {
                    string value = _settings["BATCH_SIZE"];

                    if (!int.TryParse(value, out _batchsize) || value == "0")
                    {
                        _batchsize = int.MaxValue;
                    }

                    return _batchsize;
                }

                return int.MaxValue;
            }
        }

        [Obsolete("This needs to die. Inneholder en liste over entitets-ID-er og hvilken EntityTypeId den har, for oppslag senere. Livsfarlig.")]
        public Dictionary<int, string> EntityIdAndType { get; set; }

        public string[] BundleEntityTypes => StringToStringArray("BUNDLE_ENTITYTYPES");

        public string[] PackageEntityTypes => StringToStringArray("PACKAGE_ENTITYTYPES");

        public string[] DynamicPackageEntityTypes => StringToStringArray("DYNAMIC_PACKAGE_ENTITYTYPES");

        public HashSet<string> EPiFieldsIninRiver
        {
            get
            {
                if (_excludedFields != null)
                {
                    return _excludedFields;
                }

                if (!_settings.ContainsKey("EXCLUDE_FIELDS") || string.IsNullOrEmpty(_settings["EXCLUDE_FIELDS"]))
                {
                    HashSet<string> excludedFieldTypes = new HashSet<string>();
                    foreach (string baseField in _epiFieldsIninRiver)
                    {
                        foreach (var entityType in ExportEnabledEntityTypes)
                        {
                            excludedFieldTypes.Add(entityType.Id.ToLower() + baseField);  
                        }
                    }
                    
                    excludedFieldTypes.Add("skus");

                    _excludedFields = excludedFieldTypes;
                    return _excludedFields;
                }
                else
                {
                    HashSet<string> excludedFieldTypes = new HashSet<string>();
                    foreach (string baseField in _epiFieldsIninRiver)
                    {
                        foreach (var entityType in ExportEnabledEntityTypes)
                        {
                            excludedFieldTypes.Add(entityType.Id.ToLower() + baseField);
                        }
                    }
                    
                    excludedFieldTypes.Add("skus");
                    
                    string[] fields = _settings["EXCLUDE_FIELDS"].Split(',');
                    foreach (string field in fields)
                    {
                        if (!excludedFieldTypes.Contains(field.ToLower()))
                        {
                            excludedFieldTypes.Add(field.ToLower());
                        }
                    }

                    _excludedFields = excludedFieldTypes;
                    return _excludedFields;
                }
            }
        }
        
        public CVLDataMode ActiveCVLDataMode
        {
            get
            {
                return !_settings.ContainsKey("CVL_DATA") ?
                    CVLDataMode.Undefined : 
                    StringToCVLDataMode(_settings["CVL_DATA"]);
            }
        }

        private string[] StringToStringArray(string settingKey)
        {
            if(!_settings.ContainsKey(settingKey))
                return new string[0];

            var setting = _settings[settingKey];

            if (string.IsNullOrEmpty(setting))
                return new string[0];

            setting = setting.Replace(" ", string.Empty);
            return setting.Split(',');
        }

        private static CVLDataMode StringToCVLDataMode(string str)
        {
            CVLDataMode mode;

            if (!Enum.TryParse(str, out mode))
            {
                IntegrationLogger.Write(LogLevel.Error, $"Could not parse CVLDataMode for string {str}");
            }

            return mode;
        }

        private Dictionary<string, string> ParseResourceConfig(string setting)
        {
            Dictionary<string, string> settingsDictionary = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(setting))
            {
                return settingsDictionary;
            }

            setting = setting.Replace(" ", string.Empty);

            string[] resouceConfs;
            if (setting.Contains(','))
            {
                resouceConfs = setting.Split(',');
            }
            else
            {
                resouceConfs = new[] { setting };
            }

            foreach (string resouceConf in resouceConfs)
            {
                if (resouceConf.Contains(':'))
                {
                    string[] parts = resouceConf.Split(':');

                    settingsDictionary.Add(parts[0], parts[1]);
                }
                else
                {
                    settingsDictionary.Add(resouceConf, string.Empty);
                }
            }

            return settingsDictionary;
        }
    }
}
