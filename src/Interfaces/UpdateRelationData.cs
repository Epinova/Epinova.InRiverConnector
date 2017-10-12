using System.Collections.Generic;

namespace inRiver.EPiServerCommerce.Interfaces
{
    public class UpdateRelationData
    {
        public string ChannelName { get; set; }

        public List<string> RemoveFromChannelNodes { get; set; }

        public string ParentEntryId { get; set; }

        public string CatalogEntryIdString { get; set; }

        public string ChannelIdEpified { get; set; }

        public List<string> LinkEntityIdsToRemove { get; set; }

        public string InRiverAssociationsEpified { get; set; }

        public string LinkTypeId { get; set; }

        public bool IsRelation { get; set; }

        public bool ParentExistsInChannelNodes { get; set; }
    }
}
