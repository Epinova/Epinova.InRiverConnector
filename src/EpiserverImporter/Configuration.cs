using System;
using System.Configuration;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class Configuration
    {
        public bool RunICatalogImportHandlers => GetBoolSetting("inRiver.RunICatalogImportHandlers");
        public bool RunIDeleteActionsHandlers => GetBoolSetting("inRiver.RunIDeleteActionsHandlers");
        public bool RunIInRiverEventsHandlers => GetBoolSetting("inRiver.RunIInRiverEventsHandlers");
        public bool RunIResourceImporterHandlers => GetBoolSetting("inRiver.RunIResourceImporterHandlers");

        private bool GetBoolSetting(string key)
        {
            var setting = ConfigurationManager.AppSettings[key];
            return setting != null && setting.Equals(key, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}