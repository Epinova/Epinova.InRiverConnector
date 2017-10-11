using System.Xml.Serialization;

namespace inRiver.EPiServerCommerce.MediaPublisher.Poco
{
	public class Paths
	{
		// ELEMENTS
		[XmlElement("Path")]
		public Path Path { get; set; }
	}
}