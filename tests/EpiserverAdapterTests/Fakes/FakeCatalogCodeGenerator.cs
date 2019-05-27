using Epinova.InRiverConnector.EpiserverAdapter;
using Moq;

namespace Epinova.InRiverConnector.EpiserverAdapterTests.Fakes
{
    public class FakeCatalogCodeGenerator : CatalogCodeGenerator
    {
        public FakeCatalogCodeGenerator() : base(new Mock<IConfiguration>().Object, new Mock<IEntityService>().Object)
        {
        }
    }
}