using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
	public class Name
	{		
		// ELEMENTS
		[XmlText]
		public string Value { get; set; }
	}
}
