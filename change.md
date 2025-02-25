# v0.4.3


## Bugfixes
 - #34 Fixed save history being saved for clients the same way as it would for server's 
   - It is now located in `serverHistory.log`
   - This file is wiped whenever you connect to a server, including rehosts
 - #31 Fixed save history being wiped on game-over
 - Fixed save history not being renamed with other saves
 - #32 Fixed mineshaft tunnel tiles having too thick of walls, causing breakerboxes to be uninteractable
 - Fixed clients initializing their assets while some of the assets were already spawned by the server
   - While this wasn't unintended per-se, it was causing an issue with some network-related objects not being initialized properly on those already-spawned assets. 
 - #33 Fixed a bug where bees spawned by the mod would pathfind to (0,0,0) if they tried to chase a non-host client
   - This was because `RedLocustBees.hasSpawnedHive` was only initialized server-side. 

## Technical
 - Reintroduced a developer-side setseed through preprocessor flag `SeedOverride`
   - This differs from the normal SetSeed configuration option, because it allows me to use a list of seeds to cycle through throughout a play session. 
   - I didn't update the SetSeed config because BepInEx config does not support arrays, so I cannot implement this as something that would be available to users. I would have to create my own config library to support it. Maybe another day. 