using System;
using System.Linq;
using inRiver.Remoting;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class CatalogCodeGenerator
    {
        private readonly IConfiguration _config;
        private readonly IEntityService _entityService;

        public CatalogCodeGenerator()
        {
            
        }
        public CatalogCodeGenerator(IConfiguration config, IEntityService entityService)
        {
            _config = config;
            _entityService = entityService;
        }

        public string GetEpiserverCode(int entityId)
        {
            if (entityId == 0)
                return null;

            var entity = _entityService.GetEntity(entityId, LoadLevel.Shallow);
            return GetEpiserverCode(entity);
        }

        public string GetEpiserverCode(Entity entity)
        {
            var entityTypeId = entity.EntityType.Id;
            
            if (_config.EpiCodeMapping.ContainsKey(entityTypeId))
            {
                var mappedCodeFieldId = _config.EpiCodeMapping[entityTypeId];
                var mappedCodeField = entity.Fields.FirstOrDefault(x => x.FieldType.Id == mappedCodeFieldId);
                if (mappedCodeField != null)
                    return GetPrefixedValue(mappedCodeField.Data);
            }

            return GetPrefixedValue(entity.Id);
        }

        private string GetPrefixedValue(object data)
        {
            return $"{_config.ChannelIdPrefix}{data}";
        }

        [Obsolete("Get this piece of shit out of here. Let it die, ungracefully.", true)]
        internal string GetEpiserverCodeLEGACYDAMNIT(object code)
        {
            int entityId;
            string codeValue = code.ToString();
            if (!string.IsNullOrEmpty(codeValue))
            {
                if (!string.IsNullOrEmpty(_config.ChannelIdPrefix) && codeValue.StartsWith(_config.ChannelIdPrefix))
                {
                    // If the code is an entity id we should move on
                    // otherwise we should assume the code is what should be returned.
                    if (!int.TryParse(codeValue, out entityId))
                    {
                        return codeValue;
                    }

                    if (!_config.EntityIdAndType.ContainsKey(entityId))
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
                return _config.ChannelIdPrefix + codeValue;
            }
            return null;
            //int? existingEntityId = null;
            //string type = string.Empty;
            
            //if (!_config.EntityIdAndType.ContainsKey(entityId))
            //{
            //    //GetAllStructureEntities if its not a LinkEntity.
            //    if (!_config.ChannelStructureEntities.Exists(i => i.LinkEntityId != null && i.LinkEntityId.Value.Equals(entityId)))
            //    {
            //        List<StructureEntity> entities = RemoteManager.ChannelService.GetAllStructureEntitiesForEntityInChannel(_config.ChannelId, entityId);

            //        if (entities.Any())
            //        {
            //            _config.ChannelStructureEntities.AddRange(entities);
            //            _config.EntityIdAndType.Add(entities[0].EntityId, entities[0].Type);

            //            existingEntityId = entities[0].EntityId;
            //            type = entities[0].Type;
            //        }
            //        else if (_config.ChannelEntities.ContainsKey(entityId))
            //        {
            //            Entity channelEntity = _config.ChannelEntities[entityId];
            //            existingEntityId = channelEntity.Id;
            //            type = channelEntity.EntityType.Id;
            //        }
            //    }
            //    else
            //    {
            //        if (_config.ChannelEntities.ContainsKey(entityId))
            //        {
            //            Entity channelEntity = _config.ChannelEntities[entityId];
            //            existingEntityId = channelEntity.Id;
            //            type = channelEntity.EntityType.Id;
            //        }
            //    }
            //}
            //else
            //{
            //    type = _config.EntityIdAndType[entityId];
            //    existingEntityId = entityId;
            //}

            //if (existingEntityId == null)
            //{
            //    return _config.ChannelIdPrefix + code;
            //}

            //// Check if entity type is mapped
            //if (!_config.EpiCodeMapping.ContainsKey(type))
            //{
            //    return _config.ChannelIdPrefix + existingEntityId;
            //}

            //Entity entity;

            //if (_config.ChannelEntities != null && _config.ChannelEntities.ContainsKey(entityId))
            //{
            //    entity = _config.ChannelEntities[entityId];
            //}
            //else
            //{
            //    entity = RemoteManager.DataService.GetEntity(entityId, LoadLevel.DataOnly);
            //    _config.ChannelEntities?.Add(entity.Id, entity);
            //}

            //Field codeField = entity.GetField(_config.EpiCodeMapping[type]);

            //// Check if code field exists
            //if (codeField == null)
            //{
            //    return _config.ChannelIdPrefix + entity.Id;
            //}

            //// Check if code data is null
            //if (codeField.Data == null || codeField.IsEmpty())
            //{
            //    return _config.ChannelIdPrefix + entity.Id;
            //}

            //return _config.ChannelIdPrefix + codeField.Data;
        }
    }
}