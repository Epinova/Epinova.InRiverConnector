using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class ResourceMetaFields
	{
		[XmlText]
		public string Value { get; set; }
	}
}
