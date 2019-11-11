using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Http;
using inRiver.Remoting;
using inRiver.Remoting.Objects;
using inRiver.Remoting.Security;

namespace EpiserverRestApi
{
    [RoutePrefix("api/channeldata")]
    public class ChannelDataController : ApiController
    {
        [HttpGet]
        [Route("GetVariations/{nodeName}")]
        public IEnumerable<string> GetVariations(string nodeName)
        {
            Authenticate();

            int channelId = Int32.Parse(ConfigurationManager.AppSettings["channelId"]);
            Entity node = RemoteManager.ChannelService.GetEntitiesForChannelAndEntityType(channelId, "ChannelNode")
                .FirstOrDefault(x => x.DisplayName.Data.ToString().Equals(nodeName, StringComparison.InvariantCultureIgnoreCase));
            if (node == null)
                return Enumerable.Empty<string>();

            return RemoteManager.ChannelService.GetAllChannelStructureEntitiesForTypeFromPath($"{channelId}/{node.Id}", "Item")
                .Select(x => x.Name);
        }

        private static void Authenticate()
        {
            AuthenticationTicket ticket = RemoteManager.Authenticate(ConfigurationManager.AppSettings["authenticationUrl"], ConfigurationManager.AppSettings["username"],
                ConfigurationManager.AppSettings["password"]);

            // Initialize RemoteManager 
            RemoteManager.CreateInstance(ConfigurationManager.AppSettings["authenticationUrl"], ticket);
        }
    }
}
