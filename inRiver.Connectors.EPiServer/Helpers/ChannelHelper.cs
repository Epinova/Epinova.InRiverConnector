using inRiver.Integration.Logging;
using inRiver.Remoting.Log;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using inRiver.EPiServerCommerce.CommerceAdapter.EpiXml;
using inRiver.Remoting;
using inRiver.Remoting.Objects;

namespace inRiver.EPiServerCommerce.CommerceAdapter.Helpers
{
    public class ChannelHelper
    {
        public static Guid GetChannelGuid(Entity channel, Configuration configuration)
        {
            string value = channel.Id.ToString(CultureInfo.InvariantCulture);

            if (channel.DisplayName != null && !channel.DisplayName.IsEmpty())
            {
                if (channel.DisplayName.FieldType.DataType.Equals(DataType.LocaleString))
                {
                    value =
                        ((LocaleString)channel.DisplayName.Data)[configuration.LanguageMapping[configuration.ChannelDefaultLanguage]];
                }
                else
                {
                    value = channel.DisplayName.Data.ToString();
                }

                if (string.IsNullOrEmpty(value))
                {
                    value = channel.Id.ToString(CultureInfo.InvariantCulture);
                }
            }

            MD5 md5Hasher = MD5.Create();
            byte[] data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(value));
            return new Guid(data);
        }

        public static int GetParentChannelNode(StructureEntity structureEntity, Configuration config)
        {
            int entityId = 0;
            List<string> entities = structureEntity.Path.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            entities.RemoveAt(entities.Count - 1);
            entities.RemoveAt(0);
            if (entities.Count == 0)
            {
                return entityId;
            }

            for (int index = entities.Count - 1; index > -1; index--)
            {
                int tempEntityId = int.Parse(entities[index]);

                StructureEntity foundStructureEntity = config.ChannelStructureEntities.Find(i => i.EntityId.Equals(tempEntityId));

                if (foundStructureEntity != null && foundStructureEntity.Type == "ChannelNode")
                {
                    entityId = tempEntityId;
                    break;
                }
            }

            return entityId;
        }

        internal static int GetParentChannelNode(StructureEntity structureEntity, int channelId)
        {
            int parentNodeId = 0;

            List<string> parentIds = structureEntity.Path.Split('/').ToList();
            parentIds.Reverse();
            parentIds.RemoveAt(0);

            for (int i = 0; i < parentIds.Count - 1; i++)
            {
                int entityId = int.Parse(parentIds[i]);
                int parentId = int.Parse(parentIds[i + 1]);

                var structureEntities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(
                    channelId,
                    entityId,
                    parentId);

                foreach (var se in structureEntities)
                {
                    if (se.Type == "ChannelNode")
                    {
                        parentNodeId = se.EntityId;
                        break;
                    }
                }

                if (parentNodeId != 0)
                {
                    break;
                }

            }

            return parentNodeId;
        }

        internal static List<StructureEntity> FindEntitiesElementInStructure(List<StructureEntity> channelEntities, int sourceEntityId, int targetEntityId, string linktype)
        {
            List<StructureEntity> structureEntities = new List<StructureEntity>();

            structureEntities.AddRange(channelEntities.Where(e =>
                                                        e.EntityId.Equals(targetEntityId) &&
                                                        e.ParentId != 0 &&
                                                        e.ParentId.Equals(sourceEntityId)));

            return structureEntities;
        }

        internal static bool LinkTypeHasLinkEntity(string linkTypeId)
        {
            LinkType linktype = RemoteManager.ModelService.GetLinkType(linkTypeId);
            if (linktype.LinkEntityTypeId != null)
            {
                return true;
            }

            return false;
        }

        public static string GetChannelIdentifier(Entity channelEntity)
        {
            string channelIdentifier = channelEntity.Id.ToString(CultureInfo.InvariantCulture);
            if (channelEntity.DisplayName != null && !channelEntity.DisplayName.IsEmpty())
            {
                channelIdentifier = channelEntity.DisplayName.Data.ToString();
            }

            return channelIdentifier;
        }

