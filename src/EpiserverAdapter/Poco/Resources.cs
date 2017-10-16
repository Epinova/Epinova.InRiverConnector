using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
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
