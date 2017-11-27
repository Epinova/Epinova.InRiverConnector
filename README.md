# Epinova.InRiverConnector

This is a major modification of inRiver's own connector, located at https://github.com/inRiver/EPiServer. It connects inRiver PIM with Episerver Commerce, enabling you to keep your catalog in sync between the two systems.

**IMPORTANT NOTE:** If you've been using certain features of the original connector, **installing this might be a major breaking change**. Read this and test well. Godspeed!

*Big shout out to Optimera AS, Norway, that allowed me to spend time on this!*

## How to install:

This version is built for **inRiver PIM 6.3.0 SP1**. Episerver-dependencies are set in the nuspec-file and should be handled automatically.

1. Install NuGet package (For now build it yourself: `nuget pack <path-to-clone-location>/src/EpiserverImporter/EpiserverImporter.csproj`. It'll hopefully end up in the Episerver nuget feed later when it has undergone some testing.)
2. In `web.config\appSettings`, add an API key to `inRiver.apikey`. The same key must be added to the installed connector as a configuration with key `EPI_APIKEY`.
3. Locate the installed nuget package in `packages\Epinova.InRiverConnector.x.x.x.x\`. The files beneath `OutboundConnectors` must be copied to `%programfiles%\inRiver AB\inRiver Connect\OutboundConnectors`
4. Restart the `inRiver Connect` service and create a new connector. Configure it as explained below the *Connector configuration* section.

To see if it's installed correctly in the Episerver website, visit `<yourSiteRoot>/inriverapi/inriverdataimport/get` (for example `www.example.com/inriverapi/inriverdataimport/get` , with an added HTTP header `apikey: <key-from-your-settings>`. If this returns 200 OK along with a greeting, the connector is installed correctly.

Note: This connector logs quite extensively in debug mode. If that's not needed, make sure your inRiver connect service runs with a higher log level, as this might affect performance.

## Config in Episerver (`web.config\appSettings`)

These settings should be added to your `web.config` file under `<appSettings>` as `<add key="name_given_below" value="a suitable value" />`.

- Optional: `InRiverPimConnector.ResourceFolderName` to set your own root folder name for the imported resources (media files in Episerver). Defaults to `ImportedResources`.
- Optional: `InRiverConnector.DegreeOfParallelism` - integer value. sets how many parallel threads the resource imports should run in. Defaults to 2. Increase number for fast systems with large imports. 2-4 will yield the best results for most systems.
- `InRiverConnector.APIKey` - let this contain the same value as you set in the connector configuration (`EPI_APIKEY`)
- `InRiverConnector.RunICatalogImportHandlers` - `true` or `false`. Tells the connector whether or not to run your handlers when receiving messages from the PIM system.

## Connector configuration

- `PUBLISH_FOLDER` - Path for the connector to publish catalog data to. The Episerver web site needs read access to the same path.
- `PUBLISH_FOLDER_RESOURCES` - Path for the connector to publish resources to. The Episerver web site needs read access to the same path.
- `RESOURCE_CONFIGURATION` - Image formats to be exported. Values must match the imageConfigurations in inRiver.Server.exe.config.  (Original, Thumbnail, Preview, ...) This is a comma separated list. The Value Original is not a listed as a image configuration, because it returns the unchanged original image.
- `ITEM_TO_SKUS` - SKU data on the Items that should be exported as separate items (true/false)
- `USE_THREE_LEVELS_IN_COMMERCE` - Used together with ITEM_TO_SKUS for controlling if the SKUs should replace the Items or added under the Items (true/false).
- `CVL_DATA` - What information should be exported for the CVLs. (Keys/Values/KeysAndValues)
- `BUNDLE_ENTITYTYPES` - Entity types in inRiver that should be exported as Bundle. This is a comma separated list.
- `PACKAGE_ENTITYTYPES`- Entity types in inRiver that should be exported as Package. This is a comma separated list.
- `DYNAMIC_PACKAGE_ENTITYTYPES` - Entity types in inRiver that should be exported as Dynamic Package. This is a comma separated list.
- `CHANNEL_ID` - The System ID of the channel the connector should listen to.
- `EXCLUDE_FIELDS` - Used to exclude fields in the import. Write the FieldTypeIds in a comma separated list
- `EPI_CODE_FIELDS` - Used to map the Code field in EPiServer Commerce to custom fields in inRiver. Write the FieldTypeIds (one for each entity type, that should be overridden) in a comma separated list. If empty the ChannelPrefix and the internal id in inRiver will be used as Code in EPiServer Commerce. If a field in inRiver is used, this field can not be updated once the channel is published.
- `LANGUAGE_MAPPING` - Maps one PIM CultureInfo to one or several CultureInfos in Episerver. The syntax ItemStartDate the following: `<languages><language><epi>en-us</epi><inriver>en-us</inriver></language></languages>`
- `EXPORT_ENTITIES` - Should contain all the entity types you want to export. Defaults to `Product,Item,ChannelNode`, but you can add in anything really. These will be created as catalog entities, so adding things like milestones or activites will not make sense. Create your own integration for such things.
- `FORCE_INCLUDE_LINKED_CONTENT` - Set to `True` if you want to include everything linked to the channel via upsell/accessories and such, as well as via Product-Item-links or ChannelNode-Product links. Set to 'False' to only include those entities which directly belongs to a Product/Bundle/Package or a Channel Node. Defaults to False. These force-added entries will end up at the root of your catalog. Implement and register `ICatalogImportHandler.PostImport` if you want to fiddle about with it. (You might see this as a more controllable version of the old `inRiverAssociations` node.)
- `EPI_ENDPOINT_URL` - Base endpoint for the connector to talk to the Episerver website with. Defaults to `https://www.example.com/inriverapi/InriverDataImport/`. Typically, you'll only change the host name (`www.example.com`) here.
- `EPI_APIKEY` - API key to secure the importer endpoint. Must match `inRiver.apikey` setting in the Episerver website.
- `EPI_RESTTIMEOUT` - Timeout in hours for connector calls to the Episerver website. Defaults to `1`.
- HTTP_POST_URL - The connector will post the path to the recently imported file to the URL given here, if any. Could simply be implemented as shown below:
- `BATCH_SIZE` - Amount of ChannelEntities to be handled at once. Useful for larger channels. Defaults to <empty>, which takes everything in one batch.


      [System.Web.Mvc.HttpPost]
      public ActionResult Foo(string filepath)
      {
          // Do your stuff
      }


## Special fields and settings in inRiver

- `EPiMetaFieldName` - if you give your fields a Setting with this name, the value you give it will be the property name when exported to Episerver. For instance, if you add `EPiMetaFieldName: Foo` to a field with the name `Bar`, you'll need a property `Foo` on your entry in Episerver (`public virtual string Foo { get; set; }`). Without setting this on the field, it'll be named `Bar`.
- If you add the fields `ItemStartDate` and `ItemEndDate` to your `Item` entity type, these will be transferred to Episerver in the correct properties. (This can be done for all entity types, just replace the `Item` portion of the field name with whatever entity you're setting them for).
- `ChannelPrefix` - The value of this field will prefix all IDs (Typically the `Code` property) in Episerver. *Important:* Only use integers for this, since ResourceFileId which is used to uniquely identify resources is an integer. *Should only be used if you have multiple channels containing the same entities in one Episerver solution.*
- `ChannelDefaultLanguage` - The default language for the catalog. Defaults to `en-us` if the field is not present on your channel.
- `ChannelDefaultWeightbase` - The default weightbase for the catalog.
- `ChannelDefaultCurrency` - The default currency for the catalog.
- Specifications in inRiver PIM will be transferred as HTML tables in a special property. To add it to your catalog entries, give it the following property: `public virtual XhtmlString SpecificationField { get; set; }`


## Troubleshooting

- To debug the Episerver side of things, add a logger called `Epinova.InRiverConnector` to `Episerverlog.config` and set it to `DEBUG` level.
- To debug the PIM connector side, see `inRiver Connect\Logs`. Make sure the inRiver log config is set to `DEBUG` if you want detailed information.

## Changes from original connector

- Link entities will no longer be transferred in any way. They simply won't make any sense as catalog entries in Episerver - at least not generically for all solutions. Create your own integration to transfer this data correctly if needed. It only properly maintained it's display name anyways.
- inRiverAssociations node: This concept has been completely removed. The node will never be created, and anything that would live just inside this (like linked products/items) and nowhere else will simply not be transferred to Episerver. Should make catalog management drastically cleaner.
- Inventory and price exports: This has been *removed*. The PIM should not be responsible for this in any way.  The original connector contained loads of code for dealing with inventory and price updates. This was not documented anywhere, nor reflected in default connector settings. 
- No longer creates empty `InRiverGenericMedia` objects for resources in InRiver without a file attached to it.
- No longer creates `InRiverGenericMedia` for file types not matching any of your implemented media types. If your Episerver site does not have a content type matching a file extension, no file will be created.
- Support for `ChannelMimeTypeMappings` on your channel entity is removed. No need to increase complexity by tweaking internal workings of the connector.
- CVL-values are no longer transferred as dictionaries - only the key+value is returned (configuratble as before). In Episerver, model your catalog entries to have normal string properties ("LongString") for these. Episerver has no need to maintain these values internally.
- If you implement ICatalogImportHandlers that changes the exported `Catalog.xml` file, both the original file and the modified file will now reside in the data directory where the connector puts it's data for import. This should hopefully make debugging slightly easier.

### Configuration changes from the original connector - Possibe breaking changes!

- `RESOURCE_PROVIDER_TYPE` has been removed. Write your own integration if this was truly needed.
- `EPI_MAJOR_VERSION` has been removed. It had no purpose.
- `MODIFY_FILTER_BEHAVIOR` has been removed. Invisible and undocumented, poorly named and probably never used.
- Support for `EPiDataType` field setting has been removed, as it's utterly pointless and can never do anything but create harm.
- `EXPORT_ENTITIES` is new. See description above.
- `FORCE_INCLUDE_LINKED_CONTENT` - new setting. See description above.
- `HTTP_POST_URL` - The URL you supply here no longer receives a ZIP-file, but rather the path to the recently imported file. 
- `EXPORT_INVENTORY_DATA` and `EXPORT_PRICING_DATA` have been removed.