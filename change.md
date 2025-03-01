# v0.5.0

### This update breaks saves!
This update adds more information to various equipment, as well as cruisers, making them bigger than previous versions would expect. 
It also adds space for Hazards to DGameMap, making it two bytes bigger than previous versions expect. 

## New Features
 - #15 Items now have metadata saved about them (battery level, shotgun safety, number of shells, etc.)
 - #26 Items now have their y-rotation saved, and their x-rotation and z-rotation is initialized to a more appropriate default value (the resting rotation defined by vanilla LC)
 - #35 Cruiser now has its important attributes saved between days (hp, boosts, car is running, back door open)
   - !!Back door currently doesn't load from file properly. This is because the back door state is initialized while the cruiser is inactive. Will be fixed before this update is released. 

## Fixes
 - Removed spurious error messages about syncing save history files. 

## Technical
 - #36 Added space for Hazard saving in `DGameMap` serialization
 - Standardized serializing MapObjects via a new helper `MapObjectCollection`
   - This was to aid with the added complexity of the new `MapObject` types `BatteryEquipment`, `GunEquipment`, and `FueledEquipment`
   - May retire `MapObject` in the future to just reference `GrabbableObject` and its `itemProperties` flags to determine the "type" of map object?