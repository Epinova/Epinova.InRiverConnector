using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;
using Newtonsoft.Json;

namespace Epinova.InRiverConnector.EpiserverAdapter.EpiXml
{
    public class EpiDocumentFactory
    {
        private readonly IConfiguration _config;
        private readonly EpiApi _epiApi;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly EpiMappingHelper _epiMappingHelper;
        private readonly ChannelHelper _channelHelper;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        
        private CatalogElementContainer _epiElementContainer;

        public EpiDocumentFactory(IConfiguration config, 
            EpiApi epiApi, 
            EpiElementFactory epiElementFactory, 
            EpiMappingHelper epiMappingHelper, 
            ChannelHelper channelHelper,
            CatalogCodeGenerator catalogCodeGenerator)
        {
            _config = config;
            _epiApi = epiApi;
            _epiElementFactory = epiElementFactory;
            _epiMappingHelper = epiMappingHelper;
            _channelHelper = channelHelper;
            _catalogCodeGenerator = catalogCodeGenerator;
        }

        public XDocument CreateImportDocument(Entity channelEntity, 
                                                     XElement metaClasses, 
                                                     XElement associationTypes, 
                                                     CatalogElementContainer epiElements)
        {
            XElement catalogElement = _epiElementFactory.CreateCatalogElement(channelEntity);
            if (catalogElement == null)
            {
                return null;
            }

            catalogElement.Add(
                new XElement("Sites", new XElement("Site", _channelHelper.GetChannelGuid(channelEntity).ToString())),
                new XElement("Nodes", new XAttribute("totalCount", epiElements.Nodes.Count), epiElements.Nodes),
                new XElement("Entries", new XAttribute("totalCount", epiElements.Entries.Count), epiElements.Entries),
                new XElement("Relations", new XAttribute("totalCount", epiElements.Relations.Count), epiElements.Relations.OrderByDescending(e => e.Name.LocalName)),
                new XElement("Associations", new XAttribute("totalCount", epiElements.Associations.Count), epiElements.Associations));

            return CreateDocument(catalogElement, metaClasses, associationTypes);
        }

        public XDocument CreateUpdateDocument(Entity channelEntity, Entity updatedEntity)
        {
            int count = 0;
            List<XElement> skus = new List<XElement>();
            if (_config.ItemsToSkus && updatedEntity.EntityType.Id == "Item")
            {
                skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(updatedEntity);
                count += skus.Count;
            }

            XElement updatedNode = null;
            XElement updatedEntry = null;

            if (updatedEntity.EntityType.Id == "ChannelNode")
            {
                Link nodeLink = updatedEntity.Links.Find(l => l.Source.Id == channelEntity.Id);
                int sortOrder = 0;
                if (nodeLink != null)
                {
                    sortOrder = nodeLink.Index;
                }

                updatedNode = _epiElementFactory.CreateNodeElement(updatedEntity, channelEntity.Id, sortOrder);
            }
            else if (!(updatedEntity.EntityType.Id == "Item" && !_config.UseThreeLevelsInCommerce && _config.ItemsToSkus))
            {
                updatedEntry = _epiElementFactory.InRiverEntityToEpiEntry(updatedEntity);
                Link specLink = updatedEntity.OutboundLinks.Find(l => l.Target.EntityType.Id == "Specification");
                if (specLink != null)
                {
                    XElement metaField = new XElement("MetaField", 
                        new XElement("Name", "SpecificationField"), 
                        new XElement("Type", "LongHtmlString"));

                    foreach (var languageMap in _config.LanguageMapping)
                    {
                        var htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specLink.Target.Id, updatedEntity.Id, languageMap.Value);
                        metaField.Add(new XElement("Data", 
                            new XAttribute("language", languageMap.Key.Name.ToLower()), 
                            new XAttribute("value", htmlData)));
                    }

                    XElement element = updatedEntry.Descendants().FirstOrDefault(f => f.Name == "MetaFields");
                    if (element != null)
                    {
                        element.Add(metaField);
                    }
                }

                count += 1;
            }

            XElement catalogElement = _epiElementFactory.CreateCatalogElement(channelEntity);

