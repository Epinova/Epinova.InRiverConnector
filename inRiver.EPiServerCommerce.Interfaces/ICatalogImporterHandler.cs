namespace inRiver.EPiServerCommerce.Interfaces
{
    using System.Xml.Linq;

    public interface ICatalogImportHandler
    {
        /// <summary>
        /// Called before the Catalog XML is imported into Commerce
        /// </summary>
        /// <remarks>If any implementation throws an exception, the catalog will not be imported</remarks>
        /// <param name="catalog"></param>
        void PreImport(XDocument catalog);

        /// <summary>
        /// Called after the Catalog XML has been imported into Commerce
        /// </summary>
        /// <param name="catalog"></param>
        void PostImport(XDocument catalog);
    }
}
