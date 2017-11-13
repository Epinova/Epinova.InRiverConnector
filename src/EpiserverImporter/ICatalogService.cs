using System.Collections.Generic;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Commerce.Catalog.Linking;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public interface ICatalogService
    {
        IEnumerable<EntryContentBase> GetChildren(EntryContentBase product);
        IEnumerable<EntryRelation> GetParents(EntryContentBase variant);
    }
}