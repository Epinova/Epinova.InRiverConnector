using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{	
	public class Type
	{
		// ELEMENTS
		[XmlText]
		public string Value { get; set; }
	}
}
