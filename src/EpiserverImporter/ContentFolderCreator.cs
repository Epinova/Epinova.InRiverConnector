using System.Linq;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Security;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    public class ContentFolderCreator
    {
        private readonly IContentRepository _contentRepo;

        public ContentFolderCreator(IContentRepository contentRepo)
        {
            _contentRepo = contentRepo;
        }

        public ContentReference CreateOrGetFolder(ContentReference parent, string folderName)
        {
            var existingFolder = _contentRepo.GetChildren<ContentFolder>(parent)
                                             .FirstOrDefault(x => x.Name == folderName);

            if (existingFolder != null)
                return existingFolder.ContentLink;

            var newFolder = _contentRepo.GetDefault<ContentFolder>(parent);
            newFolder.Name = folderName;
            return _contentRepo.Save(newFolder, SaveAction.Save, AccessLevel.NoAccess);
        }
    }
}