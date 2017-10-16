using Epinova.InRiverConnector.Interfaces;
using EPiServer.Commerce.Catalog.ContentTypes;

namespace Epinova.InRiverConnector.EpiserverImporter.EventHandling
{
    public interface IDeleteActionsHandler
    {
        void PreDeleteCatalog(int catalogId);

        void PostDeleteCatalog(int catalogId);

        void PreDeleteCatalogNode(int catalogNodeId, int catalogId);

        void PostDeleteCatalogNode(int catalogNodeId, int catalogId);

        void PreDeleteCatalogEntry(EntryContentBase entry);

        void PostDeleteCatalogEntry(EntryContentBase deletedEntry);

        void PreDeleteResource(IInRiverImportResource resource);

        void PostDeleteResource(IInRiverImportResource resource);
    }
}