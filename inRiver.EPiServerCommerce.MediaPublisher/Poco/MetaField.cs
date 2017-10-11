using System.Collections.Generic;
using System.Xml.Serialization;

namespace inRiver.EPiServerCommerce.MediaPublisher.Poco
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
