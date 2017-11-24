using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
	public class ResourceFiles
	{		
		[XmlElement("Resource")]
		public List<Resource> Resource { get; set; }
	}
}
