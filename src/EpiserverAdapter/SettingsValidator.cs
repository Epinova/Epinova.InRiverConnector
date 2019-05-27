using System;
using System.Collections.Generic;
using System.Configuration;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class SettingsValidator
    {
        private readonly Dictionary<string, string> _settings;

        public SettingsValidator(Dictionary<string, string> settings)
        {
            _settings = settings;
        }

        public void ValidateSettings()
        {
            if (SettingHasValue(ConfigKeys.ForceIncludeLinkedContent))
            {
                if(!bool.TryParse(_settings[ConfigKeys.ForceIncludeLinkedContent], out bool _))
                    throw new ConfigurationErrorsException($"Setting FORCE_INCLUDE_LINKED_CONTENT has invalid value. Set it to True or False. Default is {ConfigDefaults.ForceIncludeLinkedContent}");
            }

            if (!SettingHasValue("EPI_APIKEY"))
            {
                throw new ConfigurationErrorsException("Missing EPI_APIKEY setting on connector. It needs to be defined to else the calls will fail. Please see the documentation.");
            }

            if (!SettingHasValue("EPI_ENDPOINT_URL"))
            {
                throw new ConfigurationErrorsException("Missing EPI_ENDPOINT_URL setting on connector. It should point to the import end point on the EPiServer Commerce web site. Please see the documentation.");
            }

            ValidateEndpointAddress(_settings["EPI_ENDPOINT_URL"]);

            var timeoutString = _settings["EPI_RESTTIMEOUT"];
            if (!SettingHasValue("EPI_RESTTIMEOUT") || !Int32.TryParse(timeoutString, out _))
            {
                throw new ConfigurationErrorsException("Missing or invalid EPI_RESTTIMEOUT. This should be a valid integer:" + timeoutString);
            }
        }

        private bool SettingHasValue(string settingKey)
        {
            return _settings.ContainsKey(settingKey) && !String.IsNullOrWhiteSpace(_settings[settingKey]);
        }

        private void ValidateEndpointAddress(string address)
        {
            if (String.IsNullOrEmpty(address))
            {
                throw new ConfigurationErrorsException("Missing ImportEndPointAddress setting on connector. It should point to the import end point on the EPiServer Commerce web site. Please see the documentation.");
            }

            if (address.EndsWith("/") == false)
            {
                throw new ConfigurationErrorsException("Endpoint address should end with /");
            }
        }
    }
}