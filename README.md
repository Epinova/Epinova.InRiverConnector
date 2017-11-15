# Test if it's installed correctly:

Visit <yourSiteRoot>/inriverapi/inriverdataimport/get, with an added HTTP header `apikey: <key-from-your-settings>`. This should return 200 OK along with a greeting.

# Config in Episerver

- You can now add a setting (appSettings) `InRiverPimConnector.ResourceFolderName` to set your own folder name for the imported resources. Defaults to `ImportedResources`.

# Changes from original connector

## inRiverAssociations

This concept has been completely removed. The node will never be created, and anything that would live just inside this (like linked products/items) and nowhere else will simply not be transferred to Episerver. Should make catalog management drastically cleaner.

## Inventory and price exports

The original connector contained loads of code for dealing with inventory and price updates. This was not documented anywhere, nor reflected in default connector settings.

This has been *removed*. The PIM should not be responsible for this in any way. 

## ICatalogImportHandlers implemented?

Both the original file AND the modified file will now reside in the data directory where the connector puts it's data for import.

## CVL-values

The adapter no longer implements `ICVLListener` - thus it no longer maintains CVLs as dictionaries in Episerver. As a result, CVL-values are no longer transferred as dictionaries - only the key+value is returned (configuratble as before). In Episerver, model your catalog entries to have normal string properties ("LongString") for these.

Episerver has no need to maintain these values.

Caution: If updating a value that's already in use, this will no longer be updated automatically. Might be re-added in a fundamentally different way later on.

TODO: As of now there's a TODO to update all Episerver entities whenever a CVL value updates. This should usually not be a common case though, so I won't prioritize it quite yet.

## Configuration changes

- `RESOURCE_PROVIDER_TYPE` has been removed. Write your own integration if this was truly needed.
- `EPI_MAJOR_VERSION` has been removed. It had no purpose.
- `MODIFY_FILTER_BEHAVIOR` has been removed. Invisible and undocumented, poorly named and probably never used.
- Support for `EPiDataType` field setting has been removed, as it's utterly pointless and can never do anything but create harm.
- `EXPORT_ENTITIES` is new. It should contain all the entity types you want to export. Defaults to `Product,Item`, but you can add in anything really. These will be created as catalog entities, so adding things like milestones or activites will not make sense. Create your own integration for such things.
- `FORCE_INCLUDE_LINKED_CONTENT` - new setting. Set to `True` if you want to include everything linked to the channel via upsell/accessories and such, as well as via Product-Item-links or ChannelNode-Product links. Set to 'False' to only include those entities which directly belongs to a Product/Bundle/Package or a Channel Node. Defaults to False. These force-added entries will end up at the root of your catalog. Implement and register `ICatalogImportHandler.PostImport` if you want to fiddle about with it.

- Support for `ChannelMimeTypeMappings` on your channel entity is removed. There's simply no need to increase complexity by tweaking internal workings of the connector.
- Setting `AllowsSearch` on your field type (with values `True` or `False`), tells the built in search index in CommerceManager whether the field is searchable or not.

## Resources

- No longer creates completely empty InRiverGenericMedia objects for resources in InRiver without a file attached to it.
- No longer creates InRiverGenericMedia for file types not matching any of your implemented media types. If your Episerver site does not have a content type matching a file extension, no file will be created.