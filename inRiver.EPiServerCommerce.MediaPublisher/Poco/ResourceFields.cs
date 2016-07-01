using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class ResourceFields
	{
		
		// ELEMENTS
		[XmlElement("MetaField")]
		public List<MetaField> MetaField { get; set; }
		
		// CONSTRUCTOR
		public ResourceFields()
		{}
	}
}
