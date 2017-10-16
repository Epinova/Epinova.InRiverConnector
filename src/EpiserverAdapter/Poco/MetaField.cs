using System.Collections.Generic;
using System.Xml.Serialization;

namespace Epinova.InRiverConnector.EpiserverAdapter.Poco
{
	
	public class MetaField
	{
		
		// ELEMENTS
		[XmlElement("Name")]
		public Name Name { get; set; }
		
		[XmlElement("Type")]
		public Type Type { get; set; }
		
		[XmlElement("Data")]
		public List<Data> Data { get; set; }
		
		// CONSTRUCTOR
		public MetaField()
		{}
	}
}
