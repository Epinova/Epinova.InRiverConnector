using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
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
        private Configuration DeleteUtilConfig { get; set; }

        public DeleteUtility(Configuration deleteUtilConfig)
        {
            DeleteUtilConfig = deleteUtilConfig;
        }

        public void Delete(Entity channelEntity, int parentEntityId, Entity targetEntity, string linkTypeId, List<int> productParentIds = null)
        {
            string channelIdentifier = ChannelHelper.GetChannelIdentifier(channelEntity);
            string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");
            
            ChannelHelper.BuildEntityIdAndTypeDict(DeleteUtilConfig);

            if (!DeleteUtilConfig.ChannelEntities.ContainsKey(targetEntity.Id))
            {
                DeleteUtilConfig.ChannelEntities.Add(targetEntity.Id, targetEntity);
            }

            string resourceZipFile = string.Format("resource_{0}.zip", folderDateTime);

            if (RemoteManager.ChannelService.EntityExistsInChannel(channelEntity.Id, targetEntity.Id))
            {
                var existingEntities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(
                                                                            channelEntity.Id,
                                                                            targetEntity.Id);

                Entity parentEnt = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);

                if (!DeleteUtilConfig.ChannelEntities.ContainsKey(parentEnt.Id))
                {
                    DeleteUtilConfig.ChannelEntities.Add(parentEnt.Id, parentEnt);
                }

                if (targetEntity.EntityType.Id == "Resource")
                {
                    //DeleteResource
                    DeleteResource(
                        targetEntity,
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
                        targetEntity,
                        parentEnt,
                        linkTypeId,
                        existingEntities,
                        channelIdentifier,
                        folderDateTime,
                        resourceZipFile);

                }
            }
            else
            {
                //DeleteEntity
                DeleteEntity(
                    channelEntity,
                    parentEntityId,
                    targetEntity,
                    linkTypeId,
                    channelIdentifier,
                    folderDateTime,
                    productParentIds);
            }
        }

        private void DeleteEntityThatStillExistInChannel(Entity channelEntity, Entity targetEntity, Entity parentEnt, string linkTypeId, List<StructureEntity> existingEntities, string channelIdentifier, string folderDateTime, string resourceZipFile)
        {
            Dictionary<string, Dictionary<string, bool>> entitiesToUpdate = new Dictionary<string, Dictionary<string, bool>>();

            var channelNodes = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(channelEntity.Id, "ChannelNode").ToList();

            if (!channelNodes.Any() && parentEnt.EntityType.Id == "Channel")
            {
                channelNodes.Add(RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelEntity.Id, parentEnt.Id).First());
            }

            List<string> linkEntityIds = new List<string>();
            if (ChannelHelper.LinkTypeHasLinkEntity(linkTypeId))
            {
                DeleteUtilConfig.ChannelStructureEntities = ChannelHelper.GetAllEntitiesInChannel(
                                                                    channelEntity.Id,
                                                                    Configuration.ExportEnabledEntityTypes);

                List<StructureEntity> newEntityNodes = ChannelHelper.FindEntitiesElementInStructure(DeleteUtilConfig.ChannelStructureEntities, parentEnt.Id, targetEntity.Id, linkTypeId);

                List<string> pars = new List<string>();
                if (parentEnt.EntityType.Id == "Item" && DeleteUtilConfig.ItemsToSkus)
                {
                    pars = EpiElement.SkuItemIds(parentEnt, DeleteUtilConfig);

                    if (DeleteUtilConfig.UseThreeLevelsInCommerce)
                    {
                        pars.Add(parentEnt.Id.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    pars.Add(parentEnt.Id.ToString(CultureInfo.InvariantCulture));
                }

                List<string> targets = new List<string>();
                if (targetEntity.EntityType.Id == "Item" && DeleteUtilConfig.ItemsToSkus)
                {
                    targets = EpiElement.SkuItemIds(targetEntity, DeleteUtilConfig);

                    if (DeleteUtilConfig.UseThreeLevelsInCommerce)
                    {
                        targets.Add(targetEntity.Id.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    targets.Add(targetEntity.Id.ToString(CultureInfo.InvariantCulture));
                }

                linkEntityIds = EpiApi.GetLinkEntityAssociationsForEntity(linkTypeId, channelEntity.Id, channelEntity, DeleteUtilConfig, pars, targets);

                linkEntityIds.RemoveAll(i => newEntityNodes.Any(n => i == ChannelPrefixHelper.GetEpiserverCode(n.ParentId, DeleteUtilConfig)));
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

                if (DeleteUtilConfig.ChannelEntities.ContainsKey(existingEntity.EntityId))
                {
                    foreach (Link outboundLinks in DeleteUtilConfig.ChannelEntities[existingEntity.EntityId].OutboundLinks)
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

                Dictionary<string, bool> shouldExsistInChannelNodes = ChannelHelper.ShouldEntityExistInChannelNodes(int.Parse(elementEntityId), channelNodes, channelEntity.Id);
                
                if (elementEntityType == "Link")
                {
                    continue;
                }

                if (elementEntityType == "Item" && DeleteUtilConfig.ItemsToSkus)
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
                        List<XElement> skus = EpiElement.GenerateSkuItemElemetsFromItem(deletedEntity, DeleteUtilConfig);
                        foreach (XElement sku in skus)
                        {
                            XElement skuCode = sku.Element("Code");
                            if (skuCode != null && !entitiesToUpdate.ContainsKey(skuCode.Value))
                            {
                                entitiesToUpdate.Add(skuCode.Value, shouldExsistInChannelNodes);
                            }
                        }
                    }

                    if (!DeleteUtilConfig.UseThreeLevelsInCommerce)
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
                if (DeleteUtilConfig.ItemsToSkus)
                {
                    parents = EpiElement.SkuItemIds(parentEnt, DeleteUtilConfig);

                    if (DeleteUtilConfig.UseThreeLevelsInCommerce)
                    {
                        parents.Add(parentEnt.Id.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            XDocument updateXml = new XDocument(new XElement("xml", new XAttribute("action", "updated")));
            if (updateXml.Root != null)
            {
                List<XElement> parentElements = ChannelHelper.GetParentXElements(parentEnt, DeleteUtilConfig);
                foreach (var parentElement in parentElements)
                {
                    updateXml.Root.Add(parentElement);
                }
            }

            foreach (KeyValuePair<string, Dictionary<string, bool>> entityIdToUpdate in entitiesToUpdate)
            {
                foreach (string parentId in parents)
                {
                    EpiApi.UpdateEntryRelations(entityIdToUpdate.Key, channelEntity.Id, channelEntity, DeleteUtilConfig, parentId, entityIdToUpdate.Value, linkTypeId, linkEntityIds);
                }

                updateXml.Root?.Add(new XElement("entry", ChannelPrefixHelper.GetEpiserverCode(entityIdToUpdate.Key, DeleteUtilConfig)));
            }

            string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, updateXml, folderDateTime, DeleteUtilConfig);
            IntegrationLogger.Write(LogLevel.Debug, "catalog saved");
            EpiApi.SendHttpPost(DeleteUtilConfig, Path.Combine(DeleteUtilConfig.PublicationsRootPath, folderDateTime, zippedfileName));
        }

        private void DeleteResource(Entity targetEntity, Entity parentEnt, string channelIdentifier, string folderDateTime, string resourceZipFile)
        {
            XDocument doc = Resources.HandleResourceUnlink(targetEntity, parentEnt, DeleteUtilConfig);

            DocumentFileHelper.SaveDocument(channelIdentifier, doc, DeleteUtilConfig, folderDateTime);
            IntegrationLogger.Write(LogLevel.Debug, "Resource update-xml saved!");

            DocumentFileHelper.ZipFile(Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), resourceZipFile);
            if (DeleteUtilConfig.ActivePublicationMode.Equals(PublicationMode.Automatic))
            {
                IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

                if (EpiApi.StartAssetImportIntoEpiServerCommerce(Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime), DeleteUtilConfig))
                {
                    EpiApi.SendHttpPost(DeleteUtilConfig, Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime, resourceZipFile));
                }
            }
        }

        private void DeleteEntity(Entity channelEntity, int parentEntityId, Entity targetEntity, string linkTypeId, string channelIdentifier, string folderDateTime, List<int> productParentIds = null)
        {
            XElement removedElement = new XElement(targetEntity.EntityType.Id + "_" + targetEntity.Id);

            List<XElement> deletedElements = new List<XElement>();

            deletedElements.Add(removedElement);

            XDocument deleteXml = new XDocument(new XElement("xml", new XAttribute("action", "deleted")));
            Entity parentEntity = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);

            if (parentEntity != null && !DeleteUtilConfig.ChannelEntities.ContainsKey(parentEntity.Id))
            {
                DeleteUtilConfig.ChannelEntities.Add(parentEntity.Id, parentEntity);
            }

            List<XElement> parentElements = ChannelHelper.GetParentXElements(parentEntity, DeleteUtilConfig);
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
                        EpiApi.DeleteCatalog(deletedElementEntityId, DeleteUtilConfig);
                        deletedResources = ChannelHelper.GetResourceIds(deletedElement, DeleteUtilConfig);
                        break;
                    case "ChannelNode":
                        EpiApi.DeleteCatalogNode(deletedElementEntityId, channelEntity.Id, DeleteUtilConfig);

                        deleteXml.Root?.Add(new XElement("entry", ChannelPrefixHelper.GetEpiserverCode(deletedElementEntityId, DeleteUtilConfig)));

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

                        deletedResources = ChannelHelper.GetResourceIds(deletedElement, DeleteUtilConfig);
                        break;
                    case "Item":
                        deletedResources = ChannelHelper.GetResourceIds(deletedElement, DeleteUtilConfig);
                        if ((DeleteUtilConfig.ItemsToSkus && DeleteUtilConfig.UseThreeLevelsInCommerce) || !DeleteUtilConfig.ItemsToSkus)
                        {
                            EpiApi.DeleteCatalogEntry(deletedElementEntityId.ToString(CultureInfo.InvariantCulture), DeleteUtilConfig);

                            deleteXml.Root?.Add(new XElement("entry", ChannelPrefixHelper.GetEpiserverCode(deletedElementEntityId, DeleteUtilConfig)));
                        }

                        if (DeleteUtilConfig.ItemsToSkus)
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
                                List<XElement> skus = EpiElement.GenerateSkuItemElemetsFromItem(deletedEntity, DeleteUtilConfig);

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
                                EpiApi.DeleteCatalogEntry(entityIdToDelete, DeleteUtilConfig);

                                deleteXml.Root?.Add(new XElement("entry", ChannelPrefixHelper.GetEpiserverCode(entityIdToDelete, DeleteUtilConfig)));
                            }
                        }

                        break;
                    case "Resource":
                        deletedResources = new List<string> { ChannelPrefixHelper.GetEpiserverCode(deletedElementEntityId, DeleteUtilConfig) };
                        break;

                    case "Product":
                        EpiApi.DeleteCatalogEntry(deletedElementEntityId.ToString(CultureInfo.InvariantCulture), DeleteUtilConfig);
                        deletedResources = ChannelHelper.GetResourceIds(deletedElement, DeleteUtilConfig);

                        deleteXml.Root?.Add(new XElement("entry", ChannelPrefixHelper.GetEpiserverCode(deletedElementEntityId, DeleteUtilConfig)));

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

                        EpiApi.DeleteCatalogEntry(deletedElementEntityId.ToString(CultureInfo.InvariantCulture), DeleteUtilConfig);
                        deletedResources = ChannelHelper.GetResourceIds(deletedElement, DeleteUtilConfig);

                        deleteXml.Root?.Add(new XElement("entry", ChannelPrefixHelper.GetEpiserverCode(deletedElementEntityId, DeleteUtilConfig)));

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
                    string resourceIdWithoutPrefix = resourceId.Substring(DeleteUtilConfig.ChannelIdPrefix.Length);

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
                    XDocument resDoc = Resources.HandleResourceDelete(deletedResources);
                    string folderDateTime2 = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

                    DocumentFileHelper.SaveDocument(channelIdentifier, resDoc, DeleteUtilConfig, folderDateTime2);
                    string zipFileDelete = string.Format(
                        "resource_{0}{1}.zip",
                        folderDateTime2,
                        deletedElementEntityId);

                    DocumentFileHelper.ZipFile(
                        Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime2, "Resources.xml"),
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
                            var unlinkDoc = Resources.HandleResourceUnlink(resource, parentEnt, DeleteUtilConfig);

                            DocumentFileHelper.SaveDocument(channelIdentifier, unlinkDoc, DeleteUtilConfig, folderDateTime);
                            zipFileUnlink = string.Format("resource_{0}{1}.zip", folderDateTime, deletedElementEntityId);
                            DocumentFileHelper.ZipFile(Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), zipFileUnlink);
                            sendUnlinkResource = true;
                        }

                        IntegrationLogger.Write(LogLevel.Debug, "Resources saved!");

                        if (DeleteUtilConfig.ActivePublicationMode.Equals(PublicationMode.Automatic))
                        {
                            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

                            if (sendUnlinkResource && EpiApi.StartAssetImportIntoEpiServerCommerce(Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime), DeleteUtilConfig))
                            {
                                EpiApi.SendHttpPost(DeleteUtilConfig, Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime, zipFileUnlink));
                            }

                            if (EpiApi.StartAssetImportIntoEpiServerCommerce(Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime2, "Resources.xml"), Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime2), DeleteUtilConfig))
                            {
                                EpiApi.SendHttpPost(DeleteUtilConfig, Path.Combine(DeleteUtilConfig.ResourcesRootPath, folderDateTime2, zipFileDelete));
                            }
                        }
                    }
                }
            }

            if (deleteXml.Root != null && deleteXml.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "entry") != null)
            {
                string zippedCatName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, deleteXml, folderDateTime, DeleteUtilConfig);
                IntegrationLogger.Write(LogLevel.Debug, "catalog saved");
                EpiApi.SendHttpPost(DeleteUtilConfig, Path.Combine(DeleteUtilConfig.PublicationsRootPath, folderDateTime, zippedCatName));
            }
        }
    }
}
