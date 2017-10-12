using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Http;
using EPiServer;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using inRiver.EPiServerCommerce.Importer.EventHandling;
using inRiver.EPiServerCommerce.Interfaces;
using Mediachase.Commerce.Catalog;
using Mediachase.Commerce.Catalog.Dto;
using Mediachase.Commerce.Catalog.Managers;
using Mediachase.Commerce.Catalog.Objects;

namespace inRiver.EPiServerCommerce.Importer
{
    public class CatalogImporter : ICatalogImporter
    {
        private readonly ILogger _logger;
        private readonly ReferenceConverter _referenceConverter;
        private readonly IContentRepository _contentRepository;

        public CatalogImporter(ILogger logger, ReferenceConverter referenceConverter, IContentRepository contentRepository)
        {
            _logger = logger;
            _referenceConverter = referenceConverter;
            _contentRepository = contentRepository;
        }

        private bool RunICatalogImportHandlers => GetBoolSetting("inRiver.RunICatalogImportHandlers");

        private bool RunIResourceImporterHandlers => GetBoolSetting("inRiver.RunIResourceImporterHandlers");

        private bool RunIDeleteActionsHandlers => GetBoolSetting("inRiver.RunIDeleteActionsHandlers");

        private bool RunIInRiverEventsHandlers => GetBoolSetting("inRiver.RunIInRiverEventsHandlers");

        private bool GetBoolSetting(string key)
        {
            var setting = ConfigurationManager.AppSettings[key];
            return setting != null && setting.Equals(key, StringComparison.CurrentCultureIgnoreCase);
        }

        public void DeleteCatalogEntry(string code)
        {
            List<IDeleteActionsHandler> deleteHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            var contentReference = _referenceConverter.GetContentLink(code);
            var entry = _contentRepository.Get<EntryContentBase>(contentReference);

            if (entry == null)
            {
                _logger.Warning($"Could not find catalog entry with id: {code}. No entry is deleted");
                return;
            }
            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in deleteHandlers)
                {
                    handler.PreDeleteCatalogEntry(entry);
                }
            }

