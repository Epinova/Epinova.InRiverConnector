using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class MetaField
	{
		
		// ELEMENTS
		[XmlElement("Name")]
		public Name Name { get; set; }
		
		[XmlElement("Type")]
		public Type Type { get; set; }
		
		[XmlElement("Data")]
		public List<Data> Data { get; set; }
		
		// CONSTRUCTOR
		public MetaField()
		{}
	}
}
