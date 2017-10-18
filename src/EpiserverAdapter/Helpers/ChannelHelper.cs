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
        private readonly EpiElementFactory _epiElementFactory;

        public ChannelHelper(EpiElementFactory epiElementFactory)
        {
            _epiElementFactory = epiElementFactory;
        }

        public static Guid GetChannelGuid(Entity channel, Configuration configuration)
        {
            string value = channel.Id.ToString(CultureInfo.InvariantCulture);

            if (channel.DisplayName != null && !channel.DisplayName.IsEmpty())
            {
                if (channel.DisplayName.FieldType.DataType.Equals(DataType.LocaleString))
                {
                    value =
                        ((LocaleString)channel.DisplayName.Data)[configuration.LanguageMapping[configuration.ChannelDefaultLanguage]];
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

        public static int GetParentChannelNode(StructureEntity structureEntity, Configuration config)
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

                StructureEntity foundStructureEntity = config.ChannelStructureEntities.Find(i => i.EntityId.Equals(tempEntityId));

                if (foundStructureEntity != null && foundStructureEntity.Type == "ChannelNode")
                {
                    entityId = tempEntityId;
                    break;
                }
            }

            return entityId;
        }

        internal static int GetParentChannelNode(StructureEntity structureEntity, int channelId)
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

        internal static List<StructureEntity> FindEntitiesElementInStructure(List<StructureEntity> channelEntities, int sourceEntityId, int targetEntityId, string linktype)
        {
            List<StructureEntity> structureEntities = new List<StructureEntity>();

            structureEntities.AddRange(channelEntities.Where(e =>
                                                        e.EntityId.Equals(targetEntityId) &&
                                                        e.ParentId != 0 &&
                                                        e.ParentId.Equals(sourceEntityId)));

            return structureEntities;
        }

        internal static bool LinkTypeHasLinkEntity(string linkTypeId)
        {
            LinkType linktype = RemoteManager.ModelService.GetLinkType(linkTypeId);
            if (linktype.LinkEntityTypeId != null)
            {
                return true;
            }

            return false;
        }

        public static string GetChannelIdentifier(Entity channelEntity)
        {
            string channelIdentifier = channelEntity.Id.ToString(CultureInfo.InvariantCulture);
            if (channelEntity.DisplayName != null && !channelEntity.DisplayName.IsEmpty())
            {
                channelIdentifier = channelEntity.DisplayName.Data.ToString();
            }

            return channelIdentifier;
        }

        public static List<StructureEntity> GetAllEntitiesInChannel(int channelId, List<EntityType> entityTypes)
        {
            List<StructureEntity> result = new List<StructureEntity>();
            foreach (EntityType entityType in entityTypes)
            {
                List<StructureEntity> response = RemoteManager.ChannelService.GetAllChannelStructureEntitiesForType(channelId, entityType.Id);
                result.AddRange(response);
            }

            return result;
        }

        public static List<StructureEntity> GetEntityInChannelWithParent(int channelId, int entityId, int parentId)
        {
            var result = new List<StructureEntity>();
            var response = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityWithParentInChannel(channelId, entityId, parentId);
            if (response.Any())
            {
                result.AddRange(response);
            }

            return result;
        }

        public static string GetTargetEntityPath(int targetEntityId, List<StructureEntity> channelEntities, int? parentId = null)
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

        public static List<StructureEntity> GetChildrenEntitiesInChannel(int entityId, string path)
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

        public static List<StructureEntity> GetStructureEntitiesForEntityInChannel(int channelId, int entityId)
        {
            return RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, entityId);
        }

        public static StructureEntity GetParentStructureEntity(int channelId, int sourceEntityId, int targetEntityId, List<StructureEntity> channelEntities)
        {
            StructureEntity targetStructureEntity =
                channelEntities.Find(i => i.EntityId.Equals(targetEntityId) && i.ParentId.Equals(sourceEntityId));

            List<StructureEntity> structureEntities =
                RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(channelId, sourceEntityId);

            if (targetStructureEntity == null || !structureEntities.Any())
            {
                return null;
            }

            int endIndex = targetStructureEntity.Path.LastIndexOf("/", StringComparison.InvariantCulture);

            string parentPath = targetStructureEntity.Path.Substring(0, endIndex);

            return structureEntities.Find(i => i.Path.Equals(parentPath) && i.EntityId.Equals(sourceEntityId));
        }

        public static void UpdateChannelSettings(Entity channel, Configuration configuration)
        {
            configuration.ChannelDefaultLanguage = GetChannelDefaultLanguage(channel);
            configuration.ChannelDefaultCurrency = GetChannelDefaultCurrency(channel);
            configuration.ChannelDefaultWeightBase = GetChannelDefaultWeightBase(channel);
            configuration.ChannelIdPrefix = GetChannelPrefix(channel);
            configuration.ChannelMimeTypeMappings = GetChannelMimeTypeMappings(channel);
        }

        public static string GetChannelPrefix(Entity channel)
        {
            Field channelPrefixField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channelprefix"));
            if (channelPrefixField == null || channelPrefixField.IsEmpty())
            {
                return string.Empty;
            }

            return channelPrefixField.Data.ToString();
        }

        public static Dictionary<string, string> GetChannelMimeTypeMappings(Entity channel)
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

        public static CultureInfo GetChannelDefaultLanguage(Entity channel)
        {
            Field defaultLanguageField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultlanguage"));
            if (defaultLanguageField == null || defaultLanguageField.IsEmpty())
            {
                return new CultureInfo("en-us");
            }

            return new CultureInfo(defaultLanguageField.Data.ToString());
        }

        public static string GetChannelDefaultCurrency(Entity channel)
        {
            Field defaultCurrencyField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultcurrency"));
            if (defaultCurrencyField == null || defaultCurrencyField.IsEmpty())
            {
                return "usd";
            }

            return defaultCurrencyField.Data.ToString();
        }

        public static string GetChannelDefaultWeightBase(Entity channel)
        {
            Field defaultWeightBaseField = channel.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("channeldefaultweightbase"));
            if (defaultWeightBaseField == null || defaultWeightBaseField.IsEmpty())
            {
                return "lbs";
            }

            return defaultWeightBaseField.Data.ToString();
        }

        public List<XElement> GetParentXElements(Entity parentEntity, Configuration configuration)
        {
            List<XElement> elements = new List<XElement>();
            List<string> parents = new List<string>();
            if (parentEntity == null)
            {
                return elements;
            }

            if (parentEntity.EntityType.Id == "Item" && configuration.ItemsToSkus)
            {
                parents = _epiElementFactory.SkuItemIds(parentEntity, configuration);
            }
            else
            {
                parents.Add(parentEntity.Id.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var parent in parents)
            {
                XElement parentElement = new XElement("parent", ChannelPrefixHelper.GetEpiserverCode(parent, configuration));
                elements.Add(parentElement);
            }

            return elements;
        }

        internal static List<string> GetResourceIds(XElement deletedElement, Configuration configuration)
        {
            List<string> foundResources = new List<string>();
            foreach (
                XElement resourceElement in
                    deletedElement.Descendants().Where(e => e.Name.LocalName.Contains("Resource_")))
            {
                foundResources.Add(configuration.ChannelIdPrefix + resourceElement.Name.LocalName.Split('_')[1]);
            }

            return foundResources;
        }

        public static Dictionary<string, bool> ShouldEntityExistInChannelNodes(int entityId, List<StructureEntity> channelNodes, int channelId)
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

        public static void BuildEntityIdAndTypeDict(Configuration config)
        {
            Dictionary<int, string> entityIdAndType = new Dictionary<int, string>();

            foreach (StructureEntity structureEntity in config.ChannelStructureEntities)
            {
                if (!entityIdAndType.ContainsKey(structureEntity.EntityId))
                {
                    entityIdAndType.Add(structureEntity.EntityId, structureEntity.Type);
                }
            }

            config.EntityIdAndType = entityIdAndType;
        }

        public void EpiCodeFieldUpdatedAddAssociationAndRelationsToDocument(XDocument doc, Entity updatedEntity, Configuration config, int channelId)
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

                if (!EpiMappingHelper.IsRelation(
                link.LinkType.SourceEntityTypeId,
                link.LinkType.TargetEntityTypeId,
                link.LinkType.Index,
                config))
                {
                    foreach (StructureEntity structureEntity in structureEntityList)
                    {
                        if (!structureEntity.LinkEntityId.HasValue)
                        {
                            associationsElements.Add(_epiElementFactory.CreateCatalogAssociationElement(
                               structureEntity,
                               null,
                               config));
                        }
                        else
                        {
                            associationsElements.Add(_epiElementFactory.CreateCatalogAssociationElement(
                               structureEntity,
                               link.LinkEntity,
                               config));
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

                        string channelPrefixAndSkuId = ChannelPrefixHelper.GetEpiserverCode(structureEntity.EntityId, config);
                        string channelPrefixAndParentNodeId = ChannelPrefixHelper.GetEpiserverCode(parentNodeId, config);

                        if (!relationsElements.ContainsKey(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId))
                        {
                            relationsElements.Add(channelPrefixAndSkuId + "_" + channelPrefixAndParentNodeId,
                                _epiElementFactory.CreateNodeEntryRelationElement(
                                    parentNodeId.ToString(CultureInfo.InvariantCulture),
                                    structureEntity.EntityId.ToString(),
                                    structureEntity.SortOrder,
                                    config));
                        }

                        string channelPrefixAndParent = ChannelPrefixHelper.GetEpiserverCode(structureEntity.ParentId, config);

                        if (!relationsElements.ContainsKey(channelPrefixAndSkuId + "_" + channelPrefixAndParent))
                        {
                            relationsElements.Add(channelPrefixAndSkuId + "_" + channelPrefixAndParent,
                                _epiElementFactory.CreateEntryRelationElement(
                                        structureEntity.ParentId.ToString(CultureInfo.InvariantCulture),
                                        link.LinkType.SourceEntityTypeId,
                                       structureEntity.EntityId.ToString(),
                                        structureEntity.SortOrder, config));
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