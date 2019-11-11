using System.Web.Http;
using Epinova.InRiverConnector.Interfaces;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public interface ICatalogImporter
    {
        void DeleteAssociation(string sourceCode, string targetCode);
        void DeleteCatalog(int catalogId);
        void DeleteCatalogEntry(string code);
        void DeleteCatalogNode(string code);
        bool DeleteCompleted(DeleteCompletedData data);
        void DeleteRelation(string sourceCode, string targetCode);
        void ImportCatalogXml([FromBody] string path);
        bool ImportUpdateCompleted(ImportUpdateCompletedData data);
        void MoveNodeToRootIfNeeded(string catalogNodeId);
    }
}
