using System.Collections.Generic;
using System.Xml.Serialization;

namespace inRiver.EPiServerCommerce.MediaPublisher.Poco
{
	public class ResourceFields
	{	
		// ELEMENTS
		[XmlElement("MetaField")]
		public List<MetaField> MetaField { get; set; }
	}
}
