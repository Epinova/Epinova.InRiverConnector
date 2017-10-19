using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public interface IEntityService
    {
        Entity GetEntity(int id, LoadLevel loadLevel);
    }
}