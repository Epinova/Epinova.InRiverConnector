using System.Collections.Generic;
using System.Linq;
using EPiServer;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Commerce.Catalog.Linking;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class CatalogService : ICatalogService
    {
        private readonly IContentLoader _contentLoader;
        private readonly IRelationRepository _relationRepository;

        public CatalogService(IContentLoader contentLoader, IRelationRepository relationRepository)
        {
            _contentLoader = contentLoader;
            _relationRepository = relationRepository;
        }

        /// <summary>
        /// Gets all children for an entry, whether it's product-variation, bundle-variation/product, package-variation/product relation.
        /// </summary>
        /// <param name="entry">The catalog entry to retrieve children for</param>
        /// <returns></returns>
        public IEnumerable<EntryContentBase> GetChildren(EntryContentBase entry)
        {
            IEnumerable<EntryRelation> relations = _relationRepository.GetChildren<EntryRelation>(entry.ContentLink);
            IEnumerable<EntryContentBase> variations = relations.Select(x => _contentLoader.Get<EntryContentBase>(x.Child));
            return variations.Where(x => x != null);
        }

        /// <summary>
        /// Gets all parents for variant-product, variant/product-bundle, variant/product-package.
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        public IEnumerable<EntryRelation> GetParents(EntryContentBase entry)
        {
            return _relationRepository.GetParents<EntryRelation>(entry.ContentLink);
        }
    }
}
