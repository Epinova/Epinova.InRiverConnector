namespace inRiver.EPiServerCommerce.Importer
{
    public class ImportStatusContainer
    {
        private static ImportStatusContainer instance = new ImportStatusContainer();

        private ImportStatusContainer()
        {
        }

        public static ImportStatusContainer Instance
        {
            get
            {
                return instance ?? (instance = new ImportStatusContainer());
            }
        }

        public string Message { get; set; }

        public bool IsImporting { get; set; }
    }
}