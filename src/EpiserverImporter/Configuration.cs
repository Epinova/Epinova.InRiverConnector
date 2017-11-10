using System;
using System.Configuration;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class Configuration
    {
        public bool RunCatalogImportHandlers => GetBoolSetting("inRiver.RunICatalogImportHandlers");
        public bool RunDeleteActionsHandlers => GetBoolSetting("inRiver.RunIDeleteActionsHandlers");
        public bool RunInRiverEventsHandlers => GetBoolSetting("inRiver.RunIInRiverEventsHandlers");
        public bool RunResourceImporterHandlers => GetBoolSetting("inRiver.RunIResourceImporterHandlers");

        private bool GetBoolSetting(string key)
        {
            var setting = ConfigurationManager.AppSettings[key];
            return setting != null && setting.Equals(key, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}