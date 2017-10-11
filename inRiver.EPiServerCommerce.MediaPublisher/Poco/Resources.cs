using System.Xml.Serialization;

namespace inRiver.EPiServerCommerce.MediaPublisher.Poco
{
	public class Resources
	{		
		// ELEMENTS
		[XmlElement("ResourceMetaFields")]
		public ResourceMetaFields ResourceMetaFields { get; set; }
		
		[XmlElement("ResourceFiles")]
		public ResourceFiles ResourceFiles { get; set; }
	}
}
