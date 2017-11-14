using System.Linq;
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

            var entity = _entityService.GetEntity(entityId, LoadLevel.DataOnly);
            return GetEpiserverCode(entity);
        }

        public string GetEpiserverCode(Entity entity)
        {
            if (entity.LoadLevel < LoadLevel.DataOnly)
                entity = _entityService.GetEntity(entity.Id, LoadLevel.DataOnly);

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

        public string GetRelationName(string skuId, string parentCode)
        {
            return $"{_config.ChannelIdPrefix}_{skuId}_{parentCode}";
        }

        public string GetRelationName(int entityId, int parentEntityId)
        {
            return $"{_config.ChannelIdPrefix}_{entityId}_{parentEntityId}";
        }

        public string GetPrefixedCode(string skuCode)
        {
            return GetPrefixedValue(skuCode);
        }

        private string GetPrefixedValue(object data)
        {
            return $"{_config.ChannelIdPrefix}{data}";
        }
    }
}