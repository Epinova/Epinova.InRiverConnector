using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Utilities
{
    public class DeleteUtility
    {
        private readonly EpiApi _epiApi;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly Configuration _config;
        private readonly ResourceElementFactory _resourceElementFactory;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly ChannelHelper _channelHelper;

        public DeleteUtility(Configuration config, 
            ResourceElementFactory resourceElementFactory, 
            EpiElementFactory epiElementFactory, 
            ChannelHelper channelHelper, 
            EpiApi epiApi,
            CatalogCodeGenerator catalogCodeGenerator)
        {
            _config = config;
            _resourceElementFactory = resourceElementFactory;
            _epiApi = epiApi;
            _catalogCodeGenerator = catalogCodeGenerator;
            _epiElementFactory = epiElementFactory;
            _channelHelper = channelHelper;
        }

        public void Delete(Entity channelEntity, int parentEntityId, Entity deletedEntity, string linkTypeId, List<int> productParentIds = null)
        {
            string channelIdentifier = _channelHelper.GetChannelIdentifier(channelEntity);
            string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

            _channelHelper.BuildEntityIdAndTypeDict(new List<StructureEntity>());

            if (!_config.ChannelEntities.ContainsKey(deletedEntity.Id))
            {
                _config.ChannelEntities.Add(deletedEntity.Id, deletedEntity);
            }

            string resourceZipFile = string.Format("resource_{0}.zip", folderDateTime);

            if (RemoteManager.ChannelService.EntityExistsInChannel(channelEntity.Id, deletedEntity.Id))
            {
                var structureEntitiesToDelete = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelEntity.Id, deletedEntity.Id);

                Entity parentEnt = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);

                if (!_config.ChannelEntities.ContainsKey(parentEnt.Id))
                {
                    _config.ChannelEntities.Add(parentEnt.Id, parentEnt);
                }

                if (deletedEntity.EntityType.Id == "Resource")
                {
                    //DeleteResource
                    DeleteResource(
                        deletedEntity,
                        parentEnt,
                        channelIdentifier,
                        folderDateTime,
                        resourceZipFile);
                }
                else
                {
                    //DeleteEntityThatExistInChannel
                    DeleteEntityThatStillExistInChannel(
                        channelEntity,
                        deletedEntity,
                        parentEnt,
                        linkTypeId,
                        structureEntitiesToDelete,
                        channelIdentifier,
                        folderDateTime);
                }
            }
            else
            {
                //DeleteEntity
                DeleteEntity(
                    channelEntity,
                    parentEntityId,
                    deletedEntity,
                    linkTypeId,
                    channelIdentifier,
                    folderDateTime,
                    productParentIds);
            }
        }

        private void DeleteEntityThatStillExistInChannel(Entity channelEntity,
            Entity targetEntity, 
            Entity parentEnt, 
            string linkTypeId, 
            List<StructureEntity> existingEntities, 
            string channelIdentifier,
            string folderDateTime)
        {
            var entitiesToUpdate = new Dictionary<string, Dictionary<string, bool>>();

            var channelNodes = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(channelEntity.Id, "ChannelNode").ToList();

            if (!channelNodes.Any() && parentEnt.EntityType.Id == "Channel")
            {
                channelNodes.Add(RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelEntity.Id, parentEnt.Id).First());
            }

            List<string> linkEntityIds = new List<string>();
            if (_channelHelper.LinkTypeHasLinkEntity(linkTypeId))
            {
                var allEntitiesInChannel = _channelHelper.GetAllEntitiesInChannel(_config.ExportEnabledEntityTypes);

                List<StructureEntity> newEntityNodes = _channelHelper.FindEntitiesElementInStructure(allEntitiesInChannel, parentEnt.Id, targetEntity.Id, linkTypeId);

                List<string> pars = new List<string>();
                if (parentEnt.EntityType.Id == "Item" && _config.ItemsToSkus)
                {
                    pars = _epiElementFactory.SkuItemIds(parentEnt, _config);

                    if (_config.UseThreeLevelsInCommerce)
                    {
                        pars.Add(parentEnt.Id.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    pars.Add(parentEnt.Id.ToString(CultureInfo.InvariantCulture));
                }

                List<string> targets = new List<string>();
                if (targetEntity.EntityType.Id == "Item" && _config.ItemsToSkus)
                {
                    targets = _epiElementFactory.SkuItemIds(targetEntity, _config);

                    if (_config.UseThreeLevelsInCommerce)
                    {
                        targets.Add(targetEntity.Id.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    targets.Add(targetEntity.Id.ToString(CultureInfo.InvariantCulture));
                }

                linkEntityIds = _epiApi.GetLinkEntityAssociationsForEntity(linkTypeId, channelEntity.Id, channelEntity, _config, pars, targets);

                linkEntityIds.RemoveAll(i => newEntityNodes.Any(n => i == _catalogCodeGenerator.GetEpiserverCode(n.ParentId)));
            }

            // Add the removed entity element together with all the underlying entity elements
            List<XElement> elementList = new List<XElement>();
            foreach (StructureEntity existingEntity in existingEntities)
            {
                XElement copyOfElement = new XElement(existingEntity.Type + "_" + existingEntity.EntityId);
                if (elementList.All(p => p.Name.LocalName != copyOfElement.Name.LocalName))
                {
                    elementList.Add(copyOfElement);
                }

                if (_config.ChannelEntities.ContainsKey(existingEntity.EntityId))
                {
                    foreach (Link outboundLinks in _config.ChannelEntities[existingEntity.EntityId].OutboundLinks)
                    {
                        XElement copyOfDescendant = new XElement(outboundLinks.Target.EntityType.Id + "_" + outboundLinks.Target.Id);
                        if (elementList.All(p => p.Name.LocalName != copyOfDescendant.Name.LocalName))
                        {
                            elementList.Add(copyOfDescendant);
                        }
                    }
                }
            }

            List<XElement> updatedElements = elementList;

            foreach (XElement element in updatedElements)
            {
                string elementEntityType = element.Name.LocalName.Split('_')[0];
                string elementEntityId = element.Name.LocalName.Split('_')[1];

                Dictionary<string, bool> shouldExsistInChannelNodes = _channelHelper.ShouldEntityExistInChannelNodes(int.Parse(elementEntityId), channelNodes, channelEntity.Id);
                
                if (elementEntityType == "Link")
                {
                    continue;
                }

                if (elementEntityType == "Item" && _config.ItemsToSkus)
                {
                    Entity deletedEntity = null;
                    
                    try
                    {
                        deletedEntity = RemoteManager.DataService.GetEntity(
                            int.Parse(elementEntityId),
                            LoadLevel.DataOnly);
                    }
                    catch (Exception ex)
                    {
                        IntegrationLogger.Write(LogLevel.Warning, "Error when getting entity:" + ex);
                    }

                    if (deletedEntity != null)
                    {
                        List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(deletedEntity, _config);
                        foreach (XElement sku in skus)
                        {
                            XElement skuCode = sku.Element("Code");
                            if (skuCode != null && !entitiesToUpdate.ContainsKey(skuCode.Value))
                            {
                                entitiesToUpdate.Add(skuCode.Value, shouldExsistInChannelNodes);
                            }
                        }
                    }

                    if (!_config.UseThreeLevelsInCommerce)
                    {
                        continue;
                    }
                }

                if (!entitiesToUpdate.ContainsKey(elementEntityId))
                {
                    entitiesToUpdate.Add(elementEntityId, shouldExsistInChannelNodes);
                }
            }

            List<string> parents = new List<string> { parentEnt.Id.ToString(CultureInfo.InvariantCulture) };
            if (parentEnt.EntityType.Id == "Item")
            {
                if (_config.ItemsToSkus)
                {
                    parents = _epiElementFactory.SkuItemIds(parentEnt, _config);

                    if (_config.UseThreeLevelsInCommerce)
                    {
                        parents.Add(parentEnt.Id.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            XDocument updateXml = new XDocument(new XElement("xml", new XAttribute("action", "updated")));
            if (updateXml.Root != null)
            {
                List<XElement> parentElements = _channelHelper.GetParentXElements(parentEnt);
                foreach (var parentElement in parentElements)
                {
                    updateXml.Root.Add(parentElement);
                }
            }

            foreach (KeyValuePair<string, Dictionary<string, bool>> entityIdToUpdate in entitiesToUpdate)
            {
                foreach (string parentId in parents)
                {
                    _epiApi.UpdateEntryRelations(entityIdToUpdate.Key, channelEntity.Id, channelEntity, _config, parentId, entityIdToUpdate.Value, linkTypeId, linkEntityIds);
                }

                updateXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(entityIdToUpdate.Key)));
            }

            string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, updateXml, folderDateTime, _config);
            IntegrationLogger.Write(LogLevel.Debug, "catalog saved");
            _epiApi.SendHttpPost(_config, Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedfileName));
        }

        private void DeleteResource(Entity targetEntity, 
                                    Entity parentEnt, 
                                    string channelIdentifier, 
                                    string folderDateTime, 
                                    string resourceZipFile)
        {
            XDocument doc = _resourceElementFactory.HandleResourceUnlink(targetEntity, parentEnt, _config);

            DocumentFileHelper.SaveDocument(channelIdentifier, doc, _config, folderDateTime);
            IntegrationLogger.Write(LogLevel.Debug, "Resource update-xml saved!");

            DocumentFileHelper.ZipFile(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"), resourceZipFile);
            
            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

            if (_epiApi.ImportResources(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(_config.ResourcesRootPath, folderDateTime), _config))
            {
                _epiApi.SendHttpPost(_config, Path.Combine(_config.ResourcesRootPath, folderDateTime, resourceZipFile));
            }
        }

        private void DeleteEntity(Entity channelEntity, 
            int parentEntityId, 
            Entity targetEntity, 
            string linkTypeId, 
            string channelIdentifier, 
            string folderDateTime,
            List<int> productParentIds = null)
        {
            XElement removedElement = new XElement(targetEntity.EntityType.Id + "_" + targetEntity.Id);

            List<XElement> deletedElements = new List<XElement>();

            deletedElements.Add(removedElement);

            XDocument deleteXml = new XDocument(new XElement("xml", new XAttribute("action", "deleted")));
            Entity parentEntity = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);

            if (parentEntity != null && !_config.ChannelEntities.ContainsKey(parentEntity.Id))
            {
                _config.ChannelEntities.Add(parentEntity.Id, parentEntity);
            }

            List<XElement> parentElements = _channelHelper.GetParentXElements(parentEntity);
            foreach (var parentElement in parentElements)
            {
                deleteXml.Root?.Add(parentElement);
            }

            deletedElements = deletedElements.GroupBy(elem => elem.Name.LocalName).Select(grp => grp.First()).ToList();

            foreach (XElement deletedElement in deletedElements)
            {
                if (!deletedElement.Name.LocalName.Contains('_'))
                {
                    continue;
                }

                string deletedElementEntityType = deletedElement.Name.LocalName.Split('_')[0];
                int deletedElementEntityId;
                int.TryParse(deletedElement.Name.LocalName.Split('_')[1], out deletedElementEntityId);

                if (deletedElementEntityType == "Link")
                {
                    continue;
                }

                List<string> deletedResources = new List<string>();

                switch (deletedElementEntityType)
                {
                    case "Channel":
                        _epiApi.DeleteCatalog(deletedElementEntityId, _config);
                        deletedResources = _channelHelper.GetResourceIds(deletedElement);
                        break;
                    case "ChannelNode":
                        _epiApi.DeleteCatalogNode(deletedElementEntityId, channelEntity.Id, _config);

                        deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(deletedElementEntityId)));

                        Entity channelNode = targetEntity.Id == deletedElementEntityId
                                                 ? targetEntity
                                                 : RemoteManager.DataService.GetEntity(
                                                     deletedElementEntityId,
                                                     LoadLevel.DataAndLinks);

                        if (channelNode == null)
                        {
                            break;
                        }

                        if (deletedElement.Elements().Any())
                        {
                            foreach (XElement linkElement in deletedElement.Elements())
                            {
                                foreach (XElement entityElement in linkElement.Elements())
                                {
                                    string elementEntityId = entityElement.Name.LocalName.Split('_')[1];

                                    Entity child = RemoteManager.DataService.GetEntity(int.Parse(elementEntityId), LoadLevel.DataAndLinks);
                                    Delete(channelEntity, targetEntity.Id, child, linkTypeId);
                                }
                            }
                        }
                        else
                        {
                            foreach (Link link in targetEntity.OutboundLinks)
                            {
                                Entity child = RemoteManager.DataService.GetEntity(link.Target.Id, LoadLevel.DataAndLinks);

                                Delete(channelEntity, targetEntity.Id, child, link.LinkType.Id);
                            }
                        }

                        deletedResources = _channelHelper.GetResourceIds(deletedElement);
                        break;
                    case "Item":
                        deletedResources = _channelHelper.GetResourceIds(deletedElement);
                        if ((_config.ItemsToSkus && _config.UseThreeLevelsInCommerce) || !_config.ItemsToSkus)
                        {
                            _epiApi.DeleteCatalogEntry(deletedElementEntityId.ToString(CultureInfo.InvariantCulture), _config);

                            deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(deletedElementEntityId)));
                        }

                        if (_config.ItemsToSkus)
                        {
                            // delete skus if exist
                            List<string> entitiesToDelete = new List<string>();

                            Entity deletedEntity = null;
                            
                            try
                            {
                                deletedEntity = RemoteManager.DataService.GetEntity(
                                    deletedElementEntityId,
                                    LoadLevel.DataOnly);
                            }
                            catch (Exception ex)
                            {
                                IntegrationLogger.Write(LogLevel.Warning, "Error when getting entity:" + ex);
                            }

                            if (deletedEntity != null)
                            {
                                List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(deletedEntity, _config);

                                foreach (XElement sku in skus)
                                {
                                    XElement skuCodElement = sku.Element("Code");
                                    if (skuCodElement != null)
                                    {
                                        entitiesToDelete.Add(skuCodElement.Value);
                                    }
                                }
                            }

                            foreach (string entityIdToDelete in entitiesToDelete)
                            {
                                _epiApi.DeleteCatalogEntry(entityIdToDelete, _config);

                                deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(entityIdToDelete)));
                            }
                        }

                        break;
                    case "Resource":
                        deletedResources = new List<string> { _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(deletedElementEntityId) };
                        break;

                    case "Product":
                        _epiApi.DeleteCatalogEntry(deletedElementEntityId.ToString(CultureInfo.InvariantCulture), _config);
                        deletedResources = _channelHelper.GetResourceIds(deletedElement);

                        deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(deletedElementEntityId)));

                        Entity delEntity = RemoteManager.DataService.GetEntity(
                            deletedElementEntityId,
                            LoadLevel.DataAndLinks);

                        if (delEntity == null)
                        {
                            break;
                        }

                        foreach (Link link in delEntity.OutboundLinks)
                        {
                            if (link.Target.EntityType.Id == "Product")
                            {
                                if (productParentIds != null && productParentIds.Contains(link.Target.Id))
                                {
                                    IntegrationLogger.Write(LogLevel.Information, string.Format("Entity with id {0} has already been deleted, break the chain to avoid circular relations behaviors (deadlocks)", link.Target.Id));
                                    continue;
                                }

                                if (productParentIds == null)
                                {
                                    productParentIds = new List<int>();
                                }

                                productParentIds.Add(delEntity.Id);
                            }

                            Entity child = RemoteManager.DataService.GetEntity(link.Target.Id, LoadLevel.DataAndLinks);

                            Delete(channelEntity, delEntity.Id, child, link.LinkType.Id, productParentIds);
                        }

                        break;
                    default:

                        _epiApi.DeleteCatalogEntry(deletedElementEntityId.ToString(CultureInfo.InvariantCulture), _config);
                        deletedResources = _channelHelper.GetResourceIds(deletedElement);

                        deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCodeLEGACYDAMNIT(deletedElementEntityId)));

                        Entity prodEntity;
                        if (targetEntity.Id == deletedElementEntityId)
                        {
                            prodEntity = targetEntity;
                        }
                        else
                        {
                            prodEntity = RemoteManager.DataService.GetEntity(
                                deletedElementEntityId,
                                LoadLevel.DataAndLinks);
                        }

                        if (prodEntity == null)
                        {
                            break;
                        }

                        foreach (Link link in prodEntity.OutboundLinks)
                        {
                            if (link.Target.EntityType.Id == "Product")
                            {
                                if (productParentIds != null && productParentIds.Contains(link.Target.Id))
                                {
                                    IntegrationLogger.Write(LogLevel.Information, string.Format("Entity with id {0} has already been deleted, break the chain to avoid circular relations behaviors (deadlocks)", link.Target.Id));
                                    continue;
                                }

                                if (productParentIds == null)
                                {
                                    productParentIds = new List<int>();
                                }

                                productParentIds.Add(prodEntity.Id);
                            }

                            Entity child = RemoteManager.DataService.GetEntity(link.Target.Id, LoadLevel.DataAndLinks);

                            Delete(channelEntity, parentEntityId, child, link.LinkType.Id);
                        }

                        break;
                }

                foreach (string resourceId in deletedResources)
                {
                    string resourceIdWithoutPrefix = resourceId.Substring(_config.ChannelIdPrefix.Length);

                    int resourceIdAsInt;

                    if (Int32.TryParse(resourceIdWithoutPrefix, out resourceIdAsInt))
                    {
                        if (RemoteManager.ChannelService.EntityExistsInChannel(channelEntity.Id, resourceIdAsInt))
                        {
                            deletedResources.Remove(resourceId);
                        }
                    }
                }
                
                if (deletedResources != null && deletedResources.Count != 0)
                {
                    XDocument resDoc = _resourceElementFactory.HandleResourceDelete(deletedResources);
                    string folderDateTime2 = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

                    DocumentFileHelper.SaveDocument(channelIdentifier, resDoc, _config, folderDateTime2);
                    string zipFileDelete = string.Format(
                        "resource_{0}{1}.zip",
                        folderDateTime2,
                        deletedElementEntityId);

                    DocumentFileHelper.ZipFile(
                        Path.Combine(_config.ResourcesRootPath, folderDateTime2, "Resources.xml"),
                        zipFileDelete);

                    foreach (string resourceIdString in deletedResources)
                    {
                        int resourceId = int.Parse(resourceIdString);
                        bool sendUnlinkResource = false;
                        string zipFileUnlink = string.Empty;
                        Entity resource = RemoteManager.DataService.GetEntity(resourceId, LoadLevel.DataOnly);
                        if (resource != null)
                        {
                            // Only do this when removing an link (unlink)
                            Entity parentEnt = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);
                            var unlinkDoc = _resourceElementFactory.HandleResourceUnlink(resource, parentEnt, _config);

                            DocumentFileHelper.SaveDocument(channelIdentifier, unlinkDoc, _config, folderDateTime);
                            zipFileUnlink = string.Format("resource_{0}{1}.zip", folderDateTime, deletedElementEntityId);
                            DocumentFileHelper.ZipFile(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"), zipFileUnlink);
                            sendUnlinkResource = true;
                        }

                        IntegrationLogger.Write(LogLevel.Debug, "Resources saved! Starting automatic import!");

                        if (sendUnlinkResource && _epiApi.ImportResources(Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(_config.ResourcesRootPath, folderDateTime), _config))
                        {
                            _epiApi.SendHttpPost(_config, Path.Combine(_config.ResourcesRootPath, folderDateTime, zipFileUnlink));
                        }

                        if (_epiApi.ImportResources(Path.Combine(_config.ResourcesRootPath, folderDateTime2, "Resources.xml"), Path.Combine(_config.ResourcesRootPath, folderDateTime2), _config))
                        {
                            _epiApi.SendHttpPost(_config, Path.Combine(_config.ResourcesRootPath, folderDateTime2, zipFileDelete));
                        }
                    }
                }
            }

            if (deleteXml.Root != null && deleteXml.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "entry") != null)
            {
                string zippedCatName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, deleteXml, folderDateTime, _config);
                IntegrationLogger.Write(LogLevel.Debug, "catalog saved");
                _epiApi.SendHttpPost(_config, Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedCatName));
            }
        }
    }
}
