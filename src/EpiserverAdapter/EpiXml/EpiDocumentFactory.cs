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
            var associationTypeElements = _config.ExportEnabledLinkTypes.Select(_epiElementFactory.CreateAssociationTypeElement);
            return new XElement("AssociationTypes", associationTypeElements);
        }
        
        private void FillElementList(Dictionary<string, List<XElement>> epiElements, List<StructureEntity> allChannelStructureEntities)
        {
            var addedEntities = new List<string>();
            var addedNodes = new List<string>();
            var addedRelations = new List<string>();

            int totalLoaded = 0;
            int batchSize = _config.BatchSize;

            do
            {
                var batch = allChannelStructureEntities.Skip(totalLoaded).Take(batchSize).ToList();

                AddNodeElements(batch, addedNodes, addedRelations, epiElements);
                AddEntryElements(batch, addedEntities, epiElements, allChannelStructureEntities.Where(x => x.Type == "Specification").ToList());
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

                if (structureEntity.Type == "Item" && _config.ItemsToSkus)
                {
                    List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(entity);
                    foreach (XElement sku in skus)
                    {
                        XElement codeElement = sku.Element("Code");
                        if (codeElement != null && !addedEntities.Contains(codeElement.Value))
                        {
                            epiElements["Entries"].Add(sku);
                            addedEntities.Add(codeElement.Value);

                            IntegrationLogger.Write(LogLevel.Debug, string.Format("Added Item/SKU {0} to Entries", sku.Name.LocalName));
                        }
                    }
                }

                if ((structureEntity.Type == "Item" && _config.ItemsToSkus && _config.UseThreeLevelsInCommerce)
                    || !(structureEntity.Type == "Item" && _config.ItemsToSkus))
                {
                    XElement element = _epiElementFactory.InRiverEntityToEpiEntry(entity);

                    var specificationEntry = specificationChannelStructureEntities.FirstOrDefault(s => s.ParentId == entity.Id);

                    if (specificationEntry != null)
                    {
                        XElement metaField = new XElement("MetaField",
                            new XElement("Name", "SpecificationField"),
                            new XElement("Type", "LongHtmlString"));

                        foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in _config.LanguageMapping)
                        {
                            string htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specificationEntry.EntityId, entity.Id, culturePair.Value);
                            metaField.Add(
                                new XElement("Data",
                                    new XAttribute("language", culturePair.Key.Name.ToLower()),
                                    new XAttribute("value", htmlData)));
                        }

                        XElement metaFieldsElement = element.Descendants().FirstOrDefault(f => f.Name == "MetaFields");
                        metaFieldsElement?.Add(metaField);
                    }

                    XElement codeElement = element.Element("Code");
                    if (codeElement != null && !addedEntities.Contains(codeElement.Value))
                    {
                        epiElements["Entries"].Add(element);
                        addedEntities.Add(codeElement.Value);

                        IntegrationLogger.Write(LogLevel.Debug, $"Added Entity {entity.Id} to Entries");
                    }
                }
            }
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
            List<StructureEntity> distinctStructureEntities = new List<StructureEntity>();

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

                    if (!distinctStructureEntities.Any(
                        x => x.EntityId == se.EntityId && x.ParentId == se.ParentId && x.LinkEntityId != null &&
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
            var nodeStructureEntities = batch.Where(x => x.Type == "ChannelNode" && x.EntityId != _config.ChannelId);

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

                        IntegrationLogger.Write(LogLevel.Debug, string.Format("Adding relation to channelNode {0}", entity.Id));
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

            var relationElement = _epiElementFactory.CreateNodeEntryRelation(distinctStructureEntity.ParentId,
                distinctStructureEntity.EntityId, distinctStructureEntity.SortOrder);

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

            int parentId = structureEntity.EntityId;

            if (structureEntity.IsItem() && _config.ItemsToSkus)
            {
                skus = _epiElementFactory.SkuItemIds(entity);
                for (int i = 0; i < skus.Count; i++)
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
                    AddRelationElements(addedRelations, epiElements, linkType, distinctStructureEntity, structureEntity, skuId, parentId);
                }
                else
                {
                    AddAssociationElements(epiElements, linkType, distinctStructureEntity, structureEntity, skuId);
                }
            }
        }

        private void AddAssociationElements(Dictionary<string, List<XElement>> epiElements, LinkType linkType, StructureEntity distinctStructureEntity,
            StructureEntity structureEntity, string skuId)
        {
            Entity linkEntity = null;

            if (distinctStructureEntity.LinkEntityId != null)
            {
                linkEntity = RemoteManager.DataService.GetEntity(distinctStructureEntity.LinkEntityId.Value,
                    LoadLevel.DataOnly);
            }

            if (!_config.UseThreeLevelsInCommerce && _config.ItemsToSkus && structureEntity.IsItem())
            {
                string linkEntityId = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.LinkEntityId ?? 0);
                string associationName = _epiMappingHelper.GetAssociationName(distinctStructureEntity, linkEntity);

                Entity source = RemoteManager.DataService.GetEntity(distinctStructureEntity.ParentId, LoadLevel.DataOnly);

                List<string> sourceSkuIds = _epiElementFactory.SkuItemIds(source);
                for (int i = 0; i < sourceSkuIds.Count; i++)
                {
                    sourceSkuIds[i] = _catalogCodeGenerator.GetPrefixedCode(sourceSkuIds[i]);
                }

                foreach (string sourceSkuId in sourceSkuIds)
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
                                               return associatedEntryCode != null &&
                                                      associatedEntryCode.Value.Equals(sourceSkuId);
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
                                return descriptionElement != null && entryCodeElement != null &&
                                       nameElement != null && nameElement.Value.Equals(
                                           associationName) && entryCodeElement.Value.Equals(sourceSkuId) &&
                                       descriptionElement.Value.Equals(linkEntityId);
                            });
                    }
                    else
                    {
                        existingAssociation = epiElements["Associations"].FirstOrDefault(
                            a =>
                            {
                                XElement nameElement = a.Element("Name");
                                XElement entryCodeElement = a.Element("EntryCode");
                                return entryCodeElement != null && nameElement != null && nameElement.Value.Equals(
                                           associationName) && entryCodeElement.Value.Equals(sourceSkuId);
                            });
                    }

                    XElement associationElement = new XElement(
                        "Association",
                        new XElement("EntryCode", skuId),
                        new XElement("SortOrder", distinctStructureEntity.SortOrder),
                        new XElement("Type", linkType.Id));

                    if (existingAssociation != null)
                    {
                        if (!existingAssociation.Descendants()
                            .Any(e => e.Name.LocalName == "EntryCode" && e.Value == skuId))
                        {
                            existingAssociation.Add(associationElement);
                        }
                    }
                    else
                    {
                        string description = distinctStructureEntity.LinkEntityId == null
                            ? linkType.Id
                            : linkEntityId;
                        description = description ?? string.Empty;

                        XElement catalogAssociation = new XElement(
                            "CatalogAssociation",
                            new XElement("Name", associationName),
                            new XElement("Description", description),
                            new XElement("SortOrder", distinctStructureEntity.SortOrder),
                            new XElement("EntryCode", sourceSkuId),
                            associationElement);

                        epiElements["Associations"].Add(catalogAssociation);
                    }
                }
            }
            else
            {
                var entityCode = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.EntityId);
                var parentCode = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.ParentId);

                string channelPrefixAndLinkEntityId = string.Empty;

                if (distinctStructureEntity.LinkEntityId != null)
                {
                    channelPrefixAndLinkEntityId =
                        _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.LinkEntityId ?? 0);
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
                            return descriptionElement != null && entryCodeElement != null &&
                                   entryCodeElement.Value.Equals(parentCode) && e.Elements("Association").Any(
                                       e2 =>
                                       {
                                           XElement associatedEntryCode = e2.Element("EntryCode");
                                           return associatedEntryCode != null && associatedEntryCode.Value.Equals(entityCode);
                                       }) && descriptionElement.Value.Equals(channelPrefixAndLinkEntityId);
                        });
                }
                else
                {
                    exists = epiElements["Associations"].Any(
                        e =>
                        {
                            XElement entryCodeElement = e.Element("EntryCode");
                            return entryCodeElement != null && entryCodeElement.Value.Equals(parentCode) && e
                                       .Elements("Association").Any(
                                           e2 =>
                                           {
                                               XElement associatedEntryCode = e2.Element("EntryCode");
                                               return associatedEntryCode != null &&
                                                      associatedEntryCode.Value.Equals(entityCode);
                                           }) && e.Elements("Association").Any(
                                       e3 =>
                                       {
                                           XElement typeElement = e3.Element("Type");
                                           return typeElement != null && typeElement.Value.Equals(linkType.Id);
                                       });
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
                            return descriptionElement != null && entryCodeElement != null && nameElement != null &&
                                   nameElement.Value.Equals(associationName) && entryCodeElement.Value.Equals(
                                       parentCode) && descriptionElement.Value.Equals(channelPrefixAndLinkEntityId);
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
                    var associationElement =
                        _epiElementFactory.CreateCatalogAssociationElement(distinctStructureEntity, linkEntity);
                    epiElements["Associations"].Add(associationElement);
                }
            }
        }

        private void AddRelationElements(List<string> addedRelations, Dictionary<string, List<XElement>> epiElements, LinkType linkType,
            StructureEntity distinctStructureEntity, StructureEntity structureEntity, string skuId, int parentId)
        {
            IntegrationLogger.Write(LogLevel.Debug,
                $"For SKU {skuId}: Found relation between {linkType.SourceEntityTypeId} and {linkType.TargetEntityTypeId} called {linkType.Id}");

            int parentNodeId = _channelHelper.GetParentChannelNode(structureEntity);
            if (parentNodeId == 0)
                return;

            var relationName = "Relation_" + _catalogCodeGenerator.GetRelationName(skuId, parentNodeId);

            if (!addedRelations.Contains(relationName))
            {
                epiElements["Relations"].Add(_epiElementFactory.CreateNodeEntryRelation(
                    parentNodeId,
                    skuId,
                    distinctStructureEntity.SortOrder));

                addedRelations.Add(relationName);

                IntegrationLogger.Write(LogLevel.Debug, $"Added Relation for EntryCode {skuId}");
            }

            var parentCode = _catalogCodeGenerator.GetEpiserverCode(distinctStructureEntity.ParentId);

            if (parentId != 0 && skuId != parentCode)
            {
                var addedRelationsName = "ChildEntryCode_" + _catalogCodeGenerator.GetRelationName(skuId, parentNodeId);

                if (!addedRelations.Contains(addedRelationsName))
                {
                    var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(
                        parentCode,
                        linkType.SourceEntityTypeId,
                        skuId,
                        distinctStructureEntity.SortOrder);

                    epiElements["Relations"].Add(entryRelationElement);
                    addedRelations.Add(addedRelationsName);

                    IntegrationLogger.Write(LogLevel.Debug, $"Added Relation for ChildEntryCode {skuId}");
                }
            }
            else
            {
                var addedRelationsName = "Relation_ChildEntryCode_" + _catalogCodeGenerator.GetRelationName(skuId, parentId);

                if (!addedRelations.Contains(addedRelationsName))
                {
                    var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(
                        distinctStructureEntity.ParentId,
                        linkType.SourceEntityTypeId,
                        skuId,
                        distinctStructureEntity.SortOrder);

                    epiElements["Relations"].Add(entryRelationElement);
                    addedRelations.Add(addedRelationsName);

                    IntegrationLogger.Write(LogLevel.Debug, $"Added Relation for ChildEntryCode {skuId}");
                }
            }
        }
    }
}
