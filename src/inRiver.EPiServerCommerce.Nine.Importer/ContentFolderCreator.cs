using System.Linq;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Security;
using EPiServer.ServiceLocation;

namespace inRiver.EPiServerCommerce.Importer
{
    public class ContentFolderCreator
    {
        public static ContentReference CreateOrGetFolder(ContentReference startFrom, string folderName)
        {
            var contentRepository = ServiceLocator.Current.GetInstance<IContentRepository>();
            var inriverContentFolder = contentRepository.GetChildren<ContentFolder>(startFrom).Where(x => x.Name == folderName);

            if (!inriverContentFolder.Any())
            {
                var newFolder = contentRepository.GetDefault<ContentFolder>(startFrom);
                newFolder.Name = folderName;
                return contentRepository.Save(newFolder, SaveAction.Save, AccessLevel.NoAccess);
            }

            return inriverContentFolder.First().ContentLink;
        }
    }
}