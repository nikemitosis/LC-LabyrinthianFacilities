# v0.4.1

## New Features
 - [#30](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/30) Added a config! 
   (Currently Implemented)
   - Global Enable/Disable
   - Save MapObjects
     - Equipment
	 - Scrap
	 - Cruisers
	 - Beehives
   - Verbose Logging
	 - Enable Verbose Deserialization
	 - Enable Verbose Serialization
	 - Enable Verbose Generation
   - Use Set Seed
     - Seed
	 - Increment seed daily
	   - Sidenote: Good for players who want a "set seed run" rather than just setting a seed for testing
	   - Set seed is currently very limited; it does not affect things like which interior is generated. It *only* affects tile and prop placement. 

## Bugfixes
 - Fixed a bug where the root tile would have its bounds ignored. This only affected the mineshaft.

## Technical
 - Added a shortcut to `BoundsMap.Remove` so now the search for the given item is terminated if its bounds don't overlap with the bounds of the `BoundsMap`