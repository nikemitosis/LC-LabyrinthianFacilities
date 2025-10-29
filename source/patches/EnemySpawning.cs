namespace LabyrinthianFacilities.Patches;

using HarmonyLib;

using UnityEngine;

[HarmonyPatch(typeof(RedLocustBees))]
public class RespawnBeesPatch {
	/* SpawnHiveNearEnemy does the following:
	 * 1. Spawns a hive (skipping)
	 * 2. Sets `hive`
	 * 3. intializes hive's grabbable object stuff (skipping since we already did that)
	 * 4. sets `lastKnownHivePosition`
	 * 5. Adds to RoundManager.totalScrapValueInLevel (skipping since it should be included already)
	 * 6. Sets `hasSpawnedHive`
	 * 
	 * 1, 3, 5(?) already done in previous days
	 * 2, 4 done in Beehive.SpawnBees
	 * 6 done in UpdateClientsOnBees (patch in this class)
	*/
	[HarmonyPatch("SpawnHiveNearEnemy")]
	[HarmonyPrefix]
	public static bool DontSpawnNewHive(RedLocustBees __instance) {
        return DummyFlag.Detect(__instance);
	}
	
	[HarmonyPatch("Start")]
	[HarmonyPostfix]
	public static void SetHasSpawnedHive(RedLocustBees __instance, ref bool ___hasSpawnedHive) {
		var flag = __instance.GetComponent<DummyFlag>();
		if (flag == null) return;
		MonoBehaviour.Destroy(flag);
		
		___hasSpawnedHive = true;
	}
}

[HarmonyPatch(typeof(GiantKiwiAI))]
public class GiantKiwiSpawn {
    [HarmonyPatch("SpawnNestEggs")]
    [HarmonyPrefix]
    public static bool DontSpawnNewEggs(GiantKiwiAI __instance) {
        return !DummyFlag.Detect(__instance);
    }
    
    [HarmonyPatch("SpawnBirdNest")]
    [HarmonyPrefix]
    public static bool DontSpawnNewNest(GiantKiwiAI __instance) {
        return !DummyFlag.Detect(__instance);
    }
    
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void PrivateInit(
        GiantKiwiAI __instance, 
        ref bool ___hasSpawnedEggs, 
        ref AudioSource ___birdNestAmbience
    ) {
        if (!DummyFlag.Destroy(__instance)) return;
        
        ___hasSpawnedEggs = true;
        ___birdNestAmbience = __instance.birdNest.GetComponent<AudioSource>();
    }
}