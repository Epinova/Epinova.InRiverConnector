using Epinova.InRiverConnector.Interfaces;
using EPiServer.Commerce.Catalog.ContentTypes;

namespace Epinova.InRiverConnector.EpiserverImporter.EventHandling
{
    public interface IDeleteActionsHandler
    {
        void PreDeleteCatalog(int catalogId);

        void PostDeleteCatalog(int catalogId);

        void PreDeleteCatalogNode(NodeContent node);

        void PostDeleteCatalogNode(NodeContent node);

        void PreDeleteCatalogEntry(EntryContentBase entry);

        void PostDeleteCatalogEntry(EntryContentBase deletedEntry);

        void PreDeleteResource(InRiverImportResource resource);

        void PostDeleteResource(InRiverImportResource resource);
    }
}