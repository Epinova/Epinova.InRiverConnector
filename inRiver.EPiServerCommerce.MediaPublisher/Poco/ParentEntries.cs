using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class ParentEntries
	{
		
		// ELEMENTS
		[XmlElement("EntryCode")]
		public List<EntryCode> EntryCode { get; set; }
		
		// CONSTRUCTOR
		public ParentEntries()
		{}
	}
}
