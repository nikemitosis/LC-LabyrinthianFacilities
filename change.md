# v0.5.0

### This update breaks saves!
This update adds more information to various equipment, as well as cruisers, making them bigger than previous versions would expect. 
It also adds space for Hazards to DGameMap, making it two bytes bigger than previous versions expect. 

## New Features
 - #15 Items now have metadata saved about them (battery level, shotgun safety, number of shells, etc.)
 - #26 Items now have their y-rotation saved, and their x-rotation and z-rotation is initialized to a more appropriate default value (the resting rotation defined by vanilla LC)
 - #35 Cruiser now has its important attributes preserved/saved (hp, boosts, car is running, back door state)

## Bug Fixes
 - Removed spurious error messages about syncing save history files. 
 - Fixed nutcracker shotguns being preserved if you didn't kill the nutcracker. 

## Technical
 - #36 Added space for Hazard saving in `DGameMap` serialization
 - Standardized serializing MapObjects via a new helper `MapObjectCollection`
   - This was to aid with the added complexity of the new `MapObject` types `BatteryEquipment`, `GunEquipment`, and `FueledEquipment`
   - May retire `MapObject` in the future to just reference `GrabbableObject` and its `itemProperties` flags to determine the "type" of map object?