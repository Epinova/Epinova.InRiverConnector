using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.XmlFactories
{
    public class CatalogDocumentFactory
    {
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly CatalogElementFactory _catalogElementFactory;
        private readonly ChannelHelper _channelHelper;
        private readonly IConfiguration _config;
        private readonly IEntityService _entityService;
        private readonly EpiApi _epiApi;
        private readonly EpiMappingHelper _epiMappingHelper;

        private CatalogElementContainer _epiElementContainer;

        public CatalogDocumentFactory(IConfiguration config,
            EpiApi epiApi,
            CatalogElementFactory catalogElementFactory,
            EpiMappingHelper epiMappingHelper,
            ChannelHelper channelHelper,
            CatalogCodeGenerator catalogCodeGenerator,
            IEntityService entityService)
        {
            _config = config;
            _epiApi = epiApi;
            _catalogElementFactory = catalogElementFactory;
            _epiMappingHelper = epiMappingHelper;
            _channelHelper = channelHelper;
            _catalogCodeGenerator = catalogCodeGenerator;
            _entityService = entityService;
        }


        public XDocument CreateDocument(XElement catalogElement, XElement metaClasses, XElement associationTypes)
        {
            var result =
                new XDocument(
                    new XElement(
                        "Catalogs",
                        new XAttribute("version", "1.0"),
                        new XElement("MetaDataScheme", metaClasses),
                        new XElement("Dictionaries", associationTypes),
                        catalogElement));

            return result;
        }

        public XDocument CreateImportDocument(Entity channelEntity, XElement metaClasses, XElement associationTypes, CatalogElementContainer epiElements)
        {
            XElement catalogElement = _catalogElementFactory.CreateCatalogElement(channelEntity);

            List<XElement> baseElements = CreateBaseCatalogDocumentNodes(channelEntity, epiElements.Nodes, epiElements.Entries, epiElements.Relations, epiElements.Associations);

            catalogElement.Add(baseElements);

            return CreateDocument(catalogElement, metaClasses, associationTypes);
        }

        public XDocument CreateUpdateDocument(Entity channelEntity, List<Entity> updatedEntity)
        {
            XElement catalogElement = _catalogElementFactory.CreateCatalogElement(channelEntity);

            List<XElement> entries = updatedEntity.Select(_catalogElementFactory.InRiverEntityToEpiEntry).ToList();
            List<XElement> baseElements = CreateBaseCatalogDocumentNodes(channelEntity, null, entries, null, null);

            catalogElement.Add(baseElements);

            return CreateDocument(catalogElement, null, null);
        }

        public XDocument CreateUpdateDocument(Entity channelEntity, Entity updatedEntity)
        {
            var skus = new List<XElement>();
            if (_config.ItemsToSkus && updatedEntity.EntityType.Id == "Item")
            {
                skus = _catalogElementFactory.GenerateSkuItemElemetsFromItem(updatedEntity);
            }

            XElement updatedNode = null;
            XElement updatedEntry = null;

            bool shouldGetUpdatedEntry = !(updatedEntity.EntityType.Id == "Item" && !_config.UseThreeLevelsInCommerce && _config.ItemsToSkus);

            if (updatedEntity.EntityType.Id == "ChannelNode")
            {
                updatedNode = GetUpdatedNode(channelEntity, updatedEntity);
            }
            else if (shouldGetUpdatedEntry)
            {
                updatedEntry = _catalogElementFactory.InRiverEntityToEpiEntry(updatedEntity);
                Link specLink = updatedEntity.OutboundLinks.Find(l => l.Target.EntityType.Id == "Specification");
                if (specLink != null)
                {
                    var specificationField = new XElement("MetaField",
                        new XElement("Name", "SpecificationField"),
                        new XElement("Type", "LongHtmlString"));

                    foreach (KeyValuePair<CultureInfo, CultureInfo> languageMap in _config.LanguageMapping)
                    {
                        string htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specLink.Target.Id, updatedEntity.Id, languageMap.Value);
                        specificationField.Add(new XElement("Data",
                            new XAttribute("language", languageMap.Key.Name.ToLower()),
                            new XAttribute("value", htmlData)));
                    }

                    XElement element = updatedEntry.Descendants().FirstOrDefault(f => f.Name == "MetaFields");
                    element?.Add(specificationField);
                }
            }

            XElement catalogElement = _catalogElementFactory.CreateCatalogElement(channelEntity);
            var updatedNodes = new List<XElement> { updatedEntry };
            updatedNodes.AddRange(skus);

            List<XElement> baseCatalogDocumentNodes = CreateBaseCatalogDocumentNodes(channelEntity, new List<XElement> { updatedNode }, updatedNodes, null, null);
            catalogElement.Add(baseCatalogDocumentNodes);

            return CreateDocument(catalogElement, null, null);
        }

        public XElement GetAssociationTypes()
        {
            IEnumerable<XElement> associationTypeElements = _config.AssociationLinkTypes.Select(_catalogElementFactory.CreateAssociationTypeElement);
            return new XElement("AssociationTypes", associationTypeElements);
        }

        public async Task<CatalogElementContainer> GetEPiElementsAsync(List<StructureEntity> structureEntities)
        {
            _epiElementContainer = new CatalogElementContainer();

            var totalLoaded = 0;
            int batchSize = _config.BatchSize;

            do
            {
                List<StructureEntity> batch = structureEntities.Skip(totalLoaded).Take(batchSize).ToList();

                await AddNodeElementsAsync(batch);
                AddEntryElements(batch);
                AddRelationElements(structureEntities);

                totalLoaded += batch.Count;

                IntegrationLogger.Write(LogLevel.Debug, $"Fetched {totalLoaded} of {structureEntities.Count} total");
            } while (structureEntities.Count > totalLoaded);

            return _epiElementContainer;
        }

        private void AddAssociationElements(LinkType linkType, StructureEntity structureEntity, string itemCode)
        {
            IntegrationLogger.Write(LogLevel.Debug, "AddAssociationElements");

            if (!IsAssociationLinkType(linkType))
                return;

            if (!_config.UseThreeLevelsInCommerce && _config.ItemsToSkus && structureEntity.IsItem())
            {
                AddItemToSkusAssociations(linkType, structureEntity, itemCode);
            }
            else
            {
                AddNormalAssociations(structureEntity);
            }
        }

        private void AddChannelNodeRelation(LinkType linkType, StructureEntity structureEntity, Entity entity)
        {
            string addedRelationName = _catalogCodeGenerator.GetRelationName(entity.Id, structureEntity.ParentId);
            if (_epiElementContainer.HasRelation(addedRelationName))
                return;

            XElement relationElement = _catalogElementFactory.CreateNodeEntryRelation(structureEntity.ParentId, structureEntity.EntityId, structureEntity.SortOrder);

            _epiElementContainer.AddRelation(relationElement, addedRelationName);

            IntegrationLogger.Write(LogLevel.Debug,
                $"Added Relation for Source {structureEntity.ParentId} and Target {structureEntity.EntityId} for LinkTypeId {linkType.Id}");
        }

        private void AddEntryElements(List<StructureEntity> batch)
        {
            foreach (StructureEntity structureEntity in batch.Where(x => x.EntityId != _config.ChannelId && !x.IsChannelNode()))
            {
                Entity entity = _entityService.GetEntity(structureEntity.EntityId, LoadLevel.DataAndLinks);

                if (ShouldCreateSkus(structureEntity))
                {
                    List<XElement> skus = _catalogElementFactory.GenerateSkuItemElemetsFromItem(entity);
                    foreach (XElement sku in skus)
                    {
                        XElement codeElement = sku.Element("Code");
                        if (codeElement != null && !_epiElementContainer.HasEntry(codeElement.Value))
                        {
                            _epiElementContainer.AddEntry(sku, codeElement.Value);

                            IntegrationLogger.Write(LogLevel.Debug, $"Added Item/SKU {sku.Name.LocalName} to Entries");
                        }
                    }
                }

                if (structureEntity.IsItem() && _config.ItemsToSkus && _config.UseThreeLevelsInCommerce ||
                    !ShouldCreateSkus(structureEntity))
                {
                    if (structureEntity.IsItem() && !_channelHelper.ItemHasParentInChannel(structureEntity))
                        continue;

                    XElement element = _catalogElementFactory.InRiverEntityToEpiEntry(entity);

                    XElement codeElement = element.Element("Code");
                    if (codeElement == null || _epiElementContainer.HasEntry(codeElement.Value))
                        continue;

                    XElement specificationField = GetSpecificationMetaField(entity);

                    if (specificationField != null)
                    {
                        XElement metaFieldsElement = element.Descendants().FirstOrDefault(f => f.Name == "MetaFields");
                        metaFieldsElement?.Add(specificationField);
                    }

                    _epiElementContainer.AddEntry(element, codeElement.Value);

                    IntegrationLogger.Write(LogLevel.Debug, $"Added Entity {entity.Id} to Entries");
                }
            }
        }

        private void AddEntryRelationElement(StructureEntity structureEntity, string skuCode, LinkType linkType)
        {
            Entity parentProduct = _entityService.GetParentProduct(structureEntity);
            if (parentProduct == null)
                return;

            string parentCode = _catalogCodeGenerator.GetEpiserverCode(parentProduct);

            if (skuCode == parentCode)
                return;

            string addedRelationsName = "EntryRelation_" + _catalogCodeGenerator.GetRelationName(skuCode, parentCode);

            if (_epiElementContainer.HasRelation(addedRelationsName))
                return;

            XElement entryRelationElement = _catalogElementFactory.CreateEntryRelationElement(parentCode, linkType.SourceEntityTypeId, skuCode, structureEntity.SortOrder);

            _epiElementContainer.AddRelation(entryRelationElement, addedRelationsName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added EntryRelation for {skuCode} to product {parentCode}. Relation name: {addedRelationsName}.");
        }

        private void AddItemToSkusAssociations(LinkType linkType, StructureEntity structureEntity, string skuId)
        {
            string associationName = _epiMappingHelper.GetAssociationName(structureEntity);

            Entity source = _entityService.GetEntity(structureEntity.ParentId, LoadLevel.DataOnly);

            List<string> skuCodes = _catalogElementFactory.SkuItemIds(source);
            for (var i = 0; i < skuCodes.Count; i++)
            {
                skuCodes[i] = _catalogCodeGenerator.GetPrefixedCode(skuCodes[i]);
            }

            foreach (string skuCode in skuCodes)
            {
                string associationKey = _catalogCodeGenerator.GetAssociationKey(skuCode, structureEntity.ParentId.ToString(), associationName);
                if (_epiElementContainer.HasAssociation(associationKey))
                    continue;

                XElement existingCatalogAssociationElement = _epiElementContainer.Associations.FirstOrDefault(
                    x => x.Element("Name")?.Value == associationName &&
                         x.Element("EntryCode")?.Value == skuCode);

                var associationElement = new XElement("Association",
                    new XElement("EntryCode", skuId),
                    new XElement("SortOrder", structureEntity.SortOrder),
                    new XElement("Type", linkType.Id));

                if (existingCatalogAssociationElement != null)
                {
                    if (existingCatalogAssociationElement.Descendants().Any(e => e.Name.LocalName == "EntryCode" && e.Value == skuId))
                        continue;

                    existingCatalogAssociationElement.Add(associationElement);
                    _epiElementContainer.AddAssociationKey(associationKey);
                }
                else
                {
                    var catalogAssociation = new XElement("CatalogAssociation",
                        new XElement("Name", associationName),
                        new XElement("Description", linkType.Id),
                        new XElement("SortOrder", structureEntity.SortOrder),
                        new XElement("EntryCode", skuCode),
                        associationElement);

                    _epiElementContainer.AddAssociation(catalogAssociation, associationKey);
                }
            }
        }

        /// <summary>
        /// Items included only as upsell/accessories etc might not have their parent products/bundles in the channel. 
        /// Add them if you're force including linked content.
        /// </summary>
        private void AddMissingParentRelation(StructureEntity structureEntity, string skuId)
        {
            Entity parentProduct = _entityService.GetParentProduct(structureEntity);
            if (parentProduct == null)
                return;

            string parentCode = _catalogCodeGenerator.GetEpiserverCode(parentProduct);
            bool hasParent = _epiElementContainer.Entries.Any(x => x.Element("Code")?.Value == parentCode);

            if (!hasParent)
            {
                IntegrationLogger.Write(LogLevel.Debug, $"Could not find parent for {skuId}, adding it to the list. Parent is {parentCode}.");
                XElement missingParent = _catalogElementFactory.InRiverEntityToEpiEntry(parentProduct);
                _epiElementContainer.Entries.Add(missingParent);
            }

            AddEntryRelationElement(structureEntity, skuId, new LinkType());
        }

        private async Task AddNodeElementsAsync(List<StructureEntity> batch)
        {
            IEnumerable<StructureEntity> nodeStructureEntities = batch.Where(x => x.IsChannelNode() && x.EntityId != _config.ChannelId);

            foreach (StructureEntity structureEntity in nodeStructureEntities)
            {
                Entity entity = _entityService.GetEntity(structureEntity.EntityId, LoadLevel.DataOnly);

                if (_config.ChannelId == structureEntity.ParentId)
                {
                    await _epiApi.MoveNodeToRootIfNeededAsync(entity.Id);
                }

                IntegrationLogger.Write(LogLevel.Debug, $"Trying to add channelNode {entity.Id} to Nodes");
                string currentNodeCode = _catalogCodeGenerator.GetEpiserverCode(entity);

                XElement nodeElement = _epiElementContainer.Nodes.FirstOrDefault(e => e.Element("Code")?.Value == currentNodeCode);

                int linkIndex = structureEntity.SortOrder;

                if (nodeElement == null)
                {
                    nodeElement = _catalogElementFactory.CreateNodeElement(entity, structureEntity.ParentId, linkIndex);
                    _epiElementContainer.AddNode(nodeElement, currentNodeCode);
                    IntegrationLogger.Write(LogLevel.Debug, $"Added channelNode {entity.Id} to Nodes");
                }
                else
                {
                    XElement parentNode = nodeElement.Element("ParentNode");
                    if (parentNode != null &&
                        parentNode.Value != _config.ChannelId.ToString(CultureInfo.InvariantCulture) &&
                        structureEntity.ParentId == _config.ChannelId)
                    {
                        parentNode.Value = _config.ChannelId.ToString(CultureInfo.InvariantCulture);
                        XElement sortOrderElement = nodeElement.Element("SortOrder");
                        if (sortOrderElement != null)
                        {
                            string oldSortOrder = sortOrderElement.Value;
                            sortOrderElement.Value = linkIndex.ToString(CultureInfo.InvariantCulture);
                            linkIndex = int.Parse(oldSortOrder);
                        }
                    }

                    string relationName = _catalogCodeGenerator.GetRelationName(entity.Id, structureEntity.ParentId);

                    if (_epiElementContainer.HasRelation(relationName))
                        continue;

                    XElement nodeRelationElement = _catalogElementFactory.CreateNodeRelation(structureEntity.ParentId, entity.Id, linkIndex);

                    _epiElementContainer.AddRelation(nodeRelationElement, relationName);

                    IntegrationLogger.Write(LogLevel.Debug, $"Adding relation to channelNode {entity.Id}");
                }
            }
        }

        private void AddNodeEntryRelationElement(LinkType linkType, StructureEntity structureEntity, string skuCode)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"For SKU {skuCode}: Found relation between {linkType.SourceEntityTypeId} and {linkType.TargetEntityTypeId} called {linkType.Id}");

            Entity parentNode = _channelHelper.GetParentChannelNode(structureEntity);
            if (parentNode == null)
                return;

            string parentCode = _catalogCodeGenerator.GetEpiserverCode(parentNode);
            string relationName = "NodeEntryRelation_" + _catalogCodeGenerator.GetRelationName(skuCode, parentCode);

            if (_epiElementContainer.HasRelation(relationName))
                return;

            XElement relationElement = _catalogElementFactory.CreateNodeEntryRelation(parentCode, skuCode, structureEntity.SortOrder);

            _epiElementContainer.AddRelation(relationElement, relationName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added NodeEntryRelation for EntryCode {skuCode}. Relation name: {relationName}.");
        }

        private void AddNormalAssociations(StructureEntity structureEntity)
        {
            IntegrationLogger.Write(LogLevel.Debug, "AddNormalAssociations");

            string entityCode = _catalogCodeGenerator.GetEpiserverCode(structureEntity.EntityId);
            string parentCode = _catalogCodeGenerator.GetEpiserverCode(structureEntity.ParentId);

            string associationName = _epiMappingHelper.GetAssociationName(structureEntity);

            string associationKey = _catalogCodeGenerator.GetAssociationKey(entityCode, parentCode, associationName);

            if (_epiElementContainer.HasAssociation(associationKey))
                return;

            XElement existingAssociation = GetExistingAssociation(associationName, parentCode);

            if (existingAssociation != null)
            {
                XElement newElement = _catalogElementFactory.CreateAssociationElement(structureEntity);

                if (!existingAssociation.Descendants().Any(e => e.Name.LocalName == "EntryCode" && e.Value == entityCode))
                {
                    existingAssociation.Add(newElement);
                    _epiElementContainer.AddAssociationKey(associationKey);
                }
            }
            else
            {
                XElement associationElement = _catalogElementFactory.CreateCatalogAssociationElement(structureEntity);
                _epiElementContainer.AddAssociation(associationElement, associationKey);
            }
        }

        private void AddRelationElements(List<StructureEntity> allChannelStructureEntities)
        {
            foreach (StructureEntity structureEntity in allChannelStructureEntities.Where(x => x.EntityId != _config.ChannelId && x.Type != "Resource"))
            {
                Entity entity = _entityService.GetEntity(structureEntity.EntityId, LoadLevel.DataOnly);

                LinkType linkType = _config.LinkTypes.FirstOrDefault(x => x.Id == structureEntity.LinkTypeIdFromParent);

                if (linkType == null)
                    continue;

                if (linkType.SourceEntityTypeIsChannelNode())
                {
                    AddChannelNodeRelation(linkType, structureEntity, entity);
                }

                AddRelations(linkType, structureEntity, entity);
            }
        }

        private void AddRelations(LinkType linkType,
            StructureEntity structureEntity,
            Entity entity)
        {
            var skus = new List<string> { _catalogCodeGenerator.GetEpiserverCode(entity.Id) };

            int parentId = structureEntity.EntityId;

            if (structureEntity.IsItem() && _config.ItemsToSkus)
            {
                skus = _catalogElementFactory.SkuItemIds(entity);
                for (var i = 0; i < skus.Count; i++)
                {
                    skus[i] = _catalogCodeGenerator.GetPrefixedCode(skus[i]);
                }

                if (_config.UseThreeLevelsInCommerce)
                {
                    skus.Add(_catalogCodeGenerator.GetEpiserverCode(parentId));
                }
            }

            foreach (string skuId in skus)
            {
                if (_epiMappingHelper.IsRelation(linkType))
                {
                    AddNodeEntryRelationElement(linkType, structureEntity, skuId);
                    AddEntryRelationElement(structureEntity, skuId, linkType);
                }
                else
                {
                    if (_config.ForceIncludeLinkedContent)
                    {
                        AddMissingParentRelation(structureEntity, skuId);
                    }

                    IntegrationLogger.Write(LogLevel.Debug, "AddAssociationElements");
                    AddAssociationElements(linkType, structureEntity, skuId);
                }
            }
        }

        private List<XElement> CreateBaseCatalogDocumentNodes(Entity channelEntity, List<XElement> nodes, List<XElement> entries, List<XElement> relations, List<XElement> associations)
        {
            return new List<XElement>
            {
                new XElement("Sites", new XElement("Site", _channelHelper.GetChannelGuid(channelEntity).ToString())),
                new XElement("Nodes", new XAttribute("totalCount", nodes?.Count ?? 0), nodes),
                new XElement("Entries", new XAttribute("totalCount", entries?.Count ?? 0), entries),
                new XElement("Relations", new XAttribute("totalCount", relations?.Count ?? 0), relations),
                new XElement("Associations", new XAttribute("totalCount", associations?.Count ?? 0), associations)
            };
        }

        private XElement GetExistingAssociation(string associationName, string parentCode)
        {
            return _epiElementContainer.Associations.FirstOrDefault(
                a => a.Element("Name")?.Value == associationName &&
                     a.Element("EntryCode")?.Value == parentCode);
        }

        private XElement GetSpecificationMetaField(Entity entity)
        {
            Link specificationLink = entity.OutboundLinks.FirstOrDefault(IsSpecificationLink);

            if (specificationLink == null)
                return null;

            IntegrationLogger.Write(LogLevel.Debug, $"Found specification for entity {entity}. Creating MetaField element.");

            var specificationMetaField = new XElement("MetaField",
                new XElement("Name", "SpecificationField"),
                new XElement("Type", "LongHtmlString"));

            foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in _config.LanguageMapping)
            {
                string htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specificationLink.Target.Id, entity.Id, culturePair.Value);
                specificationMetaField.Add(
                    new XElement("Data",
                        new XAttribute("language", culturePair.Key.Name.ToLower()),
                        new XAttribute("value", htmlData)));
            }

            return specificationMetaField;
        }

        private XElement GetUpdatedNode(Entity channelEntity, Entity updatedEntity)
        {
            Link nodeLink = updatedEntity.Links.Find(l => l.Source.Id == channelEntity.Id);
            var sortOrder = 0;
            if (nodeLink != null)
            {
                sortOrder = nodeLink.Index;
            }

            return _catalogElementFactory.CreateNodeElement(updatedEntity, channelEntity.Id, sortOrder);
        }

        private bool IsAssociationLinkType(LinkType linkType)
        {
            return _config.AssociationLinkTypes.Any(x => x.Id == linkType.Id);
        }

        private bool IsSpecificationLink(Link link)
        {
            return link.Target.EntityType.Id == "Specification";
        }

        private bool ShouldCreateSkus(StructureEntity structureEntity)
        {
            return structureEntity.IsItem() && _config.ItemsToSkus;
        }
    }
}