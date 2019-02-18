using Epinova.InRiverConnector.EpiserverAdapter;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Moq;

namespace Epinova.InRiverConnector.EpiserverAdapterTests.Fakes
{
    public class FakePimFieldAdapter : PimFieldAdapter
    {
        public FakePimFieldAdapter() : base(new Mock<IConfiguration>().Object)
        {
        }
    }
}