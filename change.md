# v0.6.0

## THIS VERSION BREAKS SAVES!

## Bugfixes
 - [#40](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/40) Fixed severe lag after visiting several moons<sup>\[Technical.1]</sup>
 - [#19](https://github.com/nikemitosis/LC-LabyrinthianFacilities/issues/19) Fixed an old bug that caused cruiser to disappear to narnia between days
 - Fixed ForbiddenPassages disabling doorways instead of blockers
 - Config is now reloaded on disconnect so when you host your own game after playing as a client on someone else's game, you don't keep using that other person's config. 

## Tweaks
 - Saving cruisers no longer requires saving grabbable map objects

## Technical
 1. Rewrote moon changing to destroy inactive moons instead of deactivating them
    - Inactive moons were causing significant lag spikes after visiting more than a couple different moons. Presumably due to EnemyAI's attempting to access inactive AI nodes? 
	- Moons are instantiated when you land on them after being on a different moon
    - May cause lag when first landing on a moon after being on another moon (including company moon)
    - Moons are no longer synced when joining a lobby, so joining lobbies should be faster
 - Removed some unused methods
 - Removed null byte from the beginning of `SerializationContext.Output`
   - References within the output byte stream still act as though this null byte exists, so references in the output appear to be off-by-one. This is accounted for by `DeserializationContext`. 
   - The only thing this changes for a user is that when deserializing a slice of a bytestream no longer requires you to add your own leading byte. 
 - Added more helpful error messages in `DeserializationContext`