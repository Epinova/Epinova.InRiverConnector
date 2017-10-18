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
        private readonly ChannelPrefixHelper _channelPrefixHelper;

        public EpiDocumentFactory(Configuration config, 
            EpiApi epiApi, 
            EpiElementFactory epiElementFactory, 
            EpiMappingHelper epiMappingHelper, 
            ChannelHelper channelHelper,
            ChannelPrefixHelper channelPrefixHelper)
        {
            _config = config;
            _epiApi = epiApi;
            _epiElementFactory = epiElementFactory;
            _epiMappingHelper = epiMappingHelper;
            _channelHelper = channelHelper;
            _channelPrefixHelper = channelPrefixHelper;
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
                string parentId = channelEntity.Id.ToString(CultureInfo.InvariantCulture);
                Link nodeLink = updatedEntity.Links.Find(l => l.Source.Id == channelEntity.Id);
                int sortOrder = 0;
                if (nodeLink != null)
                {
                    sortOrder = nodeLink.Index;
                }

                updatedNode = _epiElementFactory.CreateNodeElement(updatedEntity, parentId, sortOrder, _config);
            }
            else if (!(updatedEntity.EntityType.Id == "Item" && !_config.UseThreeLevelsInCommerce && _config.ItemsToSkus))
            {
                updatedEntry = _epiElementFactory.InRiverEntityToEpiEntry(updatedEntity, _config);
                Link specLink = updatedEntity.OutboundLinks.Find(l => l.Target.EntityType.Id == "Specification");
                if (specLink != null)
                {
                    XElement metaField = new XElement("MetaField", new XElement("Name", "SpecificationField"), new XElement("Type", "LongHtmlString"));
                    foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in _config.LanguageMapping)
                    {
                        string htmlData = RemoteManager.DataService.GetSpecificationAsHtml(specLink.Target.Id, updatedEntity.Id, culturePair.Value);
                        metaField.Add(new XElement("Data", new XAttribute("language", culturePair.Key.Name.ToLower()), new XAttribute("value", htmlData)));
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
                new XElement("Sites", new XElement("Site", _channelHelper.GetChannelGuid(channelEntity).ToString())),
                new XElement("Nodes", updatedNode),
                new XElement("Entries", new XAttribute("totalCount", count), updatedEntry, skus),
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

        public Dictionary<string, List<XElement>> GetEPiElements()
        {
            var epiElements = new Dictionary<string, List<XElement>>
                              {
                                  { "Nodes", new List<XElement>() },
                                  { "Entries", new List<XElement>() },
                                  { "Relations", new List<XElement>() },
                                  { "Associations", new List<XElement>() }
                              };

            FillElementList(epiElements);
            return epiElements;
        }

        public XElement GetAssociationTypes()
        {
            var associationTypeElements = _config.ExportEnabledLinkTypes.Select(_epiElementFactory.CreateAssociationTypeElement);
            return new XElement("AssociationTypes", associationTypeElements);
        }
        
        private void FillElementList(Dictionary<string, List<XElement>> epiElements)
        {
            try
            {
                List<string> addedEntities = new List<string>();
                List<string> addedNodes = new List<string>();
                List<string> addedRelations = new List<string>();

                int totalLoaded = 0;
                int batchSize = _config.BatchSize;

                do
                {
                    var batch = _config.ChannelStructureEntities.Skip(totalLoaded).Take(batchSize).ToList();

                    _config.ChannelEntities = GetEntitiesInStructure(batch);

                    FillElements(batch, addedEntities, addedNodes, addedRelations, epiElements);

                    totalLoaded += batch.Count;

                    IntegrationLogger.Write(LogLevel.Debug, string.Format("fetched {0} of {1} total", totalLoaded, _config.ChannelStructureEntities.Count));
                }
                while (_config.ChannelStructureEntities.Count > totalLoaded);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, ex.Message, ex);
            }
        }

        private void FillElements(List<StructureEntity> structureEntitiesBatch, 
                                  List<string> addedEntities, 
                                  List<string> addedNodes,
                                  List<string> addedRelations,
                                  Dictionary<string, 
                                  List<XElement>> epiElements)
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
                try
                {
                    if (structureEntity.EntityId == _config.ChannelId)
                        continue;
                        
                    logCounter++;
    
                    if (logCounter == 1000)
                    {
                        logCounter = 0;
                        IntegrationLogger.Write(LogLevel.Debug, "Generating catalog xml.");
                    }
    
                    int id = structureEntity.EntityId;
    
                    if (structureEntity.LinkEntityId.HasValue)
                    {
                        // Add the link entity
    
                        Entity linkEntity = null;
    
                        if (_config.ChannelEntities.ContainsKey(structureEntity.LinkEntityId.Value))
                        {
                            linkEntity = _config.ChannelEntities[structureEntity.LinkEntityId.Value];
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
    
                    if (_config.ChannelEntities.ContainsKey(id))
                    {
                        entity = _config.ChannelEntities[id];
                    }
                    else
                    {
                        entity = RemoteManager.DataService.GetEntity(id, LoadLevel.DataOnly);

                        _config.ChannelEntities.Add(id, entity);
                    }
    
                    if (entity == null)
                    {   
                        _config.ChannelEntities.Remove(id);
                        continue;
                    }
    
                    if (structureEntity.Type == "ChannelNode")
                    {
                        string parentId = structureEntity.ParentId.ToString(CultureInfo.InvariantCulture);
    
                        if (_config.ChannelId.Equals(structureEntity.ParentId))
                        {
                            _epiApi.CheckAndMoveNodeIfNeeded(id.ToString(CultureInfo.InvariantCulture), _config);
                        }
    
                        IntegrationLogger.Write(LogLevel.Debug, string.Format("Trying to add channelNode {0} to Nodes", id));
                        
                        XElement nodeElement = epiElements["Nodes"].Find(e =>
                        {
                            XElement xElement = e.Element("Code");
                            return xElement != null && xElement.Value.Equals(_channelPrefixHelper.GetEpiserverCode(entity.Id));
                        });
    
                        int linkIndex = structureEntity.SortOrder;
                        
                        if (nodeElement == null)
                        {
                            epiElements["Nodes"].Add(_epiElementFactory.CreateNodeElement(entity, parentId, linkIndex, _config));
                            addedNodes.Add(_channelPrefixHelper.GetEpiserverCode(entity.Id));
    
                            IntegrationLogger.Write(LogLevel.Debug, string.Format("Added channelNode {0} to Nodes", id));
                        }
                        else
                        {
                            XElement parentNode = nodeElement.Element("ParentNode");
                            if (parentNode != null && 
                                parentNode.Value != _config.ChannelId.ToString(CultureInfo.InvariantCulture) && 
                                parentId == _config.ChannelId.ToString(CultureInfo.InvariantCulture))
                            {
                                string oldParent = parentNode.Value;
                                parentNode.Value = _config.ChannelId.ToString(CultureInfo.InvariantCulture);
                                parentId = oldParent;
    
                                XElement sortOrderElement = nodeElement.Element("SortOrder");
                                if (sortOrderElement != null)
                                {
                                    string oldSortOrder = sortOrderElement.Value;
                                    sortOrderElement.Value = linkIndex.ToString(CultureInfo.InvariantCulture);
                                    linkIndex = int.Parse(oldSortOrder);
                                }
                            }

                            var relationName = _channelPrefixHelper.GetEpiserverCode(id.ToString(CultureInfo.InvariantCulture)) +
                                "_" + _channelPrefixHelper.GetEpiserverCode(parentId);

                            if (!addedRelations.Contains(relationName))
                            {
                                var nodeRelationElement = _epiElementFactory.CreateNodeRelationElement(
                                    parentId,
                                    id.ToString(CultureInfo.InvariantCulture),
                                    linkIndex,
                                    _config);

                                epiElements["Relations"].Add(nodeRelationElement);
                                
                                addedRelations.Add(relationName);
    
                                IntegrationLogger.Write(LogLevel.Debug, string.Format("Adding relation to channelNode {0}", id));
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
    
                    // TODO: SpecificationStructureEntity? Ta bort?
                    if ((structureEntity.Type == "Item" && _config.ItemsToSkus && _config.UseThreeLevelsInCommerce)
                        || !(structureEntity.Type == "Item" && _config.ItemsToSkus))
                    {
                        XElement element = _epiElementFactory.InRiverEntityToEpiEntry(entity, _config);
    
                        StructureEntity specificationStructureEntity =
                            _config.ChannelStructureEntities.FirstOrDefault(
                                s => s.ParentId.Equals(id) && s.Type.Equals("Specification"));
    
                        if (specificationStructureEntity != null)
                        {
                            XElement metaField = new XElement(
                                "MetaField",
                                new XElement("Name", "SpecificationField"),
                                new XElement("Type", "LongHtmlString"));
                            foreach (KeyValuePair<CultureInfo, CultureInfo> culturePair in _config.LanguageMapping)
                            {
                                string htmlData =
                                    RemoteManager.DataService.GetSpecificationAsHtml(
                                        specificationStructureEntity.EntityId,
                                        entity.Id,
                                        culturePair.Value);
                                metaField.Add(
                                    new XElement(
                                        "Data",
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
    
                            IntegrationLogger.Write(LogLevel.Debug, string.Format("Added Entity {0} to Entries", id));
                        }
                    }
    
                    List<StructureEntity> existingStructureEntities =
                        _config.ChannelStructureEntities.FindAll(i => i.EntityId.Equals(id));
    
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
                            var addedRelationName = _channelPrefixHelper.GetEpiserverCode(id) + "_" + _channelPrefixHelper.GetEpiserverCode(existingStructureEntity.ParentId);
                            if (!addedRelations.Contains(addedRelationName))
                            {
                                var nodeEntryRelationElement = _epiElementFactory.CreateNodeEntryRelationElement(existingStructureEntity.ParentId.ToString(), existingStructureEntity.EntityId.ToString(), existingStructureEntity.SortOrder, _config);

                                epiElements["Relations"].Add(nodeEntryRelationElement);

                                addedRelations.Add(addedRelationName);
    
                                IntegrationLogger.Write(LogLevel.Debug, string.Format("Added Relation for Source {0} and Target {1} for LinkTypeId {2}", existingStructureEntity.ParentId, existingStructureEntity.EntityId, linkType.Id));
                            }
    
                            continue;
                        }
    
                        List<string> skus = new List<string> { id.ToString(CultureInfo.InvariantCulture) };
                        string parent = null;
    
                        if (structureEntity.Type.Equals("Item") && _config.ItemsToSkus)
                        {
                            skus = _epiElementFactory.SkuItemIds(entity, _config);
                            for (int i = 0; i < skus.Count; i++)
                            {
                                skus[i] = _channelPrefixHelper.GetEpiserverCode(skus[i]);
                            }
    
                            if (_config.UseThreeLevelsInCommerce)
                            {
                                parent = structureEntity.EntityId.ToString(CultureInfo.InvariantCulture);
                                skus.Add(parent);
                            }
                        }
    
                        Entity linkEntity = null;
    
                        if (existingStructureEntity.LinkEntityId != null)
                        {
                            if (_config.ChannelEntities.ContainsKey(existingStructureEntity.LinkEntityId.Value))
                            {
                                linkEntity = _config.ChannelEntities[existingStructureEntity.LinkEntityId.Value];
                            }
                            else
                            {
                                linkEntity = RemoteManager.DataService.GetEntity(
                                existingStructureEntity.LinkEntityId.Value,
                                LoadLevel.DataOnly);

                                _config.ChannelEntities.Add(linkEntity.Id, linkEntity);
                            }
                        }
    
                        foreach (string skuId in skus)
                        {
                            string channelPrefixAndSkuId = _channelPrefixHelper.GetEpiserverCode(skuId);
    
                            // prod -> item link, bundle, package or dynamic package => Relation
                            if (_epiMappingHelper.IsRelation(linkType.SourceEntityTypeId, linkType.TargetEntityTypeId, linkType.Index))
                            {
                                int parentNodeId = _channelHelper.GetParentChannelNode(structureEntity);
                                if (parentNodeId == 0)
                                {
                                    continue;
                                }
    
                                string channelPrefixAndParentNodeId = _channelPrefixHelper.GetEpiserverCode(parentNodeId);
    
                                if (!addedRelations.Contains(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId))
                                {
                                    epiElements["Relations"].Add(
                                        _epiElementFactory.CreateNodeEntryRelationElement(
                                            parentNodeId.ToString(CultureInfo.InvariantCulture),
                                            skuId,
                                            existingStructureEntity.SortOrder,
                                            _config));
                                    addedRelations.Add(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId);
    
                                    IntegrationLogger.Write(
                                        LogLevel.Debug,
                                        string.Format("Added Relation for EntryCode {0}", channelPrefixAndSkuId));
                                }
    
                                string parentCode =_channelPrefixHelper.GetEpiserverCode(existingStructureEntity.ParentId.ToString(CultureInfo.InvariantCulture));
    
                                if (parent != null && skuId != parent)
                                {
                                    string channelPrefixAndParent = _channelPrefixHelper.GetEpiserverCode(parent);

                                    var addedRelationsName = channelPrefixAndSkuId + "_" + channelPrefixAndParent;

                                    if (!addedRelations.Contains(addedRelationsName))
                                    {
                                        var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(parent,
                                            linkType.SourceEntityTypeId, skuId, existingStructureEntity.SortOrder,
                                            _config);

                                        epiElements["Relations"].Add(entryRelationElement);

                                        addedRelations.Add(addedRelationsName);
    
                                        IntegrationLogger.Write(
                                            LogLevel.Debug,
                                            string.Format("Added Relation for ChildEntryCode {0}", channelPrefixAndSkuId));
                                    }
                                }
                                else
                                {
                                    var addedRelationsName = $"{channelPrefixAndSkuId}_{parentCode}";
                                    if (!addedRelations.Contains(addedRelationsName))
                                    {
                                        var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(existingStructureEntity.ParentId.ToString(CultureInfo.InvariantCulture), linkType.SourceEntityTypeId, skuId, existingStructureEntity.SortOrder, _config);

                                        epiElements["Relations"].Add(entryRelationElement);
                                        addedRelations.Add(addedRelationsName);
    
                                        IntegrationLogger.Write(
                                            LogLevel.Debug,
                                            string.Format("Added Relation for ChildEntryCode {0}", channelPrefixAndSkuId));
                                    }
                                }
                            }
                            else
                            {
                                if (!_config.UseThreeLevelsInCommerce && _config.ItemsToSkus && structureEntity.Type == "Item")
                                {
                                    string channelPrefixAndLinkEntityId = _channelPrefixHelper.GetEpiserverCode(existingStructureEntity.LinkEntityId);
                                    string associationName = _epiMappingHelper.GetAssociationName(existingStructureEntity, linkEntity);
    
                                    Entity source;
    
                                    if (_config.ChannelEntities.ContainsKey(existingStructureEntity.ParentId))
                                    {
                                        source = _config.ChannelEntities[existingStructureEntity.ParentId];
                                    }
                                    else
                                    {
                                        source = RemoteManager.DataService.GetEntity(
                                            existingStructureEntity.ParentId,
                                            LoadLevel.DataOnly);
                                        _config.ChannelEntities.Add(source.Id, source);
                                    }
    
                                    List<string> sourceSkuIds = _epiElementFactory.SkuItemIds(source, _config);
                                    for (int i = 0; i < sourceSkuIds.Count; i++)
                                    {
                                        sourceSkuIds[i] = _channelPrefixHelper.GetEpiserverCode(sourceSkuIds[i]);
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
                                                                }) && description.Value.Equals(channelPrefixAndLinkEntityId);
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
                                                                associationName) && entryCodeElement.Value.Equals(sourceSkuId) && descriptionElement.Value.Equals(channelPrefixAndLinkEntityId);
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
                                                                         : channelPrefixAndLinkEntityId;
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
                                    var entityCode = _channelPrefixHelper.GetEpiserverCode(existingStructureEntity.EntityId.ToString(CultureInfo.InvariantCulture));
                                    var parentCode = _channelPrefixHelper.GetEpiserverCode(existingStructureEntity.ParentId.ToString(CultureInfo.InvariantCulture));
    
                                    string channelPrefixAndLinkEntityId = string.Empty;
    
                                    if (existingStructureEntity.LinkEntityId != null)
                                    {
                                        channelPrefixAndLinkEntityId = _channelPrefixHelper.GetEpiserverCode(existingStructureEntity.LinkEntityId);
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
                                            epiElements["Associations"].Add(
                                                _epiElementFactory.CreateCatalogAssociationElement(
                                                    existingStructureEntity,
                                                    linkEntity));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }    
                catch (Exception ex)
                {
                    IntegrationLogger.Write(LogLevel.Error, ex.Message, ex);
                }
            }
        }

        private Dictionary<int, Entity> GetEntitiesInStructure(List<StructureEntity> batch)
        {
            Dictionary<int, Entity> channelEntites = new Dictionary<int, Entity>();

            try
            {
                foreach (EntityType entityType in _config.ExportEnabledEntityTypes)
                {
                    List<StructureEntity> structureEntities = batch.FindAll(i => i.Type.Equals(entityType.Id));

                    List<int> ids = structureEntities.Select(structureEntity => structureEntity.EntityId).Distinct().ToList();

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

            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "GetEntitiesInStructure ", ex);
            }


            return channelEntites;
        }
    }
}
