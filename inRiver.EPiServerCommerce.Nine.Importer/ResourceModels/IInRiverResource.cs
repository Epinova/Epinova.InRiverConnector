using System.Collections.Generic;
using EPiServer.Core;
using inRiver.EPiServerCommerce.Interfaces;

namespace inRiver.EPiServerCommerce.Nine.Importer.ResourceModels
{
    public interface IInRiverResource : IContentData
    {
        int ResourceFileId { get; set; }
        
        int EntityId { get; set; }

        void HandleMetaData(List<ResourceMetaField> metaFields);
    }
}