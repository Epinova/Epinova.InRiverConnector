using System.Collections.Generic;
using Epinova.InRiverConnector.Interfaces;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    /// <summary>
    /// Describes a resource coming from inRiver.
    /// </summary>
    public class InRiverImportResource : IInRiverImportResource
    {
        public InRiverImportResource()
        {
            EntryCodes = new List<EntryCode>();
        }

        /// <summary>
        /// The path to the exported resource file
        /// </summary>
        public string Path { get; set; }

        public List<string> Codes { get; set; } 

        public List<EntryCode> EntryCodes { get; set; }

        public string Action { get; set; }

        public int ResourceId { get; set; }

        public List<ResourceMetaField> MetaFields { get; set; }
    }
}