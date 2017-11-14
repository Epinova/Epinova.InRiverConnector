using System;
using System.Collections.Generic;
using System.Linq;
using inRiver.Integration.Reporting;
using inRiver.Remoting;
using inRiver.Remoting.Connect;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public class ConnectorEventHelper
    {
        internal static ConnectorEvent InitiateEvent(IConfiguration config, ConnectorEventType messageType, string message, int percentage, bool error = false)
        {
            ConnectorEvent connectorEvent = new ConnectorEvent
            {
                ChannelId = config.ChannelId,
                ConnectorEventType = messageType,
                ConnectorId = config.Id,
                EventTime = DateTime.Now,
                SessionId = Guid.NewGuid(),
                Percentage = percentage,
                IsError = error,
                Message = message
            };

            ReportManager.Instance.WriteEvent(connectorEvent);
            return connectorEvent;
        }

        internal static ConnectorEvent UpdateEvent(ConnectorEvent connectorEvent, string message, int percentage, bool error = false)
        {
            if (percentage >= 0)
            {
                connectorEvent.Percentage = percentage;
            }

            connectorEvent.Message = message;
            connectorEvent.IsError = error;
            connectorEvent.EventTime = DateTime.Now;
            ReportManager.Instance.WriteEvent(connectorEvent);
            return connectorEvent;
        }

        internal static void CleanupOngoingEvents(IConfiguration configuration)
        {
            List<ConnectorEventSession> sessions = RemoteManager.ChannelService.GetOngoingConnectorEventSessions(configuration.ChannelId, configuration.Id);
            foreach (ConnectorEventSession connectorEventSession in sessions)
            {
                ConnectorEvent latestConnectorEvent = connectorEventSession.ConnectorEvents.First();
                ConnectorEvent connectorEvent = new ConnectorEvent
                {
                    SessionId = latestConnectorEvent.SessionId,
                    ChannelId = latestConnectorEvent.ChannelId,
                    ConnectorId = latestConnectorEvent.ConnectorId,
                    ConnectorEventType = latestConnectorEvent.ConnectorEventType,
                    Percentage = latestConnectorEvent.Percentage,
                    IsError = true,
                    Message = "Event stopped due to closedown of connector",
                    EventTime = DateTime.Now
                };
                ReportManager.Instance.WriteEvent(connectorEvent);
            }
        }
    }
}