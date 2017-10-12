using System;
using System.Collections.Generic;

namespace inRiver.EPiServerCommerce.Interfaces
{
    public interface IInRiverImportResource
    {
        /// <summary>
        /// The path to the exported resource file
        /// </summary>
        string Path { get; set; }

        List<string> Codes { get; set; }

            /// <summary>
        /// Code and main picture info for the entry in Commerce that this resource should be associated with
        /// </summary>
        List<EntryCode> EntryCodes { get; set; }

        /// <summary>
        /// 
        /// </summary>
        string Action { get; set; }

        int ResourceId { get; set; }

        List<ResourceMetaField> MetaFields { get; set; }
    }
}
