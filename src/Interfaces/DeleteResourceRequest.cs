using System;

namespace Epinova.InRiverConnector.Interfaces
{
    public class DeleteResourceRequest
    {
        public Guid ResourceGuid { get; set; }

        public string EntryToRemoveFrom { get; set; }
    }
}