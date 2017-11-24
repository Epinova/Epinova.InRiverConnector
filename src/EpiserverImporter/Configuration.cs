using System;
using System.Configuration;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class Configuration
    {
        public bool RunCatalogImportHandlers => GetBoolSetting("InRiverConnector.RunICatalogImportHandlers");
        public bool RunDeleteActionsHandlers => GetBoolSetting("InRiverConnector.RunIDeleteActionsHandlers"); // TODO: Document these three below here 
        public bool RunInRiverEventsHandlers => GetBoolSetting("InRiverConnector.RunIInRiverEventsHandlers");
        public bool RunResourceImporterHandlers => GetBoolSetting("InRiverConnector.RunIResourceImporterHandlers");

        public int DegreesOfParallelism
        {
            get
            {
                var appSetting = ConfigurationManager.AppSettings["InRiverConnector.DegreeOfParallelism"];
                return appSetting != null ? Int32.Parse(appSetting) : 2;
            }
        }

        private bool GetBoolSetting(string key)
        {
            var setting = ConfigurationManager.AppSettings[key];
            return setting != null && setting.Equals(true.ToString(), StringComparison.CurrentCultureIgnoreCase);
        }
    }
}