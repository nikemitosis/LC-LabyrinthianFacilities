# v0.5.1

## New Features
 - #6 Added Hazard Preservation & Saving, plus the config options to control them
 - #30 Implemented the "Forbidden passages" config option

## Bugfixes
 - Fixed clients not recieving extra information about (Grabbable)MapObjects because they were using the wrong serializer(s)

## Technical
 - Renamed MapObject to GrabbableMapObject
   - MapObject now refers refers to both GrabbableMapObject or Hazard; it is any object that is saved to a map. 