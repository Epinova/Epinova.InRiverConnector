namespace inRiver.EPiServerCommerce.CommerceAdapter.Helpers
{
    using System.Collections.Generic;
    using System.Linq;

    using inRiver.EPiServerCommerce.CommerceAdapter;
    using inRiver.Remoting;
    using inRiver.Remoting.Objects;

    public class ChannelPrefixHelper
    {
        internal static string GetEPiCodeWithChannelPrefix(object code, Configuration configuration)
        {
            int entityId;
            string codeValue = code.ToString();
            if (!string.IsNullOrEmpty(codeValue))
            {
                if (!string.IsNullOrEmpty(configuration.ChannelIdPrefix) && codeValue.StartsWith(configuration.ChannelIdPrefix))
                {
                    // If the code is an entity id we should move on
                    // otherwise we should assume the code is what should be returned.
                    if (!int.TryParse(codeValue, out entityId))
                    {
                        return codeValue;
                    }

                    if (!configuration.EntityIdAndType.ContainsKey(entityId))
                    {
                        if (RemoteManager.DataService.GetEntity(entityId, LoadLevel.Shallow) == null)
                        {
                            return codeValue;
                        }
                    }
                }
            }

            // Check if code is int
            if (!int.TryParse(codeValue, out entityId))
            {
                return configuration.ChannelIdPrefix + codeValue;
            }

            int? existingEntityId = null;
            string type = string.Empty;

            if (!configuration.EntityIdAndType.ContainsKey(entityId))
            {
                //GetAllStructureEntities if its not a LinkEntity.
                if (!configuration.ChannelStructureEntities.Exists(i => i.LinkEntityId != null && i.LinkEntityId.Value.Equals(entityId)))
                {
                    List<StructureEntity> entities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(configuration.ChannelId, entityId);

                    if (entities.Any())
                    {
                        configuration.ChannelStructureEntities.AddRange(entities);
                        configuration.EntityIdAndType.Add(entities[0].EntityId, entities[0].Type);

                        existingEntityId = entities[0].EntityId;
                        type = entities[0].Type;
                    }
                    else if(configuration.ChannelEntities.ContainsKey(entityId))
                    {
                        Entity channelEntity = configuration.ChannelEntities[entityId];
                        existingEntityId = channelEntity.Id;
                        type = channelEntity.EntityType.Id;
                    }
                }
                else
                {
                    if (configuration.ChannelEntities.ContainsKey(entityId))
                    {
                        Entity channelEntity = configuration.ChannelEntities[entityId];
                        existingEntityId = channelEntity.Id;
                        type = channelEntity.EntityType.Id;
                    } 
                }
            }
            else
            {
                type = configuration.EntityIdAndType[entityId];
                existingEntityId = entityId;
            }

            if (existingEntityId == null)
            {
                return configuration.ChannelIdPrefix + code;
            }

            // Check if entity type is mapped
            if (!configuration.EpiCodeMapping.ContainsKey(type))
            {
                return configuration.ChannelIdPrefix + existingEntityId;
            }

            Entity entity;

            if (configuration.ChannelEntities != null && configuration.ChannelEntities.ContainsKey(entityId))
            {
                entity = configuration.ChannelEntities[entityId];
            }
            else
            {
                entity = RemoteManager.DataService.GetEntity(entityId, LoadLevel.DataOnly);
                configuration.ChannelEntities?.Add(entity.Id, entity);
            }

            Field codeField = entity.GetField(configuration.EpiCodeMapping[type]);

            // Check if code field exists
            if (codeField == null)
            {
                return configuration.ChannelIdPrefix + entity.Id;
            }

            // Check if code data is null
            if (codeField.Data == null || codeField.IsEmpty())
            {
                return configuration.ChannelIdPrefix + entity.Id;
            }

            return configuration.ChannelIdPrefix + codeField.Data;
        }
    }
}