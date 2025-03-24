# v0.6.1

## This patch breaks saves!

## Bugfixes
 - #38 Spike traps now have their settings saved (player detection, interval)


## Tweaks
 - #39 Slightly reduced save file sizes by grouping mapobjects by their prefab name<sup>Technical.Serialization.1</sup>


## Technical

### Serialization
 1. Added support for serializing `ICollection<T>` where each element shares some common information
   - In the case of `MapObject`s on a moon/cruiser/map, the common information is the prefab name
 