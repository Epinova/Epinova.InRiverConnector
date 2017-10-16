using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class ResourceFiles
	{		
		// ELEMENTS
		[XmlElement("Resource")]
		public List<Resource> Resource { get; set; }
	}
}
