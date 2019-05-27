using System.Collections.Generic;

namespace Epinova.InRiverConnector.Interfaces
{
    public class ResourceMetaField
    {
        public string Id { get; set; }

        public List<Value> Values { get; set; }
    }
}