using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class Resource
    {
        [XmlAttribute("action")]
        public string action { get; set; }

        [XmlAttribute("id")]
        public int id { get; set; }

        [XmlElement("ResourceFields")]
        public ResourceFields ResourceFields { get; set; }

        [XmlElement("Paths")]
        public Paths Paths { get; set; }

        [XmlElement("ParentEntries")]
        public ParentEntries ParentEntries { get; set; }
    }
}
