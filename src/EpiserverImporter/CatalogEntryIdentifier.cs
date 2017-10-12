using System;

namespace inRiver.EPiServerCommerce.Importer
{
    public class CatalogEntryIdentifier
    {
        public static Guid EntityIdToGuid(int entityId)
        {
            return new Guid(string.Format("00000000-0000-0000-0000-00{0:0000000000}", entityId));
        }
    }
}