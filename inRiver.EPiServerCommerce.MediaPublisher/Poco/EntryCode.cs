using System;
using System.ComponentModel;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace inRiver.EPiServerCommerce.MediaPublisher
{
	
	public class EntryCode
	{
		// ATTRIBUTES
		[XmlIgnore]
		public bool IsMainPicture { get; set; }

		[XmlAttribute("IsMainPicture")]
		public string IsMainPictureString
		{
			get { return IsMainPicture ? "true" : "false"; }
			set { IsMainPicture = value == "true"; }
		}
		
		// ELEMENTS
		[XmlText]
		public string Value { get; set; }
		
		// CONSTRUCTOR
		public EntryCode()
		{}
	}
}
