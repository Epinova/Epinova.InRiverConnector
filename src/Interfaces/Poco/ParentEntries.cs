using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class ParentEntries
    {
        [XmlElement("EntryCode")]
        public List<EntryCode> EntryCode { get; set; }
    }
}
