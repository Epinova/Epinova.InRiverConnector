using System.Collections.Generic;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.Interfaces;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Utilities
{
    public class DeleteUtility
    {
        private readonly EpiApi _epiApi;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly EpiMappingHelper _epiMappingHelper;
        private readonly IConfiguration _config;
        private readonly EpiElementFactory _epiElementFactory;

        public DeleteUtility(IConfiguration config, 
                             EpiElementFactory epiElementFactory, 
                             EpiApi epiApi,
                             CatalogCodeGenerator catalogCodeGenerator,
                             EpiMappingHelper epiMappingHelper)
        {
            _config = config;
            _epiApi = epiApi;
            _catalogCodeGenerator = catalogCodeGenerator;
            _epiMappingHelper = epiMappingHelper;
            _epiElementFactory = epiElementFactory;
        }

        public void Delete(Entity channelEntity, Entity deletedEntity)
        {
            switch (deletedEntity.EntityType.Id)
            {
                case "Resource":
                    var resourceGuid = EpiserverEntryIdentifier.EntityIdToGuid(deletedEntity.Id);
                    _epiApi.DeleteResource(resourceGuid);

                    break;
                case "Channel":
                    _epiApi.DeleteCatalog(deletedEntity.Id);
                    break;
                case "ChannelNode":
                    _epiApi.DeleteCatalogNode(deletedEntity, channelEntity.Id);
                    break;
                case "Item":
                    if ((_config.ItemsToSkus && _config.UseThreeLevelsInCommerce) || !_config.ItemsToSkus)
                    {
                        _epiApi.DeleteCatalogEntry(deletedEntity);
                    }

                    if (_config.ItemsToSkus)
                    {
                        var entitiesToDelete = new List<string>();

                        var skuElements = _epiElementFactory.GenerateSkuItemElemetsFromItem(deletedEntity);

                        foreach (XElement sku in skuElements)
                        {
                            XElement skuCodElement = sku.Element("Code");
                            if (skuCodElement != null)
                            {
                                entitiesToDelete.Add(skuCodElement.Value);
                            }
                        }

                        _epiApi.DeleteSkus(entitiesToDelete);
                    }

                    break;
                default:
                    _epiApi.DeleteCatalogEntry(deletedEntity);
                    break;
            }
        }

        public void DeleteResourceLink(Entity removedResource, Entity removalTarget)
        {
            var resourceGuid = EpiserverEntryIdentifier.EntityIdToGuid(removedResource.Id);
            var targetCode = _catalogCodeGenerator.GetEpiserverCode(removalTarget);

            _epiApi.DeleteLink(resourceGuid, targetCode);
        }

        public void DeleteLink(Entity removalSource, Entity removalTarget, string linkTypeId)
        {
            var isRelation = _epiMappingHelper.IsRelation(linkTypeId);

            var sourceCode = _catalogCodeGenerator.GetEpiserverCode(removalSource);
            var targetCode = _catalogCodeGenerator.GetEpiserverCode(removalTarget);

            _epiApi.DeleteLink(sourceCode, targetCode, isRelation);
        }
    }
}
