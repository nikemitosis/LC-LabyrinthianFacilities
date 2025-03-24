# DISCLAIMER

## ACTIVE DEVELOPMENT! EXPECT GAME-BREAKING BUGS!

It is possible, if not probable, that you may encounter game-breaking bugs during development should you choose to play with this mod. It is also worth noting that the format of savedata for this mod is not at all set in stone; that is to say that updates may break savedata relevant to this mod (but other savedata like furniture, scrap in ship, quota, etc. should be safe). Patches will not break saves (e.g. v0.2.0 to v0.2.1), but minor updates (e.g. v0.2.0 to v0.3.0) might, especially in v0.x.x. 

## Mod Compatibility
It is generally recommended that you keep other mods to a minimum, especially those that interact with level generation. Many mods, especially simple ones like Shiploot should be fine, but if you find any mods that break with this one, feel free to [create an issue](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/new?template=Blank+issue). 

# Object
The purpose of this mod is to provide a sense of continuity within each moon. In Vanilla LC, moons are regenerated every day from scratch, and all objects are deleted. This mod attempts to remove this discontinuity by only partially regenerating the interior, and preserving scrap and other items when the ship returns to orbit. The namesake of the mod is that interiors should act like labyrinths, closing up paths that used to be accessible, and opening new ones that didn't exist before, rather than just poofing the whole thing into existance. 

# Installation/Uninstallation

## Manual Installation
1. [Install BepInEx](https://docs.bepinex.dev/articles/user_guide/installation/index.html) if it is not already installed
2. Download mod (`LabyrinthianFacilities.dll`, found in each [release](https://github.com/nikemitosis/LC-LabyrinthianFacilities/releases)
3. Place mod in BepInEx/plugins

## Uninstallation

### Mod Uninstallation (Manual installation only)
1. Delete `LabyrinthianFacilities.dll` in `Lethal Company/BepInEx/plugins`

### Total Uninstallation
Only follow these steps if you do not plan on using this mod again!
1. Navigate to Lethal Company's persistent storage. On windows, this is generally`AppData/LocalLow/ZeekersRBLX/Lethal Company`.
   - If that folder doesn't exist for you, see the [Unity documenation](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-persistentDataPath.html) for other possible locations. 
2. Delete the folder within called `LabyrinthianFacilities`. 
3. Delete `mitzapper2.LethalCompany.LabyrinthianFacilities.cfg` in `Lethal Company/BepInEx/config`


## More to come!