using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public class ChannelHelper
    {
        private readonly IConfiguration _config;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;

        public ChannelHelper(IConfiguration config, EpiElementFactory epiElementFactory, EpiMappingHelper mappingHelper, CatalogCodeGenerator catalogCodeGenerator)
        {
            _config = config;
            _epiElementFactory = epiElementFactory;
            _mappingHelper = mappingHelper;
            _catalogCodeGenerator = catalogCodeGenerator;
        }


        public Entity InitiateChannelConfiguration(int channelId)
        {
            Entity channel = RemoteManager.DataService.GetEntity(channelId, LoadLevel.DataOnly);
            if (channel == null)
            {
                IntegrationLogger.Write(LogLevel.Error, "Could not find channel");
                return null;
            }

            UpdateChannelSettings(channel);
            return channel;
        }

        public Guid GetChannelGuid(Entity channel)
        {
            string value = channel.Id.ToString(CultureInfo.InvariantCulture);

            if (channel.DisplayName != null && !channel.DisplayName.IsEmpty())
            {
                if (channel.DisplayName.FieldType.DataType.Equals(DataType.LocaleString))
                {
                    var cultureInfo = _config.LanguageMapping[_config.ChannelDefaultLanguage];
                    value = ((LocaleString)channel.DisplayName.Data)[cultureInfo];
                }
                else
                {
                    value = channel.DisplayName.Data.ToString();
                }

                if (string.IsNullOrEmpty(value))
                {
                    value = channel.Id.ToString(CultureInfo.InvariantCulture);
                }
            }

            MD5 md5Hasher = MD5.Create();
            byte[] data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(value));
            return new Guid(data);
        }

        public Entity GetParentProduct(StructureEntity itemStructureEntity)
        {
            var inboundLinks = RemoteManager.DataService.GetInboundLinksForEntity(itemStructureEntity.EntityId);
            var relationLink = inboundLinks.FirstOrDefault(x => _mappingHelper.IsRelation(x.LinkType));

            return relationLink != null ? RemoteManager.DataService.GetEntity(relationLink.Source.Id, LoadLevel.DataOnly) : null;
        }

        public Entity GetParentChannelNode(StructureEntity structureEntity)
        {
            var channelNodesInPath = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeInPath(structureEntity.Path, "ChannelNode");
            var entity = channelNodesInPath.LastOrDefault();
            return entity != null ? RemoteManager.DataService.GetEntity(entity.EntityId, LoadLevel.DataOnly) : null;
        }

        public string GetChannelIdentifier(Entity channelEntity)
        {
            string channelIdentifier = channelEntity.Id.ToString(CultureInfo.InvariantCulture);
            if (channelEntity.DisplayName != null && !channelEntity.DisplayName.IsEmpty())
            {
                channelIdentifier = channelEntity.DisplayName.Data.ToString();
            }

            return channelIdentifier;
        }
        

        private string GetChannelPrefix(Entity channel)
        {
            Field channelPrefixField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channelprefix"));
            if (channelPrefixField == null || channelPrefixField.IsEmpty())
            {
                return string.Empty;
            }

            return channelPrefixField.Data.ToString();
        }

        private CultureInfo GetChannelDefaultLanguage(Entity channel)
        {
            Field defaultLanguageField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultlanguage"));
            if (defaultLanguageField == null || defaultLanguageField.IsEmpty())
            {
                return new CultureInfo("en-us");
            }

            return new CultureInfo(defaultLanguageField.Data.ToString());
        }

        private string GetChannelDefaultCurrency(Entity channel)
        {
            Field defaultCurrencyField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultcurrency"));
            if (defaultCurrencyField == null || defaultCurrencyField.IsEmpty())
            {
                return "usd";
            }

            return defaultCurrencyField.Data.ToString();
        }

        public string GetChannelDefaultWeightBase(Entity channel)
        {
            Field defaultWeightBaseField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultweightbase"));
            if (defaultWeightBaseField == null || defaultWeightBaseField.IsEmpty())
            {
                return "lbs";
            }

            return defaultWeightBaseField.Data.ToString();
        }

        public List<XElement> GetParentXElements(Entity parentEntity)
        {
            var elements = new List<XElement>();

            if (parentEntity == null)
            {
                return elements;
            }

            if (parentEntity.EntityType.Id == "Item" && _config.ItemsToSkus)
            {
                var parents = _epiElementFactory.SkuItemIds(parentEntity);
                elements.AddRange(parents.Select(parent => new XElement("parent", _catalogCodeGenerator.GetPrefixedCode(parent))));
            }
            else
            {
                elements.Add(new XElement("parent", _catalogCodeGenerator.GetEpiserverCode(parentEntity)));
            }

            return elements;
        }

        public Dictionary<string, bool> ShouldEntityExistInChannelNodes(int entityId, List<StructureEntity> channelNodes, int channelId)
        {
            Dictionary<string, bool> dictionary = new Dictionary<string, bool>();

            var entities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, entityId);
            foreach (var node in channelNodes)
            {
                bool result = entities.Any(x => x.ParentId == node.EntityId);
                if (result)
                {
                    IntegrationLogger.Write(LogLevel.Debug, $"Entity {entityId} exists in channel node {node.EntityId}");
                }

                if (!dictionary.ContainsKey(node.EntityId.ToString()))
                {
                    dictionary.Add(_catalogCodeGenerator.GetEpiserverCode(node.EntityId), result);
                }
            }

            return dictionary;
        }

        private void UpdateChannelSettings(Entity channel)
        {
            _config.ChannelDefaultLanguage = GetChannelDefaultLanguage(channel);
            _config.ChannelDefaultCurrency = GetChannelDefaultCurrency(channel);
            _config.ChannelDefaultWeightBase = GetChannelDefaultWeightBase(channel);
            _config.ChannelIdPrefix = GetChannelPrefix(channel);
        }
    }
}