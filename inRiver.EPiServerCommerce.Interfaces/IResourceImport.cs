namespace inRiver.EPiServerCommerce.Interfaces
{
    public interface IResourceImport
    {
        bool ImportResources(string manifest, string baseResourcePath, string id);
    }
}
