using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.EpiXml;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.Interfaces;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Utilities
{
    public class DeleteUtility
    {
        private readonly EpiApi _epiApi;
        private readonly CatalogCodeGenerator _catalogCodeGenerator;
        private readonly DocumentFileHelper _documentFileHelper;
        private readonly IConfiguration _config;
        private readonly ResourceElementFactory _resourceElementFactory;
        private readonly EpiElementFactory _epiElementFactory;
        private readonly ChannelHelper _channelHelper;

        public DeleteUtility(IConfiguration config, 
                             ResourceElementFactory resourceElementFactory, 
                             EpiElementFactory epiElementFactory, 
                             ChannelHelper channelHelper, 
                             EpiApi epiApi,
                             CatalogCodeGenerator catalogCodeGenerator,
                             DocumentFileHelper documentFileHelper)
        {
            _config = config;
            _resourceElementFactory = resourceElementFactory;
            _epiApi = epiApi;
            _catalogCodeGenerator = catalogCodeGenerator;
            _documentFileHelper = documentFileHelper;
            _epiElementFactory = epiElementFactory;
            _channelHelper = channelHelper;
        }

        public void Delete(Entity channelEntity, Entity deletedEntity)
        {
            string channelIdentifier = _channelHelper.GetChannelIdentifier(channelEntity);

            IntegrationLogger.Write(LogLevel.Debug, "Deleting entity that doesn't exist in channel.");

            DeleteEntity(channelEntity, deletedEntity);
        }

        private void DeleteEntity(Entity channelEntity, Entity deletedEntity)
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
    }
}
