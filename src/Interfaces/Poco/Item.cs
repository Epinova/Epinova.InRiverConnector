using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class Item
    {
        public Item()
        {
        }

        [XmlAttribute("value")]
        public string value { get; set; }
    }
}
