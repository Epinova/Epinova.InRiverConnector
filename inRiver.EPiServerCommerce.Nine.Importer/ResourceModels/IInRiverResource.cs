namespace inRiver.EPiServerCommernce.Nine.Importer.ResourceModels
{
    using System.Collections.Generic;

    using EPiServer.Core;

    using inRiver.EPiServerCommerce.Interfaces;

    public interface IInRiverResource : IContentData
    {
        int ResourceFileId { get; set; }
        
        int EntityId { get; set; }

        void HandleMetaData(List<ResourceMetaField> metaFields);
    }
}