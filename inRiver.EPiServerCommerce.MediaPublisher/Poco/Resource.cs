using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class Resource
	{
		// ATTRIBUTES
		[XmlAttribute("id")]
		public int id  { get; set; }
		
		[XmlAttribute("action")]
		public string action { get; set; }
		
		// ELEMENTS
		[XmlElement("ResourceFields")]
		public ResourceFields ResourceFields { get; set; }
		
		[XmlElement("Paths")]
		public Paths Paths { get; set; }
		
		[XmlElement("ParentEntries")]
		public ParentEntries ParentEntries { get; set; }
		
		// CONSTRUCTOR
		public Resource()
		{}
	}
}
