namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class ConfigDefaults
    {
        public static string CvlData = "Keys|Values|KeysAndValues";
        public static string EpiApiKey = "SomeGreatKey123";
        public static string EpiEndpoint = "https://www.example.com/inriverapi/InriverDataImport/";
        public static string EpiTimeout = "1";
        public static string ExportEntities = "Product,Item,ChannelNode";
        public static string ForceIncludeLinkedContent = "False";
        public static string ItemToSkus = "false";
        public static string LanguageMapping = "<languages><language><epi>en</epi><inriver>en-us</inriver></language></languages>";
        public static string PublishFolder = @"C:\temp\Publish\Epi";
        public static string ResourceConfiguration = "Preview";
        public static string ResourcesPublishFolder = @"C:\temp\Publish\Epi\Resources";
        public static string UseThreeLevelsinCommerce = "false";
    }
}