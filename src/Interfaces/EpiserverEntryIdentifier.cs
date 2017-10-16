using System;

namespace Epinova.InRiverConnector.Interfaces
{
    public class EpiserverEntryIdentifier
    {
        public static Guid EntityIdToGuid(int entityId)
        {
            return new Guid(string.Format("00000000-0000-0000-0000-00{0:0000000000}", entityId));
        }
    }
}