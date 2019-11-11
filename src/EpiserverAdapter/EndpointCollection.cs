namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class EndpointCollection
    {
        private readonly string _baseUrl;

        public EndpointCollection(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public string CheckAndMoveNodeIfNeeded => _baseUrl + "MoveNodeToRootIfNeeded";
        public string DeleteCatalog => _baseUrl + "DeleteCatalog";
        public string DeleteCatalogEntry => _baseUrl + "DeleteCatalogEntry";
        public string DeleteCatalogNode => _baseUrl + "DeleteCatalogNode";
        public string DeleteCompleted => _baseUrl + "DeleteCompleted";
        public string DeleteLink => _baseUrl + "DeleteLink";
        public string DeleteResource => _baseUrl + "DeleteResource";
        public string ImportCatalogXml => _baseUrl + "ImportCatalogXml";
        public string ImportResources => _baseUrl + "ImportResources";
        public string ImportUpdateCompleted => _baseUrl + "ImportUpdateCompleted";
        public string IsImporting => _baseUrl + "IsImporting";
    }
}
