namespace inRiver.EPiServerCommerce.Interfaces
{
    using inRiver.EPiServerCommerce.Interfaces.Enums;

    public class DeleteCompletedData
    {
        public string CatalogName { get; set; }

        public DeleteCompletedEventType EventType { get; set; }
    }
}