# v0.2.1-beta
## Patch 1

### Tweaks
- When a map first generates, it generates 1.5x more tiles than normal. This is a band-aid fix to maps feeling too small in the beginning. 

### Bugfixes
- [#9](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/9)
  Rewrote prop handling to prioritize blockers, then deal with tile props (local props), then map props (global props). This should prevent blockers/fire exits from going missing. 
  - Currently as implemented, blocker props are only enabled if *all* doorways that use them agree to use them. This was to resolve an issue with Manor's CloverTile's Fireplace blocker. 
  - Door props (connectors & blocker) are not *enabled* when resolving tile prop/map props, but they may be *disabled*. 

### Code Changes
- Refactored Serialization to unify it with Deserialization
- Returned to differentiating PropSets between local props and global props (and blockers/connectors)
  - Attempting to unify them resulted in the map rarely having a valid configuration of Props s.t. the range of no PropSet was violated. Getting a "close" answer would sometimes result in arrangements of props that didn't make sense (missing blockers, missing fire exits)
- Refactored GameMap, DDoorway, and DungeonFlowConverter to move all randomness to ITileGenerator
- Fixed a bug where WeightedList\<T> wouldn't immediately throw an error if you tried to access an element at 0.0 when it had zero elements. (it would still result in an exception, just not one explicitly thrown by WeightedList\<T>)
- Fixed a pseudo-bug where WeightedList\<T>[float] would throw an error if you passed the size of the list
  - While this was originally intended behaviour, it made the pattern `list[list.SummedWeight*(float)Rng.NextDouble()]` throw unexpected errors on rare occassion (due to rounding error, presumably)