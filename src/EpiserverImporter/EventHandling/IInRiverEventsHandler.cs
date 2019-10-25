using Epinova.InRiverConnector.Interfaces.Enums;

namespace Epinova.InRiverConnector.EpiserverImporter.EventHandling
{
    public interface IInRiverEventsHandler
    {
        /// <summary>
        /// Called when a delete of data has been committed in Commerce.
        /// </summary>
        /// <param name="catalogName">The name of the catalog</param>
        /// <param name="eventType">Which connector event that was the origin</param>
        void DeleteCompleted(string catalogName, DeleteCompletedEventType eventType);

        /// <summary>
        /// Called when an import or updated of data into Commerce are done.
        /// </summary>
        /// <param name="catalogName">The name of the catalog</param>
        /// <param name="eventType">Which connector event that was the origin</param>
        /// <param name="resourceIncluded">if resources was in included in the import/update</param>
        void ImportUpdateCompleted(string catalogName, ImportUpdateCompletedEventType eventType, bool resourceIncluded);
    }
}
