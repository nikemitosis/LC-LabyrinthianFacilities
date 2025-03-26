# DISCLAIMER - EXPECT GAME-BREAKING BUGS!

This is a fairly large mod, created by a single developer. It is very possible that you may encounter game-breaking bugs. If you run into a bug that's not [listed on github](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues?q=is%3Aissue%20state%3Aopen%20label%3Abug%20OR%20label%3A"mod%20incompatibility"%20), feel free to [create an issue](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/new?template=Blank+issue). If you run into a bug and create an issue, it often helps if you can include your save history, found in
`<LC's save directory>/LabyrinthianFacilities/<savename>History.log`. 

It is also worth noting that the format of savedata for this mod is not set in stone; that is to say that updates may break savedata relevant to this mod (but other savedata like furniture, scrap in ship, quota, etc. should be safe). Patches will not break saves (e.g. v0.2.0 to v0.2.1), but minor updates (e.g. v0.2.0 to v0.3.0) might, especially in v0.x.x. 

*All* players must have this mod in order for it to work. As of writing, not all players having the mod can result from anything from the entire interior desyncing to softlocks, so be sure everyone has the mod installed. 

# Labyrinthian Facilities
The purpose of this mod is to provide a sense of continuity within each moon. In Vanilla LC, moons are regenerated every day from scratch, and all objects are deleted. This mod attempts to remove this discontinuity by only partially regenerating the interior, and preserving scrap and other items when the ship returns to orbit. The namesake of the mod is that interiors should act like labyrinths, closing up paths that used to be accessible, and opening new ones that didn't exist before, rather than just poofing the whole thing into existance. 

# Mod Compatibility
It is generally recommended that you keep other mods to a minimum, particularly those that interact with level generation. Many mods, especially simple ones like Shiploot should be fine, but if you find any mods that break with this one, feel free to [create an issue](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/new?template=Blank+issue). 

# Installation/Uninstallation

## Manual Installation
1. [Install BepInEx](https://docs.bepinex.dev/articles/user_guide/installation/index.html) if it is not already installed
2. Download mod (`LabyrinthianFacilities.dll`, found in each [release](https://github.com/nikemitosis/LC-LabyrinthianFacilities/releases)\)
3. Place mod in BepInEx/plugins

## Uninstallation

### Mod Uninstallation (This step for manual installation only)
1. Delete `LabyrinthianFacilities.dll` in `Lethal Company/BepInEx/plugins`

### Total Uninstallation
Only follow these steps if you do not plan on using this mod again!
1. Navigate to Lethal Company's persistent storage. On windows, this is generally`AppData/LocalLow/ZeekersRBLX/Lethal Company`.
   - If that folder doesn't exist for you, see the [Unity documenation](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-persistentDataPath.html) for other possible locations. 
2. Delete the folder within called `LabyrinthianFacilities`. 
3. Delete `mitzapper2.LethalCompany.LabyrinthianFacilities.cfg` in `Lethal Company/BepInEx/config`