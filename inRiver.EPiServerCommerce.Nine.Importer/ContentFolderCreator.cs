namespace inRiver.EPiServerCommerce.Nine.Importer
{
    using System.Linq;

    using EPiServer;
    using EPiServer.Core;
    using EPiServer.DataAccess;
    using EPiServer.Security;
    using EPiServer.ServiceLocation;

    public class ContentFolderCreator
    {
        // ReSharper disable PossibleMultipleEnumeration
        public static ContentReference CreateOrGetFolder(string folderName)
        {
            return CreateOrGetFolder(ContentReference.GlobalBlockFolder, folderName);
        }

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

        public static void DeleteInRiverFolder(string folderName)
        {
            var contentRepository = ServiceLocator.Current.GetInstance<IContentRepository>();
            var inriverContentFolder = contentRepository.GetChildren<ContentFolder>(ContentReference.GlobalBlockFolder).Where(x => x.Name == folderName);
            if (inriverContentFolder.Any())
            {
                contentRepository.Delete(inriverContentFolder.First().ContentLink, true, AccessLevel.Administer);
            }
        }
    }
}