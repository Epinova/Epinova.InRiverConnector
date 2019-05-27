using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class ResourceMetaFields
    {
        [XmlText]
        public string Value { get; set; }
    }
}
