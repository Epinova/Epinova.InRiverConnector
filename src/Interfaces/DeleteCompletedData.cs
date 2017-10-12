using inRiver.EPiServerCommerce.Interfaces.Enums;

namespace inRiver.EPiServerCommerce.Interfaces
{
    public class DeleteCompletedData
    {
        public string CatalogName { get; set; }

        public DeleteCompletedEventType EventType { get; set; }
    }
}