# v0.6.1

## This patch breaks saves! (sorry)

## Bugfixes
 - #38 Spike traps now have their settings saved (player detection, interval)
 - Cruisers left on moons no longer appear in the side of the ship for client when the magnet is on overnight


## Tweaks
 - #39 Slightly reduced save file sizes by grouping mapobjects by their prefab name<sup>Technical.Serialization.1</sup>


## Technical

### Serialization
1. Added support for serializing `ICollection<T>` where each element shares some common information
   - In the case of `MapObject`s on a moon/cruiser/map, the common information is the prefab name
 - Added new `ISerializer` template `ItemSerializer<T>`, where data has distinct "Preamble" and "Data" segments. 
   - These segments are implied in `Serializer<T>` through the difference between `Deserialize(DeserializationContext)` and `Deserialize(T, DeserializationContext)`, but making them explicit enables `CollectionSerializer<T>` to conserve space. 
 