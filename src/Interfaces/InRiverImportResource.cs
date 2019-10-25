using System.Collections.Generic;

namespace Epinova.InRiverConnector.Interfaces
{
    /// <summary>
    /// Describes a resource coming from inRiver.
    /// </summary>
    public class InRiverImportResource
    {
        public InRiverImportResource()
        {
            EntryCodes = new List<EntryCode>();
            Codes = new List<string>();
        }

        public string Action { get; set; }

        public List<string> Codes { get; set; }

        public List<EntryCode> EntryCodes { get; set; }

        public List<ResourceMetaField> MetaFields { get; set; }

        /// <summary>
        /// The path to the exported resource file
        /// </summary>
        public string Path { get; set; }

        public int ResourceId { get; set; }
    }
}
