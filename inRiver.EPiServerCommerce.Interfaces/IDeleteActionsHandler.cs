namespace inRiver.EPiServerCommerce.Interfaces
{
    public interface IDeleteActionsHandler
    {
        void PreDeleteCatalog(int catalogId);

        void PostDeleteCatalog(int catalogId);

        void PreDeleteCatalogNode(int catalogNodeId, int catalogId);

        void PostDeleteCatalogNode(int catalogNodeId, int catalogId);

        void PreDeleteCatalogEntry(int catalogEntryId, int metaClassId, int catalogId);

        void PostDeleteCatalogEntry(int catalogEntryId, int metaClassId, int catalogId);

        void PreDeleteResource(IInRiverImportResource resource);

        void PostDeleteResource(IInRiverImportResource resource);
    }
}