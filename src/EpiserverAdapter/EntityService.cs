using inRiver.Remoting;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class EntityService : IEntityService
    {
        public Entity GetEntity(int id, LoadLevel loadLevel)
        {
            return RemoteManager.DataService.GetEntity(id, loadLevel);
        }
    }
}