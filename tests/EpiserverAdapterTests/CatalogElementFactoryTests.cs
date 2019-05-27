using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.EpiserverAdapter.XmlFactories;
using Epinova.InRiverConnector.EpiserverAdapterTests.Fakes;
using inRiver.Remoting.Objects;
using Moq;
using Xunit;

namespace Epinova.InRiverConnector.EpiserverAdapterTests
{
    public class CatalogElementFactoryTests
    {
        private readonly CatalogElementFactory _catalogElementFactory;
        private readonly Mock<IPimFieldAdapter> _pimFieldAdapterMock;

        public CatalogElementFactoryTests()
        {
            var configMock = new Mock<IConfiguration>();
            var mappingHelper = new FakeEpiMappingHelper();
            var catalogCodeGenerator = new FakeCatalogCodeGenerator();
            _pimFieldAdapterMock = new Mock<IPimFieldAdapter>();

            _catalogElementFactory = new CatalogElementFactory(configMock.Object, mappingHelper, catalogCodeGenerator, _pimFieldAdapterMock.Object);

            configMock.Setup(m => m.LanguageMapping).Returns(new Dictionary<CultureInfo, CultureInfo> { { CultureInfo.CurrentCulture, CultureInfo.CurrentCulture } });
        }

        [Theory]
        [InlineData("seouri")]
        [InlineData("seotitle")]
        [InlineData("seodescription")]
        [InlineData("seokeywords")]
        [InlineData("seourisegment")]
        public void SeoFieldValueIsEmpty_CreateSEOInfoElement_DoesNotReturnEmptyNode(string name)
        {
            _pimFieldAdapterMock
                .Setup(m => m.GetFieldValue(It.IsAny<Entity>(), name, It.IsAny<CultureInfo>()))
                .Returns(Guid.NewGuid().ToString("N"));

            _pimFieldAdapterMock
                .Setup(m => m.GetFieldValue(It.IsAny<Entity>(), It.Is<string>(s => !s.Equals(name)), It.IsAny<CultureInfo>()))
                .Returns(String.Empty);

            XElement result = _catalogElementFactory.CreateSEOInfoElement(EntityFactory.CreateItem(123));

            Assert.Equal(3, result.Descendants().Count());
        }
    }
}