            catalogElement.Add(
                new XElement("Sites", 
                    new XElement("Site", _channelHelper.GetChannelGuid(channelEntity).ToString())),
                new XElement("Nodes", updatedNode),
                new XElement("Entries", 
                    new XAttribute("totalCount", count), 
                    updatedEntry, 
                    skus),
                new XElement("Relations"),
                new XElement("Associations"));

            return CreateDocument(catalogElement, null, null);
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

        public CatalogElementContainer GetEPiElements(List<StructureEntity> allChannelStructureEntities)
        {
            _epiElementContainer = new CatalogElementContainer();

            int totalLoaded = 0;
            int batchSize = _config.BatchSize;

            do
            {
                var batch = allChannelStructureEntities.Skip(totalLoaded).Take(batchSize).ToList();

                AddNodeElements(batch);
                AddEntryElements(batch);
                AddRelationElements(batch, allChannelStructureEntities);

                totalLoaded += batch.Count;

                IntegrationLogger.Write(LogLevel.Debug, $"Fetched {totalLoaded} of {allChannelStructureEntities.Count} total");
            }
            while (allChannelStructureEntities.Count > totalLoaded);

            return _epiElementContainer;
        }

        public XElement GetAssociationTypes()
        {
            var associationTypeElements = _config.AssociationLinkTypes.Select(_epiElementFactory.CreateAssociationTypeElement);
            return new XElement("AssociationTypes", associationTypeElements);
        }
               
