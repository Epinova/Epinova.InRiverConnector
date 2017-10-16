using System.Collections.Generic;

namespace Epinova.InRiverConnector.Interfaces
{
    public class GetLinkEntityAssociationsForEntityData
    {
        public string LinkTypeId { get; set; }

        public string ChannelName { get; set; }

        public List<string> ParentIds { get; set; }

        public List<string> TargetIds { get; set; }
    }
}
