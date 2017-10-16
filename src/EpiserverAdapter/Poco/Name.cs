using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class Name
	{		
		// ELEMENTS
		[XmlText]
		public string Value { get; set; }
	}
}
