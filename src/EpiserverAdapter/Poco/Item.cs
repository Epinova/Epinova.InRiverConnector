using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{

    public class Item
    {
        // ATTRIBUTES
        [XmlAttribute("value")]
        public string value { get; set; }

        // CONSTRUCTOR
        public Item()
        { }
    }
}
