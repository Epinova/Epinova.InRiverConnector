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
        private readonly Configuration _config;
        private readonly EpiApi _epiApi;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly EpiMappingHelper _epiMappingHelper;
        private readonly ChannelHelper _channelHelper;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;

        public EpiDocumentFactory(Configuration config, 
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
                                                     Dictionary<string, List<XElement>> epiElements)
        {
            XElement catalogElement = _epiElementFactory.CreateCatalogElement(channelEntity);
            if (catalogElement == null)
            {
                return null;
            }

            catalogElement.Add(
                new XElement("Sites", new XElement("Site", _channelHelper.GetChannelGuid(channelEntity).ToString())),
                new XElement("Nodes", new XAttribute("totalCount", epiElements["Nodes"].Count), epiElements["Nodes"]),
                new XElement("Entries", new XAttribute("totalCount", epiElements["Entries"].Count), epiElements["Entries"]),
                new XElement("Relations", new XAttribute("totalCount", epiElements["Relations"].Count), epiElements["Relations"].OrderByDescending(e => e.Name.LocalName)),
                new XElement("Associations", new XAttribute("totalCount", epiElements["Associations"].Count), epiElements["Associations"]));

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

        public Dictionary<string, List<XElement>> GetEPiElements(List<StructureEntity> channelStructureEntities)
        {
            var epiElements = new Dictionary<string, List<XElement>>
                              {
                                  { "Nodes", new List<XElement>() },
                                  { "Entries", new List<XElement>() },
                                  { "Relations", new List<XElement>() },
                                  { "Associations", new List<XElement>() }
                              };

            FillElementList(epiElements, channelStructureEntities);
            return epiElements;
        }

        public XElement GetAssociationTypes()
        {
            var associationTypeElements = _config.AssociationLinkTypes.Select(_epiElementFactory.CreateAssociationTypeElement);
            return new XElement("AssociationTypes", associationTypeElements);
        }
        
        private void FillElementList(Dictionary<string, List<XElement>> epiElements, List<StructureEntity> allChannelStructureEntities)
        {
            var addedEntities = new List<string>();
            var addedNodes = new List<string>();
            var addedRelations = new List<string>();

            int totalLoaded = 0;
            int batchSize = _config.BatchSize;
            var specifiactionEntries = allChannelStructureEntities.Where(x => x.Type == "Specification").ToList();
            IntegrationLogger.Write(LogLevel.Debug, $"Found {specifiactionEntries.Count} specifications in channel structure.");

            do
            {
                var batch = allChannelStructureEntities.Skip(totalLoaded).Take(batchSize).ToList();

                AddNodeElements(batch, addedNodes, addedRelations, epiElements);
                AddEntryElements(batch, addedEntities, epiElements, specifiactionEntries);
                AddRelationElements(batch, addedRelations, epiElements, allChannelStructureEntities);

                totalLoaded += batch.Count;

                IntegrationLogger.Write(LogLevel.Debug, $"Fetched {totalLoaded} of {allChannelStructureEntities.Count} total");
            }
            while (allChannelStructureEntities.Count > totalLoaded);
          
        }

        private void AddEntryElements(List<StructureEntity> batch, 
                                      List<string> addedEntities, 
                                      Dictionary<string, List<XElement>> epiElements,
                                      List<StructureEntity> specificationChannelStructureEntities)
        {
            foreach (var structureEntity in batch.Where(x => x.EntityId != _config.ChannelId))
            {
                if (structureEntity.LinkEntityId.HasValue)
                {
                    Entity linkEntity = RemoteManager.DataService.GetEntity(structureEntity.LinkEntityId.Value, LoadLevel.DataOnly);

                    XElement entryElement = _epiElementFactory.InRiverEntityToEpiEntry(linkEntity);

                    XElement codeElement = entryElement.Element("Code");
                    if (codeElement != null && !addedEntities.Contains(codeElement.Value))
                    {
                        epiElements["Entries"].Add(entryElement);
                        addedEntities.Add(codeElement.Value);
                        IntegrationLogger.Write(LogLevel.Debug, $"Added Entity {linkEntity.DisplayName} to Entries");
                    }
                }

                Entity entity = RemoteManager.DataService.GetEntity(structureEntity.EntityId, LoadLevel.DataOnly);

                if (structureEntity.IsItem() && _config.ItemsToSkus)
                {
                    List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(entity);
                    foreach (XElement sku in skus)
                    {
                        XElement codeElement = sku.Element("Code");
                        if (codeElement != null && !addedEntities.Contains(codeElement.Value))
                        {
                            epiElements["Entries"].Add(sku);
                            addedEntities.Add(codeElement.Value);

                            IntegrationLogger.Write(LogLevel.Debug, $"Added Item/SKU {sku.Name.LocalName} to Entries");
                        }
                    }
                }

                if ((structureEntity.IsItem() && _config.ItemsToSkus && _config.UseThreeLevelsInCommerce) || !(structureEntity.IsItem() && _config.ItemsToSkus))
                {
                    var element = _epiElementFactory.InRiverEntityToEpiEntry(entity);

                    var codeElement = element.Element("Code");
                    if (codeElement == null || addedEntities.Contains(codeElement.Value))
                        continue;

                    var specificationField = GetSpecificationMetaField(specificationChannelStructureEntities, entity.Id);

                    if (specificationField != null)
                    {
                        XElement metaFieldsElement = element.Descendants().FirstOrDefault(f => f.Name == "MetaFields");
                        metaFieldsElement?.Add(specificationField);
                    }

                    epiElements["Entries"].Add(element);
                    addedEntities.Add(codeElement.Value);

                    IntegrationLogger.Write(LogLevel.Debug, $"Added Entity {entity.Id} to Entries");
                }
            }
        }

        private XElement GetSpecificationMetaField(List<StructureEntity> allSpecificationEntries, int entityId)
        {
            var specificationEntry = allSpecificationEntries.FirstOrDefault(s => s.ParentId == entityId);

            if (specificationEntry == null)
                return null;
            
            IntegrationLogger.Write(LogLevel.Debug, $"Found specification for entity {entityId}. Creating MetaField element.");

            XElement specificationMetaField = new XElement("MetaField",
                new XElement("Name", "SpecificationField"),
                new XElement("Type", "LongHtmlString"));

            foreach (var culturePair in _config.LanguageMapping)
            {
                var htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specificationEntry.EntityId, entityId, culturePair.Value);
                specificationMetaField.Add(
                    new XElement("Data",
                        new XAttribute("language", culturePair.Key.Name.ToLower()),
                        new XAttribute("value", htmlData)));
            }

            return specificationMetaField;
        }

        private void AddRelationElements(List<StructureEntity> structureEntitiesBatch, 
                                         List<string> addedRelations,
                                         Dictionary<string, List<XElement>> epiElements,
                                         List<StructureEntity> allChannelStructureEntities)
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
                        AddChannelNodeRelation(addedRelations, epiElements, linkType, distinctEntity, entity);
                    }
    
                    AddRelations(addedRelations, epiElements, linkType, distinctEntity, structureEntity, entity);
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

        private void AddNodeElements(List<StructureEntity> batch, List<string> addedNodes, List<string> addedRelations, Dictionary<string, List<XElement>> epiElements)
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

                XElement nodeElement = epiElements["Nodes"].FirstOrDefault(e => e.Element("Code") != null && e.Element("Code").Value.Equals(currentNodeCode));

                int linkIndex = structureEntity.SortOrder;

                if (nodeElement == null)
                {
                    epiElements["Nodes"].Add(_epiElementFactory.CreateNodeElement(entity, structureEntity.ParentId, linkIndex));
                    addedNodes.Add(currentNodeCode);

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

                    if (!addedRelations.Contains(relationName))
                    {
                        var nodeRelationElement = _epiElementFactory.CreateNodeRelation(structureEntity.ParentId, entity.Id, linkIndex);

                        epiElements["Relations"].Add(nodeRelationElement);

                        addedRelations.Add(relationName);

                        IntegrationLogger.Write(LogLevel.Debug, $"Adding relation to channelNode {entity.Id}");
                    }
                }
            }
        }

        private void AddChannelNodeRelation(List<string> addedRelations,
                                            Dictionary<string, List<XElement>> epiElements,
                                            LinkType linkType,
                                            StructureEntity distinctStructureEntity, 
                                            Entity entity)
        {
            var addedRelationName = _catalogCodeGenerator.GetRelationName(entity.Id, distinctStructureEntity.ParentId);
            if (addedRelations.Contains(addedRelationName))
                return;

            var relationElement = _epiElementFactory.CreateNodeEntryRelation(distinctStructureEntity.ParentId, distinctStructureEntity.EntityId, distinctStructureEntity.SortOrder);

            epiElements["Relations"].Add(relationElement);

            addedRelations.Add(addedRelationName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added Relation for Source {distinctStructureEntity.ParentId} and Target {distinctStructureEntity.EntityId} for LinkTypeId {linkType.Id}");
        }

        private void AddRelations(List<string> addedRelations, 
                                  Dictionary<string, List<XElement>> epiElements, 
                                  LinkType linkType,
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
                    AddNodeEntryRelationElement(addedRelations, epiElements, linkType, distinctStructureEntity, skuId);
                    AddEntryRelationElement(structureEntity, skuId, addedRelations, epiElements, linkType);
                }
                else
                {
                    AddAssociationElements(epiElements, linkType, distinctStructureEntity, structureEntity, skuId);
                }
            }
        }

        private void AddEntryRelationElement(StructureEntity structureEntity, 
                                          string skuCode, 
                                          List<string> addedRelations,
                                          Dictionary<string, List<XElement>> epiElements,
                                          LinkType linkType)
        {
            var parentProduct = _channelHelper.GetParentProduct(structureEntity);
            var parentCode = _catalogCodeGenerator.GetEpiserverCode(parentProduct);
            
            if (skuCode == parentCode)
                return;

            var addedRelationsName = "EntryRelation_" + _catalogCodeGenerator.GetRelationName(skuCode, parentCode);

            if (addedRelations.Contains(addedRelationsName))
                return;

            var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(parentCode, linkType.SourceEntityTypeId, skuCode, structureEntity.SortOrder);

            epiElements["Relations"].Add(entryRelationElement);

            addedRelations.Add(addedRelationsName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added EntryRelation for {skuCode} to product {parentCode}. Relation name: {addedRelationsName}.");

        }

        private void AddNodeEntryRelationElement(List<string> addedRelations,
                                                 Dictionary<string, List<XElement>> epiElements,
                                                 LinkType linkType,
                                                 StructureEntity distinctStructureEntity,
                                                 string skuCode)
        {
            IntegrationLogger.Write(LogLevel.Debug, $"For SKU {skuCode}: Found relation between {linkType.SourceEntityTypeId} and {linkType.TargetEntityTypeId} called {linkType.Id}");

            var parentNode = _channelHelper.GetParentChannelNode(distinctStructureEntity); // TODO: Kan en sånn sjekk hindre Entries uten tilhørighet noe sted å bli med også?
            if (parentNode == null)
                return;

            var parentCode = _catalogCodeGenerator.GetEpiserverCode(parentNode);
            var relationName = "NodeEntryRelation_" + _catalogCodeGenerator.GetRelationName(skuCode, parentCode);

            if (addedRelations.Contains(relationName))
                return;

            var relationElement = _epiElementFactory.CreateNodeEntryRelation(parentCode, skuCode, distinctStructureEntity.SortOrder);
            epiElements["Relations"].Add(relationElement);

            addedRelations.Add(relationName);

            IntegrationLogger.Write(LogLevel.Debug, $"Added NodeEntryRelation for EntryCode {skuCode}. Relation name: {relationName}.");
        }

        private void AddAssociationElements(Dictionary<string, List<XElement>> epiElements, 
                                            LinkType linkType, 
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
                AddItemToSkusAssociations(epiElements, linkType, distinctStructureEntity, skuId, linkEntity);
            }
            else
            {
                AddNormalAssociations(epiElements, linkType, distinctStructureEntity, linkEntity);
            }
        }

        private bool IsAssociationLinkType(LinkType linkType)
        {
            return _config.AssociationLinkTypes.Any(x => x.Id == linkType.Id);
        }

        private void AddNormalAssociations(Dictionary<string, List<XElement>> epiElements, 
                                           LinkType linkType, 
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
                exists = epiElements["Associations"].Any(
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
                exists = epiElements["Associations"].Any(
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
                existingAssociation = epiElements["Associations"].FirstOrDefault(
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
                existingAssociation = epiElements["Associations"].FirstOrDefault(
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
                epiElements["Associations"].Add(associationElement);
            }
        }

        private void AddItemToSkusAssociations(Dictionary<string, List<XElement>> epiElements, 
                                               LinkType linkType,
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
                    exists = epiElements["Associations"].Any(
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
                    exists = epiElements["Associations"].Any(
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
                    existingAssociation = epiElements["Associations"].FirstOrDefault(
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
                    existingAssociation = epiElements["Associations"].FirstOrDefault(
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

                    epiElements["Associations"].Add(catalogAssociation);
                }
            }
        }
    }
}
