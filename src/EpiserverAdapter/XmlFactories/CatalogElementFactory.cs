using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.XmlFactories
{
    public class CatalogElementFactory
    {
        private readonly IConfiguration _config;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly PimFieldAdapter _pimFieldAdapter;

        public CatalogElementFactory(IConfiguration config, EpiMappingHelper mappingHelper, CatalogCodeGenerator catalogCodeGenerator, PimFieldAdapter pimFieldAdapter)
        {
            _config = config;
            _mappingHelper = mappingHelper;
            _catalogCodeGenerator = catalogCodeGenerator;
            _pimFieldAdapter = pimFieldAdapter;
        }

        public XElement InRiverEntityTypeToMetaClass(string name, string entityTypeName)
        {
            return new XElement(
                "MetaClass",
                new XElement("Namespace", "Mediachase.Commerce.Catalog.User"),
                new XElement("Name", name),
                new XElement("FriendlyName", name),
                new XElement("MetaClassType", "User"),
                new XElement("ParentClass", _mappingHelper.GetParentClassForEntityType(entityTypeName)),
                new XElement("TableName", _mappingHelper.GetTableNameForEntityType(entityTypeName, name)),
                new XElement("Description", "From inRiver"),
                new XElement("IsSystem", "False"),
                new XElement("IsAbstract", "False"),
                new XElement("FieldListChangedSqlScript"),
                new XElement("Tag"),
                new XElement("Attributes"));
        }

        public XElement InRiverFieldTypeToMetaField(FieldType fieldType)
        {
            return new XElement(
                "MetaField",
                new XElement("Namespace", "Mediachase.Commerce.Catalog"),
                new XElement("Name", _mappingHelper.GetEpiserverFieldName(fieldType)),
                new XElement("FriendlyName", _mappingHelper.GetEpiserverFieldName(fieldType)),
                new XElement("Description", "From inRiver"),
                new XElement("DataType", _mappingHelper.GetEpiserverDataType(fieldType)),
                new XElement("Length", _mappingHelper.GetMetaFieldLength(fieldType)),
                new XElement("AllowNulls", !fieldType.Mandatory),
                new XElement("SaveHistory", "False"),
                new XElement("AllowSearch", _pimFieldAdapter.GetAllowSearch(fieldType)),
                new XElement("MultiLanguageValue", _pimFieldAdapter.FieldTypeIsMultiLanguage(fieldType)),
                new XElement("IsSystem", "False"),
                new XElement("Tag"),
                new XElement("Attributes",
                    new XElement("Attribute",
                        new XElement("Key", "useincomparing"),
                        new XElement("Value", _pimFieldAdapter.FieldIsUseInCompare(fieldType)))),
                new XElement("OwnerMetaClass", fieldType.EntityTypeId));
        }

        public XElement CreateEpiserverLongStringField(string name)
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
                new XElement("Attributes",
                    new XElement("Attribute", new XElement("Key", "useincomparing"), new XElement("Value", "True"))));
        }

        public XElement CreateEpiserverLongHtmlField(string name)
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

        public XElement CreateAssociationTypeElement(LinkType linkType)
        {
            return new XElement(
                "AssociationType",
                new XElement("TypeId", linkType.Id),
                new XElement("Description", linkType.Id));
        }

        public XElement CreateCatalogElement(Entity channel)
        {
            return new XElement("Catalog",
                new XAttribute("name", _mappingHelper.GetNameForEntity(channel, 100)),
                new XAttribute("lastmodified", channel.LastModified.ToString("O")),
                new XAttribute("startDate", _pimFieldAdapter.GetStartDate(channel)),
                new XAttribute("endDate", _pimFieldAdapter.GetEndDate(channel)),
                new XAttribute("defaultCurrency", _config.ChannelDefaultCurrency),
                new XAttribute("weightBase", _config.ChannelDefaultWeightBase),
                new XAttribute("defaultLanguage", _config.ChannelDefaultLanguage.Name.ToLower()),
                new XAttribute("sortOrder", 0),
                new XAttribute("isActive", "True"),
                new XAttribute("languages", string.Join(",", _pimFieldAdapter.CultureInfosToStringArray(_config.LanguageMapping.Keys.ToArray()))));
        }

        public XElement CreateNodeElement(Entity entity, int parentId, int sortOrder)
        {
            return new XElement("Node",
                new XElement("Name", _mappingHelper.GetNameForEntity(entity, 100)),
                new XElement("StartDate", _pimFieldAdapter.GetStartDate(entity)),
                new XElement("EndDate", _pimFieldAdapter.GetEndDate(entity)),
                new XElement("IsActive", true.ToString()),
                new XElement("SortOrder", sortOrder),
                new XElement("DisplayTemplate", string.Empty),
                new XElement("Guid", GetChannelEntityGuid(_config.ChannelId, entity.Id)),
                new XElement("Code", _catalogCodeGenerator.GetEpiserverCode(entity)),
                new XElement("MetaData",
                    new XElement("MetaClass", new XElement("Name", GetMetaClassForEntity(entity))),
                    new XElement("MetaFields",
                        GetDisplayFieldElement(entity.DisplayName, "DisplayName"),
                        GetDisplayFieldElement(entity.DisplayDescription, "DisplayDescription"),
                        from f in entity.Fields
                        where !f.IsEmpty() && !_mappingHelper.SkipField(f.FieldType)
                        select GetMetaFieldValueElement(f))),
                new XElement("ParentNode", _catalogCodeGenerator.GetEpiserverCode(parentId)),
                CreateSEOInfoElement(entity));
        }

        public XElement CreateSEOInfoElement(Entity entity)
        {
            XElement seoInfo = new XElement("SeoInfo");
            foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in _config.LanguageMapping)
            {
                string uri = _pimFieldAdapter.GetFieldValue(entity, "seouri", culturePair.Value);
                string title = _pimFieldAdapter.GetFieldValue(entity, "seotitle", culturePair.Value);
                string description = _pimFieldAdapter.GetFieldValue(entity, "seodescription", culturePair.Value);
                string keywords = _pimFieldAdapter.GetFieldValue(entity, "seokeywords", culturePair.Value);
                string urisegment = _pimFieldAdapter.GetFieldValue(entity, "seourisegment", culturePair.Value);

                if (string.IsNullOrEmpty(uri) && 
                    string.IsNullOrEmpty(title) && 
                    string.IsNullOrEmpty(description) && 
                    string.IsNullOrEmpty(keywords) && 
                    string.IsNullOrEmpty(urisegment))
                {
                    continue;
                }

                seoInfo.Add(
                    new XElement("Seo",
                        new XElement("LanguageCode", culturePair.Key.Name.ToLower()),
                        new XElement("Uri", uri),
                        new XElement("Title", title),
                        new XElement("Description", description),
                        new XElement("Keywords", keywords),
                        new XElement("UriSegment", urisegment)));
            }

            return seoInfo;
        }
        
        public XElement InRiverEntityToEpiEntry(Entity entity)
        {
            var metaFields = from f in entity.Fields
                where UseField(entity, f) && !_mappingHelper.SkipField(f.FieldType)
                select GetMetaFieldValueElement(f);

            return new XElement("Entry",
                new XElement("Name", _mappingHelper.GetNameForEntity(entity, 100)),
                new XElement("StartDate", _pimFieldAdapter.GetStartDate(entity)),
                new XElement("EndDate", _pimFieldAdapter.GetEndDate(entity)),
                new XElement("IsActive", "True"),
                new XElement("DisplayTemplate", string.Empty),
                new XElement("Code", _catalogCodeGenerator.GetEpiserverCode(entity)),
                new XElement("EntryType", _mappingHelper.GetEntryType(entity.EntityType.Id)),
                new XElement("Guid", GetChannelEntityGuid(_config.ChannelId, entity.Id)),
                new XElement(
                    "MetaData",
                    new XElement("MetaClass", new XElement("Name", GetMetaClassForEntity(entity))),
                    new XElement(
                        "MetaFields",
                        GetDisplayFieldElement(entity.DisplayName, "DisplayName"),
                        GetDisplayFieldElement(entity.DisplayDescription, "DisplayDescription"),
                        metaFields)),
                        CreateSEOInfoElement(entity)
                        );
        }

        private Guid GetChannelEntityGuid(int channelId, int entityId)
        {
            var concatIds = channelId.ToString().PadLeft(16, '0') + entityId.ToString().PadLeft(16, '0');
            return new Guid(concatIds);
        }

        public XElement GetMetaFieldValueElement(Field field)
        {
            XElement metaField = new XElement(
                "MetaField",
                new XElement("Name", _mappingHelper.GetEpiserverFieldName(field.FieldType)),
                new XElement("Type", _mappingHelper.GetEpiserverDataType(field.FieldType))
            );
            
            if (field.FieldType.DataType.Equals(DataType.LocaleString))
            {
                var ls = field.Data as LocaleString;
                if (!field.IsEmpty())
                {
                    foreach (var culturePair in _config.LanguageMapping)
                    {
                        if (ls != null)
                        {
                            metaField.Add(
                                new XElement("Data",
                                    new XAttribute("language", culturePair.Key.Name.ToLower()),
                                    new XAttribute("value", ls[culturePair.Value] ?? string.Empty)));
                        }
                    }
                }
                else
                {
                    foreach (var culturePair in _config.LanguageMapping)
                    {
                        metaField.Add(new XElement("Data", new XAttribute("language", culturePair.Key.Name.ToLower()), new XAttribute("value", string.Empty)));
                    }
                }
            }
            else if (field.FieldType.DataType.Equals(DataType.CVL))
            {
                var cvlDataElement = _pimFieldAdapter.GetCVLValues(field);
                metaField.Add(cvlDataElement);
            }
            else
            {
                metaField.Add(
                    new XElement("Data",
                        new XAttribute("language", _config.ChannelDefaultLanguage.Name.ToLower()),
                        new XAttribute("value", _pimFieldAdapter.GetFlatFieldData(field))));
            }

            return metaField;
        }

        public XElement CreateSimpleMetaFieldElement(string name, string value)
        {
            return new XElement(
                "MetaField",
                new XElement("Name", name),
                new XElement("Type", "ShortString"),
                new XElement("Data",
                    new XAttribute("language", _config.ChannelDefaultLanguage.Name.ToLower()),
                    new XAttribute("value", value)));
        }

        public XElement CreateNodeRelation(int sourceId, int targetId, int sortOrder)
        {
            return new XElement("NodeRelation",
                new XElement("ChildNodeCode", _catalogCodeGenerator.GetEpiserverCode(targetId)),
                new XElement("ParentNodeCode", _catalogCodeGenerator.GetEpiserverCode(sourceId)),
                new XElement("SortOrder", sortOrder));
        }

        public XElement CreateNodeEntryRelation(int sourceId, int targetId, int sortOrder)
        {
            return new XElement("NodeEntryRelation",
                new XElement("EntryCode", _catalogCodeGenerator.GetEpiserverCode(targetId)),
                new XElement("NodeCode", _catalogCodeGenerator.GetEpiserverCode(sourceId)),
                new XElement("SortOrder", sortOrder));
        }

        public XElement CreateNodeEntryRelation(string nodeCode, string skuId, int sortOrder)
        {
            return new XElement("NodeEntryRelation",
                new XElement("EntryCode", skuId),
                new XElement("NodeCode", nodeCode),
                new XElement("SortOrder", sortOrder));
        }

        public XElement CreateEntryRelationElement(string parentCode, string parentEntityType, string childCode, int sortOrder)
        {
            string relationType = "ProductVariation";

            if (!string.IsNullOrEmpty(parentEntityType))
            {
                var sourceType = _mappingHelper.GetEntryType(parentEntityType);
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
                new XElement("ParentEntryCode", parentCode),
                new XElement("ChildEntryCode", childCode),
                new XElement("RelationType", relationType),
                new XElement("Quantity", 0),
                new XElement("GroupName", "default"),
                new XElement("SortOrder", sortOrder));
        }

        public XElement CreateCatalogAssociationElement(StructureEntity structureEntity, Dictionary<int, Entity> channelEntities = null)
        {
            string name = _mappingHelper.GetAssociationName(structureEntity);

            return new XElement(
                "CatalogAssociation",
                new XElement("Name", name),
                new XElement("Description", structureEntity.LinkTypeIdFromParent),
                new XElement("SortOrder", structureEntity.SortOrder),
                new XElement("EntryCode", _catalogCodeGenerator.GetEpiserverCode(structureEntity.ParentId)),
                CreateAssociationElement(structureEntity));
        }

        public XElement CreateAssociationElement(StructureEntity structureEntity)
        {
            return new XElement(
                "Association",
                new XElement("EntryCode", _catalogCodeGenerator.GetEpiserverCode(structureEntity.EntityId)),
                new XElement("SortOrder", structureEntity.SortOrder),
                    new XElement("Type", structureEntity.LinkTypeIdFromParent));
        }


        public XElement CreateResourceMetaFieldsElement(EntityType resourceType)
        {
            return new XElement(
                "ResourceMetaFields",
                resourceType.FieldTypes.Select(
                    fieldtype =>
                    new XElement(
                        "ResourceMetaField",
                        new XElement("FieldName", _mappingHelper.GetEpiserverFieldName(fieldtype)),
                        new XElement("FriendlyName", _mappingHelper.GetEpiserverFieldName(fieldtype)),
                        new XElement("Description", _mappingHelper.GetEpiserverFieldName(fieldtype)),
                        new XElement("FieldType", _mappingHelper.GetEpiserverDataType(fieldtype)),
                        new XElement("Format", "Text"),
                        new XElement("MaximumLength", _mappingHelper.GetMetaFieldLength(fieldtype)),
                        new XElement("AllowNulls", !fieldtype.Mandatory),
                        new XElement("UniqueValue", fieldtype.Unique))));
        }

        public XElement GetMetaClassesFromFieldSets()
        {
            List<XElement> metaClasses = new List<XElement>();
            List<XElement> metafields = new List<XElement>();

            XElement diaplyNameElement = CreateEpiserverLongStringField("DisplayName");
            XElement displayDescriptionElement = CreateEpiserverLongStringField("DisplayDescription");
            XElement specification = CreateEpiserverLongHtmlField("SpecificationField");
            bool addSpec = false;

            foreach (EntityType entityType in _config.ExportEnabledEntityTypes)
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
                    if (_mappingHelper.SkipField(fieldType))
                    {
                        continue;
                    }

                    XElement metaField = InRiverFieldTypeToMetaField(fieldType);

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

                    if (metafields.Any(mf =>
                    {
                        XElement nameElement = mf.Element("Name");
                        return nameElement != null && nameElement.Value.Equals(_mappingHelper.GetEpiserverFieldName(fieldType));
                    }))
                    {
                        XElement existingMetaField = metafields.FirstOrDefault(mf =>
                        {
                            XElement nameElement = mf.Element("Name");
                            return nameElement != null && nameElement.Value.Equals(_mappingHelper.GetEpiserverFieldName(fieldType));
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

        public List<XElement> GenerateSkuItemElemetsFromItem(Entity item)
        {
            XDocument skuDoc = SkuFieldToDocument(item);
            if (skuDoc.Root == null || skuDoc.Element("SKUs") == null)
            {
                return new List<XElement>();
            }

            Link specLink = item.OutboundLinks.Find(l => l.Target.EntityType.Id == "Specification");
            XElement specificationMetaField = null;
            if (specLink != null)
            {
                specificationMetaField = new XElement("MetaField",
                        new XElement("Name", "SpecificationField"),
                        new XElement("Type", "LongHtmlString"));

                foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in _config.LanguageMapping)
                {
                    string htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specLink.Target.Id, item.Id, culturePair.Value);
                    specificationMetaField.Add(
                        new XElement("Data",
                            new XAttribute("language", culturePair.Key.Name.ToLower()),
                            new XAttribute("value", htmlData)));
                }
            }

            List<XElement> skuElements = new List<XElement>();
            XElement skuElement = skuDoc.Element("SKUs");
            if (skuElement == null)
                return skuElements;
            
            foreach (XElement sku in skuElement.Elements())
            {
                string skuId = sku.Attribute("id").Value;
                if (string.IsNullOrEmpty(skuId))
                {
                    IntegrationLogger.Write(LogLevel.Information, $"Could not find the id for the SKU data for item: {item.Id}");
                    continue;
                }

                XElement itemElement = InRiverEntityToEpiEntry(item);
                XElement nameElement = sku.Element("Name");
                if (nameElement != null)
                {
                    string name = (!string.IsNullOrEmpty(nameElement.Value)) ? nameElement.Value : skuId;
                    XElement itemElementName = itemElement.Element("Name");
                    if (itemElementName != null)
                    {
                        itemElementName.Value = name;
                    }
                }

                XElement codeElement = itemElement.Element("Code");
                if (codeElement != null)
                {
                    codeElement.Value = _catalogCodeGenerator.GetPrefixedCode(skuId);
                }

                XElement entryTypeElement = itemElement.Element("EntryType");
                if (entryTypeElement != null)
                {
                    entryTypeElement.Value = "Variation";
                }

                XElement skuDataElement = sku.Element(FieldNames.SKUData);
                if (skuDataElement != null)
                {
                    foreach (XElement skuData in skuDataElement.Elements())
                    {
                        XElement metaDataElement = itemElement.Element("MetaData");
                        if (metaDataElement?.Element("MetaFields") != null)
                        {
                            metaDataElement.Element("MetaFields")?.Add(CreateSimpleMetaFieldElement(skuData.Name.LocalName, skuData.Value));
                        }
                    }
                }

                if (specificationMetaField != null)
                {
                    XElement metaDataElement = itemElement.Element("MetaData");
                    if (metaDataElement?.Element("MetaFields") != null)
                    {
                        metaDataElement.Element("MetaFields")?.Add(specificationMetaField);
                    }
                }

                skuElements.Add(itemElement);
            }

            return skuElements;
        }

        public XDocument SkuFieldToDocument(Entity item)
        {
            Field skuField = item.GetField(FieldNames.SKUFieldName);
            if (skuField == null || skuField.Data == null)
            {
                XElement itemElement = InRiverEntityToEpiEntry(item);
                IntegrationLogger.Write(LogLevel.Information, $"Could not find SKU data for item: {item.Id}");
                return new XDocument(itemElement);
            }

            return XDocument.Parse(skuField.Data.ToString());
        }

        public List<string> SkuItemIds(Entity item)
        {
            Field skuField = item.GetField(FieldNames.SKUFieldName);
            if (skuField == null || skuField.IsEmpty())
            {
                return new List<string> { item.Id.ToString(CultureInfo.InvariantCulture) };
            }

            XDocument skuDoc = SkuFieldToDocument(item);

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

        private XElement GetDisplayFieldElement(Field displayField, string name)
        {
            if (displayField == null || displayField.IsEmpty())
            {
                return new XElement("MetaField",
                    new XElement("Name", name),
                    new XElement("Type", "LongHtmlString"),
                    new XElement("Data",
                        new XAttribute("language", _config.ChannelDefaultLanguage.Name.ToLower()),
                        new XAttribute("value", string.Empty)));
            }

            var element = GetMetaFieldValueElement(displayField);
            var nameElement = element.Element("Name");
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

        private bool UseField(Entity entity, Field field)
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

        private string GetMetaClassForEntity(Entity entity)
        {
            if (!string.IsNullOrEmpty(entity.FieldSetId) && entity.EntityType.FieldSets.Any(fs => fs.Id == entity.FieldSetId))
            {
                return entity.EntityType.Id + "_" + entity.FieldSetId;
            }

            return entity.EntityType.Id;
        }
    }
}
