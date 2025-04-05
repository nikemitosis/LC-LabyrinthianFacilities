# v0.7.0

## Bugfixes
 - #42 
 - Major changes generation to be compatible with [GenericInteriors](https://thunderstore.io/c/lethal-company/p/Generic_GMD/Generic_Interiors/)
   - Changed how tiles derive their bounds to be more generic (hehe)
   - Changed how doors have their rotation tweaked
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