using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
	public class Paths
	{
		[XmlElement("Path")]
		public Path Path { get; set; }
	}
}