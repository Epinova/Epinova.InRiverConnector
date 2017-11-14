using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{	  
	public class Resource
	{
		[XmlAttribute("id")]
		public int id  { get; set; }
		
		[XmlAttribute("action")]
		public string action { get; set; }
		
		[XmlElement("ResourceFields")]
		public ResourceFields ResourceFields { get; set; }
		
		[XmlElement("Paths")]
		public Paths Paths { get; set; }
		
		[XmlElement("ParentEntries")]
		public ParentEntries ParentEntries { get; set; }
	}
}
