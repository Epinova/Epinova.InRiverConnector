using System;
using System.IO;
using System.Reflection;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.EpiserverAdapter.Utilities;
using inRiver.Integration.Configuration;
using inRiver.Integration.Export;
using inRiver.Integration.Interface;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Connect;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class EpiserverAdapter : ServerListener, IOutboundConnector, IChannelListener, ICVLListener
    {
        private bool _started;
        private Configuration _config;
        private EpiApi _epiApi;
        private EpiElementFactory _epiElementFactory;
        private EpiDocumentFactory _epiDocumentFactory;
        private AddUtility _addUtility;
        private ChannelHelper _channelHelper;
        private ResourceElementFactory _resourceElementFactory;
        private DeleteUtility _deleteUtility;
        private EpiMappingHelper _epiMappingHelper;
        private ChannelPrefixHelper _channelPrefixHelper;
        private ChannelPublisher _publisher;

        public new void Start()
        {
            _config = new Configuration(Id);
            ConnectorEvent connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Start, "Connector is starting", 0);

            try
            {
                ConnectorEventHelper.CleanupOngoingEvents(_config);

                Entity channel = RemoteManager.DataService.GetEntity(_config.ChannelId, LoadLevel.Shallow);
                if (channel == null || channel.EntityType.Id != "Channel")
                {
                    _started = false;
                    ConnectorEventHelper.UpdateEvent(connectorEvent, "Channel id is not valid: Entity with given ID is not a channel, or doesn't exist. Unable to start", -1, true);
                    return;
                }

                _channelPrefixHelper = new ChannelPrefixHelper(_config);
                _epiMappingHelper = new EpiMappingHelper(_config);
                _epiApi = new EpiApi(_config, _epiMappingHelper, _channelPrefixHelper);
                _epiElementFactory = new EpiElementFactory(_config, _epiMappingHelper, _channelPrefixHelper);
                _channelHelper = new ChannelHelper(_config, _epiElementFactory, _epiMappingHelper, _channelPrefixHelper);
                _epiDocumentFactory = new EpiDocumentFactory(_config, _epiApi, _epiElementFactory, _epiMappingHelper, _channelHelper, _channelPrefixHelper);
                _resourceElementFactory = new ResourceElementFactory(_epiElementFactory, _epiMappingHelper, _channelPrefixHelper);
                _addUtility = new AddUtility(_config, _epiApi, _epiDocumentFactory, _resourceElementFactory, _channelHelper);
                _deleteUtility = new DeleteUtility(_config, _resourceElementFactory, _epiElementFactory, _channelHelper, _epiApi, _channelPrefixHelper);

                _publisher = new ChannelPublisher(_config, 
                                                  _channelHelper, 
                                                  _epiDocumentFactory, 
                                                  _epiElementFactory,
                                                  _resourceElementFactory, 
                                                  _epiApi,
                                                  _epiMappingHelper, 
                                                  _addUtility,
                                                  _deleteUtility,
                                                  _channelPrefixHelper);

                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainAssemblyResolve;

                if (!InitConnector())
                {
                    return;
                }

                base.Start();
                _started = true;
                ConnectorEventHelper.UpdateEvent(connectorEvent, "Connector has started", 100);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Error while starting connector", ex);
                ConnectorEventHelper.UpdateEvent(connectorEvent, "Issue while starting, see log.", 100, true);
                throw;
            }
        }

        public new void Stop()
        {
            base.Stop();
            _started = false;
            ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Stop, "Connector is stopped", 100);
        }

        public new void InitConfigurationSettings()
        {
            ConfigurationManager.Instance.SetConnectorSetting(Id, "PUBLISH_FOLDER", @"C:\temp\Publish\Epi");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "PUBLISH_FOLDER_RESOURCES", @"C:\temp\Publish\Epi\Resources");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "RESOURCE_CONFIGURATION", "Preview");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "LANGUAGE_MAPPING", "<languages><language><epi>en</epi><inriver>en-us</inriver></language></languages>");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "ITEM_TO_SKUs", "false");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "CVL_DATA", "Keys|Values|KeysAndValues");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "BUNDLE_ENTITYTYPES", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, "PACKAGE_ENTITYTYPES", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, "DYNAMIC_PACKAGE_ENTITYTYPES", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, "CHANNEL_ID", "123");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "EPI_CODE_FIELDS", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, "EXCLUDE_FIELDS", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, "EPI_NAME_FIELDS", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, "USE_THREE_LEVELS_IN_COMMERCE", "false");
            ConfigurationManager.Instance.SetConnectorSetting(Id, "HTTP_POST_URL", string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiEndpoint, "https://www.example.com/inriverapi/InriverDataImport/");
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiApiKey, "SomeGreatKey123");
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiTimeout, "1");
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ExportEntities, ConfigDefaults.ExportEntities);
            ConfigurationManager.Instance.SetConnectorSetting(Id, "BATCH_SIZE", string.Empty);
        }

        public new bool IsStarted()
        {
            return _started;
        }

        public void Publish(int channelId)
        {
            if (channelId != _config.ChannelId)
                return;

            _publisher.Publish(channelId);
        }

        public void UnPublish(int channelId)
        {
            if (channelId != _config.ChannelId)
                return;

            IntegrationLogger.Write(LogLevel.Information, string.Format("Unpublish on channel: {0} called. No action made.", channelId));
        }

        public void Synchronize(int channelId)
        {
        }

        public void ChannelEntityAdded(int channelId, int entityId)
        {
            if (channelId != _config.ChannelId)
                return;

            _publisher.ChannelEntityAdded(channelId, entityId);
        }
        
        public void ChannelEntityUpdated(int channelId, int entityId, string data)
        {
            if (channelId != _config.ChannelId)
                return;

            _publisher.ChannelEntityUpdated(channelId, entityId, data);
        }

        public void ChannelEntityDeleted(int channelId, Entity deletedEntity)
        {
            if (channelId != _config.ChannelId)
                return;
            
            _publisher.ChannelEntityDeleted(channelId, deletedEntity);
        }

        public void ChannelEntityFieldSetUpdated(int channelId, int entityId, string fieldSetId)
        {
            if (channelId != _config.ChannelId)
            {
                return;
            }

            ChannelEntityUpdated(channelId, entityId, null);
        }

        public void ChannelEntitySpecificationFieldAdded(int channelId, int entityId, string fieldName)
        {
            if (channelId != _config.ChannelId)
            {
                return;
            }

            ChannelEntityUpdated(channelId, entityId, null);
        }

        public void ChannelEntitySpecificationFieldUpdated(int channelId, int entityId, string fieldName)
        {
            if (channelId != _config.ChannelId)
            {
                return;
            }

            ChannelEntityUpdated(channelId, entityId, null);
        }

        public void ChannelLinkAdded(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            if (channelId != _config.ChannelId)
            {
                return;
            }
            
            _publisher.ChannelLinkAdded(channelId, sourceEntityId, targetEntityId, linkTypeId, linkEntityId);
        }

        public void ChannelLinkDeleted(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            if (channelId != _config.ChannelId)
            {
                return;
            }

            _publisher.ChannelLinkDeleted(channelId, sourceEntityId, targetEntityId, linkTypeId, linkEntityId);
        }

        public void ChannelLinkUpdated(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            if (channelId != _config.ChannelId)
                return;

            _publisher.ChannelLinkUpdated(channelId, sourceEntityId, targetEntityId, linkTypeId, linkEntityId);
        }

        public void AssortmentCopiedInChannel(int channelId, int assortmentId, int targetId, string targetType)
        {

        }

        private bool InitConnector()
        {
            bool result = true;
            try
            {
                if (!Directory.Exists(_config.PublicationsRootPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_config.PublicationsRootPath);
                    }
                    catch (Exception exception)
                    {
                        result = false;
                        IntegrationLogger.Write(LogLevel.Error, string.Format("Root directory {0} is missing, and not creatable.\n", _config.PublicationsRootPath), exception);
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

        public void CVLValueCreated(string cvlId, string cvlValueKey)
        {
        }

        public void CVLValueUpdated(string cvlId, string cvlValueKey)
        {
            // TODO: Search all entities with this CVL and cvlValueKey, and pass on updates to episerver
        }

        public void CVLValueDeleted(string cvlId, string cvlValueKey)
        {
        }

        public void CVLValueDeletedAll(string cvlId)
        {
        }
    }
}