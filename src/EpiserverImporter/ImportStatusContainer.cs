namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class ImportStatusContainer
    {
        private static ImportStatusContainer _instance = new ImportStatusContainer();

        private ImportStatusContainer()
        {
        }

        public static ImportStatusContainer Instance => _instance ?? (_instance = new ImportStatusContainer());

        public bool IsImporting { get; set; }

        public string Message { get; set; }
    }
}