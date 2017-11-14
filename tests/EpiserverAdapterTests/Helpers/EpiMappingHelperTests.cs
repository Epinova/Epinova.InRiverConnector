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
        Mock<IConfiguration> _config;
        EpiMappingHelper _epiMappingHelper;
        Mock<IPimFieldAdapter> _pimFieldAdapter;

        public EpiMappingHelperTests()
        {
            _config = new Mock<IConfiguration>();
            _pimFieldAdapter = new Mock<IPimFieldAdapter>();
            _epiMappingHelper = new EpiMappingHelper(_config.Object, _pimFieldAdapter.Object);
        }

        [Fact]
        public void IsChannelNodeLink_SourceTypeIsChannelNode_True()
        {
            var linkTypeId = "ChannelNodeProduct";
            var linkTypes = new List<LinkType>
            {
                new LinkType {Id = linkTypeId, SourceEntityTypeId = "ChannelNode"}
            };

            _config.Setup(x => x.LinkTypes).Returns(linkTypes);

            var result = _epiMappingHelper.IsChannelNodeLink(linkTypeId);
            Assert.True(result);
        }

        [Fact]
        public void IsChannelNodeLink_SourceTypeIsChannelTargetIsChannelNode_True()
        {
            var linkTypeId = "ChannelNodeProduct";
            var linkTypes = new List<LinkType>
            {
                new LinkType {Id = linkTypeId, SourceEntityTypeId = "Channel", TargetEntityTypeId = "ChannelNode"}
            };

            _config.Setup(x => x.LinkTypes).Returns(linkTypes);

            var result = _epiMappingHelper.IsChannelNodeLink(linkTypeId);
            Assert.True(result);
        }

        [Fact]
        public void IsChannelNodeLink_SourceTypeIsNotChannelNode_False()
        {
            var linkTypeId = "ProductItems";
            var linkTypes = new List<LinkType>
            {
                new LinkType {Id = linkTypeId, SourceEntityTypeId = "Product"}
            };

            _config.Setup(x => x.LinkTypes).Returns(linkTypes);

            var result = _epiMappingHelper.IsChannelNodeLink(linkTypeId);
            Assert.False(result);
        }
    }
}