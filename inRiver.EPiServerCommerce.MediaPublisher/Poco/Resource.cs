using System.Xml.Serialization;

namespace inRiver.EPiServerCommerce.MediaPublisher.Poco
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
	}
}
