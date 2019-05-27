using System.Xml.Serialization;

namespace Epinova.InRiverConnector.Interfaces.Poco
{
    public class EntryCode
    {
        public EntryCode()
        {
        }

        [XmlIgnore]
        public bool IsMainPicture { get; set; }

        [XmlAttribute("IsMainPicture")]
        public string IsMainPictureString
        {
            get { return IsMainPicture ? "true" : "false"; }
            set { IsMainPicture = value == "true"; }
        }

        [XmlText]
        public string Value { get; set; }
    }
}