        private void AddEntryElements(List<StructureEntity> batch)
        {
            foreach (var structureEntity in batch.Where(x => x.EntityId != _config.ChannelId))
            {
                if (structureEntity.LinkEntityId.HasValue)
                {
                    Entity linkEntity = RemoteManager.DataService.GetEntity(structureEntity.LinkEntityId.Value, LoadLevel.DataOnly);

                    XElement entryElement = _epiElementFactory.InRiverEntityToEpiEntry(linkEntity);

                    XElement codeElement = entryElement.Element("Code");
                    if (codeElement != null && !_epiElementContainer.HasEntry(codeElement.Value))
                    {
                        _epiElementContainer.AddEntry(entryElement, codeElement.Value);
                        IntegrationLogger.Write(LogLevel.Debug, $"Added Entity {linkEntity.DisplayName} to Entries");
                    }
                }

                Entity entity = RemoteManager.DataService.GetEntity(structureEntity.EntityId, LoadLevel.DataAndLinks);

                if (ShouldCreateSkus(structureEntity))
                {
                    List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(entity);
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
                    var element = _epiElementFactory.InRiverEntityToEpiEntry(entity);

                    var codeElement = element.Element("Code");
                    if (codeElement == null || _epiElementContainer.HasEntry(codeElement.Value))
                        continue;

                    var specificationField = GetSpecificationMetaField(entity);

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

        private bool ShouldCreateSkus(StructureEntity structureEntity)
        {
            return structureEntity.IsItem() && _config.ItemsToSkus;
        }

        private XElement GetSpecificationMetaField(Entity entity)
        {
            var specificationLink = entity.OutboundLinks.FirstOrDefault(IsSpecificationLink);

            if (specificationLink == null)
                return null;

            IntegrationLogger.Write(LogLevel.Debug, $"Found specification for entity {entity}. Creating MetaField element.");

            XElement specificationMetaField = new XElement("MetaField",
                new XElement("Name", "SpecificationField"),
                new XElement("Type", "LongHtmlString"));

            foreach (var culturePair in _config.LanguageMapping)
            {
                var htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specificationLink.Target.Id, entity.Id, culturePair.Value);
                specificationMetaField.Add(
                    new XElement("Data",
                        new XAttribute("language", culturePair.Key.Name.ToLower()),
                        new XAttribute("value", htmlData)));
            }

            return specificationMetaField;
        }

        private bool IsSpecificationLink(Link link)
        {
            return link.Target.EntityType.Id == "Specification";
        }

        private void AddRelationElements(List<StructureEntity> structureEntitiesBatch, List<StructureEntity> allChannelStructureEntities)
        {           
            foreach (var structureEntity in structureEntitiesBatch.Where(x => x.EntityId != _config.ChannelId && x.Type != "Resource"))
            {
                Entity entity = RemoteManager.DataService.GetEntity(structureEntity.EntityId, LoadLevel.DataOnly);

                var distinctStructureEntities = GetDistinctStructureEntities(allChannelStructureEntities, entity);

                foreach (var distinctEntity in distinctStructureEntities)
                {
                    var linkType = _config.LinkTypes.FirstOrDefault(x => x.Id == distinctEntity.LinkTypeIdFromParent);

                    if (linkType == null)
                        continue;

                    if (linkType.SourceEntityTypeIsChannelNode())
                    {
                        AddChannelNodeRelation(linkType, distinctEntity, entity);
                    }
    
                    AddRelations(linkType, distinctEntity, structureEntity, entity);
                }
            }
        }

        private List<StructureEntity> GetDistinctStructureEntities(List<StructureEntity> allChannelStructureEntities, Entity entity)
        {
            var distinctStructureEntities = new List<StructureEntity>();

            foreach (StructureEntity se in allChannelStructureEntities.FindAll(i => i.EntityId == entity.Id))
            {
                if (!distinctStructureEntities.Any(i => i.EntityId == se.EntityId && i.ParentId == se.ParentId))
                {
                    distinctStructureEntities.Add(se);
                }
                else
                {
                    if (!se.LinkEntityId.HasValue)
                        continue;

                    if (!distinctStructureEntities.Any(x => x.EntityId == se.EntityId && 
                                                            x.ParentId == se.ParentId && 
                                                            x.LinkEntityId != null &&
                                                            x.LinkEntityId == se.LinkEntityId))
                    {
                        distinctStructureEntities.Add(se);
                    }
                }
            }
            return distinctStructureEntities;
        }

        private void AddNodeElements(List<StructureEntity> batch)
        {
            var nodeStructureEntities = batch.Where(x => x.IsChannelNode() && x.EntityId != _config.ChannelId);

            foreach (var structureEntity in nodeStructureEntities)
            {
                var entity = RemoteManager.DataService.GetEntity(structureEntity.EntityId, LoadLevel.DataOnly);

                if (_config.ChannelId.Equals(structureEntity.ParentId))
                {
                    _epiApi.CheckAndMoveNodeIfNeeded(entity.Id);
                }

                IntegrationLogger.Write(LogLevel.Debug, $"Trying to add channelNode {entity.Id} to Nodes");
                var currentNodeCode = _catalogCodeGenerator.GetEpiserverCode(entity);

                XElement nodeElement = _epiElementContainer.Nodes.FirstOrDefault(e => e.Element("Code")?.Value == currentNodeCode);

                int linkIndex = structureEntity.SortOrder;

                if (nodeElement == null)
                {
                    nodeElement = _epiElementFactory.CreateNodeElement(entity, structureEntity.ParentId, linkIndex);
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
                    
                    var relationName = _catalogCodeGenerator.GetRelationName(entity.Id, structureEntity.ParentId);

                    if (_epiElementContainer.HasRelation(relationName))
                        continue;

                    var nodeRelationElement = _epiElementFactory.CreateNodeRelation(structureEntity.ParentId, entity.Id, linkIndex);

                    _epiElementContainer.AddRelation(nodeRelationElement, relationName);

                    IntegrationLogger.Write(LogLevel.Debug, $"Adding relation to channelNode {entity.Id}");
                }
            }
        }

        private void AddChannelNodeRelation(LinkType linkType,
                                            StructureEntity distinctStructureEntity, 
                                            Entity entity)
        {
            var addedRelationName = _catalogCodeGenerator.GetRelationName(entity.Id, distinctStructureEntity.ParentId);
            if (_epiElementContainer.HasRelation(addedRelationName))
                return;

            var relationElement = _epiElementFactory.CreateNodeEntryRelation(distinctStructureEntity.ParentId, distinctStructureEntity.EntityId, distinctStructureEntity.SortOrder);

            _epiElementContainer.AddRelation(relationElement, addedRelationName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added Relation for Source {distinctStructureEntity.ParentId} and Target {distinctStructureEntity.EntityId} for LinkTypeId {linkType.Id}");
        }

        private void AddRelations(LinkType linkType,
                                  StructureEntity distinctStructureEntity, 
                                  StructureEntity structureEntity,
                                  Entity entity)
        {
            List<string> skus = new List<string> { _catalogCodeGenerator.GetEpiserverCode(entity.Id) };

            var parentId = structureEntity.EntityId;

            if (structureEntity.IsItem() && _config.ItemsToSkus)
            {
                skus = _epiElementFactory.SkuItemIds(entity);
                for (var i = 0; i < skus.Count; i++)
                {
                    skus[i] = _catalogCodeGenerator.GetPrefixedCode(skus[i]);
                }

                if (_config.UseThreeLevelsInCommerce)
                {
                    skus.Add(_catalogCodeGenerator.GetEpiserverCode(parentId));
                }
            }
            
            foreach (var skuId in skus)
            {
                if (_epiMappingHelper.IsRelation(linkType))
                {
                    AddNodeEntryRelationElement(linkType, distinctStructureEntity, skuId);
                    AddEntryRelationElement(structureEntity, skuId, linkType);
                }
                else
                {
                    if (_config.ForceIncludeLinkedContent)
                    {
                        AddMissingParentRelation(structureEntity, skuId);
                    }
                    AddAssociationElements(linkType, distinctStructureEntity, structureEntity, skuId);
                }
            }
        }

        /// <summary>
        /// Items included only as upsell/accessories etc might not have their parent products/bundles in the channel. 
        /// Add them if you're force including linked content.
        /// </summary>
        private void AddMissingParentRelation(StructureEntity structureEntity,
                                              string skuId)
        {
            var parentProduct = _channelHelper.GetParentProduct(structureEntity);
            if (parentProduct == null)
                return;

            var parentCode = _catalogCodeGenerator.GetEpiserverCode(parentProduct);
            var hasParent = _epiElementContainer.Entries.Any(x => x.Element("Code").Value == parentCode);

            if (!hasParent)
            {
                IntegrationLogger.Write(LogLevel.Debug, $"Could not find parent for {skuId}, adding it to the list. Parent is {parentCode}.");
                var missingParent = _epiElementFactory.InRiverEntityToEpiEntry(parentProduct);
                _epiElementContainer.Entries.Add(missingParent);
            }

            AddEntryRelationElement(structureEntity, skuId, new LinkType());
        }

        private void AddEntryRelationElement(StructureEntity structureEntity, 
                                             string skuCode, 
                                             LinkType linkType)
        {
            var parentProduct = _channelHelper.GetParentProduct(structureEntity);
            if (parentProduct == null)
                return;

            var parentCode = _catalogCodeGenerator.GetEpiserverCode(parentProduct);
            
            if (skuCode == parentCode)
                return;

            var addedRelationsName = "EntryRelation_" + _catalogCodeGenerator.GetRelationName(skuCode, parentCode);

            if (_epiElementContainer.HasRelation(addedRelationsName))
                return;

            var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(parentCode, linkType.SourceEntityTypeId, skuCode, structureEntity.SortOrder);

            _epiElementContainer.AddRelation(entryRelationElement, addedRelationsName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added EntryRelation for {skuCode} to product {parentCode}. Relation name: {addedRelationsName}.");

        }

        private void AddNodeEntryRelationElement(LinkType linkType,
                                                 StructureEntity distinctStructureEntity,
                                                 string skuCode)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"For SKU {skuCode}: Found relation between {linkType.SourceEntityTypeId} and {linkType.TargetEntityTypeId} called {linkType.Id}");

            var parentNode = _channelHelper.GetParentChannelNode(distinctStructureEntity); // TODO: Kan en sånn sjekk hindre Entries uten tilhørighet noe sted å bli med også?
            if (parentNode == null)
                return;

            var parentCode = _catalogCodeGenerator.GetEpiserverCode(parentNode);
            var relationName = "NodeEntryRelation_" + _catalogCodeGenerator.GetRelationName(skuCode, parentCode);

            if (_epiElementContainer.HasRelation(relationName))
                return;

            var relationElement = _epiElementFactory.CreateNodeEntryRelation(parentCode, skuCode, distinctStructureEntity.SortOrder);

            _epiElementContainer.AddRelation(relationElement, relationName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added NodeEntryRelation for EntryCode {skuCode}. Relation name: {relationName}.");
        }

        private void AddAssociationElements(LinkType linkType, 
                                            StructureEntity distinctStructureEntity,
                                            StructureEntity structureEntity, 
                                            string skuId)
        {
            if (!IsAssociationLinkType(linkType))
                return;

            Entity linkEntity = null;

            if (distinctStructureEntity.LinkEntityId != null)
            {
                linkEntity = RemoteManager.DataService.GetEntity(distinctStructureEntity.LinkEntityId.Value, LoadLevel.DataOnly);
            }
            
            if (!_config.UseThreeLevelsInCommerce && _config.ItemsToSkus && structureEntity.IsItem())
            {
                AddItemToSkusAssociations(linkType, distinctStructureEntity, skuId, linkEntity);
            }
            else
            {
                AddNormalAssociations(linkType, distinctStructureEntity, linkEntity);
            }
        }

        private bool IsAssociationLinkType(LinkType linkType)
        {
            return _config.AssociationLinkTypes.Any(x => x.Id == linkType.Id);
        }

        private void AddNormalAssociations(LinkType linkType, 
                                           StructureEntity distinctStructureEntity,
                                           Entity linkEntity)
        {
            var entityCode = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.EntityId);
            var parentCode = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.ParentId);
            // TODO: Detta ække parenten vi egentlig er ute etter. Dette er parent-channel node, ikke parent i association-forholdet.
            // GetLink-ellernoe, og finn source/target der. Det blir riktig. Evt distinctStructureEntity.LinkEntityId ?

            string linkEntityId = string.Empty;
            
            if (distinctStructureEntity.LinkEntityId != null)
            {
                linkEntityId = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.LinkEntityId ?? 0);
            }
            
            string associationName = _epiMappingHelper.GetAssociationName(distinctStructureEntity, linkEntity);

            bool exists;
            if (distinctStructureEntity.LinkEntityId != null)
            {
                exists = _epiElementContainer.Associations.Any(
                    e =>
                    {
                        XElement entryCodeElement = e.Element("EntryCode");
                        XElement descriptionElement = e.Element("Description");
                        return descriptionElement != null && 
                               entryCodeElement != null &&
                               entryCodeElement.Value == parentCode && 
                               e.Elements("Association").Any(x => x.Element("EntryCode") != null && x.Element("EntryCode").Value == entityCode) && 
                               descriptionElement.Value.Equals(linkEntityId);
                    });
            }
            else
            {
                exists = _epiElementContainer.Associations.Any(
                    e =>
                    {
                        XElement entryCodeElement = e.Element("EntryCode");
                        return entryCodeElement != null && 
                               entryCodeElement.Value == parentCode && 
                               e.Elements("Association").Any(e2 => e2.Element("EntryCode") != null && e2.Element("EntryCode").Value == entityCode) && 
                               e.Elements("Association").Any(e3 => e3.Element("Type") != null && e3.Element("Type").Value == linkType.Id);
                    });
            }

            if (exists)
                return;

            XElement existingAssociation;

            if (distinctStructureEntity.LinkEntityId != null)
            {
                existingAssociation = _epiElementContainer.Associations.FirstOrDefault(
                    a =>
                    {
                        XElement nameElement = a.Element("Name");
                        XElement entryCodeElement = a.Element("EntryCode");
                        XElement descriptionElement = a.Element("Description");
                        return descriptionElement != null && 
                               entryCodeElement != null && 
                               nameElement != null &&
                               nameElement.Value == associationName && 
                               entryCodeElement.Value == parentCode && 
                               descriptionElement.Value == linkEntityId;
                    });
            }
            else
            {
                existingAssociation = _epiElementContainer.Associations.FirstOrDefault(
                    a =>
                    {
                        XElement nameElement = a.Element("Name");
                        XElement entryCodeElement = a.Element("EntryCode");
                        return entryCodeElement != null && nameElement != null &&
                               nameElement.Value.Equals(associationName) &&
                               entryCodeElement.Value.Equals(parentCode);
                    });
            }

            if (existingAssociation != null)
            {
                XElement newElement = _epiElementFactory.CreateAssociationElement(distinctStructureEntity);

                if (!existingAssociation.Descendants().Any(e => e.Name.LocalName == "EntryCode" && e.Value == entityCode))
                {
                    existingAssociation.Add(newElement);
                }
            }
            else
            {
                var associationElement = _epiElementFactory.CreateCatalogAssociationElement(distinctStructureEntity, linkEntity);
                _epiElementContainer.Associations.Add(associationElement);
            }
        }

