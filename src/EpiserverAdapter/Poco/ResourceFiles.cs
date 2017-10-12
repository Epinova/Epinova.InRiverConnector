using System.Collections.Generic;
using System.Xml.Serialization;

namespace inRiver.EPiServerCommerce.MediaPublisher.Poco
{
	public class ResourceFiles
	{		
		// ELEMENTS
		[XmlElement("Resource")]
		public List<Resource> Resource { get; set; }
	}
}
