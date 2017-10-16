using System.Collections.Generic;
using Epinova.InRiverConnector.Interfaces;
using EPiServer.Core;

namespace Epinova.InRiverConnector.EpiserverImporter.ResourceModels
{
    public interface IInRiverResource : IContentData
    {
        int ResourceFileId { get; set; }
        
        int EntityId { get; set; }

        void HandleMetaData(List<ResourceMetaField> metaFields);
    }
}