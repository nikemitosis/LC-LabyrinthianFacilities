# v0.3.0

### TODO
 - For Release (Im writing this here because if I don't I *will* forget one of these :P)
   - Update version number
   - Disable verbose logging (if it is on)
   - Disable SetSeed (if it is on)
   - Disable log promotion (if it is on)
 - Actual development
   - [#1 - Company Cruiser Saving](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/1)
   - [#8 - Optimize prop serialization](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/8)


### New Features
 - [#2](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/2)
   Made the surfaces of moons distinct from their interiors. If you bring scrap outside, it will still be outside if you take off and come back, even if a different interior is used. 

### Bug fixes
 - Fixed a bug where radar icons for scrap would still appear if the scrap was destroyed during map generation. 

### Code Changes
 - Removed `DGameMap.DestroyAllScrap`, put the functionality in `MapHandler.DestroyAllScrap`
 - Changed `Serializer<T>` so the method `Serialize` and `Finalize` could be written with better typing. It still won't generate compile-time errors for mistakes, but it's prettier to read for inheriting classes. 
 - Changed `MapObject.FindParent` to include two new (optional) paramters
   1. A `Moon` 'moon' to parent to if the map had no tiles for the `MapObject` to parent to (where it would've previously parented to the map. 
   2. A `bool?` 'includeInactive' for whether to include inactive tiles in the search for a tile to parent to.
      - Defaults to `null`, which means to include inactive tiles only if the map itself is inactive. 
 - Reorganized patches
   - Moved patches relating to saving from `patches/LevelGeneration.cs` to `patches/Saving.cs`
   - Renamed and repurposed `PrefabHandler.cs` to be for any networking-related patches instead of just initializing network prefabs. 
     - New name is `Networking.cs`
 - Added an `InvalidOperationException` to `DTile` if it is initialized while inactive
    - This was to avoid the situation where a tile would attempt to get its bounds from a collider while inactive, since collider bounds are uninitialized until the gameobject becomes active. 