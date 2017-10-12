using System.Collections.Generic;
using System.Web.Http;
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

        void ImportCatalogXml([FromBody] string path);

        bool ImportResources(List<InRiverImportResource> resources);

        bool ImportUpdateCompleted(ImportUpdateCompletedData data);

        bool DeleteCompleted(DeleteCompletedData data);
    }
}