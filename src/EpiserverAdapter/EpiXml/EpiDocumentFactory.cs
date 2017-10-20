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
            XElement catalogElement = _epiElementFactory.CreateCatalogElement(channelEntity, _config);
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
                skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(updatedEntity, _config);
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

                updatedNode = _epiElementFactory.CreateNodeElement(updatedEntity, channelEntity.Id, sortOrder, _config);
            }
            else if (!(updatedEntity.EntityType.Id == "Item" && !_config.UseThreeLevelsInCommerce && _config.ItemsToSkus))
            {
                updatedEntry = _epiElementFactory.InRiverEntityToEpiEntry(updatedEntity, _config);
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

            XElement catalogElement = _epiElementFactory.CreateCatalogElement(channelEntity, _config);

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
        
        private void FillElementList(Dictionary<string, List<XElement>> epiElements, List<StructureEntity> channelStructureEntities)
        {
            List<string> addedEntities = new List<string>();
            List<string> addedNodes = new List<string>();
            List<string> addedRelations = new List<string>();

            int totalLoaded = 0;
            int batchSize = _config.BatchSize;

            do
            {
                var batch = channelStructureEntities.Skip(totalLoaded).Take(batchSize).ToList();
                Dictionary<int, Entity> channelEntities = GetEntitiesInStructure(batch);

                FillElements(batch, addedEntities, addedNodes, addedRelations, epiElements, channelStructureEntities, channelEntities);

                totalLoaded += batch.Count;

                IntegrationLogger.Write(LogLevel.Debug, $"fetched {totalLoaded} of {channelStructureEntities.Count} total");
            }
            while (channelStructureEntities.Count > totalLoaded);
          
        }

        private void FillElements(List<StructureEntity> structureEntitiesBatch, 
                                  List<string> addedEntities, 
                                  List<string> addedNodes,
                                  List<string> addedRelations,
                                  Dictionary<string, List<XElement>> epiElements,
                                  List<StructureEntity> channelStructureEntities,
                                  Dictionary<int, Entity> channelEntities)
        {
            int logCounter = 0;

            Dictionary<string, LinkType> linkTypes = new Dictionary<string, LinkType>();

            foreach (LinkType linkType in _config.LinkTypes)
            {
                if (!linkTypes.ContainsKey(linkType.Id))
                {
                    linkTypes.Add(linkType.Id, linkType);
                }
            }

            foreach (StructureEntity structureEntity in structureEntitiesBatch)
            {
                if (structureEntity.EntityId == _config.ChannelId)
                        continue;
                        
                    logCounter++;
    
                if (logCounter == 1000)
                {
                    logCounter = 0;
                    IntegrationLogger.Write(LogLevel.Debug, "Generating catalog xml.");
                }
    
                int entityId = structureEntity.EntityId;
    
                if (structureEntity.LinkEntityId.HasValue)
                {
                    // Add the link entity
    
                    Entity linkEntity = null;
    
                    if (channelEntities.ContainsKey(structureEntity.LinkEntityId.Value))
                    {
                        linkEntity = channelEntities[structureEntity.LinkEntityId.Value];
                    }
    
                    if (linkEntity == null)
                    {
                        IntegrationLogger.Write(
                            LogLevel.Warning,
                            string.Format(
                                "Link Entity with id {0} does not exist in system or ChannelStructure table is not in sync.",
                                (int)structureEntity.LinkEntityId));
                        continue;
                    }
    
                    XElement entryElement = _epiElementFactory.InRiverEntityToEpiEntry(linkEntity, _config);
    
                    XElement codeElement = entryElement.Element("Code");
                    if (codeElement != null && !addedEntities.Contains(codeElement.Value))
                    {
                        epiElements["Entries"].Add(entryElement);
                        addedEntities.Add(codeElement.Value);
    
                        IntegrationLogger.Write(LogLevel.Debug, string.Format("Added Entity {0} to Entries", linkEntity.DisplayName));
                    }
                }
    
                if (structureEntity.Type == "Resource")
                {
                    continue;
                }
    
                Entity entity;
    
                if (channelEntities.ContainsKey(entityId))
                {
                    entity = channelEntities[entityId];
                }
                else
                {
                    entity = RemoteManager.DataService.GetEntity(entityId, LoadLevel.DataOnly);

                    channelEntities.Add(entityId, entity);
                }
    
                if (entity == null)
                {   
                    channelEntities.Remove(entityId);
                    continue;
                }
    
                if (structureEntity.Type == "ChannelNode")
                {
                    var parentId = structureEntity.ParentId;
                    var parentCode = _catalogCodeGenerator.GetEpiserverCode(structureEntity.ParentId);
    
                    if (_config.ChannelId.Equals(structureEntity.ParentId))
                    {
                        _epiApi.CheckAndMoveNodeIfNeeded(entityId, _config);
                    }
    
                    IntegrationLogger.Write(LogLevel.Debug, $"Trying to add channelNode {entityId} to Nodes");
                        
                    XElement nodeElement = epiElements["Nodes"].Find(e =>
                    {
                        XElement xElement = e.Element("Code");
                        return xElement != null && xElement.Value.Equals(_catalogCodeGenerator.GetEpiserverCode(entity));
                    });
    
                    int linkIndex = structureEntity.SortOrder;
                        
                    if (nodeElement == null)
                    {
                        epiElements["Nodes"].Add(_epiElementFactory.CreateNodeElement(entity, parentId, linkIndex, _config));
                        addedNodes.Add(_catalogCodeGenerator.GetEpiserverCode(entity));
    
                        IntegrationLogger.Write(LogLevel.Debug, $"Added channelNode {entityId} to Nodes");
                    }
                    else
                    {
                        XElement parentNode = nodeElement.Element("ParentNode");
                        if (parentNode != null && 
                            parentNode.Value != _config.ChannelId.ToString(CultureInfo.InvariantCulture) && 
                            parentId == _config.ChannelId)
                        {
                            string oldParent = parentNode.Value;
                            parentNode.Value = _config.ChannelId.ToString(CultureInfo.InvariantCulture);
                            parentCode = oldParent;
    
                            XElement sortOrderElement = nodeElement.Element("SortOrder");
                            if (sortOrderElement != null)
                            {
                                string oldSortOrder = sortOrderElement.Value;
                                sortOrderElement.Value = linkIndex.ToString(CultureInfo.InvariantCulture);
                                linkIndex = int.Parse(oldSortOrder);
                            }
                        }


                        var relationName = _catalogCodeGenerator.GetRelationName(entityId, parentId);

                        if (!addedRelations.Contains(relationName))
                        {
                            var nodeRelationElement = _epiElementFactory.CreateNodeRelation(parentId, entityId, linkIndex);

                            epiElements["Relations"].Add(nodeRelationElement);
                                
                            addedRelations.Add(relationName);
    
                            IntegrationLogger.Write(LogLevel.Debug, string.Format("Adding relation to channelNode {0}", entityId));
                        }
                            
                    }
    
                    continue;
                }
    
                if (structureEntity.Type == "Item" && _config.ItemsToSkus)
                {
                    List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(entity, _config);
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
                    XElement element = _epiElementFactory.InRiverEntityToEpiEntry(entity, _config);
    
                    var specificationEntry = channelStructureEntities.FirstOrDefault(s => s.ParentId.Equals(entityId) && s.Type.Equals("Specification"));
    
                    if (specificationEntry != null)
                    {
                        XElement metaField = new XElement(
                            "MetaField",
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
    
                        IntegrationLogger.Write(LogLevel.Debug, string.Format("Added Entity {0} to Entries", entityId));
                    }
                }
    
                List<StructureEntity> existingStructureEntities = channelStructureEntities.FindAll(i => i.EntityId.Equals(entityId));
                List<StructureEntity> filteredStructureEntities = new List<StructureEntity>();
    
                foreach (StructureEntity se in existingStructureEntities)
                {
                    if (!filteredStructureEntities.Exists(i => i.EntityId == se.EntityId && i.ParentId == se.ParentId))
                    {
                        filteredStructureEntities.Add(se);
                    }
                    else
                    {
                        if (se.LinkEntityId.HasValue)
                        {
                            if (!filteredStructureEntities.Exists(
                                    i =>
                                    i.EntityId == se.EntityId && i.ParentId == se.ParentId
                                    && (i.LinkEntityId != null && i.LinkEntityId == se.LinkEntityId)))
                            {
                                filteredStructureEntities.Add(se);
                            }
                        }
                    }
                }
    
                foreach (StructureEntity existingStructureEntity in filteredStructureEntities)
                {
                    //Parent.
                    LinkType linkType = null;
    
                    if (linkTypes.ContainsKey(existingStructureEntity.LinkTypeIdFromParent))
                    {
                        linkType = linkTypes[existingStructureEntity.LinkTypeIdFromParent];
                    }
    
                    if (linkType == null)
                    {
                        continue;
                    }
    
                    if (linkType.SourceEntityTypeId == "ChannelNode")
                    {
                        var addedRelationName = _catalogCodeGenerator.GetRelationName(entityId, existingStructureEntity.ParentId);
                        if (!addedRelations.Contains(addedRelationName))
                        {
                            var relationElement = _epiElementFactory.CreateNodeEntryRelation(existingStructureEntity.ParentId, existingStructureEntity.EntityId, existingStructureEntity.SortOrder);

                            epiElements["Relations"].Add(relationElement);

                            addedRelations.Add(addedRelationName);
    
                            IntegrationLogger.Write(LogLevel.Debug, string.Format("Added Relation for Source {0} and Target {1} for LinkTypeId {2}", existingStructureEntity.ParentId, existingStructureEntity.EntityId, linkType.Id));
                        }
    
                        continue;
                    }
    
                    List<string> skus = new List<string> { entityId.ToString(CultureInfo.InvariantCulture) };
                    int parentId = structureEntity.EntityId;

                    if (structureEntity.Type.Equals("Item") && _config.ItemsToSkus)
                    {
                        skus = _epiElementFactory.SkuItemIds(entity, _config);
                        for (int i = 0; i < skus.Count; i++)
                        {
                            skus[i] = _catalogCodeGenerator.GetPrefixedCode(skus[i]);
                        }
    
                        if (_config.UseThreeLevelsInCommerce)                           
                            skus.Add(_catalogCodeGenerator.GetEpiserverCode(parentId));
                    }
    
                    Entity linkEntity = null;
    
                    if (existingStructureEntity.LinkEntityId != null)
                    {
                        if (channelEntities.ContainsKey(existingStructureEntity.LinkEntityId.Value))
                        {
                            linkEntity = channelEntities[existingStructureEntity.LinkEntityId.Value];
                        }
                        else
                        {
                            linkEntity = RemoteManager.DataService.GetEntity(
                            existingStructureEntity.LinkEntityId.Value,
                            LoadLevel.DataOnly);

                            channelEntities.Add(linkEntity.Id, linkEntity);
                        }
                    }
    
                    foreach (string skuId in skus)
                    {   
                        // prod -> item link, bundle, package or dynamic package => Relation
                        if (_epiMappingHelper.IsRelation(linkType.SourceEntityTypeId, linkType.TargetEntityTypeId, linkType.Index))
                        {
                            int parentNodeId = _channelHelper.GetParentChannelNode(structureEntity);
                            if (parentNodeId == 0)
                                continue;

                            var relationName = _catalogCodeGenerator.GetRelationName(skuId, parentNodeId);

                            if (!addedRelations.Contains(relationName))
                            {
                                epiElements["Relations"].Add(_epiElementFactory.CreateNodeEntryRelation(
                                        parentNodeId,
                                        skuId,
                                        existingStructureEntity.SortOrder));

                                addedRelations.Add(relationName);
    
                                IntegrationLogger.Write(LogLevel.Debug, $"Added Relation for EntryCode {skuId}");
                            }
    
                            var parentCode =_catalogCodeGenerator.GetEpiserverCode(existingStructureEntity.ParentId);
    
                            if (parentId != 0 && skuId != parentCode)
                            {
                                var addedRelationsName = _catalogCodeGenerator.GetRelationName(skuId, parentNodeId);

                                if (!addedRelations.Contains(addedRelationsName))
                                {
                                    var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(
                                                                    parentId,
                                                                    linkType.SourceEntityTypeId, 
                                                                    skuId, 
                                                                    existingStructureEntity.SortOrder);

                                    epiElements["Relations"].Add(entryRelationElement);
                                    addedRelations.Add(addedRelationsName);
    
                                    IntegrationLogger.Write(LogLevel.Debug,
                                        $"Added Relation for ChildEntryCode {skuId}");
                                }
                            }
                            else
                            {
                                var addedRelationsName = _catalogCodeGenerator.GetRelationName(skuId, parentId);

                                if (!addedRelations.Contains(addedRelationsName))
                                {
                                    var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(existingStructureEntity.ParentId,
                                        linkType.SourceEntityTypeId,
                                        skuId,
                                        existingStructureEntity.SortOrder);

                                    epiElements["Relations"].Add(entryRelationElement);
                                    addedRelations.Add(addedRelationsName);
    
                                    IntegrationLogger.Write(LogLevel.Debug, $"Added Relation for ChildEntryCode {skuId}");
                                }
                            }
                        }
                        else
                        {
                            if (!_config.UseThreeLevelsInCommerce && _config.ItemsToSkus && structureEntity.Type == "Item")
                            {
                                string linkEntityId = _catalogCodeGenerator.GetEpiserverCode(existingStructureEntity.LinkEntityId ?? 0);
                                string associationName = _epiMappingHelper.GetAssociationName(existingStructureEntity, linkEntity);
    
                                Entity source;
    
                                if (channelEntities.ContainsKey(existingStructureEntity.ParentId))
                                {
                                    source = channelEntities[existingStructureEntity.ParentId];
                                }
                                else
                                {
                                    source = RemoteManager.DataService.GetEntity(existingStructureEntity.ParentId, LoadLevel.DataOnly);
                                    channelEntities.Add(source.Id, source);
                                }
    
                                List<string> sourceSkuIds = _epiElementFactory.SkuItemIds(source, _config);
                                for (int i = 0; i < sourceSkuIds.Count; i++)
                                {
                                    sourceSkuIds[i] = _catalogCodeGenerator.GetPrefixedCode(sourceSkuIds[i]);
                                }
    
                                foreach (string sourceSkuId in sourceSkuIds)
                                {
                                    bool exists;
                                    if (existingStructureEntity.LinkEntityId != null)
                                    {
                                        exists = epiElements["Associations"].Any(
                                            e =>
                                                {
                                                    XElement entryCode = e.Element("EntryCode");
                                                    XElement description = e.Element("Description");
                                                    return description != null && entryCode != null && entryCode.Value.Equals(sourceSkuId) && e.Elements("Association").Any(
                                                        e2 =>
                                                            {
                                                                XElement associatedEntryCode =
                                                                    e2.Element("EntryCode");
                                                                return associatedEntryCode != null
                                                                        && associatedEntryCode.Value
                                                                                .Equals(sourceSkuId);
                                                            }) && description.Value.Equals(linkEntityId);
                                                });
                                    }
                                    else
                                    {
                                        exists = epiElements["Associations"].Any(
                                            e =>
                                                {
                                                    XElement entryCode = e.Element("EntryCode");
                                                    return entryCode != null && entryCode.Value.Equals(sourceSkuId) && e.Elements("Association").Any(
                                                        e2 =>
                                                            {
                                                                XElement associatedEntryCode = e2.Element("EntryCode");
                                                                return associatedEntryCode != null && associatedEntryCode.Value.Equals(sourceSkuId);
                                                            }) && e.Elements("Association").Any(
                                                                e3 =>
                                                                    {
                                                                        XElement typeElement = e3.Element("Type");
                                                                        return typeElement != null && typeElement.Value.Equals(linkType.Id);
                                                                    });
                                                });
                                    }
    
                                    if (!exists)
                                    {
                                        XElement existingAssociation;
    
                                        if (existingStructureEntity.LinkEntityId != null)
                                        {
                                            existingAssociation = epiElements["Associations"].FirstOrDefault(
                                                a =>
                                                    {
                                                        XElement nameElement = a.Element("Name");
                                                        XElement entryCodeElement = a.Element("EntryCode");
                                                        XElement descriptionElement = a.Element("Description");
                                                        return descriptionElement != null && entryCodeElement != null && nameElement != null && nameElement.Value.Equals(
                                                            associationName) && entryCodeElement.Value.Equals(sourceSkuId) && descriptionElement.Value.Equals(linkEntityId);
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
                                            new XElement("SortOrder", existingStructureEntity.SortOrder),
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
    
                                            string description = existingStructureEntity.LinkEntityId == null
                                                                        ? linkType.Id
                                                                        : linkEntityId;
                                            description = description ?? string.Empty;
    
                                            XElement catalogAssociation = new XElement(
                                                "CatalogAssociation",
                                                new XElement("Name", associationName),
                                                new XElement("Description", description),
                                                new XElement("SortOrder", existingStructureEntity.SortOrder),
                                                new XElement("EntryCode", sourceSkuId),
                                                associationElement);
    
                                            epiElements["Associations"].Add(catalogAssociation);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                var entityCode = _catalogCodeGenerator.GetEpiserverCode(existingStructureEntity.EntityId);
                                var parentCode = _catalogCodeGenerator.GetEpiserverCode(existingStructureEntity.ParentId);
    
                                string channelPrefixAndLinkEntityId = string.Empty;
    
                                if (existingStructureEntity.LinkEntityId != null)
                                {
                                    channelPrefixAndLinkEntityId = _catalogCodeGenerator.GetEpiserverCode(existingStructureEntity.LinkEntityId ?? 0);
                                }
    
                                string associationName = _epiMappingHelper.GetAssociationName(existingStructureEntity, linkEntity);
    
                                bool exists;
                                if (existingStructureEntity.LinkEntityId != null)
                                {
                                    exists = epiElements["Associations"].Any(
                                        e =>
                                            {
                                                XElement entryCodeElement = e.Element("EntryCode");
                                                XElement descriptionElement = e.Element("Description");
                                                return descriptionElement != null && entryCodeElement != null && entryCodeElement.Value.Equals(parentCode) && e.Elements("Association").Any(
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
                                                return entryCodeElement != null && entryCodeElement.Value.Equals(parentCode) && e.Elements("Association").Any(
                                                    e2 =>
                                                        {
                                                            XElement associatedEntryCode = e2.Element("EntryCode");
                                                            return associatedEntryCode != null && associatedEntryCode.Value.Equals(entityCode);
                                                        }) && e.Elements("Association").Any(
                                                            e3 =>
                                                                {
                                                                    XElement typeElement = e3.Element("Type");
                                                                    return typeElement != null && typeElement.Value.Equals(linkType.Id);
                                                                });
                                            });
                                }
    
                                if (!exists)
                                {
                                    XElement existingAssociation;
    
                                    if (existingStructureEntity.LinkEntityId != null)
                                    {
                                        existingAssociation = epiElements["Associations"].FirstOrDefault(
                                            a =>
                                                {
                                                    XElement nameElement = a.Element("Name");
                                                    XElement entryCodeElement = a.Element("EntryCode");
                                                    XElement descriptionElement = a.Element("Description");
                                                    return descriptionElement != null && entryCodeElement != null && nameElement != null && nameElement.Value.Equals(associationName) && entryCodeElement.Value.Equals(
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
                                                    return entryCodeElement != null && nameElement != null && nameElement.Value.Equals(associationName) && entryCodeElement.Value.Equals(parentCode);
                                                });
                                    }
    
                                    if (existingAssociation != null)
                                    {
                                        XElement newElement = _epiElementFactory.CreateAssociationElement(existingStructureEntity);
    
                                        if (!existingAssociation.Descendants().Any(
                                                        e =>
                                                        e.Name.LocalName == "EntryCode"
                                                        && e.Value == entityCode))
                                        {
                                            existingAssociation.Add(newElement);
                                        }
                                    }
                                    else
                                    {
                                        var associationElement = _epiElementFactory.CreateCatalogAssociationElement(existingStructureEntity, linkEntity);
                                        epiElements["Associations"].Add(associationElement);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private Dictionary<int, Entity> GetEntitiesInStructure(List<StructureEntity> batch)
        {
            Dictionary<int, Entity> channelEntites = new Dictionary<int, Entity>();
            
            foreach (EntityType entityType in _config.ExportEnabledEntityTypes)
            {
                List<StructureEntity> structureEntities = batch.FindAll(i => i.Type.Equals(entityType.Id));

                List<int> ids = structureEntities.Select(x => x.EntityId).Distinct().ToList();

                if (!ids.Any())
                {
                    continue;
                }

                List<Entity> entities = RemoteManager.DataService.GetEntities(ids, LoadLevel.DataOnly);

                foreach (Entity entity in entities)
                {
                    if (!channelEntites.ContainsKey(entity.Id))
                    {
                        channelEntites.Add(entity.Id, entity);
                    }
                }
            }

            List<int> linkEntityIds = (from channelEntity in batch
                                        where channelEntity.LinkEntityId.HasValue
                                        select channelEntity.LinkEntityId.Value).Distinct().ToList();


            List<Entity> linkEntities = RemoteManager.DataService.GetEntities(linkEntityIds, LoadLevel.DataOnly);

            foreach (Entity entity in linkEntities)
            {
                if (!channelEntites.ContainsKey(entity.Id))
                {
                    channelEntites.Add(entity.Id, entity);
                }
            }

            return channelEntites;
        }
    }
}
