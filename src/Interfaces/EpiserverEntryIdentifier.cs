using System;

namespace Epinova.InRiverConnector.Interfaces
{
    public class EpiserverEntryIdentifier
    {
        // INFO: Modifying the format of this will break all ties with existing catalog entries in Episerver.
        //       If this is changed, it has to be flagged as a MAJOR BREAKING CHANGE.

        public static Guid EntityIdToGuid(int entityId)
        {
            return new Guid($"00000000-0000-0000-0000-00{entityId:0000000000}");
        }
    }
}