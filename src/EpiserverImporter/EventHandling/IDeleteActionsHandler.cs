using Epinova.InRiverConnector.Interfaces;
using EPiServer.Commerce.Catalog.ContentTypes;

namespace Epinova.InRiverConnector.EpiserverImporter.EventHandling
{
    public interface IDeleteActionsHandler
    {
        void PostDeleteCatalog(int catalogId);

        void PostDeleteCatalogEntry(EntryContentBase deletedEntry);

        void PostDeleteCatalogNode(NodeContent node);

        void PostDeleteResource(InRiverImportResource resource);

        void PreDeleteCatalog(int catalogId);

        void PreDeleteCatalogEntry(EntryContentBase entry);

        void PreDeleteCatalogNode(NodeContent node);

        void PreDeleteResource(InRiverImportResource resource);
    }
}
