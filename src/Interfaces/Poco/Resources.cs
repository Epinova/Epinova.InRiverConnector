using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class Resources
    {
        [XmlElement("ResourceMetaFields")]
        public ResourceMetaFields ResourceMetaFields { get; set; }

        [XmlElement("ResourceFiles")]
        public ResourceFiles ResourceFiles { get; set; }
    }
}
