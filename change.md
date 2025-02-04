# v0.3.0

### TODO
 - For Release (Im writing this here because if I don't I *will* forget one of these :P)
   - Disable verbose logging (if it is on)
   - Disable SetSeed (if it is on)
   - Disable log promotion (if it is on)
 - Actual development
   - Check that everything works between server/client because I don't trust it


### New Features
 - [#2](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/2)
   Made the surfaces of moons distinct from their interiors. If you bring scrap outside, it will still be outside if you take off and come back, even if a different interior is used the next day. 
 - [#1](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/1)
   Added cruiser preservation/saving

### Bugfixes
 - Fixed a bug where radar icons for scrap would still appear if the scrap was destroyed during map generation. 

### QOL Improvements
- [#8](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/8) Improved prop serialization, props should take ~1/8 the size on disk they were before. 

### Code Changes

#### Mapstuff
 - Removed `DGameMap.DestroyAllScrap`, put the functionality in `MapHandler.DestroyAllScrap`
 - `MapObject.FindParent`:
   - Now uses a `DGameMap` parameter instead of a `GameMap`
   - Now includes inactive tiles if the map itself is inactive in hierarchy. 
 - Added an `InvalidOperationException` to `DTile.Initialize` if it is initialized while inactive
    - This was to avoid the situation where a tile would attempt to get its bounds from a collider while inactive, since collider bounds are uninitialized until the gameobject becomes active. 
 - Added a `public class Cruiser : NetworkBehaviour` to handle cruisers. 
   - Instead of enabling/disabling cruisers at the beginning/end of each day, we spawn a new cruiser where the old one was and despawn the old one. The reason for this is when a cruiser is simply disabled and then reactivated, [it achieves sentience, and really doesn't like it](https://drive.google.com/file/d/1aLKINnCEqxu60rzCalJXg-Va1eajOqaN/view?usp=sharing). 

#### Patches
 - Reorganized patches
   - Moved patches relating to saving from `patches/LevelGeneration.cs` to `patches/Saving.cs`
   - Renamed and repurposed `PrefabHandler.cs` to be for any networking-related patches instead of just initializing network prefabs. 
     - New name is `Networking.cs`

#### Serialization
 - Changed `Serializer<T>` so the method `Serialize` and `Finalize` could be written with better typing. It still won't generate compile-time errors for mistakes, but it's prettier to read for inheriting classes. 
 - Added methods `AddBools` and `ConsumeBools` to `SerializationContext` and `DeserializationContext` respectively to allow the packing of bools conveniently. 