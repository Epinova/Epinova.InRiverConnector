using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class Configuration : IConfiguration
    {
        private readonly List<string> _epiFieldsIninRiver;
        private readonly Dictionary<string, string> _settings;
        private int _batchsize;
        private string _channelDefaultCurrency;
        private CultureInfo _channelDefaultLanguage;
        private string _channelWeightBase;
        private Dictionary<string, string> _epiCodeMapping;
        private Dictionary<string, string> _epiNameMapping;
        private HashSet<string> _excludedFields;
        private List<EntityType> _exportEnabledEntityTypes;
        private List<LinkType> _exportEnabledLinkTypes;
        private bool _itemsToSkus;
        private Dictionary<CultureInfo, CultureInfo> _languageMapping;
        private Dictionary<string, string> _resourceConfiugExtensions;
        private bool? _useThreeLevelsInCommerce;

        public Configuration(string id)
        {
            _settings = RemoteManager.UtilityService.GetConnector(id).Settings;
            var settingsValidator = new SettingsValidator(_settings);
            settingsValidator.ValidateSettings();

            Id = id;
            Endpoints = new EndpointCollection(EpiEndpoint);
            LinkTypes = new List<LinkType>(RemoteManager.ModelService.GetAllLinkTypes());

            _epiFieldsIninRiver = new List<string> { "startdate", "enddate", "displaytemplate", "seodescription", "seokeywords", "seotitle", "seouri", "skus" };
        }

        public static string CVLKeyDelimiter => "||";

        public static string OriginalDisplayConfiguration => "Original";

        public CVLDataMode ActiveCVLDataMode => !_settings.ContainsKey(ConfigKeys.CvlData) ? CVLDataMode.Undefined : StringToCVLDataMode(_settings[ConfigKeys.CvlData]);

        public LinkType[] AssociationLinkTypes
        {
            get
            {
                if (_exportEnabledLinkTypes != null)
                    return _exportEnabledLinkTypes.ToArray();

                _exportEnabledLinkTypes = new List<LinkType>();
                List<LinkType> allLinkTypes = RemoteManager.ModelService.GetAllLinkTypes();

                LinkType productItemLink = allLinkTypes.Where(x => x.SourceEntityTypeId.Equals("Product") && x.TargetEntityTypeId.Equals("Item"))
                    .OrderBy(l => l.Index)
                    .FirstOrDefault();

                foreach (LinkType linkType in allLinkTypes)
                {
                    if (linkType.TargetEntityTypeId == "Specification")
                        continue;

                    if (linkType.IsProductItemLink() && linkType.Id == productItemLink?.Id)
                        continue;

                    if (linkType.SourceEntityTypeIsChannelNode())
                        continue;

                    if (BundleEntityTypes.Contains(linkType.SourceEntityTypeId) ||
                        PackageEntityTypes.Contains(linkType.SourceEntityTypeId))
                        continue;

                    if (ExportEnabledEntityTypes.Any(x => x.Id == linkType.SourceEntityTypeId) &&
                        ExportEnabledEntityTypes.Any(x => x.Id == linkType.TargetEntityTypeId))
                    {
                        _exportEnabledLinkTypes.Add(linkType);
                    }
                }

                IntegrationLogger.Write(LogLevel.Debug, $"ExportEnabledLinkTypes: {string.Join(",", _exportEnabledLinkTypes)}.");

                return _exportEnabledLinkTypes.ToArray();
            }
        }

        public int BatchSize
        {
            get
            {
                if (!_settings.ContainsKey(ConfigKeys.BatchSize))
                    return Int32.MaxValue;

                string value = _settings[ConfigKeys.BatchSize];

                if (!Int32.TryParse(value, out _batchsize) || value == "0")
                {
                    _batchsize = Int32.MaxValue;
                }

                return _batchsize;
            }
        }

        public string[] BundleEntityTypes => SplitString(ConfigKeys.BundleTypes);

        public string ChannelDefaultCurrency
        {
            get
            {
                if (String.IsNullOrEmpty(_channelDefaultCurrency))
                {
                    _channelDefaultCurrency = "USD";
                }

                return _channelDefaultCurrency;
            }

            set => _channelDefaultCurrency = value;
        }

        public CultureInfo ChannelDefaultLanguage
        {
            get => _channelDefaultLanguage ?? (_channelDefaultLanguage = new CultureInfo("en-us"));
            set => _channelDefaultLanguage = value;
        }


        public string ChannelDefaultWeightBase
        {
            get
            {
                if (String.IsNullOrEmpty(_channelWeightBase))
                {
                    _channelWeightBase = "kg";
                }

                return _channelWeightBase;
            }
            set => _channelWeightBase = value;
        }

        public int ChannelId
        {
            get
            {
                if (!_settings.ContainsKey(ConfigKeys.ChannelId))
                {
                    return 0;
                }

                return Int32.Parse(_settings[ConfigKeys.ChannelId]);
            }
        }

        public string ChannelIdPrefix { get; set; } = String.Empty;
        public string[] DynamicPackageEntityTypes => SplitString(ConfigKeys.DynamicPackageTypes);
        public EndpointCollection Endpoints { get; set; }
        public string EpiApiKey => _settings[ConfigKeys.EpiApiKey];

        public Dictionary<string, string> EpiCodeMapping
        {
            get
            {
                if (_epiCodeMapping != null)
                    return _epiCodeMapping;

                _epiCodeMapping = new Dictionary<string, string>();

                if (!_settings.ContainsKey(ConfigKeys.EpiCodeFields))
                {
                    return _epiCodeMapping;
                }

                string rawValue = _settings[ConfigKeys.EpiCodeFields];

                if (String.IsNullOrEmpty(rawValue))
                    return _epiCodeMapping;

                List<FieldType> fieldTypes = RemoteManager.ModelService.GetAllFieldTypes();

                string[] values = rawValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string val in values)
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

        public string EpiEndpoint => _settings[ConfigKeys.EpiEndpoint];

        public HashSet<string> EPiFieldsIninRiver
        {
            get
            {
                if (_excludedFields != null)
                {
                    return _excludedFields;
                }

                if (!_settings.ContainsKey(ConfigKeys.ExcludeFields) || String.IsNullOrEmpty(_settings[ConfigKeys.ExcludeFields]))
                {
                    var excludedFieldTypes = new HashSet<string>();
                    foreach (string baseField in _epiFieldsIninRiver)
                    {
                        foreach (EntityType entityType in ExportEnabledEntityTypes)
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
                    var excludedFieldTypes = new HashSet<string>();
                    foreach (string baseField in _epiFieldsIninRiver)
                    {
                        foreach (EntityType entityType in ExportEnabledEntityTypes)
                        {
                            excludedFieldTypes.Add(entityType.Id.ToLower() + baseField);
                        }
                    }

                    excludedFieldTypes.Add("skus");

                    string[] fields = _settings[ConfigKeys.ExcludeFields].Split(',');
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

        public Dictionary<string, string> EpiNameMapping
        {
            get
            {
                if (_epiNameMapping != null)
                    return _epiNameMapping;

                _epiNameMapping = new Dictionary<string, string>();

                if (!_settings.ContainsKey(ConfigKeys.EpiNameFields))
                    return _epiNameMapping;

                string value = _settings[ConfigKeys.EpiNameFields];

                if (String.IsNullOrEmpty(value))
                    return _epiNameMapping;

                List<FieldType> fieldTypes = RemoteManager.ModelService.GetAllFieldTypes();

                string[] values = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string val in values)
                {
                    if (String.IsNullOrEmpty(val))
                        continue;

                    FieldType fieldType = fieldTypes.FirstOrDefault(fT => fT.Id.Equals(val, StringComparison.InvariantCultureIgnoreCase));

                    if (fieldType != null && !_epiNameMapping.ContainsKey(fieldType.EntityTypeId))
                    {
                        _epiNameMapping.Add(fieldType.EntityTypeId, fieldType.Id);
                    }
                }

                return _epiNameMapping;
            }
        }

        public int EpiRestTimeout => int.Parse(_settings[ConfigKeys.EpiTimeout]);

        public List<EntityType> ExportEnabledEntityTypes
        {
            get
            {
                if (_exportEnabledEntityTypes != null)
                    return _exportEnabledEntityTypes;

                if (!_settings.ContainsKey(ConfigKeys.ExportEntities))
                    throw new Exception($"Need to add exportable entities (config value {ConfigKeys.ExportEntities}. Default value is: {ConfigDefaults.ExportEntities}.");

                List<string> list = _settings[ConfigKeys.ExportEntities].Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
                List<EntityType> allEntityTypes = RemoteManager.ModelService.GetAllEntityTypes();
                _exportEnabledEntityTypes = allEntityTypes.Where(x => list.Contains(x.Id)).ToList();
                IntegrationLogger.Write(LogLevel.Debug, $"ExportEnabledEntityTypes: {string.Join(",", _exportEnabledEntityTypes)}.");

                return _exportEnabledEntityTypes;
            }
        }

        public bool ForceIncludeLinkedContent => bool.Parse(_settings[ConfigKeys.ForceIncludeLinkedContent]);

        public string HttpPostUrl
        {
            get
            {
                if (!_settings.ContainsKey(ConfigKeys.HttpPostUrl))
                {
                    return null;
                }

                return _settings[ConfigKeys.HttpPostUrl];
            }
        }

        public string Id { get; }

        public bool ItemsToSkus
        {
            get
            {
                string value = _settings[ConfigKeys.ItemToSkus];
                if (!Boolean.TryParse(value, out _itemsToSkus))
                {
                    _itemsToSkus = false;
                }

                return _itemsToSkus;
            }
        }

        public Dictionary<CultureInfo, CultureInfo> LanguageMapping
        {
            get
            {
                if (_languageMapping != null)
                    return _languageMapping;

                if (!_settings.ContainsKey(ConfigKeys.LanguageMapping))
                {
                    return new Dictionary<CultureInfo, CultureInfo>();
                }

                string mappingXml = _settings[ConfigKeys.LanguageMapping];

                _languageMapping = new Dictionary<CultureInfo, CultureInfo>();

                var doc = new XmlDocument();
                doc.LoadXml(mappingXml);

                List<CultureInfo> allLanguages = RemoteManager.UtilityService.GetAllLanguages();

                if (doc.DocumentElement == null)
                    return _languageMapping;

                foreach (XmlNode languageNode in doc.DocumentElement)
                {
                    XmlElement epiLanguage = languageNode["epi"];
                    XmlElement inriverLanguage = languageNode["inriver"];

                    if (epiLanguage != null && inriverLanguage != null)
                    {
                        var episerverCulture = new CultureInfo(epiLanguage.InnerText);
                        var pimCulture = new CultureInfo(inriverLanguage.InnerText);

                        if (!allLanguages.Exists(ci => ci.LCID == pimCulture.LCID))
                        {
                            throw new Exception($"ERROR: Mapping Language incorrect, {inriverLanguage.InnerText} is not a valid pim culture info");
                        }

                        _languageMapping.Add(episerverCulture, pimCulture);
                    }
                    else
                    {
                        throw new Exception("ERROR: Mapping language is missing.");
                    }
                }

                return _languageMapping;
            }
        }

        public List<LinkType> LinkTypes { get; set; }
        public string[] PackageEntityTypes => SplitString(ConfigKeys.PackageTypes);
        public string PublicationsRootPath => !_settings.ContainsKey(ConfigKeys.PublishFolder) ? @"C:\temp\Publish\Epi" : _settings[ConfigKeys.PublishFolder];

        public string[] ResourceConfigurations
        {
            get
            {
                if (!_settings.ContainsKey(ConfigKeys.ResourceConfiguration))
                {
                    return new string[0];
                }

                Dictionary<string, string> resourceConfWithExt = ParseResourceConfig(_settings[ConfigKeys.ResourceConfiguration]);
                return resourceConfWithExt.Keys.ToArray();
            }
        }

        public Dictionary<string, string> ResourceConfiugurationExtensions => _resourceConfiugExtensions ??
                                                                              (_resourceConfiugExtensions = ParseResourceConfig(_settings[ConfigKeys.ResourceConfiguration]));

        public string ResourcesRootPath
        {
            get
            {
                if (!_settings.ContainsKey(ConfigKeys.ResourcesPublishFolder))
                {
                    return @"C:\temp\Publish\Epi\Resources";
                }

                return _settings[ConfigKeys.ResourcesPublishFolder];
            }
        }

        public bool UseThreeLevelsInCommerce
        {
            get
            {
                if (_useThreeLevelsInCommerce != null)
                    return (bool) _useThreeLevelsInCommerce;

                if (!_settings.ContainsKey(ConfigKeys.UseThreeLevelsInCommerce))
                {
                    _useThreeLevelsInCommerce = false;
                    return _useThreeLevelsInCommerce.Value;
                }

                string value = _settings[ConfigKeys.UseThreeLevelsInCommerce];

                _useThreeLevelsInCommerce = !String.IsNullOrEmpty(value) && bool.Parse(value);

                return (bool) _useThreeLevelsInCommerce;
            }
        }

        private static Dictionary<string, string> ParseResourceConfig(string setting)
        {
            var settingsDictionary = new Dictionary<string, string>();
            if (String.IsNullOrEmpty(setting))
            {
                return settingsDictionary;
            }

            setting = setting.Replace(" ", String.Empty);

            string[] resouceConfs = setting.Split(',');

            foreach (string resouceConf in resouceConfs)
            {
                if (resouceConf.Contains(':'))
                {
                    string[] parts = resouceConf.Split(':');

                    settingsDictionary.Add(parts[0], parts[1]);
                }
                else
                {
                    settingsDictionary.Add(resouceConf, String.Empty);
                }
            }

            return settingsDictionary;
        }

        private static CVLDataMode StringToCVLDataMode(string str)
        {
            if (!Enum.TryParse(str, out CVLDataMode mode))
            {
                IntegrationLogger.Write(LogLevel.Error, $"Could not parse CVLDataMode for string {str}");
            }

            return mode;
        }

        private string[] SplitString(string settingKey)
        {
            if (!_settings.ContainsKey(settingKey))
                return new string[0];

            string setting = _settings[settingKey];

            if (String.IsNullOrEmpty(setting))
                return new string[0];

            setting = setting.Replace(" ", String.Empty);
            return setting.Split(',');
        }
    }
}