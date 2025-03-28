# v0.6.3

## Bugfixes
 - #41 Fixed an issue with set-seed runs not acting consistent if you played two runs back-to-back or with a game restart between days of a run. 

## Tweaks
 - Changed config setting `Debug.History` to be enabled by default

## Technical
 - Added `Parent`, `Tile`, and `DDoorway` properties to `Prop` to access a prop's tile/doorway
   - `Parent` is the general version that just returns `Tile` or `Doorway` depending on whether it's a door prop
 - Changed `DTile` and `DDoorway` to use `List`s to store props instead of arrays (in case I want to remove props later)
   - Added `RemoveProp` methods to `DTile` and `DDoorway`
 - Added a constructor for `WeightedList` to set an initial capacity