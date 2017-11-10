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

        public Entity GetParentProduct(StructureEntity structureEntity)
        {
            var channelNodesInPath = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeInPath(structureEntity.Path, "Product");
            var entity = channelNodesInPath.LastOrDefault();
            return entity != null ? RemoteManager.DataService.GetEntity(entity.EntityId, LoadLevel.DataOnly) : null;
        }

        public Entity GetParentChannelNode(StructureEntity structureEntity)
        {
            var channelNodesInPath = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeInPath(structureEntity.Path, "ChannelNode");
            var entity = channelNodesInPath.LastOrDefault();
            return entity != null ? RemoteManager.DataService.GetEntity(entity.EntityId, LoadLevel.DataOnly) : null;
        }

        internal List<StructureEntity> FindEntitiesElementInStructure(List<StructureEntity> channelEntities, int sourceEntityId, int targetEntityId, string linktype)
        {
            List<StructureEntity> structureEntities = new List<StructureEntity>();

            structureEntities.AddRange(channelEntities.Where(e =>
                                                        e.EntityId.Equals(targetEntityId) &&
                                                        e.ParentId != 0 &&
                                                        e.ParentId.Equals(sourceEntityId)));

            return structureEntities;
        }

        internal bool LinkTypeHasLinkEntity(string linkTypeId)
        {
            LinkType linktype = RemoteManager.ModelService.GetLinkType(linkTypeId);
            if (linktype.LinkEntityTypeId != null)
            {
                return true;
            }

            return false;
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

        public List<StructureEntity> GetAllEntitiesInChannel(List<EntityType> entityTypes)
        {
            List<StructureEntity> result = new List<StructureEntity>();
            foreach (EntityType entityType in entityTypes)
            {
                List<StructureEntity> response = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(_config.ChannelId, entityType.Id);
                result.AddRange(response);
            }

            return result;
        }

        public List<StructureEntity> GetAllEntitiesInChannel(string type)
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

        public void UpdateChannelSettings(Entity channel)
        {
            _config.ChannelDefaultLanguage = GetChannelDefaultLanguage(channel);
            _config.ChannelDefaultCurrency = GetChannelDefaultCurrency(channel);
            _config.ChannelDefaultWeightBase = GetChannelDefaultWeightBase(channel);
            _config.ChannelIdPrefix = GetChannelPrefix(channel);
        }

        public string GetChannelPrefix(Entity channel)
        {
            Field channelPrefixField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channelprefix"));
            if (channelPrefixField == null || channelPrefixField.IsEmpty())
            {
                return string.Empty;
            }

            return channelPrefixField.Data.ToString();
        }

        public CultureInfo GetChannelDefaultLanguage(Entity channel)
        {
            Field defaultLanguageField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultlanguage"));
            if (defaultLanguageField == null || defaultLanguageField.IsEmpty())
            {
                return new CultureInfo("en-us");
            }

            return new CultureInfo(defaultLanguageField.Data.ToString());
        }

        public string GetChannelDefaultCurrency(Entity channel)
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

        // TODO: Hvafaen er det her slags navn? Fiks, for pokker.
        // Blir kalt når Code oppdaterer seg, slik at relasjonene mellom ting må oppdateres. Men - er det nødvendig? SJekk Epi-basen, tror den kan endre code uten å gjøre noe med assosiasjoner osv.
        public void EpiCodeFieldUpdatedAddAssociationAndRelationsToDocument(XDocument doc, Entity updatedEntity, Entity channel)
        {
            List<Link> links = new List<Link>();

            if (updatedEntity.EntityType.IsLinkEntityType)
            {
                links = RemoteManager.DataService.GetLinksForLinkEntity(updatedEntity.Id);
            }
            else
            {
                links = RemoteManager.DataService.GetLinksForEntity(updatedEntity.Id);
            }

            var associationsElements = new List<XElement>();
            var relationsElements = new Dictionary<string, XElement>();

            foreach (Link link in links)
            {
                var structureEntityList = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channel.Id, link.Target.Id, link.Source.Id);

                if (!_mappingHelper.IsRelation(link.LinkType))
                {
                    foreach (StructureEntity structureEntity in structureEntityList)
                    {
                        if (!structureEntity.LinkEntityId.HasValue)
                        {
                            associationsElements.Add(_epiElementFactory.CreateCatalogAssociationElement(
                               structureEntity,
                               null));
                        }
                        else
                        {
                            associationsElements.Add(_epiElementFactory.CreateCatalogAssociationElement(
                               structureEntity,
                               link.LinkEntity));
                        }
                    }
                }
                else
                {
                    foreach (StructureEntity structureEntity in structureEntityList)
                    {
                        var parentNode = GetParentChannelNode(structureEntity);

                        if (parentNode == null)
                        {
                            continue;
                        }

                        string skuCode = _catalogCodeGenerator.GetEpiserverCode(structureEntity.EntityId);
                        string parentNodeCode = _catalogCodeGenerator.GetEpiserverCode(parentNode);

                        if (!relationsElements.ContainsKey(skuCode + "_" + parentNodeCode))
                        {
                            relationsElements.Add(skuCode + "_" + parentNodeCode, 
                                                  _epiElementFactory.CreateNodeEntryRelation(parentNode.Id, structureEntity.EntityId, structureEntity.SortOrder));
                        }

                        string parentCode = _catalogCodeGenerator.GetEpiserverCode(structureEntity.ParentId);
                        var relationName = skuCode + "_" + parentCode;

                        if (!relationsElements.ContainsKey(relationName))
                        {
                            var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(
                                                                            structureEntity.ParentId.ToString(CultureInfo.InvariantCulture),
                                                                            link.LinkType.SourceEntityTypeId,
                                                                            structureEntity.EntityId.ToString(),
                                                                            structureEntity.SortOrder);
                            relationsElements.Add(relationName, entryRelationElement);
                        }
                    }
                }
            }

            if (relationsElements.Any())
            {
                doc.Descendants("Relations").ElementAt(0).Add(new XAttribute("totalCount", relationsElements.Count), relationsElements.Values);
            }

            if (associationsElements.Any())
            {
                doc.Descendants("Associations").ElementAt(0).Add(new XAttribute("totalCount", associationsElements.Count), associationsElements);
            }

        }
    }
}