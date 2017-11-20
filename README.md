# Epinova.InRiverConnector

This is a major modification of inRiver's own connector, located at https://github.com/inRiver/EPiServer. It connects inRiver PIM with Episerver Commerce, enabling you to keep your catalog in sync between the two systems.

**IMPORTANT NOTE:** If you've been using certain features of the original connector, **installing this might be a major breaking change**. Read this and test well. Godspeed!

*Big shout out to Optimera AS, Norway, that allowed me to spend time on this!*

## How to install:

1. Install NuGet
2. In `web.config\appSettings`, add an API key to `inRiver.apikey`. The same key must be added to the installed connector as a configuration with key `EPI_APIKEY`.
3. Locate the installed nuget package in `packages\Epinova.InRiverConnector.x.x.x.x\`. The files beneath `OutboundConnectors` must be copied to `%programfiles%\inRiver AB\inRiver Connect\OutboundConnectors`
4. Restart the `inRiver Connect` service and create a new connector. Configure it as explained below the *Connector configuration* section.

Visit `<yourSiteRoot>/inriverapi/inriverdataimport/get`, with an added HTTP header `apikey: <key-from-your-settings>`. This should return 200 OK along with a greeting.

## Config in Episerver (`web.config\appSettings`)

- Optional: Add an application setting (`appSettings` in `web.config`) `InRiverPimConnector.ResourceFolderName` to set your own root folder name for the imported resources (media files in Episerver). Defaults to `ImportedResources`.
- `inRiver.apikey` - let this contain the same value as you set in the connector configuration (`EPI_APIKEY`)
- `inRiver.RunICatalogImportHandlers` - `true` or `false`. Tells the connector whether or not to run your handlers when receiving messages from the PIM system.

## Connector configuration

- PUBLISH_FOLDER - Path for the connector to publish catalog data to. The Episerver web site needs read access to the same path.
- PUBLISH_FOLDER_RESOURCES - Path for the connector to publish resources to. The Episerver web site needs read access to the same path.
- RESOURCE_CONFIGURATION - Image formats to be exported. Values must match the imageConfigurations in inRiver.Server.exe.config.  (Original, Thumbnail, Preview, ...) This is a comma separated list. The Value Original is not a listed as a image configuration, because it returns the unchanged original image.
- ITEM_TO_SKUS - SKU data on the Items that should be exported as separate items (true/false)
- USE_THREE_LEVELS_IN_COMMERCE - Used together with ITEM_TO_SKUS for controlling if the SKUs should replace the Items or added under the Items (true/false).
- CVL_DATA - What information should be exported for the CVLs. (Keys/Values/KeysAndValues)
- BUNDLE_ENTITYTYPES - Entity types in inRiver that should be exported as Bundle. This is a comma separated list.
- PACKAGE_ENTITYTYPES- Entity types in inRiver that should be exported as Package. This is a comma separated list.
- DYUNAMIC_PACKAGE_ENTITYTYPES- Entity types in inRiver that should be exported as Dynamic Package. This is a comma separated list.
- CHANNEL_ID - The System ID of the channel the connector should listen to.
- EXCLUDE_FIELDS - Used to exclude fields in the import. Write the FieldTypeIds in a comma separated list
- EPI_CODE_FIELDS - Used to map the Code field in EPiServer Commerce to custom fields in inRiver. Write the FieldTypeIds (one for each entity type, that should be overridden) in a comma separated list. If empty the ChannelPrefix and the internal id in inRiver will be used as Code in EPiServer Commerce. If a field in inRiver is used, this field can not be updated once the channel is published.
- LANGUAGE_MAPPING setting makes it possible to map one PIM cultureinfo to one or several EPiCommerce cultureinfos. The syntax is the following: `<languages><language><epi>en-us</epi><inriver>en-us</inriver></language></languages>`
- `EXPORT_ENTITIES` - Should contain all the entity types you want to export. Defaults to `Product,Item,ChannelNode`, but you can add in anything really. These will be created as catalog entities, so adding things like milestones or activites will not make sense. Create your own integration for such things.
- `FORCE_INCLUDE_LINKED_CONTENT` - Set to `True` if you want to include everything linked to the channel via upsell/accessories and such, as well as via Product-Item-links or ChannelNode-Product links. Set to 'False' to only include those entities which directly belongs to a Product/Bundle/Package or a Channel Node. Defaults to False. These force-added entries will end up at the root of your catalog. Implement and register `ICatalogImportHandler.PostImport` if you want to fiddle about with it. (You might see this as a more controllable version of the old `inRiverAssociations` node.)
- HTTP_POST_URL - The connector will post the path to the recently imported file to the URL given here, if any. Could simply be implemented as shown below:


      [System.Web.Mvc.HttpPost]
      public ActionResult Foo(string filepath)
      {
          // Do your stuff
      }



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