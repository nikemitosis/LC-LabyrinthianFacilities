namespace LabyrinthianFacilities.Patches;
using DgConversion;

using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using DunGen;

[HarmonyPatch(typeof(DungeonGenerator))]
class GenerateLevel {
	
	#if SETSEED
	public static int SetSeed = 0;
	#endif
	
	[HarmonyPatch("Generate")]
	[HarmonyPrefix]
	public static bool CustomGenerate(DungeonGenerator __instance) {
		try {
			#if SETSEED
			int seed = SetSeed;
			#else
			int seed = __instance.Seed;
			#endif
			
			Plugin.LogInfo($"Custom Generate! (Seed={seed})");
			var flow = new DungeonFlowConverter(
				__instance.DungeonFlow,
				seed
			);
			
			MapHandler.Instance.StartCoroutine(MapHandler.Instance.Generate(
				StartOfRound.Instance.currentLevel,
				flow,
				(GameMap map) => GenerateLevel.ChangeStatus(__instance,GenerationStatus.Complete)
			));
		} catch (Exception e) {
			Plugin.LogError($"{e}");
			throw;
		}
		
		return false;
	}
	
	[HarmonyReversePatch]
	[HarmonyPatch("ChangeStatus")]
	public static void ChangeStatus(object instance, GenerationStatus status) {
		throw new NotImplementedException("Reverse patch stub");
	}
}

[HarmonyPatch(typeof(RoundManager))]
class PreserveScrapPatch {
	
	[HarmonyPatch("UnloadSceneObjectsEarly")]
	[HarmonyPrefix]
	public static void PreserveScrap() {
		try {
			MapHandler.Instance.PreserveMapObjects();
			if (StartOfRound.Instance.allPlayersDead) {
				MapHandler.Instance.DestroyAllScrap();
			}
		} catch (Exception e) {
			Plugin.LogError($"{e}");
			throw;
		}
	}
}

[HarmonyPatch(typeof(StormyWeather))]
class FixLightningStrikingInactiveScrapPatch {
	[HarmonyPatch("GetMetalObjectsAfterDelay")]
	[HarmonyPrefix]
	public static void ResetMetalScrapBuffer(ref List<GrabbableObject> ___metalObjects) {
		try {
			___metalObjects.Clear();
		} catch (Exception e) {
			Plugin.LogError($"{e}");
			throw;
		}
	}
}

[HarmonyPatch(typeof(StartOfRound))]
class StartOfRoundPatch {
	[HarmonyPatch("OpenShipDoors")]
	[HarmonyPrefix]
	public static void ExcludeCompanyLevel() {
		try {
			if (StartOfRound.Instance.currentLevel.name == "CompanyBuildingLevel") {
				MapHandler.Instance.ClearActiveMoon();
			}
		} catch (Exception e) {
			Plugin.LogError($"{e}");
			throw;
		}
	}
	
	[HarmonyPatch("ResetShip")]
	[HarmonyPostfix]
	public static void ResetMapHandler() {
		MapHandler.Instance.Clear();
	}
}

[HarmonyPatch(typeof(RedLocustBees))]
public class RespawnBeesPatch {
	/* SpawnHiveNearEnemy does the following:
	 * 1. Spawns a hive (skipping)
	 * 2. Sets `hive`
	 * 3. intializes hive's grabbable object stuff (skipping since we already did that)
	 * 4. sets `lastKnownHivePosition`
	 * 5. Adds to RoundManager.totalScrapValueInLevel (skipping since it should be included already)
	 * 6. Sets `hasSpawnedHive`
	*/
	[HarmonyPatch("SpawnHiveNearEnemy")]
	[HarmonyPrefix]
	public static bool DontSpawnNewHive(RedLocustBees __instance, ref bool ___hasSpawnedHive) {
		try {
			var flag = __instance.GetComponent<Beehive.DummyFlag>();
			if (flag == null) return true;
			MonoBehaviour.Destroy(flag);
			
			// (hive and lastKnownHivePosition are both set by Beehive.SpawnBees)
			
			___hasSpawnedHive = true;
			
			return false;
		} catch (Exception e) {
			Plugin.LogError($"{e}");
			throw;
		}
	}
}