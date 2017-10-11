using System.Collections.Generic;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using inRiver.EPiServerCommerce.Interfaces;

namespace inRiver.EPiServerCommerce.Importer.ResourceModels
{
    /// <summary>
    /// This media type will be used if there is no more specific type
    /// available.
    /// </summary>
    [ContentType(GUID = "6A6BF35B-F76D-49FD-B4D0-BAF5DB36EB38")]
    public class InRiverGenericMedia : MediaData, IInRiverResource
    {
        public virtual int ResourceFileId { get; set; }
        
        public virtual int EntityId { get; set; }

        public void HandleMetaData(List<ResourceMetaField> metaFields)
        {
        }
    }
}