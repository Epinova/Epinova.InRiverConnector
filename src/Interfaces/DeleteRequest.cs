using System.Collections.Generic;

namespace Epinova.InRiverConnector.Interfaces
{
    public class DeleteRequest
    {
        public DeleteRequest()
        {
        }

        public DeleteRequest(string code)
        {
            Codes = new List<string> { code };
        }

        public DeleteRequest(List<string> codes)
        {
            Codes = codes;
        }

        public List<string> Codes { get; set; }
    }
}