            _contentRepository.Delete(entry.ContentLink, true);

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in deleteHandlers)
                {
                    handler.PostDeleteCatalogEntry(entry);
                }
            }
        }

        public void DeleteCatalog(int catalogId)
        {
            _logger.Debug("DeleteCatalog");
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteCatalog(catalogId);
                }
            }

            CatalogContext.Current.DeleteCatalog(catalogId);
          
            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalog(catalogId);
                }
            }
        }

        public void DeleteCatalogNode(string catalogNodeId)
        {
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();
            CatalogNode cn = CatalogContext.Current.GetCatalogNode(catalogNodeId);

            if (cn == null || cn.CatalogNodeId == 0)
            {
                _logger.Error($"Could not find catalog node with id: {catalogNodeId}. No node is deleted");
                return;
            }

            var catalogId = cn.CatalogId;
            var nodeId = cn.CatalogNodeId;

            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteCatalogNode(nodeId, catalogId);
                }
            }

            CatalogContext.Current.DeleteCatalogNode(cn.CatalogNodeId, cn.CatalogId);
            
            if (RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalogNode(nodeId, catalogId);
                }
            }
        }

        public void CheckAndMoveNodeIfNeeded(string catalogNodeId)
        {
            CatalogNodeDto nodeDto = CatalogContext.Current.GetCatalogNodeDto(catalogNodeId);
            if (nodeDto.CatalogNode.Count > 0)
            {
                // Node exists
                if (nodeDto.CatalogNode[0].ParentNodeId != 0)
                {
                    MoveNode(nodeDto.CatalogNode[0].Code, 0);
                }
            }
        }

        public void UpdateLinkEntityData(LinkEntityUpdateData linkEntityUpdateData)
        {
            int catalogId = FindCatalogByName(linkEntityUpdateData.ChannelName);

            CatalogAssociationDto associationsDto2 = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, linkEntityUpdateData.ParentEntryId);
            foreach (CatalogAssociationDto.CatalogEntryAssociationRow row in associationsDto2.CatalogEntryAssociation)
            {
                if (row.CatalogAssociationRow.AssociationDescription == linkEntityUpdateData.LinkEntityIdString)
                {
                    row.BeginEdit();
                    row.CatalogAssociationRow.AssociationName = linkEntityUpdateData.LinkEntryDisplayName;
                    row.AcceptChanges();
                }
            }

            CatalogContext.Current.SaveCatalogAssociation(associationsDto2);
        }

        public void UpdateEntryRelations(UpdateEntryRelationData updateEntryRelationData)
        {
            int catalogId = FindCatalogByName(updateEntryRelationData.ChannelName);
            CatalogEntryDto ced = CatalogContext.Current.GetCatalogEntryDto(updateEntryRelationData.CatalogEntryIdString);
            CatalogEntryDto ced2 = CatalogContext.Current.GetCatalogEntryDto(updateEntryRelationData.ParentEntryId);
            _logger.Debug($"UpdateEntryRelations called for catalog {catalogId} between {updateEntryRelationData.ParentEntryId} and {updateEntryRelationData.CatalogEntryIdString}");

            // See if channelnode
            CatalogNodeDto nodeDto = CatalogContext.Current.GetCatalogNodeDto(updateEntryRelationData.CatalogEntryIdString);
            if (nodeDto.CatalogNode.Count > 0)
            {
                _logger.Debug(string.Format("found {0} as a catalog node", updateEntryRelationData.CatalogEntryIdString));
                CatalogRelationDto rels = CatalogContext.Current.GetCatalogRelationDto(
                    catalogId,
                    nodeDto.CatalogNode[0].CatalogNodeId,
                    0,
                    string.Empty,
                    new CatalogRelationResponseGroup(CatalogRelationResponseGroup.ResponseGroup.CatalogNode));

                foreach (CatalogRelationDto.CatalogNodeRelationRow row in rels.CatalogNodeRelation)
                {
                    CatalogNode parentCatalogNode = CatalogContext.Current.GetCatalogNode(row.ParentNodeId);
                    if (updateEntryRelationData.RemoveFromChannelNodes.Contains(parentCatalogNode.ID))
                    {
                        row.Delete();
                        updateEntryRelationData.RemoveFromChannelNodes.Remove(parentCatalogNode.ID);
                    }
                }

                if (rels.HasChanges())
                {
                    _logger.Debug("Relations between nodes has been changed, saving new catalog releations");
                    CatalogContext.Current.SaveCatalogRelationDto(rels);
                }

                CatalogNode parentNode = null;
                if (nodeDto.CatalogNode[0].ParentNodeId != 0)
                {
                    parentNode = CatalogContext.Current.GetCatalogNode(nodeDto.CatalogNode[0].ParentNodeId);
                }

                if ((updateEntryRelationData.RemoveFromChannelNodes.Contains(updateEntryRelationData.ChannelIdEpified) && nodeDto.CatalogNode[0].ParentNodeId == 0)
                    || (parentNode != null && updateEntryRelationData.RemoveFromChannelNodes.Contains(parentNode.ID)))
                {
                    CatalogNode associationNode = CatalogContext.Current.GetCatalogNode(updateEntryRelationData.InRiverAssociationsEpified);

                    MoveNode(nodeDto.CatalogNode[0].Code, associationNode.CatalogNodeId);
                }
            }

            if (ced.CatalogEntry.Count <= 0)
            {
                _logger.Debug($"No catalog entry with id {updateEntryRelationData.CatalogEntryIdString} found, will not continue.");
                return;
            }

            if (updateEntryRelationData.RemoveFromChannelNodes.Count > 0)
            {
                _logger.Debug(string.Format("Look for removal from channel nodes, nr of possible nodes: {0}", updateEntryRelationData.RemoveFromChannelNodes.Count));
                CatalogRelationDto rel = CatalogContext.Current.GetCatalogRelationDto(catalogId, 0, ced.CatalogEntry[0].CatalogEntryId, string.Empty, new CatalogRelationResponseGroup(CatalogRelationResponseGroup.ResponseGroup.NodeEntry));

                foreach (CatalogRelationDto.NodeEntryRelationRow row in rel.NodeEntryRelation)
                {
                    CatalogNode catalogNode = CatalogContext.Current.GetCatalogNode(row.CatalogNodeId);
                    if (updateEntryRelationData.RemoveFromChannelNodes.Contains(catalogNode.ID))
                    {
                        row.Delete();
                    }
                }

                if (rel.HasChanges())
                {
                    _logger.Debug("Relations between entries has been changed, saving new catalog releations");
                    CatalogContext.Current.SaveCatalogRelationDto(rel);
                }
            }
            else
            {
                _logger.Debug($"{updateEntryRelationData.CatalogEntryIdString} shall not be removed from node {updateEntryRelationData.ParentEntryId}");
            }

            if (ced2.CatalogEntry.Count <= 0)
            {
                return;
            }

            if (!updateEntryRelationData.ParentExistsInChannelNodes)
            {
                if (updateEntryRelationData.IsRelation)
                {
                    _logger.Debug("Checking other relations");
                    CatalogRelationDto rel3 = CatalogContext.Current.GetCatalogRelationDto(catalogId, 0, ced2.CatalogEntry[0].CatalogEntryId, string.Empty, new CatalogRelationResponseGroup(CatalogRelationResponseGroup.ResponseGroup.CatalogEntry));
                    foreach (CatalogRelationDto.CatalogEntryRelationRow row in rel3.CatalogEntryRelation)
                    {
                        Entry childEntry = CatalogContext.Current.GetCatalogEntry(row.ChildEntryId);
                        if (childEntry.ID == updateEntryRelationData.CatalogEntryIdString)
                        {
                            _logger.Debug(string.Format("Relations between entries {0} and {1} has been removed, saving new catalog releations", row.ParentEntryId, row.ChildEntryId));
                            row.Delete();
                            CatalogContext.Current.SaveCatalogRelationDto(rel3);
                            break;
                        }
                    }
                }
                else
                {
                    List<int> catalogAssociationIds = new List<int>();
                    _logger.Debug("Checking other associations");

                    CatalogAssociationDto associationsDto = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, updateEntryRelationData.ParentEntryId);
                    foreach (CatalogAssociationDto.CatalogEntryAssociationRow row in associationsDto.CatalogEntryAssociation)
                    {
                        if (row.AssociationTypeId == updateEntryRelationData.LinkTypeId)
                        {
                            Entry childEntry = CatalogContext.Current.GetCatalogEntry(row.CatalogEntryId);
                            if (childEntry.ID == updateEntryRelationData.CatalogEntryIdString)
                            {
                                if (updateEntryRelationData.LinkEntityIdsToRemove.Count == 0 || updateEntryRelationData.LinkEntityIdsToRemove.Contains(row.CatalogAssociationRow.AssociationDescription))
                                {
                                    catalogAssociationIds.Add(row.CatalogAssociationId);
                                    _logger.Debug(string.Format("Removing association for {0}", row.CatalogEntryId));
                                    row.Delete();
                                }
                            }
                        }
                    }

                    if (associationsDto.HasChanges())
                    {
                        _logger.Debug("Saving updated associations");
                        CatalogContext.Current.SaveCatalogAssociation(associationsDto);
                    }

                    if (catalogAssociationIds.Count > 0)
                    {
                        foreach (int catalogAssociationId in catalogAssociationIds)
                        {
                            associationsDto = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, updateEntryRelationData.ParentEntryId);
                            if (associationsDto.CatalogEntryAssociation.Count(r => r.CatalogAssociationId == catalogAssociationId) == 0)
                            {
                                foreach (CatalogAssociationDto.CatalogAssociationRow assRow in associationsDto.CatalogAssociation)
                                {
                                    if (assRow.CatalogAssociationId == catalogAssociationId)
                                    {
                                        assRow.Delete();
                                        _logger.Debug($"Removing association with id {catalogAssociationId} and sending update.");
                                        CatalogContext.Current.SaveCatalogAssociation(associationsDto);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public List<string> GetLinkEntityAssociationsForEntity(GetLinkEntityAssociationsForEntityData data)
        {
            List<string> ids = new List<string>();
            int catalogId = FindCatalogByName(data.ChannelName);

            foreach (string parentId in data.ParentIds)
            {
                CatalogAssociationDto associationsDto2 = CatalogContext.Current.GetCatalogAssociationDtoByEntryCode(catalogId, parentId);
                foreach (CatalogAssociationDto.CatalogEntryAssociationRow row in associationsDto2.CatalogEntryAssociation)
                {
                    if (row.AssociationTypeId == data.LinkTypeId)
                    {
                        Entry childEntry = CatalogContext.Current.GetCatalogEntry(row.CatalogEntryId);

                        if (data.TargetIds.Contains(childEntry.ID))
                        {
                            if (!ids.Contains(row.CatalogAssociationRow.AssociationDescription))
                            {
                                ids.Add(row.CatalogAssociationRow.AssociationDescription);
                            }
                        }
                    }
                }

                CatalogContext.Current.SaveCatalogAssociation(associationsDto2);
            }
        }

        private void MoveNode(string nodeCode, int newParent)
        {
            CatalogNodeDto catalogNodeDto = CatalogContext.Current.GetCatalogNodeDto(nodeCode, new CatalogNodeResponseGroup(CatalogNodeResponseGroup.ResponseGroup.CatalogNodeFull));

            // Move node to new parent
            _logger.Debug($"Move {nodeCode} to new parent ({newParent}).");
            catalogNodeDto.CatalogNode[0].ParentNodeId = newParent;
            CatalogContext.Current.SaveCatalogNode(catalogNodeDto);
        }

        private int FindCatalogByName(string name)
        {
            try
            {
                CatalogDto d = CatalogContext.Current.GetCatalogDto();
                foreach (CatalogDto.CatalogRow catalog in d.Catalog)
                {
                    if (name.Equals(catalog.Name))
                    {
                        return catalog.CatalogId;
                    }
                }

                return -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

    }
}