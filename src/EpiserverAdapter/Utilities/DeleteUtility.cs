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
        private readonly DocumentFileHelper _documentFileHelper;
        private readonly IConfiguration _config;
        private readonly ResourceElementFactory _resourceElementFactory;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly ChannelHelper _channelHelper;

        public DeleteUtility(IConfiguration config, 
                             ResourceElementFactory resourceElementFactory, 
                             EpiElementFactory epiElementFactory, 
                             ChannelHelper channelHelper, 
                             EpiApi epiApi,
                             CatalogCodeGenerator catalogCodeGenerator,
                             DocumentFileHelper documentFileHelper)
        {
            _config = config;
            _resourceElementFactory = resourceElementFactory;
            _epiApi = epiApi;
            _catalogCodeGenerator = catalogCodeGenerator;
            _documentFileHelper = documentFileHelper;
            _epiElementFactory = epiElementFactory;
            _channelHelper = channelHelper;
        }

        public void Delete(Entity channelEntity, int parentEntityId, Entity deletedEntity, string linkTypeId, List<int> productParentIds = null)
        {
            string channelIdentifier = _channelHelper.GetChannelIdentifier(channelEntity);
            string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

            string resourceZipFile = string.Format("resource_{0}.zip", folderDateTime);

            if (RemoteManager.ChannelService.EntityExistsInChannel(channelEntity.Id, deletedEntity.Id))
            {
                var structureEntitiesToDelete = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelEntity.Id, deletedEntity.Id);

                Entity parentEnt = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);

                if (deletedEntity.EntityType.Id == "Resource")
                {
                    DeleteResource(deletedEntity, parentEnt, channelIdentifier, folderDateTime, resourceZipFile);
                }
                else
                {
                    DeleteEntityThatStillExistInChannel(channelEntity, deletedEntity, parentEnt, linkTypeId, structureEntitiesToDelete, folderDateTime);
                }
            }
            else
            {
                DeleteEntity(channelEntity, parentEntityId, deletedEntity, channelIdentifier, folderDateTime, productParentIds);
            }
        }


        private void DeleteEntityThatStillExistInChannel(Entity channelEntity, 
                                                         Entity deletedEntity, 
                                                         Entity parentEnt, 
                                                         string linkTypeId,
                                                         List<StructureEntity> structureEntitiesToDelete, 
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
                linkEntityIds = GetLinkEntitiesToRemove(channelEntity, deletedEntity, parentEnt, linkTypeId, linkEntityIds);
            }

            // Add the removed entity element together with all the underlying entity elements
            var updatedEntities = new List<KeyValuePair<string, int>> ();
            
            foreach (var structureEntityToDelete in structureEntitiesToDelete)
            {
                var isContainedAlready = updatedEntities.Any(x => x.Key == structureEntityToDelete.Type && x.Value == structureEntityToDelete.EntityId);
                if (!isContainedAlready)
                {
                    updatedEntities.Add(new KeyValuePair<string, int>(structureEntityToDelete.Type, structureEntityToDelete.EntityId));
                }

                var outboundLinksToUpdate = RemoteManager.DataService.GetOutboundLinksForEntity(structureEntityToDelete.EntityId);
                foreach (Link link in outboundLinksToUpdate)
                {
                    var linkContainedAlready = updatedEntities.Any(x => x.Key == link.Target.EntityType.Id && x.Value == link.Target.Id);

                    if (!linkContainedAlready)
                    {
                        updatedEntities.Add(new KeyValuePair<string, int>(link.Target.EntityType.Id, link.Target.Id));
                    }
                }
            }

            foreach (var updatedEntity in updatedEntities)
            {
                string updatedEntityType = updatedEntity.Key;
                int updatedEntityId = updatedEntity.Value;

                Dictionary<string, bool> shouldExsistInChannelNodes = _channelHelper.ShouldEntityExistInChannelNodes(updatedEntityId, channelNodes, channelEntity.Id);
                
                if (updatedEntityType == "Link")
                    continue;

                if (updatedEntityType == "Item" && _config.ItemsToSkus)
                {
                    var entityToDelete = RemoteManager.DataService.GetEntity(updatedEntityId, LoadLevel.DataOnly);

                    List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(entityToDelete);
                    foreach (XElement sku in skus)
                    {
                        XElement skuCode = sku.Element("Code");
                        if (skuCode != null && !entitiesToUpdate.ContainsKey(skuCode.Value))
                        {
                            entitiesToUpdate.Add(skuCode.Value, shouldExsistInChannelNodes);
                        }
                    }

                    if (!_config.UseThreeLevelsInCommerce)
                    {
                        continue;
                    }
                }

                var updatedEntityCode = _catalogCodeGenerator.GetEpiserverCode(updatedEntityId);
                if (!entitiesToUpdate.ContainsKey(updatedEntityCode))
                {
                    entitiesToUpdate.Add(updatedEntityCode, shouldExsistInChannelNodes);
                }
            }

            List<string> parents = new List<string>
            {
                _catalogCodeGenerator.GetEpiserverCode(parentEnt)
            };

            if (parentEnt.EntityType.Id == "Item" && _config.ItemsToSkus)
            {
                parents = _epiElementFactory.SkuItemIds(parentEnt);

                if (_config.UseThreeLevelsInCommerce)
                {
                    parents.Add(_catalogCodeGenerator.GetEpiserverCode(parentEnt));
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
                    _epiApi.UpdateEntryRelations(entityIdToUpdate.Key, channelEntity.Id, channelEntity, parentId, entityIdToUpdate.Value, linkTypeId, linkEntityIds);
                }

                updateXml.Root?.Add(new XElement("entry", entityIdToUpdate.Key));
            }

            string zippedfileName = _documentFileHelper.SaveAndZipDocument(channelEntity, updateXml, folderDateTime);

            IntegrationLogger.Write(LogLevel.Debug, "catalog saved");
            _epiApi.SendHttpPost(Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedfileName));
        }

        private List<string> GetLinkEntitiesToRemove(Entity channelEntity, Entity deletedEntity, Entity parentEnt, string linkTypeId, List<string> linkEntityIds)
        {
            var allEntitiesInChannel = _channelHelper.GetAllEntitiesInChannel(_config.ExportEnabledEntityTypes);

            List<StructureEntity> newEntityNodes = _channelHelper.FindEntitiesElementInStructure(allEntitiesInChannel, parentEnt.Id, deletedEntity.Id, linkTypeId);

            List<string> parentCodes = new List<string>();
            if (parentEnt.EntityType.Id == "Item" && _config.ItemsToSkus)
            {
                var prefixedSkuCodes = _epiElementFactory.SkuItemIds(parentEnt)
                    .Select(_catalogCodeGenerator.GetPrefixedCode);
                parentCodes.AddRange(prefixedSkuCodes);
            }

            parentCodes.Add(_catalogCodeGenerator.GetEpiserverCode(parentEnt));

            var targetCodes = new List<string>();
            if (deletedEntity.EntityType.Id == "Item" && _config.ItemsToSkus)
            {
                var prefixedSkuIds = _epiElementFactory.SkuItemIds(deletedEntity)
                    .Select(_catalogCodeGenerator.GetPrefixedCode);
                targetCodes.AddRange(prefixedSkuIds);
            }

            targetCodes.Add(_catalogCodeGenerator.GetEpiserverCode(deletedEntity));
            linkEntityIds = _epiApi.GetLinkEntityAssociationsForEntity(linkTypeId, channelEntity, parentCodes, targetCodes);

            linkEntityIds.RemoveAll(i => newEntityNodes.Any(n => i == _catalogCodeGenerator.GetEpiserverCode(n.ParentId)));
            return linkEntityIds;
        }

        private void DeleteResource(Entity targetEntity, Entity parentEnt, string channelIdentifier, string folderDateTime, string resourceZipFile)
        {
            XDocument doc = _resourceElementFactory.HandleResourceUnlink(targetEntity, parentEnt);

            _documentFileHelper.SaveDocument(channelIdentifier, doc, folderDateTime);
            IntegrationLogger.Write(LogLevel.Debug, "Resource update-xml saved!");

            var fileToZip = Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml");
            _documentFileHelper.ZipFile(fileToZip, resourceZipFile);
            
            IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

            var baseFilePpath = Path.Combine(_config.ResourcesRootPath, folderDateTime);

            _epiApi.ImportResources(fileToZip, baseFilePpath);
            _epiApi.SendHttpPost(Path.Combine(_config.ResourcesRootPath, folderDateTime, resourceZipFile));
        }

        private void DeleteEntity(Entity channelEntity, 
                                  int parentEntityId,
                                  Entity deletedEntity, 
                                  string channelIdentifier, 
                                  string folderDateTime,
                                  List<int> productParentIds = null)
        {
            XDocument deleteXml = new XDocument(new XElement("xml", new XAttribute("action", "deleted")));
            Entity parentEntity = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);

            List<XElement> parentElements = _channelHelper.GetParentXElements(parentEntity);
            foreach (var parentElement in parentElements)
            {
                deleteXml.Root?.Add(parentElement);
            }
            
            switch (deletedEntity.EntityType.Id)
            {
                case "Resource":
                    var resourceStillExistsInChannel = RemoteManager.ChannelService.EntityExistsInChannel(channelEntity.Id, deletedEntity.Id);
                    if (resourceStillExistsInChannel)
                        break;

                    var deletedResource = _catalogCodeGenerator.GetEpiserverCode(deletedEntity);

                    XDocument resDoc = _resourceElementFactory.HandleResourceDelete(deletedResource);

                    _documentFileHelper.SaveDocument(channelIdentifier, resDoc, folderDateTime);

                    string zipFileDelete = $"resourceDelete_{folderDateTime}{deletedEntity.Id}.zip";

                    var removeResourceFileToZip = Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml");
                    _documentFileHelper.ZipFile(removeResourceFileToZip, zipFileDelete);

                    var unlinkFileToZip = Path.Combine(_config.ResourcesRootPath, folderDateTime, "Resources.xml");
                    
                    Entity parentEnt = RemoteManager.DataService.GetEntity(parentEntityId, LoadLevel.DataOnly);
                    var unlinkDoc = _resourceElementFactory.HandleResourceUnlink(deletedEntity, parentEnt);

                    _documentFileHelper.SaveDocument(channelIdentifier, unlinkDoc, folderDateTime);
                    var zipFileUnlink = $"resourceUnlink_{folderDateTime}{deletedEntity.Id}.zip";

                    _documentFileHelper.ZipFile(unlinkFileToZip, zipFileUnlink);

                    IntegrationLogger.Write(LogLevel.Debug, "Resources saved! Starting automatic import!");

                    _epiApi.ImportResources(unlinkFileToZip, Path.Combine(_config.ResourcesRootPath, folderDateTime));
                    _epiApi.SendHttpPost(Path.Combine(_config.ResourcesRootPath, folderDateTime, zipFileUnlink));

                    _epiApi.ImportResources(removeResourceFileToZip, Path.Combine(_config.ResourcesRootPath, folderDateTime));
                    _epiApi.SendHttpPost(Path.Combine(_config.ResourcesRootPath, folderDateTime, zipFileDelete));
                    
                    break;
                case "Channel":
                    _epiApi.DeleteCatalog(deletedEntity.Id);
                    break;
                case "ChannelNode":
                    _epiApi.DeleteCatalogNode(deletedEntity.Id, channelEntity.Id);
                    deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCode(deletedEntity)));

                    foreach (Link link in deletedEntity.OutboundLinks)
                    {
                        Entity child = RemoteManager.DataService.GetEntity(link.Target.Id, LoadLevel.DataAndLinks);

                        Delete(channelEntity, deletedEntity.Id, child, link.LinkType.Id);
                    }
                    break;
                case "Item":
                    if ((_config.ItemsToSkus && _config.UseThreeLevelsInCommerce) || !_config.ItemsToSkus)
                    {
                        _epiApi.DeleteCatalogEntry(deletedEntity);

                        deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCode(deletedEntity)));
                    }

                    if (_config.ItemsToSkus)
                    {
                        var entitiesToDelete = new List<Entity>();

                        List<XElement> skus = _epiElementFactory.GenerateSkuItemElemetsFromItem(deletedEntity);

                        foreach (XElement sku in skus)
                        {
                            XElement skuCodElement = sku.Element("Code");
                            if (skuCodElement != null)
                            {
                                entitiesToDelete.Add(deletedEntity);
                            }
                        }

                        foreach (var entity in entitiesToDelete)
                        {
                            _epiApi.DeleteCatalogEntry(entity);

                            deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCode(entity)));
                        }
                    }

                    break;

                // default represents products, bundles, packages etc.
                default:
                    _epiApi.DeleteCatalogEntry(deletedEntity);
                    deleteXml.Root?.Add(new XElement("entry", _catalogCodeGenerator.GetEpiserverCode(deletedEntity)));
                    deletedEntity = RemoteManager.DataService.GetEntity(deletedEntity.Id, LoadLevel.DataAndLinks);

                    foreach (Link link in deletedEntity.OutboundLinks)
                    {
                        if (link.Target.EntityType.Id == "Product")
                        {
                            if (productParentIds != null && productParentIds.Contains(link.Target.Id))
                            {
                                IntegrationLogger.Write(LogLevel.Information, $"Entity with id {link.Target.Id} has already been deleted, skipping it to avoid problems.");
                                continue;
                            }

                            if (productParentIds == null)
                            {
                                productParentIds = new List<int>();
                            }

                            productParentIds.Add(deletedEntity.Id);
                        }

                        Entity child = RemoteManager.DataService.GetEntity(link.Target.Id, LoadLevel.DataAndLinks);

                        if(deletedEntity.EntityType.Id == "Product")
                            Delete(channelEntity, deletedEntity.Id, child, link.LinkType.Id, productParentIds);

                        else
                            Delete(channelEntity, parentEntityId, child, link.LinkType.Id);
                    }
                    break;
            }

            if (deleteXml.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "entry") == null)
                return;

            var zippedCatName = _documentFileHelper.SaveAndZipDocument(channelEntity, deleteXml, folderDateTime);
            IntegrationLogger.Write(LogLevel.Debug, "Catalog XML saved.");
            _epiApi.SendHttpPost(Path.Combine(_config.PublicationsRootPath, folderDateTime, zippedCatName));
        }
    }
}
