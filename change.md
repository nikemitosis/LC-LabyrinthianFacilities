# 0.8
 - Fixed Manor start room being able to generate twice due to its name being changed from `ManorStartRoom` to `ManorStartRoomSmall`
 - \[IN PROGRESS\] Fixed Sapsuckers orphaning their eggs. 
   - Refactored some code involving beehives so eggs could inherit some of their behaviour
 - Removed incompatibility with PathFindingLib/PathFindingLagFix because the mods *might* now be compatible. 
## TODO: 
 - Make sure sapsuckers work (takeoff/relanding, reloading the save, w/ multiple sapsuckers, w/ some eggs gone)
 - Make sure new manor tiles are spawning with y-axis vertical
 - (probably 0.8.1) Add a way for players to manually specify names of tiles whose z-axis is the vertical in the config so people don't necessarily have to wait for me

# 0.7.3
 - Fixed a `NullReferenceException` with `BlacklistedInteriors` bricking the mod

# 0.7.2
 - Added a "BlacklistedInteriors" config option. This is a comma separated list of `DungeonFlow` names 

# v0.7.1

## Bugfixes
 - Disabled `debugForceFlow`

# v0.7.0

## Bugfixes
 - Major changes generation to be compatible with [GenericInteriors](https://thunderstore.io/c/lethal-company/p/Generic_GMD/Generic_Interiors/)
   - #42 Changed how tiles derive their bounds to be more generic (hehe)
   - Changed how doors have their rotation corrected
   - Added support for vertical doorways
   - Implemented usage of `TileConnectionRule` for `DTile`
 - Fixed a bug where nested tiles would break map generation

## Known Incompatibilities
### Major (Plugin will not load to avoid serious issues)
 - PathFindingLib/PathFindingLagFix
   - Causes crashes
### Minor (Plugin will still load, but you may encounter some bugs)
 - NeedyCats
   - Cat positions in interiors desync on save load

Other custom interiors may or may not be broken, I haven't checked yet. 