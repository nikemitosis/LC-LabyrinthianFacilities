# v0.4.0

### This version breaks old saves!!

## TODO Before release
 - Disable preprocessor symbols
   - SetSeed
   - Verbose flags
 - Issues to confirm and/or write and/or fix
   - Main often has very few doorways in use
   - Right now the two tunnel areas often spawn together in mineshafts
     - Maybe make archetypes spawn in order as they would in dungen?
       - Cheap, but wouldn't necessarily fix the issue
     - Maybe make it so archetypes can only use a root from an archetype it could normally spawn next to?
       - More expensive, but more definitive

### New Features
 - Overhauled tile placement
   - Map Size
     - At most, 10% of all active tiles can be deleted
     - There is now a minimum amount of tiles that can exist at any given time
       - This minimum is much greater than what would typically generate before
     - There is now a maximum amount of tiles that can exist at any given time
     - There is a maximum number of tiles that can be placed in one day
   - Tiles are now placed in regions with respect to DunGen's archetypes. 
     - DunGen GraphNodes are still not really implemented/planned. 
   - Added semisupport for DunGen's `TileRepeatMode.Allow`/`TileRepeatMode.Disallow`
     - DisallowImmediate still not supported
	 - Tiles are still allowed to repeat if they are generated on different days
 - Added cave lights

### Tweaks
 - Improved tile intersection detection<sup>1</sup>
 - Improved tile placement speeds drastically
 - Generation time reporting is now always reported as a Debug log (instead of requiring the `VERBOSE_GENERATION` flag)

### Bugfixes
 - Fixed a bug where conservative bounds for mineshaft tiles caused mapobjects to not be considered inside a tile
 - Fixed scrap radar icons not being retained between days
 - Fixed hazards not being able to spawn in tiles generated on previous days
 - Fixed a bug where Doorways wouldn't save which prop was their selected random prop. This would lead to doorways mistakenly believing they didn't have a door and spawning a second door. 
 - "Fixed" a bug where Serialization finalizers were not actually called in reverse-order of when they appeared. 
   - "Fixed" because now it's just in-order because it was easier to implement. 

### Code Changes
 - Moved global prop registration to be handled by `DGameMap` instead of `DTile`. This removes the need for DTile to have a DGameMap parent at the time of its initialization. 
 - Created a simple data structure `ChoiceList<T>` that's just an array that serves to yield a single element at a time until it has gone through all elements. It is intended that you call `Yield`
   - It is different than an `IEnumerable` in that you can yield elements in a random order.
 - Created another data structure `WeightedChoiceList<T>` that serves a similar purpose to `ChoiceList<T>`, except with weights for the choices. 
 - Fixed a bug in `WeightedList.Clear` where `SummedWeight` would not be reset when the list was cleared. 
 - Moved generation logic to a new file `GenerationImpl.cs`
   - This is the new location of `DungeonFlowConverter` and its new helper class `ArchetypeRegionPlacer`
 - Moved some debug messages to a flag called `VERBOSE_TILE_INIT`
 - Removed the debug message produced by `DGameMap` on tile placement fail
 - Named a comma. No, you read that right. 
 - Made `GenerationAction.YieldFrame` into its own distinct action; all other actions no longer produce a frame of delay. 
   - Also reverted `GenerationAction` back into an abstract class
 - Added support for setting an item's weight via indexer for `WeightedList<T>`
 - Added a data structure `BoundsMap` that's more or less a binary search for objects with `Bounds`
 - Moved logic involving doorways/leaves to a new class/interface `DoorwayManager`/`IDoorwayManager`
 - Removed `Tile.Intersects` in favor of doing an intersection check on a new `LooseBoundingBox` that is the same as the bounding box that was being compared before. 

#### Footnotes
1. Instead of linearly checking all tile bounds, tiles are organized into quadrants of the map, and those quadrants are organized in subquadrants, and so on. It is organized such that a tile only needs to check 64 tiles in the absolute worst case. In practice this number should typically be closer to 8. 