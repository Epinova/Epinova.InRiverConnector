using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class Path
	{		
		[XmlText]
		public string Value { get; set; }
	}
}
