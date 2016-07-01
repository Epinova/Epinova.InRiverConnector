namespace inRiver.EPiServerCommerce.Interfaces
{
    using System.Collections.Generic;

    public class GetLinkEntityAssociationsForEntityData
    {
        public string LinkTypeId { get; set; }

        public string ChannelName { get; set; }

        public List<string> ParentIds { get; set; }

        public List<string> TargetIds { get; set; }
    }
}
