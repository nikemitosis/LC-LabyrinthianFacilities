#define SETSEED

namespace LabyrinthianFacilities.Patches;
using DgConversion;

using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using DunGen;

using Object=UnityEngine.Object;
using Tile=LabyrinthianFacilities.Tile;
using Doorway=LabyrinthianFacilities.Doorway;

[HarmonyPatch(typeof(DungeonGenerator))]
class GenerateLevel {
	
	#if SETSEED
	public static int SetSeed = 150997470;
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

// Mostly blatantly stolen from decompiled LC code
[HarmonyPatch(typeof(RoundManager))]
public class AddCaveLights {
	[HarmonyPrefix]
	[HarmonyPatch("SpawnCaveDoorLights")]
	public static void SpawnCaveDoorLights(RoundManager __instance) {
		if (__instance.currentDungeonType != 4) {
			return;
		}
		
		Tile[] array = UnityEngine.Object.FindObjectsByType<Tile>(FindObjectsSortMode.None);
		for (int i = 0; i < array.Length; i++) {
			
			if (!array[i].GetComponent<DunGen.Tile>().Tags.HasTag(__instance.MineshaftTunnelTag)) continue;
			
			for (int j = 0; j < array[i].Doorways.Length; j++) {
				Doorway doorway = array[i].Doorways[j];
				if (doorway.IsVacant) continue;
				DunGen.Doorway dungenDoorway = doorway.GetComponent<DunGen.Doorway>();
				DunGen.Doorway connection = doorway.Connection.GetComponent<DunGen.Doorway>();
				if (!connection.Tags.HasTag(__instance.CaveDoorwayTag)) continue;
				
				
				var obj = UnityEngine.Object.Instantiate(
					__instance.caveEntranceProp, 
					doorway.transform, 
					worldPositionStays: false
				);
				((DGameMap)array[0].Map).CaveLights.Add(obj);
				
				Transform[] componentsInChildren = array[i].GetComponentsInChildren<Transform>();
				foreach (Transform transform in componentsInChildren) {
					if (transform.tag == "PoweredLight") {
						transform.gameObject.SetActive(false);
					}
				}
			}
		}
		
		return;
	}
}

[HarmonyPatch(typeof(RoundManager))]
class PreserveScrapPatch {
	
	[HarmonyPatch("UnloadSceneObjectsEarly")]
	[HarmonyPrefix]
	public static void PreserveEnemies() {
		try {
			MapHandler.Instance.PreserveBees();
		} catch (Exception e) {
			Plugin.LogError($"{e}");
			throw;
		}
	}
	
	[HarmonyPatch("DespawnPropsAtEndOfRound")]
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
			var flag = __instance.GetComponent<DummyFlag>();
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

[HarmonyPatch(typeof(GrabbableObject))]
public class DontFallOnLoad {
	[HarmonyPatch("Start")]
	[HarmonyPostfix]
	public static void CancelFall(GrabbableObject __instance) {
		DummyFlag dummy = __instance.GetComponent<DummyFlag>();
		if (dummy != null) {
			Object.Destroy(dummy);
			__instance.fallTime = 1f;
			__instance.hasHitGround = true;
			__instance.reachedFloorTarget = true;
			__instance.targetFloorPosition  = __instance.transform.localPosition;
			__instance.startFallingPosition = __instance.transform.localPosition;
		}
	}
}
