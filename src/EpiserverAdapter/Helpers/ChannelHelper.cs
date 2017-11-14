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

        public List<StructureEntity> GetAllStructureEntitiesInChannel(List<EntityType> entityTypes)
        {
            List<StructureEntity> result = new List<StructureEntity>();
            foreach (EntityType entityType in entityTypes)
            {
                List<StructureEntity> response = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(_config.ChannelId, entityType.Id);
                result.AddRange(response);
            }

            return _config.ForceIncludeLinkedContent ? 
                        result.ToList() : 
                        result.Where(x => FilterLinkedContentNotBelongingToChannelNode(x, result)).ToList();
        }

        /// <summary>
        /// Tells you whether or not a structure entity belongs in the channel, based on it's links.
        /// True for any entity that has a direct relation with either a product/bundle/package, or a channel node. False for 
        /// anything that's ONLY included in the channel as upsell/accessories and the like (typically item-item-links or product-product-links).
        /// </summary>
        /// <param name="structureEntity">The StructureEntity to query.</param>
        /// <param name="allStructureEntities">Everything in the channel.</param>
        private bool FilterLinkedContentNotBelongingToChannelNode(StructureEntity structureEntity, List<StructureEntity> allStructureEntities)
        {
            var sameEntityStructureEntities = allStructureEntities.Where(x => x.EntityId == structureEntity.EntityId);
            return sameEntityStructureEntities.Any(BelongsInChannel);
        }

        private bool BelongsInChannel(StructureEntity arg)
        {
            var isRelation = _mappingHelper.IsRelation(arg.LinkTypeIdFromParent);
            var isChannelNodeLink = _mappingHelper.IsChannelNodeLink(arg.LinkTypeIdFromParent);

            return isRelation || isChannelNodeLink;
        }

        public List<StructureEntity> GetAllStructureEntitiesInChannel(string type)
        {
            return RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(_config.ChannelId, type);
        }
        
        public List<StructureEntity> GetEntityInChannelWithParent(int channelId, int entityId, int parentId)
        {
            var result = new List<StructureEntity>();
            var response = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channelId, entityId, parentId);
            if (response.Any())
            {
                result.AddRange(response);
            }

            return result;
        }

        public string GetTargetEntityPath(int targetEntityId, List<StructureEntity> channelEntities, int? parentId = null)
        {
            StructureEntity targetStructureEntity = new StructureEntity();

            if (parentId == null)
            {
                targetStructureEntity = channelEntities.Find(i => i.EntityId.Equals(targetEntityId));
            }
            else
            {
                targetStructureEntity = channelEntities.Find(i => i.EntityId.Equals(targetEntityId) && i.ParentId.Equals(parentId));
            }


            string path = string.Empty;

            if (targetStructureEntity != null)
            {
                path = targetStructureEntity.Path;
            }

            return path;
        }

        public List<StructureEntity> GetChildrenEntitiesInChannel(int entityId, string path)
        {
            var result = new List<StructureEntity>();
            if (!string.IsNullOrEmpty(path))
            {
                var response = RemoteManager.ChannelService.GetChannelStructureChildrenFromPath(entityId, path);
                if (response.Any())
                {
                    result.AddRange(response);
                }
            }

            return result;
        }

        public List<StructureEntity> GetStructureEntitiesForEntityInChannel(int channelId, int entityId)
        {
            return RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, entityId);
        }

        public StructureEntity GetParentStructureEntity(int channelId, int sourceEntityId, int targetEntityId, List<StructureEntity> channelEntities)
        {
            var targetStructureEntity = channelEntities.Find(i => i.EntityId.Equals(targetEntityId) && i.ParentId.Equals(sourceEntityId));
            var structureEntities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, sourceEntityId);

            if (targetStructureEntity == null || !structureEntities.Any())
            {
                return null;
            }

            int endIndex = targetStructureEntity.Path.LastIndexOf("/", StringComparison.InvariantCulture);

            string parentPath = targetStructureEntity.Path.Substring(0, endIndex);

            return structureEntities.Find(i => i.Path.Equals(parentPath) && i.EntityId.Equals(sourceEntityId));
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