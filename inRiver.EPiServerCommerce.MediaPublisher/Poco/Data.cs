using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class Data
	{
		// ATTRIBUTES
		[XmlAttribute("language")]
		public string language { get; set; }
		
		[XmlAttribute("value")]
		public string value { get; set; }
		
		// ELEMENTS
		[XmlText]
		public string Value { get; set; }

        [XmlElement("Item")]
        public List<Item> Item { get; set; }
		
		// CONSTRUCTOR
		public Data()
		{}
	}
}
