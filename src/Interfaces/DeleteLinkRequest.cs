namespace Epinova.InRiverConnector.Interfaces
{
    public class DeleteLinkRequest
    {
        public bool IsRelation { get; set; }
        public string SourceCode { get; set; }
        public string TargetCode { get; set; }
    }
}