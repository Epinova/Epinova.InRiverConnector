using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class Resources
	{
		
		// ELEMENTS
		[XmlElement("ResourceMetaFields")]
		public ResourceMetaFields ResourceMetaFields { get; set; }
		
		[XmlElement("ResourceFiles")]
		public ResourceFiles ResourceFiles { get; set; }
		
		// CONSTRUCTOR
		public Resources()
		{}
	}
}
