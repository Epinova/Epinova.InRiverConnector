using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	public class ParentEntries
	{		
		// ELEMENTS
		[XmlElement("EntryCode")]
		public List<EntryCode> EntryCode { get; set; }
	}
}