        private void AddItemToSkusAssociations(LinkType linkType,
                                               StructureEntity distinctStructureEntity, 
                                               string skuId, 
                                               Entity linkEntity)
        {
            string linkEntityId = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.LinkEntityId ?? 0);
            string associationName = _epiMappingHelper.GetAssociationName(distinctStructureEntity, linkEntity);

            Entity source = RemoteManager.DataService.GetEntity(distinctStructureEntity.ParentId, LoadLevel.DataOnly);

            List<string> sourceSkuIds = _epiElementFactory.SkuItemIds(source);
            for (int i = 0; i < sourceSkuIds.Count; i++)
            {
                sourceSkuIds[i] = _catalogCodeGenerator.GetPrefixedCode(sourceSkuIds[i]);
            }

            foreach (var sourceSkuId in sourceSkuIds)
            {
                bool exists;
                if (distinctStructureEntity.LinkEntityId != null)
                {
                    exists = _epiElementContainer.Associations.Any(
                        e =>
                        {
                            XElement entryCode = e.Element("EntryCode");
                            XElement description = e.Element("Description");
                            return description != null && entryCode != null &&
                                   entryCode.Value.Equals(sourceSkuId) && e.Elements("Association").Any(
                                       e2 =>
                                       {
                                           XElement associatedEntryCode = e2.Element("EntryCode");
                                           return associatedEntryCode != null && associatedEntryCode.Value.Equals(sourceSkuId);
                                       }) && description.Value.Equals(linkEntityId);
                        });
                }
                else
                {
                    exists = _epiElementContainer.Associations.Any(
                        e =>
                        {
                            XElement entryCode = e.Element("EntryCode");
                            return entryCode != null && entryCode.Value.Equals(sourceSkuId) && e
                                       .Elements("Association").Any(
                                           e2 =>
                                           {
                                               XElement associatedEntryCode = e2.Element("EntryCode");
                                               return associatedEntryCode != null &&
                                                      associatedEntryCode.Value.Equals(sourceSkuId);
                                           }) && e.Elements("Association").Any(
                                       e3 =>
                                       {
                                           XElement typeElement = e3.Element("Type");
                                           return typeElement != null && typeElement.Value.Equals(linkType.Id);
                                       });
                        });
                }

                if (exists)
                    continue;

                XElement existingAssociation;

                if (distinctStructureEntity.LinkEntityId != null)
                {
                    existingAssociation = _epiElementContainer.Associations.FirstOrDefault(
                        a =>
                        {
                            XElement nameElement = a.Element("Name");
                            XElement entryCodeElement = a.Element("EntryCode");
                            XElement descriptionElement = a.Element("Description");

                            return descriptionElement != null &&
                                   entryCodeElement != null &&
                                   nameElement != null &&
                                   nameElement.Value == associationName &&
                                   entryCodeElement.Value == sourceSkuId &&
                                   descriptionElement.Value == linkEntityId;
                        });
                }
                else
                {
                    existingAssociation = _epiElementContainer.Associations.FirstOrDefault(
                        a =>
                        {
                            XElement nameElement = a.Element("Name");
                            XElement entryCodeElement = a.Element("EntryCode");
                            return entryCodeElement != null &&
                                   nameElement != null &&
                                   nameElement.Value == associationName &&
                                   entryCodeElement.Value == sourceSkuId;
                        });
                }

                XElement associationElement = new XElement("Association",
                    new XElement("EntryCode", skuId),
                    new XElement("SortOrder", distinctStructureEntity.SortOrder),
                    new XElement("Type", linkType.Id));

                if (existingAssociation != null)
                {
                    if (!existingAssociation.Descendants().Any(e => e.Name.LocalName == "EntryCode" && e.Value == skuId))
                    {
                        existingAssociation.Add(associationElement);
                    }
                }
                else
                {
                    string description = distinctStructureEntity.LinkEntityId == null ? linkType.Id : linkEntityId;
                    description = description ?? string.Empty;

                    XElement catalogAssociation = new XElement("CatalogAssociation",
                        new XElement("Name", associationName),
                        new XElement("Description", description),
                        new XElement("SortOrder", distinctStructureEntity.SortOrder),
                        new XElement("EntryCode", sourceSkuId),
                        associationElement);

                    _epiElementContainer.Associations.Add(catalogAssociation);
                }
            }
        }
    }
}
