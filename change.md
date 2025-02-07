# v0.3.1

### Bugfixes
 - Potential fix for beehives being left behind even if they are considered "collected"

### Code Changes

#### Serialization
 - [#14](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/14)
   Removed `extraContext` parameter for deserialization. (`extraContext` can always be implemented as fields/properties/etc. in inheritors of `ISerializer<T>`)
   - As of now, most inheritors of `Serializer<T>` have their old `extraContext` as a required parameter (if applicable). This means that even during serialization, where `extraContext` did not previously exist, we still must provide the parameter. For now, we are providing `null` in most cases. 
 - Added more/better error messages to various public methods