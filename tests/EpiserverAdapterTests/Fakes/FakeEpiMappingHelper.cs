using Epinova.InRiverConnector.EpiserverAdapter;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Moq;

namespace Epinova.InRiverConnector.EpiserverAdapterTests.Fakes
{
    public class FakeEpiMappingHelper : EpiMappingHelper
    {
        public FakeEpiMappingHelper() : base(new Mock<IConfiguration>().Object, new Mock<IPimFieldAdapter>().Object)
        {
        }
    }
}
