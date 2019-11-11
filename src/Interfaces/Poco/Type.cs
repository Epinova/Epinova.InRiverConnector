using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class Type
    {
        [XmlText]
        public string Value { get; set; }
    }
}
