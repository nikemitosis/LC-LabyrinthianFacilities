# v0.5.0

### This update breaks saves!
This update adds more information to various equipment, as well as cruisers, making them bigger than previous versions would expect. 
It also adds space for Hazards to DGameMap, making it two bytes bigger than previous versions expect. 

## New Features
 - #15 Items now have metadata saved about them (battery level, shotgun safety, number of shells, etc.)
 

## Technical
 - #36 Added space for Hazard saving in `DGameMap` serialization
 - Standardized serializing MapObjects via a new helper `MapObjectCollection`
   - This was to aid with the added complexity of the new `MapObject` types `BatteryEquipment`, `GunEquipment`, and `FueledEquipment`
   - May retire `MapObject` in the future to just reference `GrabbableObject` and its `itemProperties` flags to determine the "type" of map object?