# v0.2.2

### New Features
 - [#3](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/3) Added loops
   - The chance for two overlapping doorways to connect/disconnect is half the chance specified by DunGen. This is to reduce the chaos involved with toggling half the doorways every day. 
   - Eventually I'd like to move to a system where even the original path along a set of tiles can be modified so you wouldn't necessarily be able to rely on the same path every day. 
   I.e. no doorway connection would be considered "special" the way they are now; the extra connections for loops are explicitly added separately from the connections just to place two tiles together. Changes like this won't be for a while, though. 
   
 - [#7](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/7)
   Added preservation of bees in between days. 
   - Bees are preserved unless the beehive is in the ship room at the end of the day.
   - Roaming bees will stay roaming if their beehive is left behind.
     - If a beehive is left in a facility and gets destroyed, there will still be roaming bees for that day!
   - Bees are not yet saved to file, so beehives all spawn with bees by default unless they are in the ship room. 
     - If bees are roaming, they will be at their nest upon save-load. 
     - This doesn't matter for most gameplay, but if you collect a beehive one day, leave it behind somewhere later on, then close and open the save, the beehive will magically respawn its bees. 
	 - If you leave a hive in the facility and save-load, ***you will have bees in the facility.*** Do with this information what you will. 

### Bugfixes
 - Fixed a bug that would cause GameMap to not remove items from `leavesByPos`. This was effectively a memory leak until the server shut down, but it also caused ghost connections to attempt to be created when creating loops. 

### Code Changes
 - Doorway
   - Added an event `OnConnectEvent` for Doorway
   - Changed the event name `OnDisconnect` to `OnDisconnectEvent`
   - Changed DDoorway's static `DisconnectAction` to a nonstatic method `OnDisconnect`
   - Simplified the process by which we disable blockers that should be inactive
     - When handling door props, we check each door to see if it is in use. If it is, we disable all its blockers. Yes this is slow. 
 - Added a class `ConnectionAction` to represent any action specifically involving a connection between two tiles
   - `ConnectAction` now inherits from this
   - A new class `DisconnectAction` represents the inverse of a ConnectAction
 - Moved the invocation of `MapHandler.PreserveMapObjects` to be at a prefix of `RoundManager.UnloadSceneObjectsEarly` instead of `RoundManager.DespawnPropsAtEndOfRound` to allow the preservation of enemies (in particular, the bees of beehives)
 - Added a class `Beehive` to represent beehives distinctly from normal `Scrap`. This is for beehives to store information about their bees. 