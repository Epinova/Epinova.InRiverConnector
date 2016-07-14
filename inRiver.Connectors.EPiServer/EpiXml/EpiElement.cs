namespace inRiver.EPiServerCommerce.CommerceAdapter.EpiXml
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;

    using inRiver.EPiServerCommerce.CommerceAdapter.Helpers;
    using inRiver.Integration.Logging;
    using inRiver.Remoting;
    using inRiver.Remoting.Log;
    using inRiver.Remoting.Objects;

    public class EpiElement
    {
        public static XElement InRiverEntityTypeToMetaClass(string name, string entityTypeName)
        {
            return new XElement(
                "MetaClass",
                new XElement("Namespace", "Mediachase.Commerce.Catalog.User"),
                new XElement("Name", name),
                new XElement("FriendlyName", name),
                new XElement("MetaClassType", "User"),
                new XElement("ParentClass", EpiMappingHelper.GetParentClassForEntityType(entityTypeName)),
                new XElement("TableName", EpiMappingHelper.GetTableNameForEntityType(entityTypeName, name)),
                new XElement("Description", "From inRiver"),
                new XElement("IsSystem", "False"),
                new XElement("IsAbstract", "False"),
                new XElement("FieldListChangedSqlScript"),
                new XElement("Tag"),
                new XElement("Attributes"));
        }

        public static XElement InRiverFieldTypeToMetaField(FieldType fieldType, Configuration config)
        {
            return new XElement(
                "MetaField",
                new XElement("Namespace", "Mediachase.Commerce.Catalog"),
                new XElement("Name", EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, config)),
                new XElement("FriendlyName", EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, config)),
                new XElement("Description", "From inRiver"),
                new XElement("DataType", EpiMappingHelper.InRiverDataTypeToEpiType(fieldType, config)),
                new XElement("Length", EpiMappingHelper.GetMetaFieldLength(fieldType, config)),
                new XElement("AllowNulls", BusinessHelper.GetAllowsNulls(fieldType, config)),
                new XElement("SaveHistory", "False"),
                new XElement("AllowSearch", BusinessHelper.GetAllowsSearch(fieldType)),
                new XElement("MultiLanguageValue", BusinessHelper.FieldTypeIsMultiLanguage(fieldType)),
                new XElement("IsSystem", "False"),
                new XElement("Tag"),
                new XElement(
                    "Attributes",
                    new XElement(
                        "Attribute",
                        new XElement("Key", "useincomparing"),
                        new XElement("Value", BusinessHelper.FieldIsUseInCompare(fieldType)))),
                new XElement("OwnerMetaClass", fieldType.EntityTypeId));
        }

        public static XElement EPiMustHaveMetaField(string name)
        {
            return new XElement(
                "MetaField",
                new XElement("Namespace", "Mediachase.Commerce.Catalog"),
                new XElement("Name", name),
                new XElement("FriendlyName", name),
                new XElement("Description", "From inRiver"),
                new XElement("DataType", "LongString"),
                new XElement("Length", 150),
                new XElement("AllowNulls", "True"),
                new XElement("SaveHistory", "False"),
                new XElement("AllowSearch", "True"),
                new XElement("MultiLanguageValue", "True"),
                new XElement("IsSystem", "False"),
                new XElement("Tag"),
                new XElement(
                    "Attributes",
                    new XElement("Attribute", new XElement("Key", "useincomparing"), new XElement("Value", "True"))));
        }

        public static XElement EPiSpecificationField(string name)
        {
            return new XElement(
                "MetaField",
                new XElement("Namespace", "Mediachase.Commerce.Catalog"),
                new XElement("Name", name),
                new XElement("FriendlyName", name),
                new XElement("Description", "From inRiver"),
                new XElement("DataType", "LongHtmlString"),
                new XElement("Length", 65000),
                new XElement("AllowNulls", "True"),
                new XElement("SaveHistory", "False"),
                new XElement("AllowSearch", "True"),
                new XElement("MultiLanguageValue", "True"),
                new XElement("IsSystem", "False"),
                new XElement("Tag"),
                new XElement(
                    "Attributes",
                    new XElement("Attribute", new XElement("Key", "useincomparing"), new XElement("Value", "False"))));
        }

        public static XElement CreateAssociationTypeElement(LinkType linkType)
        {
            return new XElement(
                "AssociationType",
                new XElement("TypeId", linkType.Id),
                new XElement("Description", linkType.Id));
        }

        public static XElement CreateCatalogElement(Entity channel, Configuration config)
        {
            return new XElement(
                "Catalog",
                new XAttribute("name", EpiMappingHelper.GetNameForEntity(channel, config, 100)),
                new XAttribute("lastmodified", channel.LastModified.ToString(Configuration.DateTimeFormatString)),
                new XAttribute("startDate", BusinessHelper.GetStartDateFromEntity(channel)),
                new XAttribute("endDate", BusinessHelper.GetEndDateFromEntity(channel)),
                new XAttribute("defaultCurrency", config.ChannelDefaultCurrency),
                new XAttribute("weightBase", config.ChannelDefaultWeightBase),
                new XAttribute("defaultLanguage", config.ChannelDefaultLanguage.Name.ToLower()),
                new XAttribute("sortOrder", 0),
                new XAttribute("isActive", "True"),
                new XAttribute(
                    "languages",
                    string.Join(",", BusinessHelper.CultureInfosToStringArray(config.LanguageMapping.Keys.ToArray()))));
        }

        public static XElement CreateNodeElement(Entity entity, string parentId, int sortOrder, Configuration config)
        {
            return new XElement(
                "Node",
                new XElement("Name", EpiMappingHelper.GetNameForEntity(entity, config, 100)),
                new XElement("StartDate", BusinessHelper.GetStartDateFromEntity(entity)),
                new XElement("EndDate", BusinessHelper.GetEndDateFromEntity(entity)),
                new XElement("IsActive", !entity.EntityType.IsLinkEntityType),
                new XElement("SortOrder", sortOrder),
                new XElement("DisplayTemplate", BusinessHelper.GetDisplayTemplateEntity(entity)),
                new XElement("Guid", GetChannelEntityGuid(config.ChannelId, entity.Id)),
                new XElement("Code", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(entity.Id, config)),
                new XElement(
                    "MetaData",
                    new XElement("MetaClass", new XElement("Name", GetMetaClassForEntity(entity))),
                    new XElement(
                        "MetaFields",
                        GetDisplayXXElement(entity.DisplayName, "DisplayName", config),
                        GetDisplayXXElement(entity.DisplayDescription, "DisplayDescription", config),
                        from f in entity.Fields
                        where !f.IsEmpty() && !EpiMappingHelper.SkipField(f.FieldType, config)
                        select InRiverFieldToMetaField(f, config))),
                new XElement(
                    "ParentNode",
                    string.IsNullOrEmpty(parentId) ? null : ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(parentId, config)),
                CreateSEOInfoElement(entity, config));
        }

        public static XElement CreateAssociationNodeElement(string name, Configuration config)
        {
            return new XElement(
                "Node",
                new XElement("Name", name),
                new XElement("StartDate", DateTime.UtcNow.ToString("u")),
                new XElement("EndDate", DateTime.UtcNow.AddYears(100).ToString("u")),
                new XElement("IsActive", "True"),
                new XElement("SortOrder", 999),
                new XElement("DisplayTemplate", string.Empty),
                new XElement("Code", config.ChannelIdPrefix + "_inRiverAssociations"),
                new XElement("Guid", new Guid(config.ChannelId, 0, 0, new byte[8])),
                new XElement(
                    "MetaData",
                    new XElement("MetaClass", new XElement("Name", "ChannelNode")),
                    new XElement("MetaFields")),
                new XElement("ParentNode", null),
                new XElement("SeoInfo"));
        }

        public static XElement CreateSEOInfoElement(Entity entity, Configuration config)
        {
            XElement seoInfo = new XElement("SeoInfo");
            foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in config.LanguageMapping)
            {
                string uri = BusinessHelper.GetSeoUriFromEntity(entity, culturePair.Value, config);
                string title = BusinessHelper.GetSeoTitleFromEntity(entity, culturePair.Value);
                string description = BusinessHelper.GetSeoDescriptionFromEntity(entity, culturePair.Value);
                string keywords = BusinessHelper.GetSeoKeywordsFromEntity(entity, culturePair.Value);
                string urisegment = BusinessHelper.GetSeoUriSegmentFromEntity(entity, culturePair.Value, config);

                if (string.IsNullOrEmpty(uri) && string.IsNullOrEmpty(title) && string.IsNullOrEmpty(description)
                    && string.IsNullOrEmpty(keywords) && string.IsNullOrEmpty(urisegment))
                {
                    continue;
                }

                seoInfo.Add(
                    new XElement(
                        "Seo",
                        new XElement("LanguageCode", culturePair.Key.Name.ToLower()),
                        new XElement("Uri", uri),
                        new XElement("Title", title),
                        new XElement("Description", description),
                        new XElement("Keywords", keywords),
                        new XElement("UriSegment", urisegment)));
            }

            return seoInfo;
        }

        // <Inventory>
        //  <AllowBackorder>True</AllowBackorder>
        //  <AllowPreorder>True</AllowPreorder>
        //  <BackorderAvailabilityDate>2020-01-04 02:00:00Z</BackorderAvailabilityDate>
        //  <BackorderQuantity>6</BackorderQuantity>
        //  <InStockQuantity>10</InStockQuantity>
        //  <InventoryStatus>1</InventoryStatus>
        //  <PreorderAvailabilityDate>2010-09-01 16:00:00Z</PreorderAvailabilityDate>
        //  <PreorderQuantity>4</PreorderQuantity>
        //  <ReorderMinQuantity>3</ReorderMinQuantity>
        //  <ReservedQuantity>2</ReservedQuantity>
        // </Inventory>
        public static XElement CreateInventoryInfoElement(Entity entity, Configuration config)
        {
            if (!config.ExportInventoryData)
            {
                return new XElement("Inventory");
            }

            XElement inventoryInfo = new XElement("Inventory");

            string allowBackorder = ChannelHelper.GetEntityAllowBackorder(entity, config).ToString();
            string allowPreorder = ChannelHelper.GetEntityAllowPreorder(entity, config).ToString();
            string backorderAvailabilityDate = ChannelHelper.GetEntityBackorderAvailabilityDate(entity, config).ToString("u");
            string backorderQuantity = ChannelHelper.GetEntityBackorderQuantity(entity, config).ToString(CultureInfo.InvariantCulture);
            string instockQuantity = ChannelHelper.GetEntityInStockQuantity(entity, config).ToString(CultureInfo.InvariantCulture);
            string inventoryStatus = ChannelHelper.GetEntityInventoryStatus(entity, config).ToString(CultureInfo.InvariantCulture);
            string preorderAvailabilityDate = ChannelHelper.GetEntityPreorderAvailabilityDate(entity, config).ToString("u");
            string preorderQuantity = ChannelHelper.GetEntityPreorderQuantity(entity, config).ToString(CultureInfo.InvariantCulture);
            string reorderMinQuantity = ChannelHelper.GetEntityReorderMinQuantity(entity, config).ToString(CultureInfo.InvariantCulture);
            string reservedQuantity = ChannelHelper.GetEntityReservedQuantity(entity, config).ToString(CultureInfo.InvariantCulture);

            inventoryInfo.Add(
                new XElement("AllowBackorder", allowBackorder),
                new XElement("AllowPreorder", allowPreorder),
                new XElement("BackorderAvailabilityDate", backorderAvailabilityDate),
                new XElement("BackorderQuantity", backorderQuantity),
                new XElement("InStockQuantity", instockQuantity),
                new XElement("InventoryStatus", inventoryStatus),
                new XElement("PreorderAvailabilityDate", preorderAvailabilityDate),
                new XElement("PreorderQuantity", preorderQuantity),
                new XElement("ReorderMinQuantity", reorderMinQuantity),
                new XElement("ReservedQuantity", reservedQuantity));

            return inventoryInfo;
        }

        // <Prices>
        //  <Price>
        //    <MarketId>DEFAULT</MarketId>
        //    <CurrencyCode>USD</CurrencyCode>
        //    <PriceTypeId>0</PriceTypeId>
        //    <PriceCode/>
        //    <ValidFrom>1900-01-01 00:00:00Z</ValidFrom>
        //    <ValidUntil/>
        //    <MinQuantity>0.000000000</MinQuantity>
        //    <UnitPrice>1000.0000</UnitPrice>
        //  </Price>
        // </Prices>
        public static XElement CreatePriceInfoElement(Entity entity, Configuration config)
        {
            if (!config.ExportPricingData)
            {
                return new XElement("Prices");
            }

            XElement priceInfo = new XElement("Prices");

            string marketId = ChannelHelper.GetEntityMarketId(entity, config);
            string currencyCode = ChannelHelper.GetEntityCurrencyCode(entity, config);
            string priceTypeId = ChannelHelper.GetEntityPriceTypeId(entity, config).ToString(CultureInfo.InvariantCulture);
            string priceCode = ChannelHelper.GetEntityPriceCode(entity, config);
            string validFrom = ChannelHelper.GetEntityValidFrom(entity, config).ToString("u");
            string validUntil = ChannelHelper.GetEntityValidUntil(entity, config).ToString("u");
            string minQuantity = ChannelHelper.GetEntityMinQuantity(entity, config).ToString(CultureInfo.InvariantCulture);
            string unitPrice = ChannelHelper.GetEntityUnitPrice(entity, config).ToString(CultureInfo.InvariantCulture);

            priceInfo.Add(
                new XElement(
                    "Price",
                    new XElement("MarketId", marketId),
                    new XElement("CurrencyCode", currencyCode),
                    new XElement("PriceTypeId", priceTypeId),
                    new XElement("PriceCode", priceCode),
                    new XElement("ValidFrom", validFrom),
                    new XElement("ValidUntil", validUntil),
                    new XElement("MinQuantity", minQuantity),
                    new XElement("UnitPrice", unitPrice)));
            return priceInfo;
        }

        public static XElement InRiverEntityToEpiEntry(Entity entity, Configuration config)
        {
            return new XElement(
                "Entry",
                new XElement("Name", EpiMappingHelper.GetNameForEntity(entity, config, 100)),
                new XElement("StartDate", BusinessHelper.GetStartDateFromEntity(entity)),
                new XElement("EndDate", BusinessHelper.GetEndDateFromEntity(entity)),
                new XElement("IsActive", "True"),
                new XElement("DisplayTemplate", BusinessHelper.GetDisplayTemplateEntity(entity)),
                new XElement("Code", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(entity.Id, config)),
                new XElement("EntryType", EpiMappingHelper.GetEntryType(entity.EntityType.Id, config)),
                new XElement("Guid", GetChannelEntityGuid(config.ChannelId, entity.Id)),
                new XElement(
                    "MetaData",
                    new XElement("MetaClass", new XElement("Name", GetMetaClassForEntity(entity))),
                    new XElement(
                        "MetaFields",
                        GetDisplayXXElement(entity.DisplayName, "DisplayName", config),
                        GetDisplayXXElement(entity.DisplayDescription, "DisplayDescription", config),
                        from f in entity.Fields
                        where UseField(entity, f) && !EpiMappingHelper.SkipField(f.FieldType, config)
                        select InRiverFieldToMetaField(f, config))),
                        CreateSEOInfoElement(entity, config),
                        CreateInventoryInfoElement(entity, config),
                        CreatePriceInfoElement(entity, config));
        }

        private static Guid GetChannelEntityGuid(int channelId, int entityId)
        {
            var concatIds = channelId.ToString().PadLeft(16, '0') + entityId.ToString().PadLeft(16, '0');
            return new Guid(concatIds);
        }

        public static XElement InRiverFieldToMetaField(Field field, Configuration config)
        {
            XElement metaField = new XElement(
                "MetaField",
                new XElement("Name", EpiMappingHelper.GetEPiMetaFieldNameFromField(field.FieldType, config)),
                new XElement("Type", EpiMappingHelper.InRiverDataTypeToEpiType(field.FieldType, config)));

            if (field.FieldType.DataType.Equals(DataType.CVL))
            {
                metaField.Add(BusinessHelper.GetCVLValues(field, config));
            }
            else
            {
                if (field.FieldType.DataType.Equals(DataType.LocaleString))
                {
                    LocaleString ls = field.Data as LocaleString;
                    if (!field.IsEmpty())
                    {
                        foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in config.LanguageMapping)
                        {
                            if (ls != null)
                            {
                                metaField.Add(
                                    new XElement(
                                        "Data",
                                        new XAttribute("language", culturePair.Key.Name.ToLower()),
                                        new XAttribute("value", ls[culturePair.Value])));
                            }
                        }
                    }
                    else
                    {
                        foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in config.LanguageMapping)
                        {
                            metaField.Add(
                                new XElement(
                                    "Data",
                                    new XAttribute("language", culturePair.Key.Name.ToLower()),
                                    new XAttribute("value", string.Empty)));
                        }
                    }
                }
                else
                {
                    metaField.Add(
                        new XElement(
                            "Data",
                            new XAttribute("language", config.ChannelDefaultLanguage.Name.ToLower()),
                            new XAttribute("value", BusinessHelper.GetFieldDataAsString(field, config))));
                }
            }

            if (field.FieldType.Settings.ContainsKey("EPiDataType"))
            {
                if (field.FieldType.Settings["EPiDataType"] == "ShortString")
                {
                    foreach (XElement dataElement in metaField.Descendants().Where(e => e.Attribute("value") != null))
                    {
                        int lenght = dataElement.Attribute("value").Value.Length;

                        int defaultLength = 150;
                        if (field.FieldType.Settings.ContainsKey("MetaFieldLength"))
                        {
                            if (!int.TryParse(field.FieldType.Settings["MetaFieldLength"], out defaultLength))
                            {
                                defaultLength = 150;
                            }
                        }

                        if (lenght > defaultLength)
                        {
                            IntegrationLogger.Write(
                                LogLevel.Error,
                                string.Format("Field {0} for entity {1} has a longer value [{2}] than defined by MetaFieldLength [{3}]", field.FieldType.Id, field.EntityId, lenght, defaultLength));
                        }
                    }
                }
            }

            return metaField;
        }

        public static XElement CreateSimpleMetaFieldElement(string name, string value, Configuration config)
        {
            return new XElement(
                "MetaField",
                new XElement("Name", name),
                new XElement("Type", "ShortString"),
                new XElement(
                    "Data",
                    new XAttribute("language", config.ChannelDefaultLanguage.Name.ToLower()),
                    new XAttribute("value", value)));
        }

        [Obsolete]
        public static XElement CreateNodeEntryRelationElement(Link link, Configuration config)
        {
            return CreateNodeEntryRelationElement(link.Source.Id.ToString(CultureInfo.InvariantCulture), link.Target.Id.ToString(CultureInfo.InvariantCulture), link.Index, config);
        }

        public static XElement CreateNodeEntryRelationElement(string sourceId, string targetId, int sortOrder, Configuration config, Dictionary<int, Entity> channelEntities = null)
        {
            return new XElement(
                "NodeEntryRelation",
                new XElement("EntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(targetId, config)),
                new XElement("NodeCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(sourceId, config)),
                new XElement("SortOrder", sortOrder));
        }

        public static XElement CreateNodeRelationElement(string sourceId, string targetId, int sortOrder, Configuration config)
        {
            return new XElement(
                "NodeRelation",
                new XElement("ChildNodeCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(targetId, config)),
                new XElement("ParentNodeCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(sourceId, config)),
                new XElement("SortOrder", sortOrder));
        }

        [Obsolete]
        public static XElement CreateEntryRelationElement(Link link, Configuration config)
        {
            return CreateEntryRelationElement(link.Source.Id.ToString(CultureInfo.InvariantCulture), link.Source.EntityType.Id, link.Target.Id.ToString(CultureInfo.InvariantCulture), link.Index, config);
        }

        public static XElement CreateEntryRelationElement(string sourceId, string parentEntityType, string targetId, int sortOrder, Configuration config, Dictionary<int, Entity> channelEntities = null)
        {
            string relationType = "ProductVariation";

            if (!string.IsNullOrEmpty(parentEntityType))
            {
                string sourceType = EpiMappingHelper.GetEntryType(parentEntityType, config);

                // Change it if needed.
                switch (sourceType)
                {
                    case "Package":
                    case "DynamicPackage":
                        relationType = "PackageEntry";
                        break;
                    case "Bundle":
                        relationType = "BundleEntry";
                        break;
                }
            }

            return new XElement(
                "EntryRelation",
                new XElement("ParentEntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(sourceId, config)),
                new XElement("ChildEntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(targetId, config)),
                new XElement("RelationType", relationType),
                new XElement("Quantity", 0),
                new XElement("GroupName", "default"),
                new XElement("SortOrder", sortOrder));
        }

        [Obsolete]
        public static XElement CreateCatalogAssociationElement(Link link, Configuration config)
        {
            // Unique Name with no spaces required for EPiServer Commerce
            string name = EpiMappingHelper.GetAssociationName(link, config);
            string description = link.LinkEntity == null ? link.LinkType.Id : ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(link.LinkEntity.Id, config);
            description = description ?? string.Empty;

            return new XElement(
                "CatalogAssociation",
                new XElement("Name", name),
                new XElement("Description", description),
                new XElement("SortOrder", link.Index),
                new XElement("EntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(link.Source.Id, config)),
                CreateAssociationElement(link, config));
        }

        public static XElement CreateCatalogAssociationElement(StructureEntity structureEntity, Entity linkEntity, Configuration config, Dictionary<int, Entity> channelEntities = null)
        {
            // Unique Name with no spaces required for EPiServer Commerce
            string name = EpiMappingHelper.GetAssociationName(structureEntity, linkEntity, config);
            string description = structureEntity.LinkEntityId == null ? structureEntity.LinkTypeIdFromParent : ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(structureEntity.LinkEntityId.Value, config);
            description = description ?? string.Empty;

            return new XElement(
                "CatalogAssociation",
                new XElement("Name", name),
                new XElement("Description", description),
                new XElement("SortOrder", structureEntity.SortOrder),
                new XElement("EntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(structureEntity.ParentId, config)),
                CreateAssociationElement(structureEntity, config));
        }

        [Obsolete]
        public static XElement CreateAssociationElement(Link link, Configuration config)
        {
            return new XElement(
                "Association",
                new XElement("EntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(link.Target.Id, config)),
                new XElement("SortOrder", link.Index),
                    new XElement("Type", link.LinkType.Id));
        }

        public static XElement CreateAssociationElement(StructureEntity structureEntity, Configuration config)
        {
            return new XElement(
                "Association",
                new XElement("EntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(structureEntity.EntityId, config)),
                new XElement("SortOrder", structureEntity.SortOrder),
                    new XElement("Type", structureEntity.LinkTypeIdFromParent));
        }

        public static XElement GetLinkItemFields(Entity linkEntity, Configuration config)
        {
            return new XElement(
                "LinkItemMetaFields",
                from Field f in linkEntity.Fields where !EpiMappingHelper.SkipField(f.FieldType, config) && !f.IsEmpty() select InRiverFieldToMetaField(f, config));
        }

        public static XElement CreateResourceElement(Entity resource, string action, Configuration config, Dictionary<int, Entity> parentEntities = null)
        {
            string resourceFileId = "-1";
            Field resourceFileIdField = resource.GetField("ResourceFileId");
            if (resourceFileIdField != null && !resourceFileIdField.IsEmpty())
            {
                resourceFileId = resource.GetField("ResourceFileId").Data.ToString();
            }

            Dictionary<string, int?> parents = new Dictionary<string, int?>();

            string resourceId = ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(resource.Id, config);
            resourceId = resourceId.Replace("_", string.Empty);

            if (action == "unlinked")
            {
                var resourceParents = config.ChannelEntities.Where(i => !i.Key.Equals(resource.Id));

                foreach (KeyValuePair<int, Entity> resourceParent in resourceParents)
                {
                    List<string> ids = new List<string> { resourceParent.Value.Id.ToString(CultureInfo.InvariantCulture) };

                    if (config.ItemsToSkus && resourceParent.Value.EntityType.Id == "Item")
                    {
                        List<string> skuIds = SkuItemIds(resourceParent.Value, config);

                        foreach (string skuId in skuIds)
                        {
                            ids.Add(skuId);
                        }

                        if (config.UseThreeLevelsInCommerce == false)
                        {
                            ids.Remove(resourceParent.Value.Id.ToString(CultureInfo.InvariantCulture));
                        }
                    }

                    foreach (string id in ids)
                    {
                        if (!parents.ContainsKey(id))
                        {
                            parents.Add(id, resourceParent.Value.MainPictureId);
                        }
                    }
                }
            }
            else
            {
                List<StructureEntity> allResourceLocations = config.ChannelStructureEntities.FindAll(i => i.EntityId.Equals(resource.Id));

                List<Link> links = new List<Link>();

                foreach (Link inboundLink in resource.InboundLinks)
                {
                    if (allResourceLocations.Exists(i => i.ParentId.Equals(inboundLink.Source.Id)))
                    {
                        links.Add(inboundLink);
                    }
                }

                foreach (Link link in links)
                {
                    Entity linkedEntity = link.Source;
                    List<string> ids = new List<string> { linkedEntity.Id.ToString(CultureInfo.InvariantCulture) };
                    if (config.ItemsToSkus && linkedEntity.EntityType.Id == "Item")
                    {
                        List<string> skuIds = SkuItemIds(linkedEntity, config);
                        foreach (string skuId in skuIds)
                        {
                            ids.Add(skuId);
                        }

                        if (config.UseThreeLevelsInCommerce == false)
                        {
                            ids.Remove(linkedEntity.Id.ToString(CultureInfo.InvariantCulture));
                        }
                    }

                    foreach (string id in ids)
                    {
                        if (!parents.ContainsKey(id))
                        {
                            parents.Add(id, linkedEntity.MainPictureId);
                        }
                    }
                }

                if (parents.Any() && parentEntities != null)
                {
                    List<int> nonExistingIds =
                        (from id in parents.Keys where !parentEntities.ContainsKey(int.Parse(id)) select int.Parse(id))
                            .ToList();

                    if (nonExistingIds.Any())
                    {
                        foreach (Entity entity in RemoteManager.DataService.GetEntities(nonExistingIds, LoadLevel.DataOnly))
                        {
                            if (!parentEntities.ContainsKey(entity.Id))
                            {
                                parentEntities.Add(entity.Id, entity);
                            }
                        }
                    }
                }
            }

            return new XElement(
                "Resource",
                new XAttribute("id", resourceId),
                new XAttribute("action", action),
                new XElement(
                    "ResourceFields",
                    resource.Fields.Where(field => !EpiMappingHelper.SkipField(field.FieldType, config))
                        .Select(field => InRiverFieldToMetaField(field, config))),
                Resources.GetInternalPathsInZip(resource, config),
                new XElement(
                    "ParentEntries",
                    parents.Select(
                        parent =>
                        new XElement("EntryCode", ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(parent.Key, config), new XAttribute("IsMainPicture", parent.Value != null && parent.Value.ToString().Equals(resourceFileId))))));
        }

        public static XElement CreateResourceMetaFieldsElement(EntityType resourceType, Configuration config)
        {
            return new XElement(
                "ResourceMetaFields",
                resourceType.FieldTypes.Select(
                    fieldtype =>
                    new XElement(
                        "ResourceMetaField",
                        new XElement("FieldName", EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldtype, config)),
                        new XElement("FriendlyName", EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldtype, config)),
                        new XElement("Description", EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldtype, config)),
                        new XElement("FieldType", EpiMappingHelper.InRiverDataTypeToEpiType(fieldtype, config)),
                        new XElement("Format", "Text"),
                        new XElement("MaximumLength", EpiMappingHelper.GetMetaFieldLength(fieldtype, config)),
                        new XElement("AllowNulls", BusinessHelper.GetAllowsNulls(fieldtype, config)),
                        new XElement("UniqueValue", fieldtype.Unique))));
        }

        public static XElement GetMetaClassesFromFieldSets(Configuration config)
        {
            List<XElement> metaClasses = new List<XElement>();
            List<XElement> metafields = new List<XElement>();

            XElement diaplyNameElement = EPiMustHaveMetaField("DisplayName");
            XElement displayDescriptionElement = EPiMustHaveMetaField("DisplayDescription");
            XElement specification = EPiSpecificationField("SpecificationField");
            bool addSpec = false;

            foreach (EntityType entityType in Configuration.ExportEnabledEntityTypes)
            {
                if (entityType.LinkTypes.Find(a => a.TargetEntityTypeId == "Specification") != null && entityType.Id != "Specification")
                {
                    specification.Add(new XElement("OwnerMetaClass", entityType.Id));
                    foreach (FieldSet fieldSet in entityType.FieldSets)
                    {
                        string name = entityType.Id + "_" + fieldSet.Id;
                        specification.Add(new XElement("OwnerMetaClass", name));
                    }

                    addSpec = true;
                }

                Dictionary<string, List<XElement>> fieldTypesFieldSets = new Dictionary<string, List<XElement>>();
                metaClasses.Add(InRiverEntityTypeToMetaClass(entityType.Id, entityType.Id));
                foreach (FieldSet fieldset in entityType.FieldSets)
                {
                    string name = entityType.Id + "_" + fieldset.Id;
                    metaClasses.Add(InRiverEntityTypeToMetaClass(name, entityType.Id));
                    foreach (string fieldTypeName in fieldset.FieldTypes)
                    {
                        if (!fieldTypesFieldSets.ContainsKey(fieldTypeName))
                        {
                            fieldTypesFieldSets.Add(fieldTypeName, new List<XElement> { new XElement("OwnerMetaClass", name) });
                        }
                        else
                        {
                            fieldTypesFieldSets[fieldTypeName].Add(new XElement("OwnerMetaClass", name));
                        }
                    }

                    diaplyNameElement.Add(new XElement("OwnerMetaClass", name));
                    displayDescriptionElement.Add(new XElement("OwnerMetaClass", name));
                }

                diaplyNameElement.Add(new XElement("OwnerMetaClass", entityType.Id));
                displayDescriptionElement.Add(new XElement("OwnerMetaClass", entityType.Id));
                foreach (FieldType fieldType in entityType.FieldTypes)
                {
                    if (EpiMappingHelper.SkipField(fieldType, config))
                    {
                        continue;
                    }

                    XElement metaField = InRiverFieldTypeToMetaField(fieldType, config);

                    if (fieldTypesFieldSets.ContainsKey(fieldType.Id))
                    {
                        foreach (XElement element in fieldTypesFieldSets[fieldType.Id])
                        {
                            metaField.Add(element);
                        }
                    }
                    else
                    {
                        foreach (FieldSet fieldSet in entityType.FieldSets)
                        {
                            string name = entityType.Id + "_" + fieldSet.Id;
                            metaField.Add(new XElement("OwnerMetaClass", name));
                        }
                    }

                    if (fieldType.DataType.Equals(DataType.CVL))
                    {
                        metaField.Add(EpiMappingHelper.GetDictionaryValues(fieldType, config));
                    }

                    if (metafields.Any(mf =>
                    {
                        XElement nameElement = mf.Element("Name");
                        return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, config));
                    }))
                    {
                        XElement existingMetaField = metafields.FirstOrDefault(mf =>
                        {
                            XElement nameElement = mf.Element("Name");
                            return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, config));
                        });
                        if (existingMetaField != null)
                        {
                            var movefields = metaField.Elements("OwnerMetaClass");
                            existingMetaField.Add(movefields);
                        }
                    }
                    else
                    {
                        metafields.Add(metaField);
                    }
                }
            }

            metafields.Add(diaplyNameElement);
            metafields.Add(displayDescriptionElement);
            if (addSpec)
            {
                metafields.Add(specification);
            }

            return new XElement("MetaDataPlusBackup", new XAttribute("version", "1.0"), metaClasses.ToArray(), metafields.ToArray());
        }

        public static List<XElement> GenerateSkuItemElemetsFromItem(Entity item, Configuration configuration)
        {
            XDocument skuDoc = SkuFieldToDocument(item, configuration);
            if (skuDoc.Root == null || skuDoc.Element("SKUs") == null)
            {
                return new List<XElement>();
            }

            Link specLink = item.OutboundLinks.Find(l => l.Target.EntityType.Id == "Specification");
            XElement specificationMetaField = null;
            if (specLink != null)
            {
                specificationMetaField = new XElement(
                    "MetaField",
                    new XElement("Name", "SpecificationField"),
                    new XElement("Type", "LongHtmlString"));
                foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in configuration.LanguageMapping)
                {
                    string htmlData = RemoteManager.DataService.GetSpecificationAsHtml(
                        specLink.Target.Id,
                        item.Id,
                        culturePair.Value);
                    specificationMetaField.Add(
                        new XElement(
                            "Data",
                            new XAttribute("language", culturePair.Key.Name.ToLower()),
                            new XAttribute("value", htmlData)));
                }
            }

            List<XElement> skuElements = new List<XElement>();
            XElement skuElement = skuDoc.Element("SKUs");
            if (skuElement != null)
            {
                foreach (XElement sku in skuElement.Elements())
                {
                    string id = sku.Attribute("id").Value;
                    if (string.IsNullOrEmpty(id))
                    {
                        IntegrationLogger.Write(
                            LogLevel.Information,
                            string.Format("Could not find the id for the SKU data for item: {0}", item.Id));
                        continue;
                    }

                    XElement itemElement = InRiverEntityToEpiEntry(item, configuration);
                    XElement nameElement = sku.Element("Name");
                    if (nameElement != null)
                    {
                        string name = (!string.IsNullOrEmpty(nameElement.Value)) ? nameElement.Value : id;
                        XElement itemElementName = itemElement.Element("Name");
                        if (itemElementName != null)
                        {
                            itemElementName.Value = name;
                        }
                    }

                    XElement codeElement = itemElement.Element("Code");
                    if (codeElement != null)
                    {
                        codeElement.Value = ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(id, configuration);
                    }

                    XElement entryTypeElement = itemElement.Element("EntryType");
                    if (entryTypeElement != null)
                    {
                        entryTypeElement.Value = "Variation";
                    }

                    XElement skuDataElement = sku.Element(Configuration.SKUData);
                    if (skuDataElement != null)
                    {
                        foreach (XElement skuData in skuDataElement.Elements())
                        {
                            XElement metaDataElement = itemElement.Element("MetaData");
                            if (metaDataElement != null && metaDataElement.Element("MetaFields") != null)
                            {
                                // ReSharper disable once PossibleNullReferenceException
                                metaDataElement.Element("MetaFields").Add(CreateSimpleMetaFieldElement(skuData.Name.LocalName, skuData.Value, configuration));
                            }
                        }
                    }

                    if (specificationMetaField != null)
                    {
                        XElement metaDataElement = itemElement.Element("MetaData");
                        if (metaDataElement != null && metaDataElement.Element("MetaFields") != null)
                        {
                            // ReSharper disable once PossibleNullReferenceException
                            metaDataElement.Element("MetaFields").Add(specificationMetaField);
                        }
                    }

                    skuElements.Add(itemElement);
                }
            }

            return skuElements;
        }

        public static XDocument SkuFieldToDocument(Entity item, Configuration configuration)
        {
            Field skuField = item.GetField(Configuration.SKUFieldName);
            if (skuField == null || skuField.Data == null)
            {
                XElement itemElement = InRiverEntityToEpiEntry(item, configuration);
                IntegrationLogger.Write(
                    LogLevel.Information,
                    string.Format("Could not find SKU data for item: {0}", item.Id));
                return new XDocument(itemElement);
            }

            return XDocument.Parse(skuField.Data.ToString());
        }

        public static List<string> SkuItemIds(Entity item, Configuration configuration)
        {
            Field skuField = item.GetField(Configuration.SKUFieldName);
            if (skuField == null || skuField.IsEmpty())
            {
                return new List<string> { item.Id.ToString(CultureInfo.InvariantCulture) };
            }

            XDocument skuDoc = SkuFieldToDocument(item, configuration);

            XElement skusElement = skuDoc.Element("SKUs");
            if (skusElement != null)
            {
                return
                    (from skuElement in skusElement.Elements()
                     where skuElement.HasAttributes
                     select skuElement.Attribute("id").Value).ToList();
            }

            return new List<string>();
        }

        // ReSharper disable once InconsistentNaming
        private static XElement GetDisplayXXElement(Field displayField, string name, Configuration config)
        {
            if (displayField == null || displayField.IsEmpty())
            {
                return new XElement(
                    "MetaField",
                    new XElement("Name", name),
                    new XElement("Type", "LongHtmlString"),
                    new XElement(
                        "Data",
                        new XAttribute("language", config.ChannelDefaultLanguage.Name.ToLower()),
                        new XAttribute("value", string.Empty)));
            }

            XElement element = InRiverFieldToMetaField(displayField, config);
            XElement nameElement = element.Element("Name");
            if (nameElement != null)
            {
                nameElement.Value = name;
            }

            XElement typeElement = element.Element("Type");
            if (typeElement != null)
            {
                typeElement.Value = "LongHtmlString";
            }

            return element;
        }

        private static bool UseField(Entity entity, Field field)
        {
            if (!field.FieldType.ExcludeFromDefaultView)
            {
                return true;
            }

            List<FieldSet> otherFieldSets = entity.EntityType.FieldSets.Where(fs => !fs.Id.Equals(entity.FieldSetId)).ToList();
            if (otherFieldSets.Count == 0)
            {
                return true;
            }

            FieldSet fieldSet = entity.EntityType.FieldSets.Find(fs => fs.Id.Equals(entity.FieldSetId));
            if (fieldSet != null)
            {
                if (fieldSet.FieldTypes.Contains(field.FieldType.Id))
                {
                    return true;
                }
            }

            foreach (FieldSet fs in otherFieldSets)
            {
                if (fs.FieldTypes.Contains(field.FieldType.Id))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetMetaClassForEntity(Entity entity)
        {
            if (!string.IsNullOrEmpty(entity.FieldSetId) && entity.EntityType.FieldSets.Any(fs => fs.Id == entity.FieldSetId))
            {
                return entity.EntityType.Id + "_" + entity.FieldSetId;
            }

            return entity.EntityType.Id;
        }
    }
}
