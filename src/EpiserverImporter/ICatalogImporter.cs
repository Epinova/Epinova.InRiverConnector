using System.Collections.Generic;
using System.Web.Http;
using Epinova.InRiverConnector.Interfaces;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public interface ICatalogImporter
    {
        void DeleteCatalogEntry(string code);

        void DeleteCatalog(int catalogId);

        void DeleteCatalogNode(string catalogNodeId);

        void CheckAndMoveNodeIfNeeded(string catalogNodeId);

        void UpdateLinkEntityData(LinkEntityUpdateData linkEntityUpdateData);

        void UpdateEntryRelations(UpdateRelationData updateRelationData);

        List<string> GetLinkEntityAssociationsForEntity(GetLinkEntityAssociationsForEntityData data);

        void ImportCatalogXml([FromBody] string path);

        bool ImportUpdateCompleted(ImportUpdateCompletedData data);

        bool DeleteCompleted(DeleteCompletedData data);
    }
}