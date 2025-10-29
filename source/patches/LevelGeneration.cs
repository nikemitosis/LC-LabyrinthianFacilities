namespace LabyrinthianFacilities.Patches;

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;
using Unity.Netcode;

using DunGen;

using GameNetcodeStuff;

using Object=UnityEngine.Object;
using Tile=LabyrinthianFacilities.Tile;
using Doorway=LabyrinthianFacilities.Doorway;

[HarmonyPatch(typeof(DungeonGenerator))]
class GenerateLevel {
	
	#if SeedOverride
	private static int[] Seeds = [1071201032];
	private static int SeedIndex = 0;
	#endif
	public static bool debugForceFlow = false;
	public static string debugForceFlowTarget = "StorageComplex";
	
	[HarmonyPatch("Generate")]
	[HarmonyPrefix]
	public static bool CustomGenerate(DungeonGenerator __instance) {
		try {
			int seed;
			#if !SeedOverride
			if (Config.Singleton.UseSetSeed) {
				seed = Config.Singleton.Seed;
				if (Config.Singleton.IncrementSetSeed) {
					seed += (
						TimeOfDay.Instance.quotaVariables.deadlineDaysAmount
						* TimeOfDay.Instance.timesFulfilledQuota
					) + TimeOfDay.Instance.daysUntilDeadline;
				}
			} else {
				seed = __instance.Seed;
			}
			#else
			seed = Seeds[SeedIndex++];
			SeedIndex %= Seeds.Length;
			#endif
			
			// DEBUG
			DunGen.Graph.DungeonFlow flo = null;
			if (debugForceFlow) foreach (var i in RoundManager.Instance.dungeonFlowTypes) {
				if (i.dungeonFlow.name == debugForceFlowTarget) {
					flo = i.dungeonFlow;
				}
			} else {
                flo = __instance.DungeonFlow;
            }
            
			__instance.Seed = seed;
			if (
                !Config.Singleton.UseCustomGeneration 
                || flo == null
                || Config.Singleton.BlacklistedInteriors.Contains(flo.name)
            ) {
                if (flo == null) Plugin.LogError($"No DungeonFlow found/provided. Cannot use custom generation");
				MapHandler.Instance.ChangeActiveMoon(StartOfRound.Instance.currentLevel);
				return true;
			}
			
			Plugin.LogInfo($"Custom Generate! (Seed={seed})");
			var flow = new DungeonFlowConverter(
				debugForceFlow ? flo : __instance.DungeonFlow,
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
	public static void PreserveEarlyObjects() {
		try {
			MapHandler.Instance.PreserveEarlyObjects();
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
	
	#if SeedOverride
	private static int[] Seeds = [65348538];
	private static int Idx = 0;
	
	[HarmonyPatch("StartGame")]
	[HarmonyPrefix]
	public static void SetRandomMapSeed() {
		StartOfRound.Instance.overrideRandomSeed = true;
		StartOfRound.Instance.overrideSeedNumber = Seeds[Idx];
		Idx = (Idx+1)%Seeds.Length;
	}
	#endif
}

[HarmonyPatch(typeof(RoundManager))]
public class DontDestroyRandomMapObjects {
	// stop roundmanager from deleting spawn points for mapobjects (if we aren't saving hazards)
	[HarmonyPatch(nameof(RoundManager.SpawnMapObjects))]
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
		if (Config.Singleton.SaveHazards) {
			foreach (CodeInstruction instr in instructions) yield return instr;
			yield break;
		}
		
		// all this does is change the for loop condition at the very end to check for less than zero, instead 
		// of less than the array length
		int state = 0;
		Queue<CodeInstruction> buffer = new(3);
		foreach (CodeInstruction instr in instructions) {
			switch (state) {
				case 0:
					// load zero to store into local var
					if (instr.opcode == OpCodes.Ldc_I4_0) state = 1;
				break; case 1:
					// put value (0) into for loop iteration var
					int? operand = (instr.operand as LocalBuilder)?.LocalIndex ?? instr.operand as int?;
					state = (
						(instr.opcode == OpCodes.Stloc_S && operand == 17)
						? 2 : 0
					);
				break; case 2:
					// goto for loop body
					state = (
						// for some reason, harmony doesn't seem to like short branch instructions?
						(instr.opcode == OpCodes.Br_S || instr.opcode == OpCodes.Br) 
						? 3 : 0
					);
				break; case 3: // checkpoint - for loop initialization found
					// look for condition - load iteration variable
					operand = (instr.operand as LocalBuilder)?.LocalIndex ?? instr.operand as int?;
					if (
						instr.opcode == OpCodes.Ldloc_S 
						&& operand == 17
					) state = 4;
				break; case 4:
					// load array
					if (instr.opcode == OpCodes.Ldloc_1) { // need to replace this with ldc.i4.0
						buffer.Enqueue(instr);
						state = 5; 
					} else {
						while (buffer.Count != 0) yield return buffer.Dequeue();
						state = 3;
					}
				break; case 5:
					// load array.Length
					if (instr.opcode == OpCodes.Ldlen) { // need to replace this with nop
						buffer.Enqueue(instr);
						state = 6; 
					} else {
						while (buffer.Count != 0) yield return buffer.Dequeue();
						state = 3;
					}
				break; case 6:
					// convert to i32
					if (instr.opcode == OpCodes.Conv_I4) { // need to replace this with nop
						buffer.Enqueue(instr);
						state = 7;
					} else {
						while (buffer.Count != 0) yield return buffer.Dequeue();
						state = 3;
					}
				break; case 7:
					if (instr.opcode == OpCodes.Blt_S || instr.opcode == OpCodes.Blt) {
						state = 8;
						buffer.Clear();
						yield return new CodeInstruction(OpCodes.Ldc_I4_0); // was ldloc.1
						yield return new CodeInstruction(OpCodes.Nop     ); // was ldlen
						yield return new CodeInstruction(OpCodes.Nop     ); // was conv.i4
					} else {
						while (buffer.Count != 0) yield return buffer.Dequeue();
						state = 3;
					}
				break; case 8:
					// nop
				break; default:
					throw new InvalidOperationException("Illegal transpiler state");
				// break;
			}
			if (buffer.Count == 0) yield return instr;
		}
		if (state != 8) throw new Exception($"Transpiler failure - only made it to state {state}/8");
	}
}

[HarmonyPatch(typeof(HUDManager))]
public class EasterEggDetection {
	public static string cruiserMagicWord = "bouncy cruisers";
	
	[HarmonyPatch("AddTextToChatOnServer")]
	[HarmonyPostfix]
	public static void TriggerEasterEgg(string chatMessage) {
		if (Config.Singleton.BouncyCruisers && chatMessage == cruiserMagicWord) {
			MapHandler.Instance.StartCoroutine(MapHandler.Instance.EnableBouncyCruisers());
		}
	}
}