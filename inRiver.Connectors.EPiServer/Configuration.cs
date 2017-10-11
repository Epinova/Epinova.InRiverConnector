using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using inRiver.EPiServerCommerce.CommerceAdapter.Enums;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace inRiver.EPiServerCommerce.CommerceAdapter
{
    public class Configuration
    {
        private static readonly string[] ExportDisabledEntityTypes = { "Channel", "Assortment", "Resource", "Task", "Section", "Publication" };

        private static List<EntityType> exportEnabledEntityTypes;

        private readonly Dictionary<string, string> settings;

        private readonly List<string> epiFieldsIninRiver;

        private bool? modifyFilterBehavior;

        private Dictionary<CultureInfo, CultureInfo> languageMapping;

        private Dictionary<string, string> epiNameMapping;

        private bool? useThreeLevelsInCommerce;

        private CultureInfo channelDefaultLanguage;

        private string channelDefaultCurrency;

        private bool? exportInventoryData;

        private bool? exportPricingData;

        private Dictionary<string, string> epiCodeMapping;

        private string channelWeightBase;

        private string channelIdPrefix = string.Empty;

        private Dictionary<string, string> channelMimeTypeMappings = new Dictionary<string, string>();

        private bool? channelAllowBackorder;

        private bool? channelAllowPreorder;

        private DateTime? channelBackorderAvailabilityDate;

        private int? channelBackorderQuantity;

        private int? channelInStockQuantity;

        private int? channelInventoryStatus;

        private DateTime? channelPreorderAvailabilityDate;

        private int? channelPreorderQuantity;

        private int? channelReorderMinQuantity;

        private int? channelReservedQuantity;

        private string channelMarketId;

        private string channelCurrencyCode;

        private int? channelPriceTypeId;

        private string channelPriceCode;

        private DateTime? channelValidFrom;

        private DateTime? channelValidUntil;

        private double? channelMinQuantity;

        private double? channelUnitPrice;

        private Dictionary<string, string> resourceConfiugurationExtensions;

        private List<LinkType> exportEnabledLinkTypes;

        private bool itemsToSkus;

        private HashSet<string> excludedFields;

        private int batchsize;

        public Configuration(string id)
        {
            this.settings = RemoteManager.UtilityService.GetConnector(id).Settings;
            this.Id = id;
            this.LinkTypes = new List<LinkType>(RemoteManager.ModelService.GetAllLinkTypes());
            this.epiFieldsIninRiver = new List<string> { "startdate", "enddate", "displaytemplate", "seodescription", "seokeywords", "seotitle", "seouri", "skus" };
            this.ChannelStructureEntities = new List<StructureEntity>();
            this.ChannelEntities = new Dictionary<int, Entity>();
        }

        public static List<EntityType> ExportEnabledEntityTypes
        {
            get
            {
                return exportEnabledEntityTypes ?? (exportEnabledEntityTypes = (from entityType in RemoteManager.ModelService.GetAllEntityTypes()
                                                       where !ExportDisabledEntityTypes.Contains(entityType.Id)
                                                       select entityType).ToList());
            }
        }

        public static string DateTimeFormatString
        {
            get
            {
                return "yyyy-MM-dd HH:mm:ss";
            }
        }

        public static string ExportFileName
        {
            get
            {
                return "Catalog.xml";
            }
        }

        public static string MimeType
        {
            get
            {
                return "ResourceMimeType";
            }
        }

        public static string OriginalDisplayConfiguration
        {
            get
            {
                return "Original";
            }
        }

        public static string CVLKeyDelimiter
        {
            get
            {
                return "||";
            }
        }

        public static string EPiCommonField
        {
            get
            {
                return "EPiMetaFieldName";
            }
        }
        
        public static string SKUFieldName
        {
            get
            {
                return "SKUs";
            }
        }

        public static string SKUData
        {
            get
            {
                return "Data";
            }
        }

        public XDocument MappingDocument { get; set; }

        public string Id { get; private set; }

        public List<LinkType> LinkTypes { get; set; }

        public int ChannelId
        {
            get
            {
                if (!this.settings.ContainsKey("CHANNEL_ID"))
                {
                    return 0;
                }

                return int.Parse(this.settings["CHANNEL_ID"]);
            }
        }

        public bool ModifyFilterBehavior
        {
            get
            {
                if (this.modifyFilterBehavior == null)
                {
                    if (!this.settings.ContainsKey("MODIFY_FILTER_BEHAVIOR"))
                    {
                        this.modifyFilterBehavior = false;
                        return false;
                    }

                    string value = this.settings["MODIFY_FILTER_BEHAVIOR"];
                    if (!string.IsNullOrEmpty(value))
                    {
                        this.modifyFilterBehavior = bool.Parse(value);
                    }
                    else
                    {
                        this.modifyFilterBehavior = false;
                    }
                }

                return (bool)this.modifyFilterBehavior;
            }
        }

        public string PublicationsRootPath
        {
            get
            {
                if (!this.settings.ContainsKey("PUBLISH_FOLDER"))
                {
                    return @"C:\temp\Publish\Epi";
                }

                return this.settings["PUBLISH_FOLDER"];
            }
        }

        public string HttpPostUrl
        {
            get
            {
                if (!this.settings.ContainsKey("HTTP_POST_URL"))
                {
                    return string.Empty;
                }

                return this.settings["HTTP_POST_URL"];
            }
        }

        public Dictionary<CultureInfo, CultureInfo> LanguageMapping
        {
            get
            {
                if (this.languageMapping == null)
                {
                    if (!this.settings.ContainsKey("LANGUAGE_MAPPING"))
                    {
                        return new Dictionary<CultureInfo, CultureInfo>();
                    }

                    string mappingXml = this.settings["LANGUAGE_MAPPING"];

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

                    this.languageMapping = languageMapping2;
                }

                return this.languageMapping;
            }

            set
            {
                this.languageMapping = value;
            }
        }

        public Dictionary<string, string> EpiNameMapping
        {
            get
            {
                if (this.epiNameMapping == null)
                {
                    if (!this.settings.ContainsKey("EPI_NAME_FIELDS"))
                    {
                        this.epiNameMapping = new Dictionary<string, string>();

                        return this.epiNameMapping;
                    }

                    string value = this.settings["EPI_NAME_FIELDS"];

                    this.epiNameMapping = new Dictionary<string, string>();
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
                            if (fieldType != null && !this.epiNameMapping.ContainsKey(fieldType.EntityTypeId))
                            {
                                this.epiNameMapping.Add(fieldType.EntityTypeId, fieldType.Id);
                            }
                        }
                    }
                }

                return this.epiNameMapping;
            }
        }

        public string ResourcesRootPath
        {
            get
            {
                if (!this.settings.ContainsKey("PUBLISH_FOLDER_RESOURCES"))
                {
                    return @"C:\temp\Publish\Epi\Resources";
                }

                return this.settings["PUBLISH_FOLDER_RESOURCES"];
            }
        }

        public bool UseThreeLevelsInCommerce
        {
            get
            {
                if (this.useThreeLevelsInCommerce == null)
                {
                    if (!this.settings.ContainsKey("USE_THREE_LEVELS_IN_COMMERCE"))
                    {
                        this.useThreeLevelsInCommerce = false;
                        return false;
                    }

                    string value = this.settings["USE_THREE_LEVELS_IN_COMMERCE"];

                    if (!string.IsNullOrEmpty(value))
                    {
                        this.useThreeLevelsInCommerce = bool.Parse(value);
                    }
                    else
                    {
                        this.useThreeLevelsInCommerce = false;
                    }
                }

                return (bool)this.useThreeLevelsInCommerce;
            }
        }

        public CultureInfo ChannelDefaultLanguage
        {
            get
            {
                return this.channelDefaultLanguage ?? (this.channelDefaultLanguage = new CultureInfo("en-us"));
            }

            set
            {
                this.channelDefaultLanguage = value;
            }
        }

        public string ChannelDefaultCurrency
        {
            get
            {
                if (string.IsNullOrEmpty(this.channelDefaultCurrency))
                {
                    this.channelDefaultCurrency = "usd";
                }
                
                return this.channelDefaultCurrency;
            }

            set
            {
                this.channelDefaultCurrency = value;
            }
        }

        public bool ExportInventoryData
        {
            get
            {
                if (this.exportInventoryData == null)
                {
                    if (!this.settings.ContainsKey("EXPORT_INVENTORY_DATA"))
                    {
                        this.exportInventoryData = false;
                        return false;
                    }

                    string value = this.settings["EXPORT_INVENTORY_DATA"];

                    if (!string.IsNullOrEmpty(value))
                    {
                        this.exportInventoryData = bool.Parse(value);
                    }
                    else
                    {
                        this.exportInventoryData = false;
                    }
                }

                return (bool)this.exportInventoryData;
            }
        }

        public bool ExportPricingData
        {
            get
            {
                if (this.exportPricingData == null)
                {
                    if (!this.settings.ContainsKey("EXPORT_PRICING_DATA"))
                    {
                        this.exportPricingData = false;
                        return false;
                    }

                    string value = this.settings["EXPORT_PRICING_DATA"];

                    if (!string.IsNullOrEmpty(value))
                    {
                        this.exportPricingData = bool.Parse(value);
                    }
                    else
                    {
                        this.exportPricingData = false;
                    }
                }

                return (bool)this.exportPricingData;
            }
        }

        public Dictionary<string, string> EpiCodeMapping
        {
            get
            {
                if (this.epiCodeMapping == null)
                {
                    if (!this.settings.ContainsKey("EPI_CODE_FIELDS"))
                    {
                        this.epiCodeMapping = new Dictionary<string, string>();

                        return this.epiCodeMapping;
                    }

                    string value = this.settings["EPI_CODE_FIELDS"];

                    this.epiCodeMapping = new Dictionary<string, string>();
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
                            if (fieldType != null && !this.epiCodeMapping.ContainsKey(fieldType.EntityTypeId))
                            {
                                this.epiCodeMapping.Add(fieldType.EntityTypeId, fieldType.Id);
                            }
                        }
                    }
                }

                return this.epiCodeMapping;
            }
        }

        public List<StructureEntity> ChannelStructureEntities { get; set; }

        public Dictionary<int, Entity> ChannelEntities { get; set; }

        public string ChannelDefaultWeightBase
        {
            get
            {
                if (string.IsNullOrEmpty(this.channelWeightBase))
                {
                    this.channelWeightBase = "lbs";
                }

                return this.channelWeightBase;
            }

            set
            {
                this.channelWeightBase = value;
            }
        }

        public string ChannelIdPrefix
        {
            get
            {
                return this.channelIdPrefix;
            }

            set
            {
                this.channelIdPrefix = value;
            }
        }

        public Dictionary<string, string> ChannelMimeTypeMappings
        {
            get
            {
                return this.channelMimeTypeMappings;
            }

            set
            {
                this.channelMimeTypeMappings = value;
            }
        }

        public bool ChannelAllowBackorder
        {
            get
            {
                return this.channelAllowBackorder ?? true;
            }
            
            set
            {
                this.channelAllowBackorder = value;
            }
        }

        public bool ChannelAllowPreorder
        {
            get
            {
                return this.channelAllowPreorder ?? true;
            }

            set
            {
                this.channelAllowPreorder = value;
            }
        }

        public DateTime ChannelBackorderAvailabilityDate
        {
            get
            {
                return this.channelBackorderAvailabilityDate ?? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            set
            {
                this.channelBackorderAvailabilityDate = value;
            }
        }

        public int ChannelBackorderQuantity
        {
            get
            {
                return this.channelBackorderQuantity ?? 6;
            }

            set
            {
                this.channelBackorderQuantity = value;
            }
        }

        public int ChannelInStockQuantity
        {
            get
            {
                return this.channelInStockQuantity ?? 10;
            }

            set
            {
                this.channelInStockQuantity = value;
            }
        }

        public int ChannelInventoryStatus
        {
            get
            {
                return this.channelInventoryStatus ?? 1;
            }

            set
            {
                this.channelInventoryStatus = value;
            }
        }

        public DateTime ChannelPreorderAvailabilityDate
        {
            get
            {
                return this.channelPreorderAvailabilityDate ?? new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            
            set
            {
                this.channelPreorderAvailabilityDate = value;
            }
        }

        public int ChannelPreorderQuantity
        {
            get
            {
                return this.channelPreorderQuantity ?? 4;
            }
            
            set
            {
                this.channelPreorderQuantity = value;
            }
        }

        public int ChannelReorderMinQuantity
        {
            get
            {
                return this.channelReorderMinQuantity ?? 3;
            }
            
            set
            {
                this.channelReorderMinQuantity = value;
            }
        }

        public int ChannelReservedQuantity
        {
            get
            {
                return this.channelReservedQuantity ?? 2;
            }
            
            set
            {
                this.channelReservedQuantity = value;
            }
        }

        public string ChannelMarketId
        {
            get
            {
                return this.channelMarketId ?? "DEFAULT";
            }
            
            set
            {
                this.channelMarketId = value;
            }
        }

        public string ChannelCurrencyCode
        {
            get
            {
                return this.channelCurrencyCode ?? "USD";
            }
            
            set
            {
                this.channelCurrencyCode = value;
            }
        }

        public int ChannelPriceTypeId
        {
            get
            {
                return this.channelPriceTypeId ?? 0;
            }
            
            set
            {
                this.channelPriceTypeId = value;
            }
        }

        public string ChannelPriceCode
        {
            get
            {
                return this.channelPriceCode ?? string.Empty;
            }
            
            set
            {
                this.channelPriceCode = value;
            }
        }

        public DateTime ChannelValidFrom
        {
            get
            {
                return this.channelValidFrom ?? new DateTime(1967, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            
            set
            {
                this.channelValidFrom = value;
            }
        }

        public DateTime ChannelValidUntil
        {
            get
            {
                return this.channelValidUntil ?? new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            
            set
            {
                this.channelValidUntil = value;
            }
        }

        public double ChannelMinQuantity
        {
            get
            {
                return this.channelMinQuantity ?? 0.0;
            }
            
            set
            {
                this.channelMinQuantity = value;
            }
        }

        public double ChannelUnitPrice
        {
            get
            {
                return this.channelUnitPrice ?? 0.0;
            }
            
            set
            {
                this.channelUnitPrice = value;
            }
        }

        public string[] ResourceConfigurations
        {
            get
            {
                if (!this.settings.ContainsKey("RESOURCE_CONFIGURATION"))
                {
                    return new string[0];
                }

                Dictionary<string, string> resourceConfWithExt = this.ParseResourceConfig(this.settings["RESOURCE_CONFIGURATION"]);
                return resourceConfWithExt.Keys.ToArray();
            }
        }

        public Dictionary<string, string> ResourceConfiugurationExtensions
        {
            get
            {
                return this.resourceConfiugurationExtensions
                       ?? (this.resourceConfiugurationExtensions =
                           this.ParseResourceConfig(this.settings["RESOURCE_CONFIGURATION"]));
            }
        }

        public LinkType[] ExportEnabledLinkTypes
        {
            get
            {
                if (this.exportEnabledLinkTypes == null)
                {
                    this.exportEnabledLinkTypes = new List<LinkType>();
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
                            || (this.BundleEntityTypes.Contains(linkType.SourceEntityTypeId) && !this.BundleEntityTypes.Contains(linkType.TargetEntityTypeId))
                            || (this.PackageEntityTypes.Contains(linkType.SourceEntityTypeId) && !this.PackageEntityTypes.Contains(linkType.TargetEntityTypeId))
                            || (this.DynamicPackageEntityTypes.Contains(linkType.SourceEntityTypeId) && !this.DynamicPackageEntityTypes.Contains(linkType.TargetEntityTypeId))
                            || (linkType.SourceEntityTypeId.Equals("Product") && linkType.TargetEntityTypeId.Equals("Item") && firstProdItemLink != null && linkType.Id == firstProdItemLink.Id)))
                        {
                            continue;
                        }

                        if (ExportEnabledEntityTypes.Any(eee => eee.Id.Equals(linkType.SourceEntityTypeId))
                            && ExportEnabledEntityTypes.Any(eee => eee.Id.Equals(linkType.TargetEntityTypeId)))
                        {
                            this.exportEnabledLinkTypes.Add(linkType);
                        }
                    }
                }

                return this.exportEnabledLinkTypes.ToArray();
            }
        }

        public bool ItemsToSkus
        {
            get
            {
                string value = this.settings["ITEM_TO_SKUs"];
                if (!bool.TryParse(value, out this.itemsToSkus))
                {
                    this.itemsToSkus = false;
                }

                return this.itemsToSkus;
            }
        }

        public int BatchSize
        {
            get
            {
                if (this.settings.ContainsKey("BATCH_SIZE"))
                {
                    string value = this.settings["BATCH_SIZE"];

                    if (!int.TryParse(value, out this.batchsize) || value == "0")
                    {
                        this.batchsize = int.MaxValue;
                    }

                    return this.batchsize;
                }

                return int.MaxValue;
            }
        }

        public Dictionary<int, string> EntityIdAndType { get; set; }

        public string[] BundleEntityTypes
        {
            get
            {
                if (!this.settings.ContainsKey("BUNDLE_ENTITYTYPES"))
                {
                    return new string[0];
                }

                return StringToStringArray(this.settings["BUNDLE_ENTITYTYPES"]);
            }
        }

        public string[] PackageEntityTypes
        {
            get
            {
                if (!this.settings.ContainsKey("PACKAGE_ENTITYTYPES"))
                {
                    return new string[0];
                }

                return StringToStringArray(this.settings["PACKAGE_ENTITYTYPES"]);
            }
        }

        public string[] DynamicPackageEntityTypes
        {
            get
            {
                if (!this.settings.ContainsKey("DYNAMIC_PACKAGE_ENTITYTYPES"))
                {
                    return new string[0];
                }

                return StringToStringArray(this.settings["DYNAMIC_PACKAGE_ENTITYTYPES"]);
            }
        }

        public HashSet<string> EPiFieldsIninRiver
        {
            get
            {
                if (this.excludedFields != null)
                {
                    return this.excludedFields;
                }

                if (!this.settings.ContainsKey("EXCLUDE_FIELDS") || string.IsNullOrEmpty(this.settings["EXCLUDE_FIELDS"]))
                {
                    HashSet<string> excludedFieldTypes = new HashSet<string>();
                    foreach (string baseField in this.epiFieldsIninRiver)
                    {
                        foreach (var entityType in ExportEnabledEntityTypes)
                        {
                            excludedFieldTypes.Add(entityType.Id.ToLower() + baseField);  
                        }
                    }
                    
                    excludedFieldTypes.Add("skus");

                    this.excludedFields = excludedFieldTypes;
                    return this.excludedFields;
                }
                else
                {
                    HashSet<string> excludedFieldTypes = new HashSet<string>();
                    foreach (string baseField in this.epiFieldsIninRiver)
                    {
                        foreach (var entityType in ExportEnabledEntityTypes)
                        {
                            excludedFieldTypes.Add(entityType.Id.ToLower() + baseField);
                        }
                    }
                    
                    excludedFieldTypes.Add("skus");
                    
                    string[] fields = this.settings["EXCLUDE_FIELDS"].Split(',');
                    foreach (string field in fields)
                    {
                        if (!excludedFieldTypes.Contains(field.ToLower()))
                        {
                            excludedFieldTypes.Add(field.ToLower());
                        }
                    }

                    this.excludedFields = excludedFieldTypes;
                    return this.excludedFields;
                }
            }
        }

        public PublicationMode ActivePublicationMode
        {
            get
            {
                return PublicationMode.Automatic;
            }
        }

        public CVLDataMode ActiveCVLDataMode
        {
            get
            {
                if (!this.settings.ContainsKey("CVL_DATA"))
                {
                    return CVLDataMode.Undefined;
                }

                return StringToCVLDataMode(this.settings["CVL_DATA"]);
            }
        }

        public string ResourceProviderType
        {
            get
            {
                if (!this.settings.ContainsKey("RESOURCE_PROVIDER_TYPE"))
                {
                    return string.Empty;
                }

                return this.settings["RESOURCE_PROVIDER_TYPE"];
            }
        }

        public Dictionary<string, string> Settings
        {
            get { return this.settings; }
        }

        private static string[] StringToStringArray(string setting)
        {
            if (string.IsNullOrEmpty(setting))
            {
                return new string[0];
            }

            setting = setting.Replace(" ", string.Empty);
            if (setting.Contains(','))
            {
                return setting.Split(',');
            }

            return new[] { setting };
        }

        private static CVLDataMode StringToCVLDataMode(string str)
        {
            CVLDataMode mode;

            if (!Enum.TryParse(str, out mode))
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not parse CVLDataMode for string {0}", str));
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
