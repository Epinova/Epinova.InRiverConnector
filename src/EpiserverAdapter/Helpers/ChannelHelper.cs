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
        private readonly Configuration _config;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly EpiMappingHelper _mappingHelper;
        private readonly ChannelPrefixHelper _channelPrefixHelper;

        public ChannelHelper(Configuration config, EpiElementFactory epiElementFactory, EpiMappingHelper mappingHelper, ChannelPrefixHelper channelPrefixHelper)
        {
            _config = config;
            _epiElementFactory = epiElementFactory;
            _mappingHelper = mappingHelper;
            _channelPrefixHelper = channelPrefixHelper;
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

        public int GetParentChannelNode(StructureEntity structureEntity)
        {
            int entityId = 0;
            List<string> entities = structureEntity.Path.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            entities.RemoveAt(entities.Count - 1);
            entities.RemoveAt(0);
            if (entities.Count == 0)
            {
                return entityId;
            }

            for (int index = entities.Count - 1; index > -1; index--)
            {
                int tempEntityId = int.Parse(entities[index]);

                StructureEntity foundStructureEntity = _config.ChannelStructureEntities.Find(i => i.EntityId.Equals(tempEntityId));

                if (foundStructureEntity != null && foundStructureEntity.Type == "ChannelNode")
                {
                    entityId = tempEntityId;
                    break;
                }
            }

            return entityId;
        }

        internal int GetParentChannelNode(StructureEntity structureEntity, int channelId)
        {
            int parentNodeId = 0;

            List<string> parentIds = structureEntity.Path.Split('/').ToList();
            parentIds.Reverse();
            parentIds.RemoveAt(0);

            for (int i = 0; i < parentIds.Count - 1; i++)
            {
                int entityId = int.Parse(parentIds[i]);
                int parentId = int.Parse(parentIds[i + 1]);

                var structureEntities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(
                    channelId,
                    entityId,
                    parentId);

                foreach (var se in structureEntities)
                {
                    if (se.Type == "ChannelNode")
                    {
                        parentNodeId = se.EntityId;
                        break;
                    }
                }

                if (parentNodeId != 0)
                {
                    break;
                }

            }

            return parentNodeId;
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

        public List<StructureEntity> GetAllEntitiesInChannel(int channelId, List<EntityType> entityTypes)
        {
            List<StructureEntity> result = new List<StructureEntity>();
            foreach (EntityType entityType in entityTypes)
            {
                List<StructureEntity> response = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(channelId, entityType.Id);
                result.AddRange(response);
            }

            return result;
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

        // TODO: What, why? Kan fjernes? Den oppdaterer _config med settings fra kanalnoden DERSOM FELTENE FINNES...?
        public void UpdateChannelSettings(Entity channel)
        {
            _config.ChannelDefaultLanguage = GetChannelDefaultLanguage(channel);
            _config.ChannelDefaultCurrency = GetChannelDefaultCurrency(channel);
            _config.ChannelDefaultWeightBase = GetChannelDefaultWeightBase(channel);
            _config.ChannelIdPrefix = GetChannelPrefix(channel);
            _config.ChannelMimeTypeMappings = GetChannelMimeTypeMappings(channel);
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

        public Dictionary<string, string> GetChannelMimeTypeMappings(Entity channel)
        {
            Dictionary<string, string> channelMimeTypeMappings = new Dictionary<string, string>();
            Field channelMimeTypeField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channelmimetypemappings"));
            if (channelMimeTypeField == null || channelMimeTypeField.IsEmpty())
            {
                return channelMimeTypeMappings;
            }

            string channelMapping = channelMimeTypeField.Data.ToString();

            if (!channelMapping.Contains(','))
            {
                return channelMimeTypeMappings;
            }

            string[] mappings = channelMapping.Split(';');

            foreach (string mapping in mappings)
            {
                if (!mapping.Contains(','))
                {
                    continue;
                }

                string[] map = mapping.Split(',');
                channelMimeTypeMappings.Add(map[0].Trim(), map[1].Trim());
            }

            return channelMimeTypeMappings;
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
            List<XElement> elements = new List<XElement>();
            List<string> parents = new List<string>();
            if (parentEntity == null)
            {
                return elements;
            }

            if (parentEntity.EntityType.Id == "Item" && _config.ItemsToSkus)
            {
                parents = _epiElementFactory.SkuItemIds(parentEntity, _config);
            }
            else
            {
                parents.Add(parentEntity.Id.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var parent in parents)
            {
                XElement parentElement = new XElement("parent", _channelPrefixHelper.GetEpiserverCode(parent));
                elements.Add(parentElement);
            }

            return elements;
        }

        internal List<string> GetResourceIds(XElement deletedElement)
        {
            List<string> foundResources = new List<string>();
            foreach (
                XElement resourceElement in
                    deletedElement.Descendants().Where(e => e.Name.LocalName.Contains("Resource_")))
            {
                foundResources.Add(_config.ChannelIdPrefix + resourceElement.Name.LocalName.Split('_')[1]);
            }

            return foundResources;
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
                    dictionary.Add(node.EntityId.ToString(), result);
                }
            }

            return dictionary;
        }

        public void BuildEntityIdAndTypeDict()
        {
            Dictionary<int, string> entityIdAndType = new Dictionary<int, string>();

            foreach (StructureEntity structureEntity in _config.ChannelStructureEntities)
            {
                if (!entityIdAndType.ContainsKey(structureEntity.EntityId))
                {
                    entityIdAndType.Add(structureEntity.EntityId, structureEntity.Type);
                }
            }

            _config.EntityIdAndType = entityIdAndType;
        }

        // TODO: Hvafaen er det her slags navn? Fiks, for pokker.
        public void EpiCodeFieldUpdatedAddAssociationAndRelationsToDocument(XDocument doc, Entity updatedEntity, int channelId)
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

            List<XElement> associationsElements = new List<XElement>();

            Dictionary<string, XElement> relationsElements = new Dictionary<string, XElement>();

            foreach (Link link in links)
            {
                var structureEntityList = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel
                            (channelId, link.Target.Id, link.Source.Id);

                if (!_mappingHelper.IsRelation(link.LinkType.SourceEntityTypeId, link.LinkType.TargetEntityTypeId, link.LinkType.Index))
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
                        int parentNodeId = GetParentChannelNode(structureEntity, channelId);

                        if (parentNodeId == 0)
                        {
                            continue;
                        }

                        string channelPrefixAndSkuId = _channelPrefixHelper.GetEpiserverCode(structureEntity.EntityId);
                        string channelPrefixAndParentNodeId = _channelPrefixHelper.GetEpiserverCode(parentNodeId);

                        if (!relationsElements.ContainsKey(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId))
                        {
                            relationsElements.Add(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId,
                                _epiElementFactory.CreateNodeEntryRelationElement(
                                    parentNodeId.ToString(CultureInfo.InvariantCulture),
                                    structureEntity.EntityId.ToString(),
                                    structureEntity.SortOrder,
                                    _config));
                        }

                        string channelPrefixAndParent = _channelPrefixHelper.GetEpiserverCode(structureEntity.ParentId);

                        var relationName = channelPrefixAndSkuId + "_" + channelPrefixAndParent;

                        if (!relationsElements.ContainsKey(relationName))
                        {
                            var entryRelationElement = _epiElementFactory.CreateEntryRelationElement(
                                structureEntity.ParentId.ToString(CultureInfo.InvariantCulture),
                                link.LinkType.SourceEntityTypeId,
                                structureEntity.EntityId.ToString(),
                                structureEntity.SortOrder, _config);
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