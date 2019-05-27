using System;
using System.Collections.Generic;
using System.Linq;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public class EpiMappingHelper
    {
        private static int _firstProductItemLinkType = -2;
        private readonly IConfiguration _config;
        private readonly IPimFieldAdapter _pimFieldAdapter;

        public EpiMappingHelper(IConfiguration config, IPimFieldAdapter pimFieldAdapter)
        {
            _config = config;
            _pimFieldAdapter = pimFieldAdapter;
        }

        public static int FirstProductItemLinkType
        {
            get
            {
                if (_firstProductItemLinkType >= -1)
                    return _firstProductItemLinkType;

                List<LinkType> linkTypes = RemoteManager.ModelService.GetLinkTypesForEntityType("Product");
                LinkType first =
                    linkTypes.Where(lt => lt.TargetEntityTypeId.Equals("Item"))
                        .OrderBy(lt => lt.Index)
                        .FirstOrDefault();

                _firstProductItemLinkType = first?.Index ?? -1;

                return _firstProductItemLinkType;
            }
        }

        /// <summary>
        /// Creates the unique name as required for by Episerver
        /// </summary>
        /// <param name="structureEntity"></param>
        /// <returns></returns>
        public string GetAssociationName(StructureEntity structureEntity)
        {
            return structureEntity.LinkTypeIdFromParent;
        }

        public string GetEntryType(string entityTypeId)
        {
            if (entityTypeId.Equals("Item"))
            {
                if (!(_config.UseThreeLevelsInCommerce && _config.ItemsToSkus))
                {
                    return "Variation";
                }
            }
            else if (_config.BundleEntityTypes.Contains(entityTypeId))
            {
                return "Bundle";
            }
            else if (_config.PackageEntityTypes.Contains(entityTypeId))
            {
                return "Package";
            }
            else if (_config.DynamicPackageEntityTypes.Contains(entityTypeId))
            {
                return "DynamicPackage";
            }

            return "Product";
        }

        public string GetEpiserverDataType(FieldType fieldType)
        {
            string type = String.Empty;

            if (fieldType == null || String.IsNullOrEmpty(fieldType.DataType))
            {
                return type;
            }

            if (fieldType.DataType.Equals(DataType.Boolean))
            {
                type = "Boolean";
            }
            else if (fieldType.DataType.Equals(DataType.CVL))
            {
                type = "LongString";
            }
            else if (fieldType.DataType.Equals(DataType.DateTime))
            {
                type = "DateTime";
            }
            else if (fieldType.DataType.Equals(DataType.Double))
            {
                type = "Float";
            }
            else if (fieldType.DataType.Equals(DataType.File))
            {
                type = "Integer";
            }
            else if (fieldType.DataType.Equals(DataType.Integer))
            {
                type = "Integer";
            }
            else if (fieldType.DataType.Equals(DataType.LocaleString))
            {
                if (fieldType.Settings.ContainsKey("AdvancedTextObject"))
                {
                    type = fieldType.Settings["AdvancedTextObject"] == "1" ? "LongHtmlString" : "LongString";
                }
                else if (fieldType.Settings.ContainsKey("EPiDataType"))
                {
                    type = fieldType.Settings["EPiDataType"] == "ShortString" ? "ShortString" : "LongString";
                }
                else
                {
                    type = "LongString";
                }
            }
            else if (fieldType.DataType.Equals(DataType.String))
            {
                if (fieldType.Settings.ContainsKey("EPiDataType"))
                {
                    type = fieldType.Settings["EPiDataType"] == "ShortString" ? "ShortString" : "LongString";
                }
                else
                {
                    type = "LongString";
                }
            }
            else if (fieldType.DataType.Equals(DataType.Xml))
            {
                if (fieldType.Settings.ContainsKey("EPiDataType"))
                {
                    type = fieldType.Settings["EPiDataType"] == "ShortString" ? "ShortString" : "LongString";
                }
                else
                {
                    type = "LongString";
                }
            }

            return type;
        }

        public string GetEpiserverFieldName(FieldType fieldType)
        {
            string name = fieldType.Id;

            if (fieldType.Settings != null &&
                fieldType.Settings.ContainsKey(FieldNames.EPiCommonField) &&
                !String.IsNullOrEmpty(fieldType.Settings[FieldNames.EPiCommonField]))
            {
                name = fieldType.Settings[FieldNames.EPiCommonField];
            }

            return name;
        }

        public int GetMetaFieldLength(FieldType fieldType)
        {
            var defaultLength = 150;

            if (fieldType.Settings.ContainsKey("MetaFieldLength"))
            {
                if (!Int32.TryParse(fieldType.Settings["MetaFieldLength"], out defaultLength))
                {
                    return 150;
                }
            }

            if (fieldType.Settings.ContainsKey("AdvancedTextObject"))
            {
                if (fieldType.Settings["AdvancedTextObject"] == "1")
                {
                    return 65000;
                }
            }

            return defaultLength;
        }

        public string GetNameForEntity(Entity entity, int maxLength)
        {
            Field nameField = null;
            if (_config.EpiNameMapping.ContainsKey(entity.EntityType.Id))
            {
                nameField = GetField(entity, _config.EpiNameMapping[entity.EntityType.Id]);
            }

            string returnString = String.Empty;
            if (nameField == null || nameField.IsEmpty())
            {
                returnString = _pimFieldAdapter.GetDisplayName(entity, maxLength);
            }
            else if (nameField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                var ls = (LocaleString) nameField.Data;
                if (!String.IsNullOrEmpty(ls[_config.LanguageMapping[_config.ChannelDefaultLanguage]]))
                {
                    returnString = ls[_config.LanguageMapping[_config.ChannelDefaultLanguage]];
                }
            }
            else
            {
                returnString = nameField.Data.ToString();
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

        public string GetParentClassForEntityType(string entityTypeName)
        {
            if (entityTypeName.ToLower().Contains("channelnode"))
            {
                return "CatalogNode";
            }

            return "CatalogEntry";
        }

        public string GetTableNameForEntityType(string entityTypeName, string name)
        {
            if (entityTypeName.ToLower().Contains("channelnode"))
            {
                return "CatalogNodeEx_" + name;
            }

            return "CatalogEntryEx_" + name;
        }

        public bool IsChannelNodeLink(string linkTypeId)
        {
            LinkType linkType = _config.LinkTypes.FirstOrDefault(x => x.Id == linkTypeId);

            return linkType?.SourceEntityTypeId == "ChannelNode" ||
                   (linkType?.SourceEntityTypeId == "Channel" && linkType.TargetEntityTypeId == "ChannelNode");
        }

        /// <summary>
        /// A LinkType is a relation if it represents a product-item, bundle, package or dynamic package in Episerver
        /// </summary>
        public bool IsRelation(LinkType linkType)
        {
            IntegrationLogger.Write(LogLevel.Debug, "SourceEntityTypeId is: " + linkType.SourceEntityTypeId);
            IntegrationLogger.Write(LogLevel.Debug, "TargetEntityTypeId is: " + linkType.TargetEntityTypeId);
            if (linkType.SourceEntityTypeId.Equals("Item") && linkType.TargetEntityTypeId.Equals("Item"))
                return false;

            if ((_config.BundleEntityTypes.Contains(linkType.SourceEntityTypeId) && !_config.BundleEntityTypes.Contains(linkType.TargetEntityTypeId)) ||
                (_config.PackageEntityTypes.Contains(linkType.SourceEntityTypeId) && !_config.PackageEntityTypes.Contains(linkType.TargetEntityTypeId)) ||
                (_config.DynamicPackageEntityTypes.Contains(linkType.SourceEntityTypeId) && !_config.DynamicPackageEntityTypes.Contains(linkType.TargetEntityTypeId)))
            {
                return true;
            }

            return linkType.SourceEntityTypeId.Equals("Product") && linkType.TargetEntityTypeId.Equals("Item") && linkType.Index == FirstProductItemLinkType;
        }

        public bool IsRelation(string linkTypeId)
        {
            LinkType linktype = _config.LinkTypes.Find(lt => lt.Id == linkTypeId);
            return IsRelation(linktype);
        }

        public bool SkipField(FieldType fieldType)
        {
            bool result = _config.EPiFieldsIninRiver.Contains(fieldType.Id.ToLower());
            return result;
        }

        private Field GetField(Entity entity, string fieldTypeId)
        {
            if (String.IsNullOrEmpty(fieldTypeId))
                return null;

            return entity.Fields.FirstOrDefault(f => f.FieldType?.Id.ToLower() == fieldTypeId.ToLower());
        }
    }
}