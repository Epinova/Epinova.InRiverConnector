using System.Collections.Generic;
using System.Web.Http;
using Epinova.InRiverConnector.Interfaces;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public interface ICatalogImporter
    {
        void DeleteCatalogEntry(string code);

        void DeleteCatalog(int catalogId);

        void DeleteCatalogNode(string code);

        void MoveNodeToRootIfNeeded(string catalogNodeId);

        void UpdateEntryRelations(UpdateRelationData updateRelationData);

        void ImportCatalogXml([FromBody] string path);

        bool ImportUpdateCompleted(ImportUpdateCompletedData data);

        bool DeleteCompleted(DeleteCompletedData data);
        void DeleteRelation(string sourceCode, string targetCode);
        void DeleteAssociation(string sourceCode, string targetCode);
    }
}