        public static List<StructureEntity> GetAllEntitiesInChannel(int channelId, List<EntityType> entityTypes)
        {
            List<StructureEntity> result = new List<StructureEntity>();
            foreach (EntityType entityType in entityTypes)
            {
                List<StructureEntity> response = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(channelId, entityType.Id);
                result.AddRange(response);
            }

            return result;
        }

        public static List<StructureEntity> GetEntityInChannelWithParent(int channelId, int entityId, int parentId)
        {
            var result = new List<StructureEntity>();
            var response = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channelId, entityId, parentId);
            if (response.Any())
            {
                result.AddRange(response);
            }

            return result;
        }

        public static string GetTargetEntityPath(int targetEntityId, List<StructureEntity> channelEntities, int? parentId = null)
        {
            StructureEntity targetStructureEntity = new StructureEntity();

            if (parentId == null)
            {
                targetStructureEntity = channelEntities.Find(i => i.EntityId.Equals(targetEntityId));
            }
            else
            {
                targetStructureEntity = channelEntities.Find(i => i.EntityId.Equals(targetEntityId) && i.ParentId.Equals(parentId));
            }


            string path = string.Empty;

            if (targetStructureEntity != null)
            {
                path = targetStructureEntity.Path;
            }

            return path;
        }

        public static List<StructureEntity> GetChildrenEntitiesInChannel(int entityId, string path)
        {
            var result = new List<StructureEntity>();
            if (!string.IsNullOrEmpty(path))
            {
                var response = RemoteManager.ChannelService.GetChannelStructureChildrenFromPath(entityId, path);
                if (response.Any())
                {
                    result.AddRange(response);
                }
            }

            return result;
        }

        public static List<StructureEntity> GetStructureEntitiesForEntityInChannel(int channelId, int entityId)
        {
            return RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, entityId);
        }

        public static StructureEntity GetParentStructureEntity(int channelId, int sourceEntityId, int targetEntityId, List<StructureEntity> channelEntities)
        {
            StructureEntity targetStructureEntity =
                channelEntities.Find(i => i.EntityId.Equals(targetEntityId) && i.ParentId.Equals(sourceEntityId));

            List<StructureEntity> structureEntities =
                RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, sourceEntityId);

            if (targetStructureEntity == null || !structureEntities.Any())
            {
                return null;
            }

            int endIndex = targetStructureEntity.Path.LastIndexOf("/", StringComparison.InvariantCulture);

            string parentPath = targetStructureEntity.Path.Substring(0, endIndex);

            return structureEntities.Find(i => i.Path.Equals(parentPath) && i.EntityId.Equals(sourceEntityId));
        }

        public static void UpdateChannelSettings(Entity channel, Configuration configuration)
        {
            configuration.ChannelDefaultLanguage = GetChannelDefaultLanguage(channel);
            configuration.ChannelDefaultCurrency = GetChannelDefaultCurrency(channel);
            configuration.ChannelDefaultWeightBase = GetChannelDefaultWeightBase(channel);
            configuration.ChannelIdPrefix = GetChannelPrefix(channel);
            configuration.ChannelMimeTypeMappings = GetChannelMimeTypeMappings(channel);
            configuration.ChannelAllowBackorder = GetEntityAllowBackorder(channel, configuration);
            configuration.ChannelAllowPreorder = GetEntityAllowPreorder(channel, configuration);
            configuration.ChannelBackorderAvailabilityDate = GetEntityBackorderAvailabilityDate(channel, configuration);
            configuration.ChannelBackorderQuantity = GetEntityBackorderQuantity(channel, configuration);
            configuration.ChannelInStockQuantity = GetEntityInStockQuantity(channel, configuration);
            configuration.ChannelInventoryStatus = GetEntityInventoryStatus(channel, configuration);
            configuration.ChannelPreorderAvailabilityDate = GetEntityPreorderAvailabilityDate(channel, configuration);
            configuration.ChannelPreorderQuantity = GetEntityPreorderQuantity(channel, configuration);
            configuration.ChannelReorderMinQuantity = GetEntityReorderMinQuantity(channel, configuration);
            configuration.ChannelReservedQuantity = GetEntityReservedQuantity(channel, configuration);

            // Default Channel Price Data
            // <Prices>
            // <Price>
            // <MarketId>DEFAULT</MarketId>
            // <CurrencyCode>USD</CurrencyCode>
            // <PriceTypeId>0</PriceTypeId>
            // <PriceCode/>
            // <ValidFrom>1900-01-01 00:00:00Z</ValidFrom>
            // <ValidUntil/>
            // <MinQuantity>0.000000000</MinQuantity>
            // <UnitPrice>1000.0000</UnitPrice>
            // </Price>
            // </Prices>
            configuration.ChannelMarketId = GetEntityMarketId(channel, configuration);
            configuration.ChannelCurrencyCode = GetEntityCurrencyCode(channel, configuration);
            configuration.ChannelPriceTypeId = GetEntityPriceTypeId(channel, configuration);
            configuration.ChannelPriceCode = GetEntityPriceCode(channel, configuration);
            configuration.ChannelValidFrom = GetEntityValidFrom(channel, configuration);
            configuration.ChannelValidUntil = GetEntityValidUntil(channel, configuration);
            configuration.ChannelMinQuantity = GetEntityMinQuantity(channel, configuration);
            configuration.ChannelUnitPrice = GetEntityUnitPrice(channel, configuration);
        }

        public static string GetChannelPrefix(Entity channel)
        {
            Field channelPrefixField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channelprefix"));
            if (channelPrefixField == null || channelPrefixField.IsEmpty())
            {
                return string.Empty;
            }

            return channelPrefixField.Data.ToString();
        }

        public static Dictionary<string, string> GetChannelMimeTypeMappings(Entity channel)
        {
            Dictionary<string, string> channelMimeTypeMappings = new Dictionary<string, string>();
            Field channelMimeTypeField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channelmimetypemappings"));
            if (channelMimeTypeField == null || channelMimeTypeField.IsEmpty())
            {
                return channelMimeTypeMappings;
            }

            string channelMapping = channelMimeTypeField.Data.ToString();

            if (!channelMapping.Contains(','))
            {
                return channelMimeTypeMappings;
            }

            string[] mappings = channelMapping.Split(';');

            foreach (string mapping in mappings)
            {
                if (!mapping.Contains(','))
                {
                    continue;
                }

                string[] map = mapping.Split(',');
                channelMimeTypeMappings.Add(map[0].Trim(), map[1].Trim());
            }

            return channelMimeTypeMappings;
        }

        public static CultureInfo GetChannelDefaultLanguage(Entity channel)
        {
            Field defaultLanguageField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultlanguage"));
            if (defaultLanguageField == null || defaultLanguageField.IsEmpty())
            {
                return new CultureInfo("en-us");
            }

            return new CultureInfo(defaultLanguageField.Data.ToString());
        }

        public static string GetChannelDefaultCurrency(Entity channel)
        {
            Field defaultCurrencyField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultcurrency"));
            if (defaultCurrencyField == null || defaultCurrencyField.IsEmpty())
            {
                return "usd";
            }

            return defaultCurrencyField.Data.ToString();
        }

        public static string GetChannelDefaultWeightBase(Entity channel)
        {
            Field defaultWeightBaseField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultweightbase"));
            if (defaultWeightBaseField == null || defaultWeightBaseField.IsEmpty())
            {
                return "lbs";
            }

            return defaultWeightBaseField.Data.ToString();
        }

        public static List<XElement> GetParentXElements(Entity parentEntity, Configuration configuration)
        {
            List<XElement> elements = new List<XElement>();
            List<string> parents = new List<string>();
            if (parentEntity == null)
            {
                return elements;
            }

            if (parentEntity.EntityType.Id == "Item" && configuration.ItemsToSkus)
            {
                parents = EpiElement.SkuItemIds(parentEntity, configuration);
            }
            else
            {
                parents.Add(parentEntity.Id.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var parent in parents)
            {
                XElement parentElement = new XElement("parent", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(parent, configuration));
                elements.Add(parentElement);
            }

            return elements;
        }

        internal static List<string> GetResourceIds(XElement deletedElement, Configuration configuration)
        {
            List<string> foundResources = new List<string>();
            foreach (
                XElement resourceElement in
                    deletedElement.Descendants().Where(e => e.Name.LocalName.Contains("Resource_")))
            {
                foundResources.Add(configuration.ChannelIdPrefix + resourceElement.Name.LocalName.Split('_')[1]);
            }

            return foundResources;
        }

        public static Dictionary<string, bool> ShouldEntityExistInChannelNodes(int entityId, List<StructureEntity> channelNodes, int channelId)
        {
            Dictionary<string, bool> dictionary = new Dictionary<string, bool>();
            var entities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, entityId);
            foreach (var node in channelNodes)
            {
                bool result = entities.Any(x => x.ParentId == node.EntityId);
                if (result)
                {
                    IntegrationLogger.Write(LogLevel.Debug, $"Entity {entityId} exists in channel node {node.EntityId}");
                }

                if (!dictionary.ContainsKey(node.EntityId.ToString()))
                {
                    dictionary.Add(node.EntityId.ToString(), result);
                }
            }

            return dictionary;
        }


        /*
         * Get Inventory data from the entity (Product, Item).  
         *      If it's not set in the entity use the values from the Channel.
         *          if it's not set in the Channel use the default values from the Configuration.
         */
        public static bool GetEntityAllowBackorder(Entity entity, Configuration configuration)
        {
            Field allowBackorderField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("allowbackorder"));
            if (allowBackorderField == null || allowBackorderField.IsEmpty())
            {
                return configuration.ChannelAllowBackorder;
            }

            return (bool)allowBackorderField.Data;
        }

        public static bool GetEntityAllowPreorder(Entity entity, Configuration configuration)
        {
            Field allowPreorderField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("allowpreorder"));
            if (allowPreorderField == null || allowPreorderField.IsEmpty())
            {
                return configuration.ChannelAllowPreorder;
            }

            return (bool)allowPreorderField.Data;
        }

        public static DateTime GetEntityBackorderAvailabilityDate(Entity entity, Configuration configuration)
        {
            Field backorderAvailabilityDateField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("backorderavailabilitydate"));
            if (backorderAvailabilityDateField == null || backorderAvailabilityDateField.IsEmpty())
            {
                return configuration.ChannelBackorderAvailabilityDate;
            }

            return (DateTime)backorderAvailabilityDateField.Data;
        }

        public static int GetEntityBackorderQuantity(Entity entity, Configuration configuration)
        {
            Field backorderQuantityField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("backorderquantity"));
            if (backorderQuantityField == null || backorderQuantityField.IsEmpty())
            {
                return configuration.ChannelBackorderQuantity;
            }

            return (int)backorderQuantityField.Data;
        }

        public static int GetEntityInStockQuantity(Entity entity, Configuration configuration)
        {
            Field instockQuantityField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("instockquantity"));
            if (instockQuantityField == null || instockQuantityField.IsEmpty())
            {
                return configuration.ChannelInStockQuantity;
            }

            return (int)instockQuantityField.Data;
        }

        public static int GetEntityInventoryStatus(Entity entity, Configuration configuration)
        {
            Field statusField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("inventorystatus"));
            if (statusField == null || statusField.IsEmpty())
            {
                return configuration.ChannelInventoryStatus;
            }

            return (int)statusField.Data;
        }

        public static DateTime GetEntityPreorderAvailabilityDate(Entity entity, Configuration configuration)
        {
            Field preorderAvailabilityDateField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("backpreorderavailabilitydate"));
            if (preorderAvailabilityDateField == null || preorderAvailabilityDateField.IsEmpty())
            {
                return configuration.ChannelPreorderAvailabilityDate;
            }

            return (DateTime)preorderAvailabilityDateField.Data;
        }

        public static int GetEntityPreorderQuantity(Entity entity, Configuration configuration)
        {
            Field quantityField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("preorderquantity"));
            if (quantityField == null || quantityField.IsEmpty())
            {
                return configuration.ChannelPreorderQuantity;
            }

            return (int)quantityField.Data;
        }

        public static int GetEntityReorderMinQuantity(Entity entity, Configuration configuration)
        {
            Field reorderMinQuantityField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("reorderminquantity"));
            if (reorderMinQuantityField == null || reorderMinQuantityField.IsEmpty())
            {
                return configuration.ChannelReorderMinQuantity;
            }

            return (int)reorderMinQuantityField.Data;
        }

        public static int GetEntityReservedQuantity(Entity entity, Configuration configuration)
        {
            Field reservedQuantityField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("reservedquantity"));
            if (reservedQuantityField == null || reservedQuantityField.IsEmpty())
            {
                return configuration.ChannelReservedQuantity;
            }

            return (int)reservedQuantityField.Data;
        }

        #region Pricing

        /*
         * Get Pricing data from the entity (Product, Item).  
         *      If it's not set in the entity use the values from the Channel.
         *          if it's not set in the Channel use the default values from the Configuration.
         */
        public static string GetEntityMarketId(Entity entity, Configuration configuration)
        {
            Field marketIdField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("marketid"));
            if (marketIdField == null || marketIdField.IsEmpty())
            {
                return configuration.ChannelMarketId;
            }

            return marketIdField.Data.ToString();
        }

        public static string GetEntityCurrencyCode(Entity entity, Configuration configuration)
        {
            Field currencyCodeField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("currencycode"));
            if (currencyCodeField == null || currencyCodeField.IsEmpty())
            {
                return configuration.ChannelCurrencyCode;
            }

            return currencyCodeField.Data.ToString();
        }

        public static int GetEntityPriceTypeId(Entity entity, Configuration configuration)
        {
            Field priceTypeIdField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("pricetypeid"));
            if (priceTypeIdField == null || priceTypeIdField.IsEmpty())
            {
                return configuration.ChannelPriceTypeId;
            }

            return (int)priceTypeIdField.Data;
        }

        public static string GetEntityPriceCode(Entity entity, Configuration configuration)
        {
            Field priceCodeField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("pricecode"));
            if (priceCodeField == null || priceCodeField.IsEmpty())
            {
                return configuration.ChannelPriceCode;
            }

            return priceCodeField.Data.ToString();
        }

        public static DateTime GetEntityValidFrom(Entity entity, Configuration configuration)
        {
            Field validFromField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("validfrom"));
            if (validFromField == null || validFromField.IsEmpty())
            {
                return configuration.ChannelValidFrom;
            }

            return (DateTime)validFromField.Data;
        }

        public static DateTime GetEntityValidUntil(Entity entity, Configuration configuration)
        {
            Field validUntilField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("validuntil"));
            if (validUntilField == null || validUntilField.IsEmpty())
            {
                return configuration.ChannelValidUntil;
            }

            return (DateTime)validUntilField.Data;
        }

        public static double GetEntityMinQuantity(Entity entity, Configuration configuration)
        {
            Field minQuantityField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("minquantity"));
            if (minQuantityField == null || minQuantityField.IsEmpty())
            {
                return configuration.ChannelMinQuantity;
            }

            return (double)minQuantityField.Data;
        }

        public static double GetEntityUnitPrice(Entity entity, Configuration configuration)
        {
            Field unitPriceField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("unitprice"));
            if (unitPriceField == null || unitPriceField.IsEmpty())
            {
                return configuration.ChannelUnitPrice;
            }

            return (double)unitPriceField.Data;
        }

        #endregion

        public static void BuildEntityIdAndTypeDict(Configuration config)
        {
            Dictionary<int, string> entityIdAndType = new Dictionary<int, string>();

            foreach (StructureEntity structureEntity in config.ChannelStructureEntities)
            {
                if (!entityIdAndType.ContainsKey(structureEntity.EntityId))
                {
                    entityIdAndType.Add(structureEntity.EntityId, structureEntity.Type);
                }
            }

            config.EntityIdAndType = entityIdAndType;
        }

        public static void EpiCodeFieldUpdatedAddAssociationAndRelationsToDocument(XDocument doc, Entity updatedEntity, Configuration config, int channelId)
        {
            List<Link> links = new List<Link>();

            if (updatedEntity.EntityType.IsLinkEntityType)
            {
                links = RemoteManager.DataService.GetLinksForLinkEntity(updatedEntity.Id);
            }
            else
            {
                links = RemoteManager.DataService.GetLinksForEntity(updatedEntity.Id);
            }

            List<XElement> associationsElements = new List<XElement>();

            Dictionary<string, XElement> relationsElements = new Dictionary<string, XElement>();

            foreach (Link link in links)
            {
                var structureEntityList = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel
                            (channelId, link.Target.Id, link.Source.Id);

                if (!EpiMappingHelper.IsRelation(
                link.LinkType.SourceEntityTypeId,
                link.LinkType.TargetEntityTypeId,
                link.LinkType.Index,
                config))
                {
                    foreach (StructureEntity structureEntity in structureEntityList)
                    {
                        if (!structureEntity.LinkEntityId.HasValue)
                        {
                            associationsElements.Add(EpiElement.CreateCatalogAssociationElement(
                               structureEntity,
                               null,
                               config));
                        }
                        else
                        {
                            associationsElements.Add(EpiElement.CreateCatalogAssociationElement(
                               structureEntity,
                               link.LinkEntity,
                               config));
                        }
                    }
                }
                else
                {
                    foreach (StructureEntity structureEntity in structureEntityList)
                    {
                        int parentNodeId = ChannelHelper.GetParentChannelNode(structureEntity, channelId);

                        if (parentNodeId == 0)
                        {
                            continue;
                        }

                        string channelPrefixAndSkuId = ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(structureEntity.EntityId, config);
                        string channelPrefixAndParentNodeId = ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(parentNodeId, config);

                        if (!relationsElements.ContainsKey(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId))
                        {
                            relationsElements.Add(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId,
                                EpiElement.CreateNodeEntryRelationElement(
                                    parentNodeId.ToString(CultureInfo.InvariantCulture),
                                    structureEntity.EntityId.ToString(),
                                    structureEntity.SortOrder,
                                    config));
                        }

                        string channelPrefixAndParent = ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(structureEntity.ParentId, config);

                        if (!relationsElements.ContainsKey(channelPrefixAndSkuId + "_" + channelPrefixAndParent))
                        {
                            relationsElements.Add(channelPrefixAndSkuId + "_" + channelPrefixAndParent,
                                EpiElement.CreateEntryRelationElement(
                                structureEntity.ParentId.ToString(CultureInfo.InvariantCulture),
                                link.LinkType.SourceEntityTypeId,
                               structureEntity.EntityId.ToString(),
                                structureEntity.SortOrder, config));
                        }
                    }
                }
            }

            if (relationsElements.Any())
            {
                doc.Descendants("Relations").ElementAt(0).Add(new XAttribute("totalCount", relationsElements.Count), relationsElements.Values);
            }

            if (associationsElements.Any())
            {
                doc.Descendants("Associations").ElementAt(0).Add(new XAttribute("totalCount", associationsElements.Count), associationsElements);
            }

        }
    }
}