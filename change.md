# v0.3.2

### Bugfixes
 - [#18](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/19) Fixed an issue where two moons could be active at the same time after landing on company
 - [#19](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/19) Potentially fixed cruiser not respawning correctly
 - [#21](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/21) Fixed MapHandler not properly resetting on game-over
 - [#22](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/22) Fixed bees not being recognized as collected
 - [#17](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/17) Improved generation times
   - Achieved a ~4x speedup by allowing `PropAction` and other `GenerationAction`s to opt-out of the frame between actions given by default. 
     - Tile placement is now the biggest contributor to the time for loading, now accounting for ~80% of the time spent, as opposed to the ~30% it was before
	   - It's worth noting that all of these statistics are based off of a sample size of one titan-factory generation (with a set seed). 
 - [#20](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/20) Fixed connectors sometimes not being disabled when they should be. 

### Code Changes
 - Mostly reverted the change that moved `MapHandler.PreserveMapObjects` from `RoundManager.DespawnPropsAtEndOfRound` to `RoundManager.UnloadSceneObjectsEarly`
   - Rewrote beehive saving to occur in two steps; 1 for handling the bees, 2 for handling the hive
   - This fixed both #19 and #22
 - Added a `PreserveBees` method to `MapHandler` and `Moon`
 - Added reporting of time in each step of generation with `VERBOSE_GENERATION` flag on
 - Gave the option for inheritors of `GenerationAction` to opt-out of the frame between actions given by default. This was particularly helpful for `PropAction`
   - The name of the property to override is `YieldFrame`. Defaults to `true`, to enable the frame between actions. 
 - Removed `abstract` from the definition of `GenerationAction` to allow it to be used as a dummy action (just for the frame)