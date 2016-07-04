using System.Globalization;
using System.Linq;
using inRiver.Integration.Reporting;
// ReSharper disable All

namespace inRiver.EPiServerCommerce.CommerceAdapter
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    using inRiver.EPiServerCommerce.CommerceAdapter;
    using inRiver.EPiServerCommerce.CommerceAdapter.Communication;
    using inRiver.EPiServerCommerce.CommerceAdapter.Enums;
    using inRiver.EPiServerCommerce.CommerceAdapter.EpiXml;
    using inRiver.EPiServerCommerce.CommerceAdapter.Helpers;
    using inRiver.EPiServerCommerce.CommerceAdapter.Utilities;
    using inRiver.EPiServerCommerce.Interfaces.Enums;
    using inRiver.Integration.Configuration;
    using inRiver.Integration.Export;
    using inRiver.Integration.Interface;
    using inRiver.Integration.Logging;
    using inRiver.Remoting;
    using inRiver.Remoting.Connect;
    using inRiver.Remoting.Log;
    using inRiver.Remoting.Objects;
    using inRiver.Remoting.Query;

    public class XmlExporter : ServerListener, IOutboundConnector, IChannelListener, ICVLListener
    {
        private bool started;

        private Configuration config;

        public new void Start()
        {
            this.config = new Configuration(this.Id);
            ConnectorEventHelper.CleanupOngoingConnectorEvents(this.config);
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.Start, "Connector is starting", 0);

            Entity channel = RemoteManager.DataService.GetEntity(this.config.ChannelId, LoadLevel.Shallow);
            if (channel == null)
            {
                this.started = false;
                ConnectorEventHelper.UpdateConnectorEvent(connectorEvent, "Channel id is not valid, could not find entity with id. Unable to start", -1, true);
                return;
            }

            if (channel.EntityType.Id != "Channel")
            {
                this.started = false;
                ConnectorEventHelper.UpdateConnectorEvent(connectorEvent, "Channel id is not valid, entity with id is no channel. Unable to start", -1, true);
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += this.CurrentDomainAssemblyResolve;

            if (!this.InitConnector())
            {
                return;
            }

            string epiMajorVersionVerification = ConfigurationManager.Instance.GetSetting(this.Id, "EPI_MAJOR_VERSION");

            if (string.IsNullOrEmpty(epiMajorVersionVerification))
            {
                ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EPI_MAJOR_VERSION", "9");
            }

            base.Start();
            this.started = true;
            ConnectorEventHelper.UpdateConnectorEvent(connectorEvent, "Connector has started", 100);
        }

        public new void Stop()
        {
            base.Stop();
            this.started = false;
            ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.Stop, "Connector is stopped", 100);
        }

        public new void InitConfigurationSettings()
        {
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "PUBLISH_FOLDER", @"C:\temp\Publish\Epi");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "PUBLISH_FOLDER_RESOURCES", @"C:\temp\Publish\Epi\Resources");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "RESOURCE_CONFIGURATION", "Preview");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "LANGUAGE_MAPPING", "<languages><language><epi>en</epi><inriver>en-us</inriver></language></languages>");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "ITEM_TO_SKUs", "false");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "CVL_DATA", "Keys|Values|KeysAndValues");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "BUNDLE_ENTITYTYPES", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "PACKAGE_ENTITYTYPES", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "DYNAMIC_PACKAGE_ENTITYTYPES", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "RESOURCE_PROVIDER_TYPE", "inRiver.EPiServerCommerce.MediaPublisher.Importer, inRiver.EPiServerCommerce.MediaPublisher");

            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "CHANNEL_ID", "123");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EXPORT_INVENTORY_DATA", "false");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EXPORT_PRICING_DATA", "false");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EPI_CODE_FIELDS", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EXCLUDE_FIELDS", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EPI_NAME_FIELDS", string.Empty);

            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "USE_THREE_LEVELS_IN_COMMERCE", "false");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "HTTP_POST_URL", string.Empty);

            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EPI_ENDPOINT_URL", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EPI_APIKEY", "SomeGreatKey123");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EPI_RESTTIMEOUT", "1");

            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "EPI_MAJOR_VERSION", "9");
            ConfigurationManager.Instance.SetConnectorSetting(this.Id, "BATCH_SIZE", string.Empty);
        }

        public new bool IsStarted()
        {
            return this.started;
        }

        public void Publish(int channelId)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            ConnectorEvent publishConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.Publish, string.Format("Publish started for channel: {0}", channelId), 0);
            Stopwatch publishStopWatch = new Stopwatch();
            bool resourceIncluded = false;

            try
            {
                publishStopWatch.Start();
                Entity channelEntity = this.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Failed to initial publish. Could not find the channel.", -1, true);
                    return;
                }

                ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Fetching all channel entities...", 1);
                List<StructureEntity> channelEntities = ChannelHelper.GetAllEntitiesInChannel(this.config.ChannelId, Configuration.ExportEnabledEntityTypes);

                this.config.ChannelStructureEntities = channelEntities;
                ChannelHelper.BuildEntityIdAndTypeDict(this.config);

                ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Done fetching all channel entities", 10);

                ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Generating catalog.xml...", 11);
                Dictionary<string, List<XElement>> epiElements = EpiDocument.GetEPiElements(this.config);

                XDocument doc = EpiDocument.CreateImportDocument(channelEntity, EpiElement.GetMetaClassesFromFieldSets(this.config), EpiDocument.GetAssociationTypes(this.config), epiElements, this.config);
                string channelIdentifier = ChannelHelper.GetChannelIdentifier(channelEntity);

                string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

                string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, this.config);
                IntegrationLogger.Write(LogLevel.Information, "Catalog saved with the following:");
                IntegrationLogger.Write(LogLevel.Information, string.Format("Nodes: {0}", epiElements["Nodes"].Count));
                IntegrationLogger.Write(LogLevel.Information, string.Format("Entries: {0}", epiElements["Entries"].Count));
                IntegrationLogger.Write(LogLevel.Information, string.Format("Relations: {0}", epiElements["Relations"].Count));
                IntegrationLogger.Write(LogLevel.Information, string.Format("Associations: {0}", epiElements["Associations"].Count));
                ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Done generating catalog.xml", 25);

                ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Generating Resource.xml and saving files to disk...", 26);

                List<StructureEntity> resources = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeFromPath(channelEntity.Id.ToString(), "Resource");

                this.config.ChannelStructureEntities.AddRange(resources);

                var resourceDocument = Resources.GetDocumentAndSaveFilesToDisk(resources, this.config, folderDateTime);
                DocumentFileHelper.SaveDocument(channelIdentifier, resourceDocument, this.config, folderDateTime);

                string resourceZipFile = string.Format("resource_{0}.zip", folderDateTime);
                DocumentFileHelper.ZipFile(Path.Combine(this.config.ResourcesRootPath, folderDateTime, "Resources.xml"), resourceZipFile);
                ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Done generating/saving Resource.xml", 50);
                publishStopWatch.Stop();

                if (this.config.ActivePublicationMode.Equals(PublicationMode.Automatic))
                {
                    IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");
                    ConnectorEventHelper.UpdateConnectorEvent(
                        publishConnectorEvent,
                        "Sending Catalog.xml to EPiServer...",
                        51);
                    if (EpiApi.StartImportIntoEpiServerCommerce(
                            Path.Combine(this.config.PublicationsRootPath, folderDateTime, Configuration.ExportFileName),
                            ChannelHelper.GetChannelGuid(channelEntity, this.config),
                            this.config))
                    {
                        ConnectorEventHelper.UpdateConnectorEvent(
                            publishConnectorEvent,
                            "Done sending Catalog.xml to EPiServer",
                            75);
                        EpiApi.SendHttpPost(
                            this.config,
                            Path.Combine(this.config.PublicationsRootPath, folderDateTime, zippedfileName));
                    }
                    else
                    {
                        ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Error while sending Catalog.xml to EPiServer", -1, true);
                        return;
                    }

                    ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Sending Resources to EPiServer...", 76);

                    if (EpiApi.StartAssetImportIntoEpiServerCommerce(Path.Combine(this.config.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(this.config.ResourcesRootPath, folderDateTime), this.config))
                    {
                        ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Done sending Resources to EPiServer...", 99);
                        EpiApi.SendHttpPost(this.config, Path.Combine(this.config.ResourcesRootPath, folderDateTime, resourceZipFile));
                        resourceIncluded = true;
                    }
                    else
                    {
                        ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Error while sending resources to EPiServer", -1, true);
                    }
                }

                if (!publishConnectorEvent.IsError)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, "Publish done!", 100);
                    string channelName =
                        EpiMappingHelper.GetNameForEntity(
                            RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow),
                            this.config,
                            100);
                    EpiApi.ImportUpdateCompleted(
                        channelName,
                        ImportUpdateCompletedEventType.Publish,
                        resourceIncluded,
                        this.config);
                }
            }
            catch (Exception exception)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in Publish", exception);
                ConnectorEventHelper.UpdateConnectorEvent(publishConnectorEvent, exception.Message, -1, true);
            }
            finally
            {
                this.config.EntityIdAndType = new Dictionary<int, string>();
                this.config.ChannelStructureEntities = new List<StructureEntity>();
                this.config.ChannelEntities = new Dictionary<int, Entity>();
            }
        }

        public void UnPublish(int channelId)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            IntegrationLogger.Write(LogLevel.Information, string.Format("Unpublish on channel: {0} called. No action made.", channelId));
        }

        public void Synchronize(int channelId)
        {
        }

        public void ChannelEntityAdded(int channelId, int entityId)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.config.ChannelStructureEntities = new List<StructureEntity>();

            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received entity added for entity {0} in channel {1}", entityId, channelId));
            ConnectorEvent entityAddedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.ChannelEntityAdded, string.Format("Received entity added for entity {0} in channel {1}", entityId, channelId), 0);

            bool resourceIncluded = false;
            Stopwatch entityAddedStopWatch = new Stopwatch();

            entityAddedStopWatch.Start();

            try
            {
                Entity channelEntity = this.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(
                        entityAddedConnectorEvent,
                        "Failed to initial ChannelLinkAdded. Could not find the channel.",
                        -1,
                        true);
                    return;
                }

                List<StructureEntity> addedStructureEntities =
                    ChannelHelper.GetStructureEntitiesForEntityInChannel(this.config.ChannelId, entityId);

                foreach (StructureEntity addedStructureEntity in addedStructureEntities)
                {
                    this.config.ChannelStructureEntities.Add(
                        ChannelHelper.GetParentStructureEntity(
                            this.config.ChannelId,
                            addedStructureEntity.ParentId,
                            addedStructureEntity.EntityId,
                            addedStructureEntities));
                }

                this.config.ChannelStructureEntities.AddRange(addedStructureEntities);

                string targetEntityPath = ChannelHelper.GetTargetEntityPath(entityId, addedStructureEntities);

                foreach (
                    StructureEntity childStructureEntity in
                        ChannelHelper.GetChildrenEntitiesInChannel(entityId, targetEntityPath))
                {
                    this.config.ChannelStructureEntities.AddRange(
                        ChannelHelper.GetChildrenEntitiesInChannel(
                            childStructureEntity.EntityId,
                            childStructureEntity.Path));
                }

                this.config.ChannelStructureEntities.AddRange(
                    ChannelHelper.GetChildrenEntitiesInChannel(entityId, targetEntityPath));
                ChannelHelper.BuildEntityIdAndTypeDict(this.config);

                new AddUtility(this.config).Add(channelEntity, entityAddedConnectorEvent, out resourceIncluded);
                entityAddedStopWatch.Stop();
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelEntityAdded", ex);
                ConnectorEventHelper.UpdateConnectorEvent(entityAddedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                this.config.EntityIdAndType = new Dictionary<int, string>();
            }

            entityAddedStopWatch.Stop();

            IntegrationLogger.Write(LogLevel.Information, string.Format("Add done for channel {0}, took {1}!", channelId, BusinessHelper.GetElapsedTimeFormated(entityAddedStopWatch)));
            ConnectorEventHelper.UpdateConnectorEvent(entityAddedConnectorEvent, "ChannelEntityAdded complete", 100);

            if (!entityAddedConnectorEvent.IsError)
            {
                string channelName = EpiMappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), this.config, 100);
                EpiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityAdded, resourceIncluded, this.config);
            }

        }

        public void ChannelEntityUpdated(int channelId, int entityId, string data)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.config.ChannelEntities = new Dictionary<int, Entity>();
            this.config.ChannelStructureEntities = new List<StructureEntity>();

            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received entity update for entity {0} in channel {1}", entityId, channelId));
            ConnectorEvent entityUpdatedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.ChannelEntityUpdated, string.Format("Received entity update for entity {0} in channel {1}", entityId, channelId), 0);

            Stopwatch entityUpdatedStopWatch = new Stopwatch();
            entityUpdatedStopWatch.Start();

            try
            {
                if (channelId == entityId)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(entityUpdatedConnectorEvent, string.Format("ChannelEntityUpdated, updated Entity is the Channel, no action required"), 100);
                    return;
                }

                Entity channelEntity = this.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(entityUpdatedConnectorEvent, string.Format("Failed to initial ChannelEntityUpdated. Could not find the channel with id: {0}", channelId), -1, true);
                    return;
                }

                string channelIdentifier = ChannelHelper.GetChannelIdentifier(channelEntity);
                Entity updatedEntity = RemoteManager.DataService.GetEntity(entityId, LoadLevel.DataAndLinks);

                if (updatedEntity == null)
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("ChannelEntityUpdated, could not find entity with id: {0}", entityId));
                    ConnectorEventHelper.UpdateConnectorEvent(entityUpdatedConnectorEvent, string.Format("ChannelEntityUpdated, could not find entity with id: {0}", entityId), -1, true);

                    return;
                }

                string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");

                bool resourceIncluded = false;
                string channelName = EpiMappingHelper.GetNameForEntity(channelEntity, this.config, 100);

                if (updatedEntity.EntityType.Id.Equals("Resource"))
                {
                    XDocument resDoc = Resources.HandleResourceUpdate(updatedEntity, this.config, folderDateTime);

                    DocumentFileHelper.SaveDocument(channelIdentifier, resDoc, this.config, folderDateTime);

                    string resourceZipFile = string.Format("resource_{0}.zip", folderDateTime);
                    DocumentFileHelper.ZipFile(
                        Path.Combine(this.config.ResourcesRootPath, folderDateTime, "Resources.xml"),
                        resourceZipFile);
                    IntegrationLogger.Write(LogLevel.Debug, "Resources saved!");
                    if (this.config.ActivePublicationMode.Equals(PublicationMode.Automatic))
                    {
                        IntegrationLogger.Write(LogLevel.Debug, "Starting automatic resource import!");
                        if (EpiApi.StartAssetImportIntoEpiServerCommerce(Path.Combine(this.config.ResourcesRootPath, folderDateTime, "Resources.xml"), Path.Combine(this.config.ResourcesRootPath, folderDateTime), this.config))
                        {
                            EpiApi.SendHttpPost(this.config, Path.Combine(this.config.ResourcesRootPath, folderDateTime, resourceZipFile));
                            resourceIncluded = true;
                        }
                    }
                }
                else
                {
                    IntegrationLogger.Write(
                        LogLevel.Debug,
                        string.Format(
                            "Updated entity found. Type: {0}, id: {1}",
                            updatedEntity.EntityType.Id,
                            updatedEntity.Id));

                    this.config.ChannelStructureEntities =
                        ChannelHelper.GetStructureEntitiesForEntityInChannel(this.config.ChannelId, entityId);

                    ChannelHelper.BuildEntityIdAndTypeDict(this.config);

                    #region SKU and ChannelNode
                    if (updatedEntity.EntityType.Id.Equals("Item") && data != null && data.Split(',').Contains("SKUs"))
                    {
                        Field currentField = RemoteManager.DataService.GetField(entityId, "SKUs");

                        List<Field> fieldHistory = RemoteManager.DataService.GetFieldHistory(entityId, "SKUs");

                        Field previousField = fieldHistory.FirstOrDefault(f => f.Revision == currentField.Revision - 1);

                        string oldXml = string.Empty;
                        if (previousField != null && previousField.Data != null)
                        {
                            oldXml = (string)previousField.Data;
                        }

                        string newXml = string.Empty;
                        if (currentField.Data != null)
                        {
                            newXml = (string)currentField.Data;
                        }

                        List<XElement> skusToDelete, skusToAdd;
                        BusinessHelper.CompareAndParseSkuXmls(oldXml, newXml, out skusToAdd, out skusToDelete);

                        foreach (XElement skuToDelete in skusToDelete)
                        {
                            EpiApi.DeleteCatalogEntry(skuToDelete.Attribute("id").Value, this.config);
                        }

                        //TODO Det måste skapas upp XML filer för  att delete items som ligger under ChannelNode också. Eftersom de ligger under channelnode i EPi
                        if (skusToAdd.Count > 0)
                        {
                            new AddUtility(this.config).Add(
                                channelEntity,
                                entityUpdatedConnectorEvent,
                                out resourceIncluded);
                        }
                    }
                    else if (updatedEntity.EntityType.Id.Equals("ChannelNode"))
                    {
                        new AddUtility(this.config).Add(
                            channelEntity,
                            entityUpdatedConnectorEvent,
                            out resourceIncluded);

                        entityUpdatedStopWatch.Stop();
                        IntegrationLogger.Write(
                            LogLevel.Information,
                            string.Format(
                                "Update done for channel {0}, took {1}!",
                                channelId,
                                BusinessHelper.GetElapsedTimeFormated(entityUpdatedStopWatch)));

                        ConnectorEventHelper.UpdateConnectorEvent(
                            entityUpdatedConnectorEvent,
                            "ChannelEntityUpdated complete",
                            100);

                        // Fire the complete event
                        EpiApi.ImportUpdateCompleted(
                            channelName,
                            ImportUpdateCompletedEventType.EntityUpdated,
                            resourceIncluded,
                            this.config);
                        return;
                    }
                    #endregion

                    if (updatedEntity.EntityType.IsLinkEntityType)
                    {
                        //ChannelEntities will be used for LinkEntity when we get EPiCode with channel prefix
                        if (!this.config.ChannelEntities.ContainsKey(updatedEntity.Id))
                        {
                            this.config.ChannelEntities.Add(updatedEntity.Id, updatedEntity);
                        }
                    }

                    XDocument doc = EpiDocument.CreateUpdateDocument(channelEntity, updatedEntity, this.config);

                    // If data exist in EPiCodeFields.
                    // Update Associations and relations for XDocument doc.
                    if (this.config.EpiCodeMapping.ContainsKey(updatedEntity.EntityType.Id) &&
                        data.Split(',').Contains(config.EpiCodeMapping[updatedEntity.EntityType.Id]))
                    {
                        ChannelHelper.EpiCodeFieldUpdatedAddAssociationAndRelationsToDocument(
                            doc,
                            updatedEntity,
                            this.config,
                            channelId);
                    }

                    if (updatedEntity.EntityType.IsLinkEntityType)
                    {
                        List<Link> links = RemoteManager.DataService.GetLinksForLinkEntity(updatedEntity.Id);
                        if (links.Count > 0)
                        {
                            string parentId = ChannelPrefixHelper.GetEPiCodeWithChannelPrefix(links.First().Source.Id, config);

                            EpiApi.UpdateLinkEntityData(updatedEntity, channelId, channelEntity, config, parentId);
                        }
                    }

                    string zippedName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, this.config);

                    if (this.config.ActivePublicationMode.Equals(PublicationMode.Automatic))
                    {
                        IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");
                        if (EpiApi.StartImportIntoEpiServerCommerce(Path.Combine(this.config.PublicationsRootPath, folderDateTime, "Catalog.xml"), ChannelHelper.GetChannelGuid(channelEntity, this.config), this.config))
                        {
                            EpiApi.SendHttpPost(this.config, Path.Combine(this.config.PublicationsRootPath, folderDateTime, zippedName));
                        }
                    }
                }

                EpiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.EntityUpdated, resourceIncluded, this.config);
                entityUpdatedStopWatch.Stop();

            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelEntityUpdated", ex);
                ConnectorEventHelper.UpdateConnectorEvent(entityUpdatedConnectorEvent, ex.Message, -1, true);
            }
            finally
            {
                this.config.ChannelStructureEntities = new List<StructureEntity>();
                this.config.EntityIdAndType = new Dictionary<int, string>();
                this.config.ChannelEntities = new Dictionary<int, Entity>();
            }

            IntegrationLogger.Write(LogLevel.Information, string.Format("Update done for channel {0}, took {1}!", channelId, BusinessHelper.GetElapsedTimeFormated(entityUpdatedStopWatch)));
            ConnectorEventHelper.UpdateConnectorEvent(entityUpdatedConnectorEvent, "ChannelEntityUpdated complete", 100);
        }

        public void ChannelEntityDeleted(int channelId, Entity deletedEntity)
        {
            int entityId = deletedEntity.Id;
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            if (config.ModifyFilterBehavior)
            {
                Entity exists = RemoteManager.DataService.GetEntity(deletedEntity.Id, LoadLevel.Shallow);
                if (exists != null)
                {
                    IntegrationLogger.Write(LogLevel.Debug, string.Format("Ignored deleted for entity {0} in channel {1} becuase of ModifiedFilterBehavior", entityId, channelId));
                    return;
                }
            }

            Stopwatch deleteStopWatch = new Stopwatch();
            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received entity deleted for entity {0} in channel {1}", entityId, channelId));
            ConnectorEvent entityDeletedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.ChannelEntityDeleted, string.Format("Received entity deleted for entity {0} in channel {1}", entityId, channelId), 0);

            try
            {
                IntegrationLogger.Write(LogLevel.Debug, string.Format("Received entity deleted for entity {0} in channel {1}", entityId, channelId));
                deleteStopWatch.Start();

                Entity channelEntity = this.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(entityDeletedConnectorEvent, "Failed to initial ChannelEntityDeleted. Could not find the channel.", -1, true);
                    return;
                }

                new DeleteUtility(this.config).Delete(channelEntity, -1, deletedEntity, string.Empty);
                deleteStopWatch.Stop();
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelEntityDeleted", ex);
                ConnectorEventHelper.UpdateConnectorEvent(entityDeletedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                this.config.EntityIdAndType = new Dictionary<int, string>();
            }

            IntegrationLogger.Write(LogLevel.Information, string.Format("Delete done for channel {0}, took {1}!", channelId, BusinessHelper.GetElapsedTimeFormated(deleteStopWatch)));
            ConnectorEventHelper.UpdateConnectorEvent(entityDeletedConnectorEvent, "ChannelEntityDeleted complete", 100);

            if (!entityDeletedConnectorEvent.IsError)
            {
                string channelName = EpiMappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), this.config, 100);
                EpiApi.DeleteCompleted(channelName, DeleteCompletedEventType.EntitiyDeleted, this.config);
            }
        }

        public void ChannelEntityFieldSetUpdated(int channelId, int entityId, string fieldSetId)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.ChannelEntityUpdated(channelId, entityId, string.Empty);
        }

        public void ChannelEntitySpecificationFieldAdded(int channelId, int entityId, string fieldName)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.ChannelEntityUpdated(channelId, entityId, string.Empty);
        }

        public void ChannelEntitySpecificationFieldUpdated(int channelId, int entityId, string fieldName)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.ChannelEntityUpdated(channelId, entityId, string.Empty);
        }

        public void ChannelLinkAdded(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.config.ChannelStructureEntities = new List<StructureEntity>();

            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received link added for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId));
            ConnectorEvent linkAddedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.ChannelLinkAdded, string.Format("Received link added for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId), 0);

            bool resourceIncluded;
            Stopwatch linkAddedStopWatch = new Stopwatch();
            try
            {
                linkAddedStopWatch.Start();

                // NEW CODE
                Entity channelEntity = this.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(linkAddedConnectorEvent, "Failed to initial ChannelLinkAdded. Could not find the channel.", -1, true);
                    return;
                }

                ConnectorEventHelper.UpdateConnectorEvent(linkAddedConnectorEvent, "Fetching channel entities...", 1);

                var existingEntitiesInChannel = ChannelHelper.GetStructureEntitiesForEntityInChannel(this.config.ChannelId, targetEntityId);

                //Get Parents EntityStructure from Path
                List<StructureEntity> parents = new List<StructureEntity>();

                foreach (StructureEntity existingEntity in existingEntitiesInChannel)
                {
                    List<string> parentIds = existingEntity.Path.Split('/').ToList();
                    parentIds.Reverse();
                    parentIds.RemoveAt(0);

                    for (int i = 0; i < parentIds.Count - 1; i++)
                    {
                        int entityId = int.Parse(parentIds[i]);
                        int parentId = int.Parse(parentIds[i + 1]);

                        parents.AddRange(RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channelId, entityId, parentId));
                    }
                }

                List<StructureEntity> children = new List<StructureEntity>();

                foreach (StructureEntity existingEntity in existingEntitiesInChannel)
                {
                    string targetEntityPath = ChannelHelper.GetTargetEntityPath(existingEntity.EntityId, existingEntitiesInChannel, existingEntity.ParentId);
                    children.AddRange(ChannelHelper.GetAllChannelStructureEntitiesFromPath(targetEntityPath));
                }

                this.config.ChannelStructureEntities.AddRange(parents);
                this.config.ChannelStructureEntities.AddRange(children);

                // Remove duplicates
                this.config.ChannelStructureEntities =
                    this.config.ChannelStructureEntities.GroupBy(x => x.EntityId).Select(x => x.First()).ToList();

                ChannelHelper.BuildEntityIdAndTypeDict(this.config);

                ConnectorEventHelper.UpdateConnectorEvent(linkAddedConnectorEvent, "Done fetching channel entities", 10);

                new AddUtility(this.config).Add(
                    channelEntity,
                    linkAddedConnectorEvent,
                    out resourceIncluded);

                linkAddedStopWatch.Stop();
            }
            catch (Exception ex)
            {

                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelLinkAdded", ex);
                ConnectorEventHelper.UpdateConnectorEvent(linkAddedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                this.config.EntityIdAndType = new Dictionary<int, string>();
            }

            linkAddedStopWatch.Stop();

            IntegrationLogger.Write(LogLevel.Information, string.Format("ChannelLinkAdded done for channel {0}, took {1}!", channelId, linkAddedStopWatch.GetElapsedTimeFormated()));
            ConnectorEventHelper.UpdateConnectorEvent(linkAddedConnectorEvent, "ChannelLinkAdded complete", 100);

            if (!linkAddedConnectorEvent.IsError)
            {
                string channelName = EpiMappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), this.config, 100);
                EpiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkAdded, resourceIncluded, this.config);
            }
        }

        public void ChannelLinkDeleted(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.config.ChannelStructureEntities = new List<StructureEntity>();

            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received link deleted for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId));
            ConnectorEvent linkDeletedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.ChannelLinkDeleted, string.Format("Received link deleted for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId), 0);

            Stopwatch linkDeletedStopWatch = new Stopwatch();

            try
            {
                linkDeletedStopWatch.Start();

                Entity channelEntity = this.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(linkDeletedConnectorEvent, "Failed to initial ChannelLinkDeleted. Could not find the channel.", -1, true);
                    return;
                }

                Entity targetEntity = RemoteManager.DataService.GetEntity(targetEntityId, LoadLevel.DataAndLinks);

                new DeleteUtility(this.config).Delete(channelEntity, sourceEntityId, targetEntity, linkTypeId);

                /*
                if (linkEntityId.HasValue)
                {
                    //If its the last one. The linkEntity should be deleted in EPi.
                    Entity linkEntity = RemoteManager.DataService.GetEntity(linkEntityId.Value, LoadLevel.DataAndLinks);

                    if (linkEntity == null)
                    {
                        //Its the last one that were existing on LinkEntity. Send Delete to EPi
                        new DeleteUtility(this.config).DeleteLinkEntity(channelEntity, linkEntityId.Value);
                    }

                    //new DeleteUtility(this.config).Delete(channelEntity, -1, linkEntity, string.Empty);
                }*/

                linkDeletedStopWatch.Stop();
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelLinkDeleted", ex);
                ConnectorEventHelper.UpdateConnectorEvent(linkDeletedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                this.config.EntityIdAndType = new Dictionary<int, string>();
                this.config.ChannelEntities = new Dictionary<int, Entity>();
            }

            linkDeletedStopWatch.Stop();

            IntegrationLogger.Write(LogLevel.Information, string.Format("ChannelLinkDeleted done for channel {0}, took {1}!", channelId, linkDeletedStopWatch.GetElapsedTimeFormated()));
            ConnectorEventHelper.UpdateConnectorEvent(linkDeletedConnectorEvent, "ChannelLinkDeleted complete", 100);

            if (!linkDeletedConnectorEvent.IsError)
            {
                string channelName = EpiMappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), this.config, 100);
                EpiApi.DeleteCompleted(channelName, DeleteCompletedEventType.LinkDeleted, this.config);
            }
        }

        public void ChannelLinkUpdated(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            if (channelId != this.config.ChannelId)
            {
                return;
            }

            this.config.ChannelStructureEntities = new List<StructureEntity>();

            IntegrationLogger.Write(LogLevel.Debug, string.Format("Received link update for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId));
            ConnectorEvent linkUpdatedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.ChannelLinkAdded, string.Format("Received link update for sourceEntityId {0} and targetEntityId {1} in channel {2}", sourceEntityId, targetEntityId, channelId), 0);

            bool resourceIncluded;

            Stopwatch linkAddedStopWatch = new Stopwatch();
            try
            {
                linkAddedStopWatch.Start();

                // NEW CODE
                Entity channelEntity = this.InitiateChannelConfiguration(channelId);
                if (channelEntity == null)
                {
                    ConnectorEventHelper.UpdateConnectorEvent(
                        linkUpdatedConnectorEvent,
                        "Failed to initial ChannelLinkUpdated. Could not find the channel.",
                        -1,
                        true);
                    return;
                }

                ConnectorEventHelper.UpdateConnectorEvent(linkUpdatedConnectorEvent, "Fetching channel entities...", 1);

                var targetEntityStructure = ChannelHelper.GetEntityInChannelWithParent(this.config.ChannelId, targetEntityId, sourceEntityId);

                StructureEntity parentStructureEntity = ChannelHelper.GetParentStructureEntity(this.config.ChannelId, sourceEntityId, targetEntityId, targetEntityStructure);
                this.config.ChannelStructureEntities.Add(parentStructureEntity);

                this.config.ChannelStructureEntities.AddRange(
                    ChannelHelper.GetChildrenEntitiesInChannel(
                        parentStructureEntity.EntityId,
                        parentStructureEntity.Path));

                ChannelHelper.BuildEntityIdAndTypeDict(this.config);

                ConnectorEventHelper.UpdateConnectorEvent(linkUpdatedConnectorEvent, "Done fetching channel entities", 10);

                new AddUtility(this.config).Add(
                        channelEntity,
                        linkUpdatedConnectorEvent,
                        out resourceIncluded);

                linkAddedStopWatch.Stop();
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelLinkUpdated", ex);
                ConnectorEventHelper.UpdateConnectorEvent(linkUpdatedConnectorEvent, ex.Message, -1, true);
                return;
            }
            finally
            {
                this.config.EntityIdAndType = new Dictionary<int, string>();
            }

            linkAddedStopWatch.Stop();

            IntegrationLogger.Write(LogLevel.Information, string.Format("ChannelLinkUpdated done for channel {0}, took {1}!", channelId, linkAddedStopWatch.GetElapsedTimeFormated()));
            ConnectorEventHelper.UpdateConnectorEvent(linkUpdatedConnectorEvent, "ChannelLinkUpdated complete", 100);

            if (!linkUpdatedConnectorEvent.IsError)
            {
                string channelName = EpiMappingHelper.GetNameForEntity(RemoteManager.DataService.GetEntity(channelId, LoadLevel.Shallow), this.config, 100);
                EpiApi.ImportUpdateCompleted(channelName, ImportUpdateCompletedEventType.LinkUpdated, resourceIncluded, this.config);
            }
        }

        public void AssortmentCopiedInChannel(int channelId, int assortmentId, int targetId, string targetType)
        {

        }

        public void CVLValueCreated(string cvlId, string cvlValueKey)
        {
            IntegrationLogger.Write(LogLevel.Information, string.Format("CVL value created event received with key '{0}' from CVL with id {1}", cvlValueKey, cvlId));

            ConnectorEvent cvlValueCreatedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.CVLValueCreated, string.Format("CVL value created event received with key '{0}' from CVL with id {1}", cvlValueKey, cvlId), 0);

            try
            {
                CVLValue val = RemoteManager.ModelService.GetCVLValueByKey(cvlValueKey, cvlId);

                if (val != null)
                {
                    if (!BusinessHelper.CVLValues.Any(cv => cv.CVLId.Equals(cvlId) && cv.Key.Equals(cvlValueKey)))
                    {
                        BusinessHelper.CVLValues.Add(val);
                    }

                    string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");
                    new CvlUtility(this.config).AddCvl(cvlId, folderDateTime);
                }
                else
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Could not add CVL value with key {0} to CVL with id {1}", cvlValueKey, cvlId));
                    ConnectorEventHelper.UpdateConnectorEvent(cvlValueCreatedConnectorEvent, string.Format("Could not add CVL value with key {0} to CVL with id {1}", cvlValueKey, cvlId), -1, true);
                }
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not add CVL value with key {0} to CVL with id {1}", cvlValueKey, cvlId), ex);
                ConnectorEventHelper.UpdateConnectorEvent(cvlValueCreatedConnectorEvent, ex.Message, -1, true);
            }

            ConnectorEventHelper.UpdateConnectorEvent(cvlValueCreatedConnectorEvent, "CVLValueCreated complete", 100);

        }

        public void CVLValueUpdated(string cvlId, string cvlValueKey)
        {
            ConnectorEvent cvlValueUpdatedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.CVLValueCreated, string.Format("CVL value updated for CVL {0} and key {1}", cvlId, cvlValueKey), 0);
            IntegrationLogger.Write(LogLevel.Debug, string.Format("CVL value updated for CVL {0} and key {1}", cvlId, cvlValueKey));

            try
            {
                RemoteManager.ModelService.ReloadCacheForCVLValuesForCVL(cvlId);
                CVLValue val = RemoteManager.ModelService.GetCVLValueByKey(cvlValueKey, cvlId);
                if (val != null)
                {
                    CVLValue cachedValue = BusinessHelper.CVLValues.FirstOrDefault(cv => cv.CVLId.Equals(cvlId) && cv.Key.Equals(cvlValueKey));
                    if (cachedValue == null)
                    {
                        return;
                    }

                    string folderDateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");
                    new CvlUtility(this.config).AddCvl(cvlId, folderDateTime);

                    if (this.config.ActiveCVLDataMode == CVLDataMode.KeysAndValues || this.config.ActiveCVLDataMode == CVLDataMode.Values)
                    {
                        List<FieldType> allFieldTypes = RemoteManager.ModelService.GetAllFieldTypes();
                        List<FieldType> allFieldsWithThisCvl = allFieldTypes.FindAll(ft => ft.CVLId == cvlId);
                        Query query = new Query
                        {
                            Join = Join.Or,
                            Criteria = new List<Criteria>()
                        };

                        foreach (FieldType fieldType in allFieldsWithThisCvl)
                        {
                            Criteria criteria = new Criteria
                            {
                                FieldTypeId = fieldType.Id,
                                Operator = Operator.Equal,
                                Value = cvlValueKey
                            };

                            query.Criteria.Add(criteria);
                        }

                        List<Entity> entitesWithThisCvlInPim = RemoteManager.DataService.Search(query, LoadLevel.Shallow);
                        if (entitesWithThisCvlInPim.Count == 0)
                        {
                            IntegrationLogger.Write(LogLevel.Debug, string.Format("CVL value updated complete"));

                            ConnectorEventHelper.UpdateConnectorEvent(cvlValueUpdatedConnectorEvent, "CVLValueUpdated complete, no action was needed", 100);
                            return;
                        }

                        List<StructureEntity> channelEntities = ChannelHelper.GetAllEntitiesInChannel(this.config.ChannelId, Configuration.ExportEnabledEntityTypes);

                        List<Entity> entitesToUpdate = new List<Entity>();

                        foreach (Entity entity in entitesWithThisCvlInPim)
                        {
                            if (channelEntities.Any() && channelEntities.Exists(i => i.EntityId.Equals(entity.Id)))
                            {
                                entitesToUpdate.Add(entity);
                            }
                        }

                        foreach (Entity entity in entitesToUpdate)
                        {
                            this.ChannelEntityUpdated(this.config.ChannelId, entity.Id, string.Empty);
                        }
                    }
                }
                else
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Could not update CVL value with key {0} for CVL with id {1}", cvlValueKey, cvlId));
                    ConnectorEventHelper.UpdateConnectorEvent(cvlValueUpdatedConnectorEvent, string.Format("Could not update CVL value with key {0} for CVL with id {1}", cvlValueKey, cvlId), -1, true);
                }
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not add CVL value {0} to CVL with id {1}", cvlValueKey, cvlId), ex);
                ConnectorEventHelper.UpdateConnectorEvent(cvlValueUpdatedConnectorEvent, ex.Message, -1, true);
            }

            IntegrationLogger.Write(LogLevel.Debug, string.Format("CVL value updated complete"));
            ConnectorEventHelper.UpdateConnectorEvent(cvlValueUpdatedConnectorEvent, "CVLValueUpdated complete", 100);
        }

        public void CVLValueDeleted(string cvlId, string cvlValueKey)
        {
            IntegrationLogger.Write(LogLevel.Information, string.Format("CVL value deleted event received with key '{0}' from CVL with id {1}", cvlValueKey, cvlId));
            ConnectorEvent cvlValueDeletedConnectorEvent = ConnectorEventHelper.InitiateConnectorEvent(this.config, ConnectorEventType.CVLValueDeleted, string.Format("CVL value deleted event received with key '{0}' from CVL with id {1}", cvlValueKey, cvlId), 0);

            if (BusinessHelper.CVLValues.RemoveAll(cv => cv.CVLId.Equals(cvlId) && cv.Key.Equals(cvlValueKey)) < 1)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not remove CVL value with key {0} from CVL with id {1}", cvlValueKey, cvlId));
                ConnectorEventHelper.UpdateConnectorEvent(cvlValueDeletedConnectorEvent, string.Format("Could not remove CVL value with key {0} from CVL with id {1}", cvlValueKey, cvlId), -1, true);

                return;
            }

            ConnectorEventHelper.UpdateConnectorEvent(cvlValueDeletedConnectorEvent, "CVLValueDeleted complete", 100);
        }

        public void CVLValueDeletedAll(string cvlId)
        {

        }

        private Entity InitiateChannelConfiguration(int channelId)
        {
            Entity channel = RemoteManager.DataService.GetEntity(channelId, LoadLevel.DataOnly);
            if (channel == null)
            {
                IntegrationLogger.Write(LogLevel.Error, "Could not find channel");
                return null;
            }

            ChannelHelper.UpdateChannelSettings(channel, this.config);
            return channel;
        }

        private bool InitConnector()
        {
            bool result = true;
            try
            {
                if (!Directory.Exists(this.config.PublicationsRootPath))
                {
                    try
                    {
                        Directory.CreateDirectory(this.config.PublicationsRootPath);
                    }
                    catch (Exception exception)
                    {
                        result = false;
                        IntegrationLogger.Write(LogLevel.Error, string.Format("Root directory {0} is missing, and not creatable.\n", this.config.PublicationsRootPath), exception);
                    }
                }
            }
            catch (Exception ex)
            {
                result = false;
                IntegrationLogger.Write(LogLevel.Error, "Error in InitConnector", ex);
            }

            return result;
        }

        private Assembly CurrentDomainAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (folderPath != null)
            {
                int ix = folderPath.LastIndexOf("\\", StringComparison.Ordinal);
                if (ix == -1)
                {
                    return null;
                }

                folderPath = folderPath.Substring(0, ix);
                string assemblyPath = Path.Combine(folderPath, new AssemblyName(args.Name).Name + ".dll");

                if (File.Exists(assemblyPath) == false)
                {
                    assemblyPath = Path.Combine(folderPath + "\\OutboundConnectors\\", new AssemblyName(args.Name).Name + ".dll");
                    if (File.Exists(assemblyPath) == false)
                    {
                        return null;
                    }
                }

                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                return assembly;
            }

            return null;
        }
    }
}
