using System.Collections.Generic;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public interface IEntityService
    {
        void FlushCache();
        List<StructureEntity> GetAllResourceLocations(int resourceEntityId);
        List<StructureEntity> GetAllStructureEntitiesInChannel(List<EntityType> entityTypes);
        List<StructureEntity> GetChannelNodeStructureEntitiesInPath(string path);
        List<StructureEntity> GetChildrenEntitiesInChannel(int entityId, string path);
        Entity GetEntity(int id, LoadLevel loadLevel);
        List<StructureEntity> GetEntityInChannelWithParent(int channelId, int entityId, int parentId);
        Entity GetParentProduct(StructureEntity structureEntity);
        StructureEntity GetParentStructureEntity(int channelId, int sourceEntityId, int targetEntityId, List<StructureEntity> channelEntities);
        List<StructureEntity> GetStructureEntitiesForEntityInChannel(int channelId, int entityId);
        string GetTargetEntityPath(int targetEntityId, List<StructureEntity> channelEntities, int? parentId = null);
    }
}
