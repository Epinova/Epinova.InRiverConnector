using System.Collections.Generic;
using inRiver.EPiServerCommerce.Interfaces;

namespace inRiver.EPiServerCommerce.Nine.Importer
{
    /// <summary>
    /// Describes a resource coming from inRiver.
    /// </summary>
    public class InRiverImportResource : IInRiverImportResource
    {
        public InRiverImportResource()
        {
            this.EntryCodes = new List<EntryCode>();
        }

        /// <summary>
        /// The path to the exported resource file
        /// </summary>
        public string Path { get; set; }

        public List<string> Codes { get; set; }

        /// <summary>
        /// The code for the EPiServer Commerce node or entry that this resource should be associated with
        /// </summary>
        public List<EntryCode> EntryCodes { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string Action { get; set; }

        public int ResourceId { get; set; }

        public List<ResourceMetaField> MetaFields { get; set; }

    }
}