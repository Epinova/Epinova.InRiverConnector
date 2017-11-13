namespace Epinova.InRiverConnector.Interfaces
{
    public class DeleteLinkRequest
    {
        public string SourceCode { get; set; }
        public string TargetCode { get; set; }
        public bool IsRelation { get; set; }
    }
}