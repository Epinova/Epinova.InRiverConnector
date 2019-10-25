using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class Data
    {
        [XmlElement("Item")]
        public List<Item> Item { get; set; }

        [XmlAttribute("language")]
        public string language { get; set; }

        [XmlAttribute("value")]
        public string value { get; set; }

        [XmlText]
        public string Value { get; set; }
    }
}
