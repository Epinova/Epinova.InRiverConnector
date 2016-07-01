namespace inRiver.EPiServerCommerce.MediaPublisher
{
    using System;
    using System.Collections.Generic;

    using inRiver.EPiServerCommerce.Interfaces;

    /// <summary>
    /// Describes a resource coming from inRiver.
    /// </summary>
    public class InRiverImportResource : IInRiverImportResource
    {
        public InRiverImportResource()
        {
            EntryCodes = new List<Interfaces.EntryCode>();
        }

        /// <summary>
        /// The path to the exported resource file
        /// </summary>
        public string Path { get; set; }

        public List<string> Codes { get; set; } 

        public List<Interfaces.EntryCode> EntryCodes { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string Action { get; set; }

        public int ResourceId { get; set; }

        public List<ResourceMetaField> MetaFields { get; set; }
    }
}