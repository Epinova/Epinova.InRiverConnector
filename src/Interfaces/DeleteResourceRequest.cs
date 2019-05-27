using System;

namespace Epinova.InRiverConnector.Interfaces
{
    public class DeleteResourceRequest
    {
        public string EntryToRemoveFrom { get; set; }
        public Guid ResourceGuid { get; set; }
    }
}