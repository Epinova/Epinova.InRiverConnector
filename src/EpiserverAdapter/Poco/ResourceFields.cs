using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class ResourceFields
	{	
		[XmlElement("MetaField")]
		public List<MetaField> MetaField { get; set; }
	}
}
