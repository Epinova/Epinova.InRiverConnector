using System.Collections.Generic;
using Epinova.InRiverConnector.EpiserverAdapter;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Remoting.Objects;
using Moq;
using Xunit;

namespace Epinova.InRiverConnector.EpiserverAdapterTests.Helpers
{
    public class EpiMappingHelperTests
    {
        private readonly Mock<IConfiguration> _config;
        private readonly EpiMappingHelper _epiMappingHelper;

        public EpiMappingHelperTests()
        {
            _config = new Mock<IConfiguration>();
            var pimFieldAdapter = new Mock<IPimFieldAdapter>();
            _epiMappingHelper = new EpiMappingHelper(_config.Object, pimFieldAdapter.Object);
        }

        [Fact]
        public void IsChannelNodeLink_SourceTypeIsChannelNode_True()
        {
            string linkTypeId = "ChannelNodeProduct";
            var linkTypes = new List<LinkType>
            {
                new LinkType { Id = linkTypeId, SourceEntityTypeId = "ChannelNode" }
            };

            _config.Setup(x => x.LinkTypes).Returns(linkTypes);

            bool result = _epiMappingHelper.IsChannelNodeLink(linkTypeId);
            Assert.True(result);
        }

        [Fact]
        public void IsChannelNodeLink_SourceTypeIsChannelTargetIsChannelNode_True()
        {
            string linkTypeId = "ChannelNodeProduct";
            var linkTypes = new List<LinkType>
            {
                new LinkType { Id = linkTypeId, SourceEntityTypeId = "Channel", TargetEntityTypeId = "ChannelNode" }
            };

            _config.Setup(x => x.LinkTypes).Returns(linkTypes);

            bool result = _epiMappingHelper.IsChannelNodeLink(linkTypeId);
            Assert.True(result);
        }

        [Fact]
        public void IsChannelNodeLink_SourceTypeIsNotChannelNode_False()
        {
            string linkTypeId = "ProductItems";
            var linkTypes = new List<LinkType>
            {
                new LinkType { Id = linkTypeId, SourceEntityTypeId = "Product" }
            };

            _config.Setup(x => x.LinkTypes).Returns(linkTypes);

            bool result = _epiMappingHelper.IsChannelNodeLink(linkTypeId);
            Assert.False(result);
        }
    }
}
