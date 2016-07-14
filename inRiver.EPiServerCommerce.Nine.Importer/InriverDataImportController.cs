namespace inRiver.EPiServerCommerce.Nine.Importer
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;
    using System.Xml.Linq;

    using EPiServer;
    using EPiServer.Commerce.Catalog.ContentTypes;
    using EPiServer.Commerce.SpecializedProperties;
    using EPiServer.Core;
    using EPiServer.DataAbstraction;
    using EPiServer.DataAccess;
    using EPiServer.Framework.Blobs;
    using EPiServer.Security;
    using EPiServer.ServiceLocation;
    using EPiServer.Web;

    using inRiver.EPiServerCommerce.Interfaces;
    using inRiver.EPiServerCommernce.Nine.Importer;
    using inRiver.EPiServerCommernce.Nine.Importer.ResourceModels;

    using log4net;

    using Mediachase.Commerce.Assets;
    using Mediachase.Commerce.Catalog;
    using Mediachase.Commerce.Catalog.Dto;
    using Mediachase.Commerce.Catalog.ImportExport;
    using Mediachase.Commerce.Catalog.Managers;
    using Mediachase.Commerce.Catalog.Objects;
    using Mediachase.Commerce.Core;

    public class InriverDataImportController : SecuredApiController
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(InriverDataImportController));

        private readonly ICatalogSystem _catalogSystem = ServiceLocator.Current.GetInstance<ICatalogSystem>();
        private Injected<IContentRepository> _contentRepository = new Injected<IContentRepository>();
        private Injected<IPermanentLinkMapper> _permanentLinkMapper { get; set; }
        private Injected<ReferenceConverter> _referenceConverter { get; set; }

        public IContentRepository ContentRepository
        {
            get { return this._contentRepository.Service; }
        }

        private bool RunICatalogImportHandlers
        {
            get
            {
                if (ConfigurationManager.AppSettings.Count > 0)
                {
                    var setting = ConfigurationManager.AppSettings["inRiver.RunICatalogImportHandlers"];
                    if (setting != null)
                    {
                        bool result;
                        if (bool.TryParse(setting, out result))
                        {
                            return result;
                        }

                        return true;
                    }

                    return true;
                }

                return true;
            }
        }

        private bool RunIResourceImporterHandlers
        {
            get
            {
                if (ConfigurationManager.AppSettings.Count > 0)
                {
                    var setting = ConfigurationManager.AppSettings["inRiver.RunIResourceImporterHandlers"];
                    if (setting != null)
                    {
                        bool result;
                        if (bool.TryParse(setting, out result))
                        {
                            return result;
                        }

                        return true;
                    }

                    return true;
                }

                return true;
            }
        }

        private bool RunIDeleteActionsHandlers
        {
            get
            {
                if (ConfigurationManager.AppSettings.Count > 0)
                {
                    var setting = ConfigurationManager.AppSettings["inRiver.RunIDeleteActionsHandlers"];
                    if (setting != null)
                    {
                        bool result;
                        if (bool.TryParse(setting, out result))
                        {
                            return result;
                        }

                        return true;
                    }

                    return true;
                }

                return true;
            }
        }

        private bool RunIInRiverEventsHandlers
        {
            get
            {
                if (ConfigurationManager.AppSettings.Count > 0)
                {
                    var setting = ConfigurationManager.AppSettings["inRiver.RunIInRiverEventsHandlers"];
                    if (setting != null)
                    {
                        bool result;
                        if (bool.TryParse(setting, out result))
                        {
                            return result;
                        }

                        return true;
                    }

                    return true;
                }

                return true;
            }
        }

        [HttpGet]
        public string IsImporting()
        {
            Log.Debug("IsImporting");

            if (Singleton.Instance.IsImporting)
            {
                return "importing";
            }

            return Singleton.Instance.Message;
        }

        [HttpPost]
        public bool DeleteCatalogEntry([FromBody] string catalogEntryId)
        {
            Log.Debug("DeleteCatalogEntry");
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();
            int entryId, metaClassId, catalogId;

            try
            {
                Entry entry = CatalogContext.Current.GetCatalogEntry(catalogEntryId);
                if (entry == null)
                {
                    Log.Error(string.Format("Could not find catalog entry with id: {0}. No entry is deleted", catalogEntryId));
                    return false;
                }

                entryId = entry.CatalogEntryId;
                metaClassId = entry.MetaClassId;
                catalogId = entry.CatalogId;

                if (this.RunIDeleteActionsHandlers)
                {
                    foreach (IDeleteActionsHandler handler in importerHandlers)
                    {
                        handler.PreDeleteCatalogEntry(entryId, metaClassId, catalogId);
                    }
                }

                CatalogContext.Current.DeleteCatalogEntry(entry.CatalogEntryId, false);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Could not delete catalog entry with id: {0}", catalogEntryId), ex);
                return false;
            }

            if (this.RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalogEntry(entryId, metaClassId, catalogId);
                }
            }

            return true;
        }

        [HttpPost]
        public bool DeleteCatalog([FromBody] int catalogId)
        {
            Log.Debug("DeleteCatalog");
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (this.RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteCatalog(catalogId);
                }
            }

            try
            {
                CatalogContext.Current.DeleteCatalog(catalogId);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Could not delete catalog with id: {0}", catalogId), ex);
                return false;
            }

            if (this.RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalog(catalogId);
                }
            }

            return true;
        }

        [HttpPost]
        public bool DeleteCatalogNode([FromBody] string catalogNodeId)
        {
            Log.Debug("DeleteCatalogNode");
            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();
            int catalogId;
            int nodeId;
            try
            {
                CatalogNode cn = CatalogContext.Current.GetCatalogNode(catalogNodeId);
                if (cn == null || cn.CatalogNodeId == 0)
                {
                    Log.Error(string.Format("Could not find catalog node with id: {0}. No node is deleted", catalogNodeId));
                    return false;
                }

                catalogId = cn.CatalogId;
                nodeId = cn.CatalogNodeId;
                if (this.RunIDeleteActionsHandlers)
                {
                    foreach (IDeleteActionsHandler handler in importerHandlers)
                    {
                        handler.PreDeleteCatalogNode(nodeId, catalogId);
                    }
                }

                CatalogContext.Current.DeleteCatalogNode(cn.CatalogNodeId, cn.CatalogId);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Could not delete catalogNode with id: {0}", catalogNodeId), ex);
                return false;
            }

            if (this.RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteCatalogNode(nodeId, catalogId);
                }
            }

            return true;
        }

        [HttpPost]
        public bool CheckAndMoveNodeIfNeeded([FromBody] string catalogNodeId)
        {
            Log.Debug("CheckAndMoveNodeIfNeeded");
            try
            {
                CatalogNodeDto nodeDto = CatalogContext.Current.GetCatalogNodeDto(catalogNodeId);
                if (nodeDto.CatalogNode.Count > 0)
                {
                    // Node exists
                    if (nodeDto.CatalogNode[0].ParentNodeId != 0)
                    {
                        this.MoveNode(nodeDto.CatalogNode[0].Code, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Could not CheckAndMoveNodeIfNeeded for catalogNode with id: {0}", catalogNodeId), ex);
                return false;
            }

            return true;
        }

        [HttpPost]
        public bool UpdateLinkEntityData(LinkEntityUpdateData linkEntityUpdateData)
        {
            Log.Debug("UpdateLinkEntityData");
            int catalogId = FindCatalogByName(linkEntityUpdateData.ChannelName);

            try
            {
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
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Could not update LinkEntityData for entity with id:{0}", linkEntityUpdateData.LinkEntityIdString), ex);
                return false;
            }
        }

        [HttpPost]
        public bool UpdateEntryRelations(UpdateEntryRelationData updateEntryRelationData)
        {
            try
            {
                int catalogId = FindCatalogByName(updateEntryRelationData.ChannelName);
                CatalogEntryDto ced = CatalogContext.Current.GetCatalogEntryDto(updateEntryRelationData.CatalogEntryIdString);
                CatalogEntryDto ced2 = CatalogContext.Current.GetCatalogEntryDto(updateEntryRelationData.ParentEntryId);
                Log.Debug(string.Format("UpdateEntryRelations called for catalog {0} between {1} and {2}", catalogId, updateEntryRelationData.ParentEntryId, updateEntryRelationData.CatalogEntryIdString));

                // See if channelnode
                CatalogNodeDto nodeDto = CatalogContext.Current.GetCatalogNodeDto(updateEntryRelationData.CatalogEntryIdString);
                if (nodeDto.CatalogNode.Count > 0)
                {
                    Log.Debug(string.Format("found {0} as a catalog node", updateEntryRelationData.CatalogEntryIdString));
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
                        Log.Debug("Relations between nodes has been changed, saving new catalog releations");
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

                        this.MoveNode(nodeDto.CatalogNode[0].Code, associationNode.CatalogNodeId);
                    }
                }

                if (ced.CatalogEntry.Count <= 0)
                {
                    Log.Debug(string.Format("No catalog entry with id {0} found, will not continue.", updateEntryRelationData.CatalogEntryIdString));
                    return true;
                }

                if (updateEntryRelationData.RemoveFromChannelNodes.Count > 0)
                {
                    Log.Debug(string.Format("Look for removal from channel nodes, nr of possible nodes: {0}", updateEntryRelationData.RemoveFromChannelNodes.Count));
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
                        Log.Debug("Relations between entries has been changed, saving new catalog releations");
                        CatalogContext.Current.SaveCatalogRelationDto(rel);
                    }
                }
                else
                {
                    Log.Debug(string.Format("{0} shall not be removed from node {1}", updateEntryRelationData.CatalogEntryIdString, updateEntryRelationData.ParentEntryId));
                }

                if (ced2.CatalogEntry.Count <= 0)
                {
                    return true;
                }

                if (!updateEntryRelationData.ParentExistsInChannelNodes)
                {
                    if (updateEntryRelationData.IsRelation)
                    {
                        Log.Debug("Checking other relations");
                        CatalogRelationDto rel3 = CatalogContext.Current.GetCatalogRelationDto(catalogId, 0, ced2.CatalogEntry[0].CatalogEntryId, string.Empty, new CatalogRelationResponseGroup(CatalogRelationResponseGroup.ResponseGroup.CatalogEntry));
                        foreach (CatalogRelationDto.CatalogEntryRelationRow row in rel3.CatalogEntryRelation)
                        {
                            Entry childEntry = CatalogContext.Current.GetCatalogEntry(row.ChildEntryId);
                            if (childEntry.ID == updateEntryRelationData.CatalogEntryIdString)
                            {
                                Log.Debug(string.Format("Relations between entries {0} and {1} has been removed, saving new catalog releations", row.ParentEntryId, row.ChildEntryId));
                                row.Delete();
                                CatalogContext.Current.SaveCatalogRelationDto(rel3);
                                break;
                            }
                        }
                    }
                    else
                    {
                        List<int> catalogAssociationIds = new List<int>();
                        Log.Debug("Checking other associations");

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
                                        Log.Debug(string.Format("Removing association for {0}", row.CatalogEntryId));
                                        row.Delete();
                                    }
                                }
                            }
                        }

                        if (associationsDto.HasChanges())
                        {
                            Log.Debug("Saving updated associations");
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
                                            Log.Debug(string.Format("Removing association with id {0} and sending update.", catalogAssociationId));
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
            catch (Exception ex)
            {
                Log.Warn(string.Format("Could not update entry relations catalog with id:{0}", updateEntryRelationData.CatalogEntryIdString), ex);
                return false;
            }

            return true;
        }

        [HttpPost]
        public List<string> GetLinkEntityAssociationsForEntity(GetLinkEntityAssociationsForEntityData data)
        {
            Log.Debug("GetLinkEntityAssociationsForEntity");

            List<string> ids = new List<string>();
            try
            {
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
            catch (Exception e)
            {
                Log.Error(string.Format("Could not GetLinkEntityAssociationsForEntity for parentIds: {0}", data.ParentIds), e);
            }

            return ids;
        }

        public string Get()
        {
            Log.Debug("Hello from inRiver!");
            return "Hello from inRiver!";
        }

        [HttpPost]
        public string ImportCatalogXml([FromBody] string path)
        {
            Singleton.Instance.Message = "importing";

            Task importTask = Task.Run(
               () =>
                   {
                       try
                       {
                           Singleton.Instance.Message = "importing";
                           Singleton.Instance.IsImporting = true;

                           FileStream catalogXmlStream = File.OpenRead(path);
                           List<ICatalogImportHandler> catalogImportHandlers = ServiceLocator.Current.GetAllInstances<ICatalogImportHandler>().ToList();
                           if (catalogImportHandlers.Any() && this.RunICatalogImportHandlers)
                           {
                               this.ImportCatalogXmlWithHandlers(catalogXmlStream, catalogImportHandlers, path);
                           }
                           else
                           {
                               this.ImportCatalogXml(catalogXmlStream);
                           }
                       }
                       catch (Exception ex)
                       {
                           Singleton.Instance.IsImporting = false;
                           Log.Error("Catalog Import Failed", ex);
                           Singleton.Instance.Message = "ERROR: " + ex.Message;
                       }

                       Singleton.Instance.IsImporting = false;
                       Singleton.Instance.Message = "Import Sucessful";
                   });

            if (importTask.Status != TaskStatus.RanToCompletion)
            {
                return "importing";
            }

            return Singleton.Instance.Message;
        }

        [HttpPost]
        public bool ImportResources(List<InRiverImportResource> resources)
        {
            if (resources == null)
            {
                Log.DebugFormat("Received resource list that is NULL");
                return false;
            }

            List<IInRiverImportResource> resourcesImport = resources.Cast<IInRiverImportResource>().ToList();

            Log.DebugFormat("Received list of {0} resources to import", resourcesImport.Count());

            Task importTask = Task.Run(
                () =>
                    {
                        try
                        {
                            Singleton.Instance.Message = "importing";
                            Singleton.Instance.IsImporting = true;

                            List<IResourceImporterHandler> importerHandlers =
                                ServiceLocator.Current.GetAllInstances<IResourceImporterHandler>().ToList();
                            if (this.RunIResourceImporterHandlers)
                            {
                                foreach (IResourceImporterHandler handler in importerHandlers)
                                {
                                    handler.PreImport(resourcesImport);
                                }
                            }

                            try
                            {
                                foreach (IInRiverImportResource resource in resources)
                                {
                                    bool found = false;
                                    int count = 0;
                                    while (!found && count < 10 && resource.Action != "added")
                                    {
                                        count++;

                                        try
                                        {
                                            MediaData existingMediaData =
                                                this.ContentRepository.Get<MediaData>(
                                                    this.EntityIdToGuid(resource.ResourceId));
                                            if (existingMediaData != null)
                                            {
                                                found = true;
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            Log.DebugFormat(
                                                "Waiting ({1}/10) for resource {0} to be ready.",
                                                resource.ResourceId,
                                                count);
                                            Thread.Sleep(500);
                                        }
                                    }

                                    Log.DebugFormat(
                                        "Working with resource {0} from {1} with action: {2}",
                                        resource.ResourceId,
                                        resource.Path,
                                        resource.Action);

                                    if (resource.Action == "added" || resource.Action == "updated")
                                    {
                                        this.ImportImageAndAttachToEntry(resource);
                                    }
                                    else if (resource.Action == "deleted")
                                    {
                                        Log.DebugFormat("Got delete action for resource id: {0}.", resource.ResourceId);
                                        this.HandleDelete(resource);
                                    }
                                    else if (resource.Action == "unlinked")
                                    {
                                        this.HandleUnlink(resource);
                                    }
                                    else
                                    {
                                        Log.DebugFormat(
                                            "Got unknown action for resource id: {0}, {1}",
                                            resource.ResourceId,
                                            resource.Action);
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                Singleton.Instance.IsImporting = false;
                                Log.Error("Resource Import Failed", exception);
                                Singleton.Instance.Message = "ERROR: " + exception.Message;
                                return;
                            }

                            Log.DebugFormat("Imported {0} resources", resources.Count());

                            if (this.RunIResourceImporterHandlers)
                            {
                                foreach (IResourceImporterHandler handler in importerHandlers)
                                {
                                    handler.PostImport(resourcesImport);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Singleton.Instance.IsImporting = false;
                            Log.Error("Resource Import Failed", ex);
                            Singleton.Instance.Message = "ERROR: " + ex.Message;
                        }

                        Singleton.Instance.Message = "Resource Import successful";
                        Singleton.Instance.IsImporting = false;
                    });

            return importTask.Status != TaskStatus.RanToCompletion;
        }

        [HttpPost]
        public bool ImportUpdateCompleted(ImportUpdateCompletedData data)
        {
            try
            {
                if (this.RunIInRiverEventsHandlers)
                {
                    IEnumerable<IInRiverEventsHandler> eventsHandlers = ServiceLocator.Current.GetAllInstances<IInRiverEventsHandler>();
                    foreach (IInRiverEventsHandler handler in eventsHandlers)
                    {
                        handler.ImportUpdateCompleted(data.CatalogName, data.EventType, data.ResourcesIncluded);
                    }

                    Log.DebugFormat(
                        "*** ImportUpdateCompleted events with parameters CatalogName={0}, EventType={1}, ResourcesIncluded={2}",
                        data.CatalogName,
                        data.EventType,
                        data.ResourcesIncluded);
                }

                return true;
            }
            catch (Exception exception)
            {
                Log.Error(exception);
                return false;
            }
        }

        [HttpPost]
        public bool DeleteCompleted(DeleteCompletedData data)
        {
            try
            {
                if (this.RunIInRiverEventsHandlers)
                {
                    IEnumerable<IInRiverEventsHandler> eventsHandlers = ServiceLocator.Current.GetAllInstances<IInRiverEventsHandler>();
                    foreach (IInRiverEventsHandler handler in eventsHandlers)
                    {
                        handler.DeleteCompleted(data.CatalogName, data.EventType);
                    }

                    Log.DebugFormat("*** DeleteCompleted events with parameters CatalogName={0}, EventType={1}", data.CatalogName, data.EventType);
                }

                return true;
            }
            catch (Exception exception)
            {
                Log.Error(exception);
                return false;
            }
        }

        internal static int FindCatalogByName(string name)
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

        /// <summary>
        /// Returns a reference to the inRiver Resource folder. It will be created if it
        /// does not already exist.
        /// </summary>
        /// <remarks>
        /// The folder structure will be: /globalassets/inRiver/Resources/...
        /// </remarks>
        protected ContentReference GetInRiverResourceFolder()
        {
            ContentReference rootInRiverFolder =
                ContentFolderCreator.CreateOrGetFolder(SiteDefinition.Current.GlobalAssetsRoot, "inRiver");
            ContentReference resourceRiverFolder =
                ContentFolderCreator.CreateOrGetFolder(rootInRiverFolder, "Resources");
            return resourceRiverFolder;
        }

        private static void DeleteMediaLinkForCatalogEntry(CommerceMedia media, CatalogEntryDto catalogEntryDto, ICatalogSystem catalogSystem)
        {
            var assetRow = media.ToItemAssetRow(catalogEntryDto);

            foreach (CatalogEntryDto.CatalogItemAssetRow row in catalogEntryDto.CatalogItemAsset)
            {
                if (row.AssetKey == assetRow.AssetKey)
                {
                    row.Delete();
                    break;
                }
            }

            if (catalogEntryDto.HasChanges())
            {
                catalogSystem.SaveCatalogEntry(catalogEntryDto);
            }
        }

        private static void DeleteMediaLinkForCatalogNode(CommerceMedia media, CatalogNodeDto catalogNodeDto, ICatalogSystem catalogSystem)
        {
            var assetRow = media.ToItemAssetRow(catalogNodeDto);

            foreach (CatalogNodeDto.CatalogItemAssetRow row in catalogNodeDto.CatalogItemAsset)
            {
                if (row.AssetKey == assetRow.AssetKey)
                {
                    row.Delete();
                    break;
                }
            }

            if (catalogNodeDto.HasChanges())
            {
                catalogSystem.SaveCatalogNode(catalogNodeDto);
            }
        }

        private void MoveNode(string nodeCode, int newParent)
        {
            CatalogNodeDto catalogNodeDto = CatalogContext.Current.GetCatalogNodeDto(
                nodeCode,
                new CatalogNodeResponseGroup(CatalogNodeResponseGroup.ResponseGroup.CatalogNodeFull));

            // Move node to new parent
            Log.Debug(string.Format("Move {0} to new parent ({1}).", nodeCode, newParent));
            catalogNodeDto.CatalogNode[0].ParentNodeId = newParent;
            CatalogContext.Current.SaveCatalogNode(catalogNodeDto);
        }

        private void ImportCatalogXml(FileStream catalogXmlStream)
        {
            Log.Info("Starting importing the xml into EPiServer Commerce.");
            CatalogImportExport cie = new CatalogImportExport();
            cie.ImportExportProgressMessage += this.ProgressHandler;
            cie.Import(catalogXmlStream, AppContext.Current.ApplicationId, string.Empty, true);
            Log.Info("Done importing the xml into EPiServer Commerce.");
        }

        private void ImportCatalogXmlWithHandlers(Stream catalogXml, List<ICatalogImportHandler> catalogImportHandlers, string path)
        {
            // Read catalog xml to allow handlers to work on it
            // NOTE! If it is very large, it might consume alot of memory.
            // The catalog xml import reads in chunks, so we might impose
            // a memory problem here for the really large catalogs.
            // The benefit outweighs the problem.

            try
            {

                XDocument catalogDoc = XDocument.Load(catalogXml);

                if (catalogImportHandlers.Any())
                {
                    foreach (ICatalogImportHandler handler in catalogImportHandlers)
                    {
                        try
                        {
                            Log.DebugFormat("Preimport handler: {0}", handler.GetType().FullName);
                            handler.PreImport(catalogDoc);
                        }
                        catch (Exception e)
                        {
                            Log.Error("Failed to run PreImport on " + handler.GetType().FullName, e);
                        }
                    }
                }

                // The handlers might have changed the xml, so we pass it on
                string filename = Path.GetFileNameWithoutExtension(path);
                string filenameWithTemp = filename + "-preImport.xml";

                if (!File.Exists(path))
                {
                    Log.Error("Catalog.xml for path " + path + " does not exist. Importer is not able to continue with this process.");
                    return;
                }

                string afterPreImportXmlPath = Path.Combine(Path.GetDirectoryName(path), filenameWithTemp);

                FileStream fs = new FileStream(afterPreImportXmlPath, FileMode.Create);
                catalogDoc.Save(fs);
                FileStream stream = new FileStream(afterPreImportXmlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                catalogXml.Dispose();

                catalogDoc = null;
                stream.Position = 0;

                CatalogImportExport cie = new CatalogImportExport();
                cie.ImportExportProgressMessage += this.ProgressHandler;
                cie.Import(stream, AppContext.Current.ApplicationId, string.Empty, true);

                if (stream.Position > 0)
                {
                    stream.Position = 0;
                }

                catalogDoc = XDocument.Load(stream);
                stream.Dispose();
                fs.Dispose();

                if (catalogImportHandlers.Any())
                {
                    foreach (ICatalogImportHandler handler in catalogImportHandlers)
                    {
                        try
                        {
                            Log.DebugFormat("Postimport handler: {0}", handler.GetType().FullName);
                            handler.PostImport(catalogDoc);
                        }
                        catch (Exception e)
                        {
                            Log.Error("Failed to run PostImport on " + handler.GetType().FullName, e);
                        }
                    }
                }

                if (File.Exists(afterPreImportXmlPath))
                {
                    File.Delete(afterPreImportXmlPath);
                }
            }
            catch (Exception exception)
            {
                Log.Error("Error in ImportCatalogXmlWithHandlers", exception);
                throw;
            }
        }

        private void ProgressHandler(object source, ImportExportEventArgs args)
        {
            string message = args.Message;
            double progress = args.CompletedPercentage;
            Log.Debug(string.Format("{0}", message));
        }

        private void HandleUnlink(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = this.ContentRepository.Get<MediaData>(this.EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception)
            {
                Log.DebugFormat("Didn't find resource with Resource ID: {0}, can't unlink", inriverResource.ResourceId);
            }

            if (existingMediaData == null)
            {
                return;
            }

            this.DeleteLinksBetweenMediaAndCodes(existingMediaData.ContentGuid, inriverResource.Codes);
        }

        private void HandleDelete(IInRiverImportResource inriverResource)
        {
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = this.ContentRepository.Get<MediaData>(this.EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception)
            {
                Log.DebugFormat("Didn't find resource with Resource ID: {0}, can't Delete", inriverResource.ResourceId);
            }

            if (existingMediaData == null)
            {
                return;
            }

            List<IDeleteActionsHandler> importerHandlers = ServiceLocator.Current.GetAllInstances<IDeleteActionsHandler>().ToList();

            if (this.RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PreDeleteResource(inriverResource);
                }
            }

            CommerceMedia media = new CommerceMedia(existingMediaData.ContentGuid.ToString(), "episerver.core.icontentmedia", "default", 0, 0);

            var relations = CatalogContext.Current.GetCatalogRelationDto(media.AssetKey);

            foreach (CatalogRelationDto.CatalogItemAssetRow row in relations.CatalogItemAsset)
            {
                row.Delete();
            }

            foreach (var row in relations.CatalogEntryRelation)
            {
                row.Delete();
            }

            foreach (var row in relations.CatalogNodeRelation)
            {
                row.Delete();
            }

            if (relations.HasChanges())
            {
                CatalogContext.Current.SaveCatalogRelationDto(relations);
            }

            this.ContentRepository.Delete(existingMediaData.ContentLink, true, AccessLevel.NoAccess);

            if (this.RunIDeleteActionsHandlers)
            {
                foreach (IDeleteActionsHandler handler in importerHandlers)
                {
                    handler.PostDeleteResource(inriverResource);
                }
            }
        }

        // TODO: Check if resources can be assigned to channel nodes (category in Commerce)
        private void ImportImageAndAttachToEntry(IInRiverImportResource inriverResource)
        {
            // Find existing resource
            MediaData existingMediaData = null;
            try
            {
                existingMediaData = this.ContentRepository.Get<MediaData>(this.EntityIdToGuid(inriverResource.ResourceId));
            }
            catch (Exception ex)
            {
                Log.Debug(string.Format("Didn't find resource with Resource ID: {0}", inriverResource.ResourceId));
                Log.DebugFormat("Didn't find resource with Resource ID: {0}", inriverResource.ResourceId);
            }

            try
            {
                if (existingMediaData != null)
                {
                    Log.DebugFormat("Found existing resource with Resource ID: {0}", inriverResource.ResourceId);

                    this.UpdateMetaData((IInRiverResource)existingMediaData, inriverResource);

                    if (inriverResource.Action == "added")
                    {
                        this.AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
                    }

                    return;
                }

                ContentReference contentReference;
                existingMediaData = this.CreateNewFile(out contentReference, inriverResource);

                this.AddLinksFromMediaToCodes(existingMediaData, inriverResource.EntryCodes);
            }
            catch (Exception exception)
            {
                Log.ErrorFormat("Unable to create/update metadata for Resource ID: {0}.\n{1}", inriverResource.ResourceId, exception.Message);
            }
        }

        private void AddLinksFromMediaToCodes(MediaData contentMedia, List<EntryCode> codes)
        {
            // TODO: This way of adding media will add and save media individually. We 
            //       should add all images, and save once instead. Will improve import speed

            int sortOrder = 1;
            CommerceMedia media = new CommerceMedia(contentMedia.ContentLink, "episerver.core.icontentmedia", "default", sortOrder);

            foreach (EntryCode entryCode in codes)
            {
                // AddOrUpdateMediaOnEntry(entryCode, linkToContent, media);

                CatalogEntryDto catalogEntry = this.GetCatalogEntryDto(entryCode.Code);
                if (catalogEntry != null)
                {
                    this.AddLinkToCatalogEntry(contentMedia, media, catalogEntry, entryCode);
                }
                else
                {
                    CatalogNodeDto catalogNodeDto = this.GetCatalogNodeDto(entryCode.Code);
                    if (catalogNodeDto != null)
                    {
                        this.AddLinkToCatalogNode(contentMedia, media, catalogNodeDto, entryCode);
                    }
                    else
                    {
                        Log.DebugFormat("Could not find entry with code: {0}, can't create link", entryCode.Code);
                    }
                }
            }
        }

        protected void AddOrUpdateMediaOnEntry(EntryCode entryCode, ContentReference linkToContent, CommerceMedia media)
        {

            IContentRepository contentRepository = this._contentRepository.Service;
            ContentReference link = this._referenceConverter.Service.GetContentLink(entryCode.Code);

            CatalogContentBase contentData = contentRepository.Get<CatalogContentBase>(link);
            if (contentData != null)
            {
                IAssetContainer assetContainer = contentData as IAssetContainer;

                if (assetContainer != null)
                {
                    CatalogContentBase writableClone = null;
                    // Is it here already? Should be imported in previous step
                    CommerceMedia existingMedia =
                        assetContainer.CommerceMediaCollection.FirstOrDefault(m => m.AssetLink.Equals(linkToContent));
                    if (existingMedia == null)
                    {
                        // Not attached to entry, add it
                        writableClone = contentData.CreateWritableClone();
                        assetContainer = writableClone as IAssetContainer;

                        if (entryCode.IsMainPicture)
                        {
                            // Main picture should be first
                            assetContainer.CommerceMediaCollection.Insert(0, media);
                        }
                        else
                        {
                            assetContainer.CommerceMediaCollection.Add(media);
                        }
                    }
                    else
                    {
                        // We have it already, check it if should be main picture
                        if (entryCode.IsMainPicture && existingMedia.SortOrder != 0)
                        {
                            writableClone = this.SetSortOrderOnMedia(contentData, linkToContent, 0);
                        }
                        else if (entryCode.IsMainPicture == false && existingMedia.SortOrder == 0)
                        {
                            // This means there is something odd with the sort order, we set it to something more than 0, 
                            // so we have a real chance to set the main picture
                            writableClone = this.SetSortOrderOnMedia(contentData, linkToContent, 1);
                        }
                    }

                    if (writableClone != null)
                    {
                        VersionStatus status = writableClone.Status;
                        SaveAction saveAction = status == VersionStatus.Published
                            ? SaveAction.Publish
                            : SaveAction.Save;
                        saveAction = saveAction | SaveAction.ForceCurrentVersion;

                        // Save what we changed
                        this._contentRepository.Service.Save(writableClone, saveAction, AccessLevel.NoAccess);
                    }
                }
            }
        }

        protected CatalogContentBase SetSortOrderOnMedia(CatalogContentBase contentData, ContentReference linkToContent, int sortOrder)
        {
            var writableClone = contentData.CreateWritableClone();
            var assetContainer = writableClone as IAssetContainer;
            // Look it up again, from the writable clone
            var existingMedia = assetContainer.CommerceMediaCollection.FirstOrDefault(m => m.AssetLink.Equals(linkToContent));

            Log.DebugFormat("Setting sort order to {0} with Episerver ID {1} on '{2}'", sortOrder, existingMedia.AssetLink, contentData.Name);
            existingMedia.SortOrder = sortOrder;
            return writableClone;
        }

        /// <summary>
        /// Looks up a content reference based on it's guid or url
        /// </summary>
        /// <param name="assetId"></param>
        /// <returns></returns>
        private ContentReference GetAssetLink(Guid assetId)
        {
            PermanentContentLinkMap permanentContentLinkMap = this._permanentLinkMapper.Service.Find(assetId) as PermanentContentLinkMap;

            if (permanentContentLinkMap == null)
                return ContentReference.EmptyReference;
            return permanentContentLinkMap.ContentReference;
        }

        private void AddLinkToCatalogNode(MediaData contentMedia, CommerceMedia media, CatalogNodeDto catalogNodeDto, EntryCode entryCode)
        {
            var newAssetRow = media.ToItemAssetRow(catalogNodeDto);

            if (catalogNodeDto.CatalogItemAsset.FirstOrDefault(row => row.AssetKey == newAssetRow.AssetKey) == null)
            {
                // This asset have not been added previously
                IAssetService assetService = ServiceLocator.Current.GetInstance<IAssetService>();

                List<CatalogNodeDto.CatalogItemAssetRow> list = new List<CatalogNodeDto.CatalogItemAssetRow>();

                if (entryCode.IsMainPicture)
                {
                    // First
                    list.Add(newAssetRow);
                    list.AddRange(catalogNodeDto.CatalogItemAsset.ToList());
                }
                else
                {
                    // Last
                    list.AddRange(catalogNodeDto.CatalogItemAsset.ToList());
                    list.Add(newAssetRow);
                }

                // Set sort order correctly (instead of having them all to 0)
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].SortOrder = i;
                }

                assetService.CommitAssetsToNode(list, catalogNodeDto);

                // NOTE! Truncates version history
                this._catalogSystem.SaveCatalogNode(catalogNodeDto);
            }
        }

        private void AddLinkToCatalogEntry(MediaData contentMedia, CommerceMedia media, CatalogEntryDto catalogEntry, EntryCode entryCode)
        {
            var newAssetRow = media.ToItemAssetRow(catalogEntry);

            var catalogItemAssetRow = catalogEntry.CatalogItemAsset.FirstOrDefault(row => row.AssetKey == newAssetRow.AssetKey);
            if (catalogItemAssetRow == null)
            {
                IAssetService assetService = ServiceLocator.Current.GetInstance<IAssetService>();

                List<CatalogEntryDto.CatalogItemAssetRow> list = new List<CatalogEntryDto.CatalogItemAssetRow>();

                if (entryCode.IsMainPicture)
                {
                    Log.DebugFormat("Adding '{0}' as main picture on {1}", contentMedia.Name, entryCode.Code);
                    // First
                    list.Add(newAssetRow);
                    list.AddRange(catalogEntry.CatalogItemAsset.ToList());
                }
                else
                {
                    Log.DebugFormat("Adding '{0}' at end of list on  {1}", contentMedia.Name, entryCode.Code);
                    // Last
                    list.AddRange(catalogEntry.CatalogItemAsset.ToList());
                    list.Add(newAssetRow);
                }

                // Set sort order correctly (instead of having them all to 0)
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].SortOrder = i;
                }

                assetService.CommitAssetsToEntry(list, catalogEntry);

                // NOTE! Truncates version history
                this._catalogSystem.SaveCatalogEntry(catalogEntry);
            }
            else
            {
                // Already in the list, check and fix sort order
                if (entryCode.IsMainPicture)
                {
                    bool needsSave = false;
                    // If more than one entry have sort order 0, we need to clean it up
                    int count = catalogEntry.CatalogItemAsset.Count(row => row.SortOrder.Equals(0));
                    if (count > 1)
                    {
                        Log.DebugFormat("Sorting and setting '{0}' as main picture on {1}", contentMedia.Name, entryCode.Code);
                        // Clean up
                        List<CatalogEntryDto.CatalogItemAssetRow> assetRows = catalogEntry.CatalogItemAsset.ToList();
                        // Keep existing sort order, but start at pos 1 since we will set the main picture to 0
                        for (int i = 0; i < assetRows.Count; i++)
                        {
                            assetRows[i].SortOrder = i + 1;
                        }
                        // Set the one we found to 0, which will make it main.
                        catalogItemAssetRow.SortOrder = 0;
                        needsSave = true;
                    }
                    else if (catalogItemAssetRow.SortOrder != 0)
                    {
                        // Switch order if it isn't already first
                        Log.DebugFormat("Setting '{0}' as main picture on {1}", contentMedia.Name, entryCode.Code);

                        int oldOrder = catalogItemAssetRow.SortOrder;
                        catalogItemAssetRow.SortOrder = 0;
                        catalogEntry.CatalogItemAsset[0].SortOrder = oldOrder;
                        needsSave = true;
                    }
                    // else - we have it already, it isn't main picture, and sort seems ok, we won't save anything

                    if (needsSave == true)
                    {
                        // Since we're not adding or deleting anything from the list, we don't have to "CommitAssetsToEntry", just save
                        this._catalogSystem.SaveCatalogEntry(catalogEntry);
                    }
                }
            }
        }

        private CatalogEntryDto GetCatalogEntryDto(string code)
        {
            CatalogEntryDto catalogEntry = this._catalogSystem.GetCatalogEntryDto(code, new CatalogEntryResponseGroup(CatalogEntryResponseGroup.ResponseGroup.Assets));
            if (catalogEntry == null || catalogEntry.CatalogEntry.Count <= 0)
            {
                return null;
            }

            return catalogEntry;
        }

        private CatalogNodeDto GetCatalogNodeDto(string code)
        {
            CatalogNodeDto catalogNodeDto = this._catalogSystem.GetCatalogNodeDto(code, new CatalogNodeResponseGroup(CatalogNodeResponseGroup.ResponseGroup.Assets));
            if (catalogNodeDto == null || catalogNodeDto.CatalogNode.Count <= 0)
            {
                return null;
            }

            return catalogNodeDto;
        }

        private void DeleteLinksBetweenMediaAndCodes(Guid contentGuid, IEnumerable<string> codes)
        {
            ICatalogSystem system = ServiceLocator.Current.GetInstance<ICatalogSystem>();

            CommerceMedia media = new CommerceMedia(contentGuid.ToString(), "episerver.core.icontentmedia", "default", 0, 0);
            foreach (string code in codes)
            {
                CatalogEntryDto catalogEntryDto = system.GetCatalogEntryDto(code, new CatalogEntryResponseGroup(CatalogEntryResponseGroup.ResponseGroup.Assets));
                if (catalogEntryDto?.CatalogEntry.Count > 0)
                {
                    DeleteMediaLinkForCatalogEntry(media, catalogEntryDto, system);
                }
                else
                {
                    CatalogNodeDto catalogNodeDto = system.GetCatalogNodeDto(code, new CatalogNodeResponseGroup(CatalogNodeResponseGroup.ResponseGroup.Assets));
                    DeleteMediaLinkForCatalogNode(media, catalogNodeDto, system);
                }
            }
        }

        private void UpdateMetaData(IInRiverResource resource, IInRiverImportResource updatedResource)
        {
            MediaData editableMediaData = (MediaData)((MediaData)resource).CreateWritableClone();

            ResourceMetaField resourceFileId = updatedResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceFileId");
            if (resourceFileId != null && !string.IsNullOrEmpty(resourceFileId.Values.First().Data) && resource.ResourceFileId != int.Parse(resourceFileId.Values.First().Data))
            {
                // Update binary information
                BlobFactory blobFactory = ServiceLocator.Current.GetInstance<BlobFactory>();

                FileInfo fileInfo = new FileInfo(updatedResource.Path);
                if (fileInfo.Exists == false)
                {
                    throw new FileNotFoundException("File could not be imported", updatedResource.Path);
                }

                string ext = fileInfo.Extension;

                Blob blob = blobFactory.CreateBlob(editableMediaData.BinaryDataContainer, ext);
                using (Stream s = blob.OpenWrite())
                {
                    FileStream fileStream = File.OpenRead(fileInfo.FullName);
                    fileStream.CopyTo(s);
                }

                // Assign to file and publish changes
                editableMediaData.BinaryData = blob;
                if (updatedResource.MetaFields.Any(f => f.Id == "ResourceFilename"))
                {
                    // Change the filename.
                    editableMediaData.RouteSegment = updatedResource.MetaFields.First(f => f.Id == "ResourceFilename").Values[0].Data;
                }
                else if (updatedResource.MetaFields.Any(f => f.Id == "ResourceFileId"))
                {
                    // Change the fileId.
                    editableMediaData.RouteSegment = updatedResource.MetaFields.First(f => f.Id == "ResourceFileId").Values[0].Data;
                }
            }

            ((IInRiverResource)editableMediaData).HandleMetaData(updatedResource.MetaFields);

            this.ContentRepository.Save(editableMediaData, SaveAction.Publish, AccessLevel.NoAccess);
        }

        private MediaData CreateNewFile(out ContentReference contentReference, IInRiverImportResource inriverResource)
        {
            IContentRepository repository = this.ContentRepository; // ServiceLocator.Current.GetInstance<IContentRepository>();
            BlobFactory blobFactory = ServiceLocator.Current.GetInstance<BlobFactory>();
            ContentMediaResolver mediaDataResolver = ServiceLocator.Current.GetInstance<ContentMediaResolver>();
            IContentTypeRepository contentTypeRepository = ServiceLocator.Current.GetInstance<IContentTypeRepository>();

            bool resourceWithoutFile = false;

            ResourceMetaField resourceFileId = inriverResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceFileId");
            if (resourceFileId == null || string.IsNullOrEmpty(resourceFileId.Values.First().Data))
            {
                resourceWithoutFile = true;
            }

            string ext;
            FileInfo fileInfo = null;
            if (resourceWithoutFile)
            {
                ext = "url";
            }
            else
            {
                fileInfo = new FileInfo(inriverResource.Path);
                if (fileInfo.Exists == false)
                {
                    throw new FileNotFoundException("File could not be imported", inriverResource.Path);
                }

                ext = fileInfo.Extension;
            }

            ContentType contentType = null;
            IEnumerable<Type> mediaTypes = mediaDataResolver.ListAllMatching(ext); // .GetFirstMatching(ext);

            foreach (Type type in mediaTypes)
            {
                if (type.GetInterfaces().Contains(typeof(IInRiverResource)))
                {
                    contentType = contentTypeRepository.Load(type);
                    break;
                }
            }

            if (contentType == null)
            {
                contentType = contentTypeRepository.Load(typeof(InRiverGenericMedia));
            }

            // Get new empty file data instance in the media folder for inRiver Resource
            // TODO: Place resource inside a sub folder, but we need to organize the folder structure.
            MediaData newFile = repository.GetDefault<MediaData>(this.GetInRiverResourceFolder(), contentType.ID);
            if (resourceWithoutFile)
            {
                // find name
                ResourceMetaField resourceName = inriverResource.MetaFields.FirstOrDefault(m => m.Id == "ResourceName");
                if (resourceName != null && !string.IsNullOrEmpty(resourceName.Values.First().Data))
                {
                    newFile.Name = resourceName.Values.First().Data;
                }
                else
                {
                    newFile.Name = inriverResource.ResourceId.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                newFile.Name = fileInfo.Name;
            }

            // This cannot fail
            IInRiverResource resource = (IInRiverResource)newFile;

            if (resourceFileId != null && fileInfo != null)
            {
                resource.ResourceFileId = int.Parse(resourceFileId.Values.First().Data);
            }

            resource.EntityId = inriverResource.ResourceId;

            try
            {
                resource.HandleMetaData(inriverResource.MetaFields);
            }
            catch (Exception exception)
            {
                Log.ErrorFormat("Error when running HandleMetaData for resource {0} with contentType {1}: {2}", inriverResource.ResourceId, contentType.Name, exception.Message);
            }

            if (!resourceWithoutFile)
            {
                // Create a blob in the binary container (folder)
                Blob blob = blobFactory.CreateBlob(newFile.BinaryDataContainer, ext);
                using (Stream s = blob.OpenWrite())
                {
                    FileStream fileStream = File.OpenRead(fileInfo.FullName);
                    fileStream.CopyTo(s);
                }

                // Assign to file and publish changes
                newFile.BinaryData = blob;
            }

            newFile.ContentGuid = this.EntityIdToGuid(inriverResource.ResourceId);
            try
            {
                contentReference = repository.Save(newFile, SaveAction.Publish, AccessLevel.NoAccess);
                return newFile;
            }
            catch (Exception exception)
            {
                Log.ErrorFormat("Error when calling Save: " + exception.Message);
                contentReference = null;
                return newFile;
            }
        }

        private Guid EntityIdToGuid(int entityId)
        {
            return new Guid(string.Format("00000000-0000-0000-0000-00{0:0000000000}", entityId));
        }
    }
}
