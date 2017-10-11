namespace inRiver.EPiServerCommerce.Importer
{
    public class Singleton
    {
        private static Singleton instance = new Singleton();

        private Singleton()
        {
        }

        public static Singleton Instance
        {
            get
            {
                return instance ?? (instance = new Singleton());
            }
        }

        public string Message { get; set; }

        public bool IsImporting { get; set; }
    }
}