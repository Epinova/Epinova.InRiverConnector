using System.Collections.Generic;
using System.Xml.Serialization;

namespace inRiver.EPiServerCommerce.MediaPublisher.Poco
{
	public class ParentEntries
	{		
		// ELEMENTS
		[XmlElement("EntryCode")]
		public List<EntryCode> EntryCode { get; set; }
	}
}
