using System.Collections.Generic;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public interface IEntityService
    {
        Entity GetEntity(int id, LoadLevel loadLevel);
        List<StructureEntity> GetAllStructureEntitiesInChannel(List<EntityType> entityTypes);
        List<StructureEntity> GetAllResourceLocations(int resourceEntityId);
        List<StructureEntity> GetEntityInChannelWithParent(int channelId, int entityId, int parentId);
        string GetTargetEntityPath(int targetEntityId, List<StructureEntity> channelEntities, int? parentId = null);
        List<StructureEntity> GetChildrenEntitiesInChannel(int entityId, string path);
        List<StructureEntity> GetStructureEntitiesForEntityInChannel(int channelId, int entityId);
        StructureEntity GetParentStructureEntity(int channelId, int sourceEntityId, int targetEntityId, List<StructureEntity> channelEntities);
        void FlushCache();
        List<StructureEntity> GetChannelNodeStructureEntitiesInPath(string path);
        Entity GetParentProduct(StructureEntity structureEntity);
    }
}