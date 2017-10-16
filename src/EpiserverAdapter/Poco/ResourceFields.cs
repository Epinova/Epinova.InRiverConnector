using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class ResourceFields
	{	
		// ELEMENTS
		[XmlElement("MetaField")]
		public List<MetaField> MetaField { get; set; }
	}
}
