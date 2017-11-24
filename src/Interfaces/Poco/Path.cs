using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
	public class Path
	{		
		[XmlText]
		public string Value { get; set; }
	}
}
