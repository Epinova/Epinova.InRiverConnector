using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class Path
	{		
		// ELEMENTS
		[XmlText]
		public string Value { get; set; }
	}
}
