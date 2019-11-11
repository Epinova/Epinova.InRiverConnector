using Epinova.InRiverConnector.Interfaces.Enums;

namespace Epinova.InRiverConnector.Interfaces
{
    public class ImportUpdateCompletedData
    {
        public string CatalogName { get; set; }

        public ImportUpdateCompletedEventType EventType { get; set; }

        public bool ResourcesIncluded { get; set; }
    }
}
