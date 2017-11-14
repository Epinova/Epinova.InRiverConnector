using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class Data
	{
		[XmlAttribute("language")]
		public string language { get; set; }
		
		[XmlAttribute("value")]
		public string value { get; set; }
		
		[XmlText]
		public string Value { get; set; }

        [XmlElement("Item")]
        public List<Item> Item { get; set; }	
	}
}
