using System.Collections.Generic;
using Epinova.InRiverConnector.EpiserverAdapter;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using inRiver.Remoting.Objects;
using Moq;
using Xunit;

namespace Epinova.InRiverConnector.EpiserverAdapterTests
{
    public class CatalogCodeGeneratorTests
    {
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private Mock<IConfiguration> _config;
        private Mock<IEntityService> _entityService;

        public CatalogCodeGeneratorTests()
        {
            _config = new Mock<IConfiguration>();
            _config.Setup(x => x.EpiCodeMapping).Returns(new Dictionary<string, string>());
            _entityService = new Mock<IEntityService>();
            _catalogCodeGenerator = new CatalogCodeGenerator(_config.Object, _entityService.Object);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_EntityIdIsZero_Null()
        {
            var episerverCode = _catalogCodeGenerator.GetEpiserverCode(0);
            Assert.Null(episerverCode);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_NoMappingsExist_EntityId()
        {
            var id = 123;
            var entity = EntityFactory.CreateItem(id);

            var result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_ValidMappingsExist_OtherFieldValue()
        {
            var mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                {"Item", mappedField}
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            var id = 123;
            var expectedCode = "ABC";
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));
            
            var result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal(expectedCode, result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_MappingsExist_NotValidForThisEntityType_EntityId()
        {
            var mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                {"Product", mappedField}
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            var id = 123;
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));

            var result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_MappingsExist_NotValidForThisEntityType_ChannelPrefixExists_PrefixedEntityId()
        {
            var mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string> { {"Product", mappedField} };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);
            
            var channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            var id = 123;
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));

            var result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_NoMappingsExist_ChannelPrefixExists_PrefixedEntityId()
        {
            var id = 123;
            var entity = EntityFactory.CreateItem(id);
        
            var channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            var result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_ValidMappingsExist_ChannelPrefixExists_PrefixedOtherFieldValue()
        {
            var mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string> { {"Item", mappedField} };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            var channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            var id = 123;
            var expectedCode = "ABC";
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));

            var result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal($"{channelPrefix}{expectedCode}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_NoMappingsExist_EntityId()
        {
            var id = 123;
            var entity = EntityFactory.CreateItem(id);

            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);

            var result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_ValidMappingsExist_OtherFieldValue()
        {
            var mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                {"Item", mappedField}
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            var id = 123;
            var expectedCode = "ABC";
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));

            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            var result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal(expectedCode, result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_MappingsExist_NotValidForThisEntityType_EntityId()
        {
            var mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                {"Product", mappedField}
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            var id = 123;
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));
            
            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            var result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_MappingsExist_NotValidForThisEntityType_ChannelPrefixExists_PrefixedEntityId()
        {
            var mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string> { { "Product", mappedField } };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            var channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            var id = 123;
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));
            
            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            var result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_NoMappingsExist_ChannelPrefixExists_PrefixedEntityId()
        {
            var id = 123;
            var entity = EntityFactory.CreateItem(id);

            var channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);
            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);

            var result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_ValidMappingsExist_ChannelPrefixExists_PrefixedOtherFieldValue()
        {
            var mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string> { { "Item", mappedField } };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            var channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            var id = 123;
            var expectedCode = "ABC";
            var entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));
            
            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            var result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal($"{channelPrefix}{expectedCode}", result);
        }
    }
}