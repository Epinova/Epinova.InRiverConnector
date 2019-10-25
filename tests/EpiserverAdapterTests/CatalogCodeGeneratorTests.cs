using System.Collections.Generic;
using Epinova.InRiverConnector.EpiserverAdapter;
using inRiver.Remoting.Objects;
using Moq;
using Xunit;

namespace Epinova.InRiverConnector.EpiserverAdapterTests
{
    public class CatalogCodeGeneratorTests
    {
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly Mock<IConfiguration> _config;
        private readonly Mock<IEntityService> _entityService;

        public CatalogCodeGeneratorTests()
        {
            _config = new Mock<IConfiguration>();
            _config.Setup(x => x.EpiCodeMapping).Returns(new Dictionary<string, string>());
            _entityService = new Mock<IEntityService>();
            _catalogCodeGenerator = new CatalogCodeGenerator(_config.Object, _entityService.Object);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_MappingsExist_NotValidForThisEntityType_ChannelPrefixExists_PrefixedEntityId()
        {
            string mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string> { { "Product", mappedField } };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            string channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));

            string result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_MappingsExist_NotValidForThisEntityType_EntityId()
        {
            string mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                { "Product", mappedField }
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));

            string result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_NoMappingsExist_ChannelPrefixExists_PrefixedEntityId()
        {
            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);

            string channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            string result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_NoMappingsExist_EntityId()
        {
            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);

            string result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_ValidMappingsExist_ChannelPrefixExists_PrefixedOtherFieldValue()
        {
            string mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string> { { "Item", mappedField } };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            string channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            int id = 123;
            string expectedCode = "ABC";
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));

            string result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal($"{channelPrefix}{expectedCode}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntity_ValidMappingsExist_OtherFieldValue()
        {
            string mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                { "Item", mappedField }
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            int id = 123;
            string expectedCode = "ABC";
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));

            string result = _catalogCodeGenerator.GetEpiserverCode(entity);
            Assert.Equal(expectedCode, result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_EntityIdIsZero_Null()
        {
            string episerverCode = _catalogCodeGenerator.GetEpiserverCode(0);
            Assert.Null(episerverCode);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_MappingsExist_NotValidForThisEntityType_ChannelPrefixExists_PrefixedEntityId()
        {
            string mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string> { { "Product", mappedField } };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            string channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));

            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            string result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_MappingsExist_NotValidForThisEntityType_EntityId()
        {
            string mappedField = "ProductOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                { "Product", mappedField }
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField("ABC", mappedField));

            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            string result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_NoMappingsExist_ChannelPrefixExists_PrefixedEntityId()
        {
            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);

            string channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);
            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);

            string result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal($"{channelPrefix}{id}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_NoMappingsExist_EntityId()
        {
            int id = 123;
            Entity entity = EntityFactory.CreateItem(id);

            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);

            string result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal(id.ToString(), result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_ValidMappingsExist_ChannelPrefixExists_PrefixedOtherFieldValue()
        {
            string mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string> { { "Item", mappedField } };
            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            string channelPrefix = "Prefix_";
            _config.Setup(x => x.ChannelIdPrefix).Returns(channelPrefix);

            int id = 123;
            string expectedCode = "ABC";
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));

            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            string result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal($"{channelPrefix}{expectedCode}", result);
        }

        [Fact]
        public void GetEpiserverCodeWithEntityId_ValidMappingsExist_OtherFieldValue()
        {
            string mappedField = "ItemOtherField";

            var codeMapping = new Dictionary<string, string>
            {
                { "Item", mappedField }
            };

            _config.Setup(x => x.EpiCodeMapping).Returns(codeMapping);

            int id = 123;
            string expectedCode = "ABC";
            Entity entity = EntityFactory.CreateItem(id);
            entity.Fields.Add(FieldFactory.CreateField(expectedCode, mappedField));

            _entityService.Setup(x => x.GetEntity(It.IsAny<int>(), It.IsAny<LoadLevel>())).Returns(entity);
            string result = _catalogCodeGenerator.GetEpiserverCode(id);
            Assert.Equal(expectedCode, result);
        }
    }
}
