namespace inRiver.EPiServerCommerce.Interfaces
{
    using System.Collections.Generic;

    public interface IResourceImporterHandler
    {
        /// <summary>
        /// Called before the Resources is imported into Commerce
        /// </summary>
        /// <remarks>If any implementation throws an exception, the resources will not be imported</remarks>
        /// <param name="resources"></param>
        void PreImport(List<IInRiverImportResource> resources);

        /// <summary>
        /// Called after the Resource has been imported into Commerce
        /// </summary>
        /// <param name="resources"></param>
        void PostImport(List<IInRiverImportResource> resources);
    }
}