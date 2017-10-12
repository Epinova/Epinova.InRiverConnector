using System.Collections.Generic;
using inRiver.EPiServerCommerce.Interfaces;

namespace inRiver.EPiServerCommerce.Importer
{
    public interface ICatalogImporter
    {
        void DeleteCatalogEntry(string code);

        void DeleteCatalog(int catalogId);

        void DeleteCatalogNode(string catalogNodeId);

        void CheckAndMoveNodeIfNeeded(string catalogNodeId);

        void UpdateLinkEntityData(LinkEntityUpdateData linkEntityUpdateData);

        void UpdateEntryRelations(UpdateEntryRelationData updateEntryRelationData);

        List<string> GetLinkEntityAssociationsForEntity(GetLinkEntityAssociationsForEntityData data);
    }
}