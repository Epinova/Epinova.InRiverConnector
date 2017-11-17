using System.Collections.Generic;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapterTests
{
    public static class EntityFactory
    {
        public static Entity CreateItem(int id)
        {
            return new Entity
            {
                EntityType = new EntityType("Item"),
                Id = id,
                Fields = new List<Field>(),
                LoadLevel = LoadLevel.DataOnly
            };
        }
    }
}