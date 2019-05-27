using System;
using System.Configuration;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class Configuration
    {
        public int DegreesOfParallelism
        {
            get
            {
                string appSetting = ConfigurationManager.AppSettings["InRiverConnector.DegreeOfParallelism"];
                return appSetting != null ? Int32.Parse(appSetting) : 2;
            }
        }

        public bool RunCatalogImportHandlers => GetBoolSetting("InRiverConnector.RunICatalogImportHandlers");
        public bool RunDeleteActionsHandlers => GetBoolSetting("InRiverConnector.RunIDeleteActionsHandlers"); // TODO: Document these three below here 
        public bool RunInRiverEventsHandlers => GetBoolSetting("InRiverConnector.RunIInRiverEventsHandlers");
        public bool RunResourceImporterHandlers => GetBoolSetting("InRiverConnector.RunIResourceImporterHandlers");

        private bool GetBoolSetting(string key)
        {
            string setting = ConfigurationManager.AppSettings[key];
            return setting != null && setting.Equals(Boolean.TrueString, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}