using System;

namespace Epinova.InRiverConnector.Interfaces
{
    public class UpdateCodeFieldRequest
    {
        public Guid EntryGuid { get; set; }
        
        public string NewCode { get; set; }
    }
}