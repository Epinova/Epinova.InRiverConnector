using Epinova.InRiverConnector.Interfaces.Enums;

namespace Epinova.InRiverConnector.Interfaces
{
    public class DeleteCompletedData
    {
        public string CatalogName { get; set; }

        public DeleteCompletedEventType EventType { get; set; }
    }
}
