using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class Paths
	{
		[XmlElement("Path")]
		public Path Path { get; set; }
	}
}