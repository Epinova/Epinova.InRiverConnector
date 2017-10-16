using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class ResourceMetaFields
	{
		// ELEMENTS
		[XmlText]
		public string Value { get; set; }
	}
}
