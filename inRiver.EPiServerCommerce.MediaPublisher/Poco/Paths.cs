using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class Paths
	{
		
		// ELEMENTS
		[XmlElement("Path")]
		public Path Path { get; set; }
		
		// CONSTRUCTOR
		public Paths()
		{}
	}
}
