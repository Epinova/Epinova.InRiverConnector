using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using inRiver.Remoting;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public class EpiMappingHelper
    {
        private static int firstProductItemLinkType = -2;

        public static int FirstProductItemLinkType
        {
            get
            {
                if (firstProductItemLinkType < -1)
                {
                    List<LinkType> linkTypes = RemoteManager.ModelService.GetLinkTypesForEntityType("Product");
                    LinkType first =
                        linkTypes.Where(lt => lt.TargetEntityTypeId.Equals("Item"))
                            .OrderBy(lt => lt.Index)
                            .FirstOrDefault();

                    firstProductItemLinkType = first != null ? first.Index : -1;
                }

                return firstProductItemLinkType;
            }
        }

        public static string GetParentClassForEntityType(string entityTypeName)
        {
            if (entityTypeName.ToLower().Contains("channelnode"))
            {
                return "CatalogNode";
            }

            return "CatalogEntry";
        }

        public static bool IsRelation(string sourceEntityTypeId, string targetEntityTypeId, int sortOrder, Configuration config)
        {
            if ((config.BundleEntityTypes.Contains(sourceEntityTypeId) && !config.BundleEntityTypes.Contains(targetEntityTypeId))
                            || (config.PackageEntityTypes.Contains(sourceEntityTypeId) && !config.PackageEntityTypes.Contains(targetEntityTypeId))
                            || (config.DynamicPackageEntityTypes.Contains(sourceEntityTypeId) && !config.DynamicPackageEntityTypes.Contains(targetEntityTypeId)))
            {
                return true;
            }

            return sourceEntityTypeId.Equals("Product") && targetEntityTypeId.Equals("Item")
                   && sortOrder == FirstProductItemLinkType;
        }

        public static bool IsRelation(string linkTypeId, Configuration config)
        {
            LinkType linktype = config.LinkTypes.Find(lt => lt.Id == linkTypeId);

            if ((config.BundleEntityTypes.Contains(linktype.SourceEntityTypeId) && !config.BundleEntityTypes.Contains(linktype.TargetEntityTypeId))
                 || (config.PackageEntityTypes.Contains(linktype.SourceEntityTypeId) && !config.PackageEntityTypes.Contains(linktype.TargetEntityTypeId))
                 || (config.DynamicPackageEntityTypes.Contains(linktype.SourceEntityTypeId) && !config.DynamicPackageEntityTypes.Contains(linktype.TargetEntityTypeId)))
            {
                return true;
            }

            return linktype.SourceEntityTypeId.Equals("Product") && linktype.TargetEntityTypeId.Equals("Item")
                   && linktype.Index == FirstProductItemLinkType;
        }

        public static string GetAssociationName(Link link, Configuration config)
        {
            if (link.LinkEntity != null)
            {
                // Use the Link name + the display name to create a unique ASSOCIATION NAME in EPi Commerce
                return link.LinkType.LinkEntityTypeId + '_'
                       + BusinessHelper.GetDisplayNameFromEntity(link.LinkEntity, config, -1).Replace(' ', '_');
            }

            return link.LinkType.Id;
        }

        public static string GetAssociationName(StructureEntity structureEntity, Entity linkEntity, Configuration config)
        {
            if (structureEntity.LinkEntityId != null)
            {
                // Use the Link name + the display name to create a unique ASSOCIATION NAME in EPi Commerce
                return linkEntity.EntityType.Id + '_'
                       + BusinessHelper.GetDisplayNameFromEntity(linkEntity, config, -1).Replace(' ', '_');
            }

            return structureEntity.LinkTypeIdFromParent;
        }

        public static string GetTableNameForEntityType(string entityTypeName, string name)
        {
            if (entityTypeName.ToLower().Contains("channelnode"))
            {
                return "CatalogNodeEx_" + name;
            }

            return "CatalogEntryEx_" + name;
        }

        public static bool SkipField(FieldType fieldType, Configuration config)
        {
            bool result = config.EPiFieldsIninRiver.Contains(fieldType.Id.ToLower());
            return result;
        }

        public static int GetMetaFieldLength(FieldType fieldType, Configuration config)
        {
            int defaultLength = 150;

            if (fieldType.Settings.ContainsKey("MetaFieldLength"))
            {
                if (!int.TryParse(fieldType.Settings["MetaFieldLength"], out defaultLength))
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

        public static string GetEpiserverDataType(FieldType fieldType, Configuration config)
        {
            string type = string.Empty;

            if (fieldType == null || string.IsNullOrEmpty(fieldType.DataType))
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
                    if (fieldType.Settings["AdvancedTextObject"] == "1")
                    {
                        type = "LongHtmlString";
                    }
                    else
                    {
                        type = "LongString";
                    }
                }
                else if (fieldType.Settings.ContainsKey("EPiDataType"))
                {
                    if (fieldType.Settings["EPiDataType"] == "ShortString")
                    {
                        type = "ShortString";
                    }
                    else
                    {
                        type = "LongString";
                    }
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
                    if (fieldType.Settings["EPiDataType"] == "ShortString")
                    {
                        type = "ShortString";
                    }
                    else
                    {
                        type = "LongString";
                    }
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
                    if (fieldType.Settings["EPiDataType"] == "ShortString")
                    {
                        type = "ShortString";
                    }
                    else
                    {
                        type = "LongString";
                    }
                }
                else
                {
                    type = "LongString";
                }
            }

            return type;
        }

        public static List<string> GetLocaleStringValues(object data, Configuration configuration)
        {
            List<string> localeStringValues = new List<string>();

            if (data == null)
            {
                return localeStringValues;
            }

            LocaleString ls = (LocaleString)data;

            foreach (KeyValuePair<CultureInfo, CultureInfo> keyValuePair in configuration.LanguageMapping)
            {
                if (!localeStringValues.Any(e => e.Equals(ls[keyValuePair.Value])))
                {
                    localeStringValues.Add(ls[keyValuePair.Value]);
                }
            }

            return localeStringValues;
        }

        public static string GetNameForEntity(Entity entity, Configuration configuration, int maxLength)
        {
            Field nameField = null;
            if (configuration.EpiNameMapping.ContainsKey(entity.EntityType.Id))
            {
                nameField = entity.GetField(configuration.EpiNameMapping[entity.EntityType.Id]);
            }

            string returnString = string.Empty;
            if (nameField == null || nameField.IsEmpty())
            {
                returnString = BusinessHelper.GetDisplayNameFromEntity(entity, configuration, maxLength);
            }
            else if (nameField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                LocaleString ls = (LocaleString)nameField.Data;
                if (!string.IsNullOrEmpty(ls[configuration.LanguageMapping[configuration.ChannelDefaultLanguage]]))
                {
                    returnString = ls[configuration.LanguageMapping[configuration.ChannelDefaultLanguage]];
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

        public static string GetEpiserverFieldName(FieldType fieldType, Configuration config)
        {
            string name = fieldType.Id;

            if (fieldType.Settings != null && 
                fieldType.Settings.ContainsKey(Configuration.EPiCommonField) &&
                !string.IsNullOrEmpty(fieldType.Settings[Configuration.EPiCommonField]))
            {
                name = fieldType.Settings[Configuration.EPiCommonField];
            }

            return name;
        }

        public static string GetEntryType(string entityTypeId, Configuration configuration)
        {
            if (entityTypeId.Equals("Item"))
            {
                if (!(configuration.UseThreeLevelsInCommerce && configuration.ItemsToSkus))
                {
                    return "Variation";
                }
            }
            else if (configuration.BundleEntityTypes.Contains(entityTypeId))
            {
                return "Bundle";
            }
            else if (configuration.PackageEntityTypes.Contains(entityTypeId))
            {
                return "Package";
            }
            else if (configuration.DynamicPackageEntityTypes.Contains(entityTypeId))
            {
                return "DynamicPackage";
            }

            return "Product";
        }
    }
}