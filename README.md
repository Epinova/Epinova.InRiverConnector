# Test if it's installed correctly:

Visit `<yourSiteRoot>/inriverapi/inriverdataimport/get`, with an added HTTP header `apikey: <key-from-your-settings>`. This should return 200 OK along with a greeting.

# Config in Episerver

- You can now add a setting (appSettings) `InRiverPimConnector.ResourceFolderName` to set your own root folder name for the imported resources (media files in Episerver). Defaults to `ImportedResources`.

# Changes from original connector

- Link entities will no longer be transferred in any way. They simply won't make any sense as catalog entries in Episerver - at least not generically for all solutions. Create your own integration to transfer this data correctly if needed. It only properly maintained it's display name anyways.
- inRiverAssociations node: This concept has been completely removed. The node will never be created, and anything that would live just inside this (like linked products/items) and nowhere else will simply not be transferred to Episerver. Should make catalog management drastically cleaner.
- Inventory and price exports: This has been *removed*. The PIM should not be responsible for this in any way.  The original connector contained loads of code for dealing with inventory and price updates. This was not documented anywhere, nor reflected in default connector settings. 
- No longer creates empty `InRiverGenericMedia` objects for resources in InRiver without a file attached to it.
- No longer creates `InRiverGenericMedia` for file types not matching any of your implemented media types. If your Episerver site does not have a content type matching a file extension, no file will be created.
- Support for `ChannelMimeTypeMappings` on your channel entity is removed. No need to increase complexity by tweaking internal workings of the connector.
- CVL-values are no longer transferred as dictionaries - only the key+value is returned (configuratble as before). In Episerver, model your catalog entries to have normal string properties ("LongString") for these. Episerver has no need to maintain these values internally.

## ICatalogImportHandlers implemented?

Both the original file AND the modified file will now reside in the data directory where the connector puts it's data for import.

## Configuration changes

- `RESOURCE_PROVIDER_TYPE` has been removed. Write your own integration if this was truly needed.
- `EPI_MAJOR_VERSION` has been removed. It had no purpose.
- `MODIFY_FILTER_BEHAVIOR` has been removed. Invisible and undocumented, poorly named and probably never used.
- Support for `EPiDataType` field setting has been removed, as it's utterly pointless and can never do anything but create harm.
- `EXPORT_ENTITIES` is new. It should contain all the entity types you want to export. Defaults to `Product,Item`, but you can add in anything really. These will be created as catalog entities, so adding things like milestones or activites will not make sense. Create your own integration for such things.
- `FORCE_INCLUDE_LINKED_CONTENT` - new setting. Set to `True` if you want to include everything linked to the channel via upsell/accessories and such, as well as via Product-Item-links or ChannelNode-Product links. Set to 'False' to only include those entities which directly belongs to a Product/Bundle/Package or a Channel Node. Defaults to False. These force-added entries will end up at the root of your catalog. Implement and register `ICatalogImportHandler.PostImport` if you want to fiddle about with it. (You might see this as a more controllable version of the `inRiverAssociations` node from the original connector.)
- `HTTP_POST_URL` - The URL you supply here no longer receives a ZIP-file, but rather the path to the recently imported file. You could implement this like below:


      [System.Web.Mvc.HttpPost]
      public ActionResult Foo(string filepath)
      {
          // Do your stuff
      }



- Setting `AllowsSearch` on your field type (with values `True` or `False`), tells the built in search index in CommerceManager whether the field is searchable or not.