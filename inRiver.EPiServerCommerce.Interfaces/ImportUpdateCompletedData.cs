using inRiver.EPiServerCommerce.Interfaces.Enums;

namespace inRiver.EPiServerCommerce.Interfaces
{
    public class ImportUpdateCompletedData
    {
        public string CatalogName { get; set; }

        public ImportUpdateCompletedEventType EventType { get; set; }

        public bool ResourcesIncluded { get; set; }
    }
}