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
	public static int SetSeed = 730112026;
	#endif
	
	[HarmonyPatch("Generate")]
	[HarmonyPrefix]
	public static bool CustomGenerate(DungeonGenerator __instance) {
		try {
			Plugin.LogInfo($"Custom Generate! (Seed={__instance.Seed})");
			var flow = new DungeonFlowConverter(
				__instance.DungeonFlow,
				#if SETSEED
				SetSeed
				#else
				__instance.Seed
				#endif
			);
			
			MapHandler.Instance.StartCoroutine(MapHandler.Instance.Generate(
				StartOfRound.Instance.currentLevel,
				flow,
				(GameMap map) => GenerateLevel.ChangeStatus(__instance,GenerationStatus.Complete)
			));
		} catch (Exception e) {
			Plugin.LogError(e.Message);
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
	
	[HarmonyPatch("DespawnPropsAtEndOfRound")]
	[HarmonyPrefix]
	public static void PreserveScrap() {
		try {
			MapHandler.Instance.PreserveMapObjects();
			if (StartOfRound.Instance.allPlayersDead) {
				MapHandler.Instance.DestroyAllScrap();
			}
		} catch (Exception e) {
			Plugin.LogError(e.Message);
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
			Plugin.LogError(e.Message);
			throw;
		}
	}
}

[HarmonyPatch(typeof(GameNetworkManager))]
class SaveMapsPatch {
	[HarmonyPatch("SaveGame")]
	[HarmonyPrefix]
	public static void SaveMaps() {
		try {
			if (!StartOfRound.Instance.inShipPhase || StartOfRound.Instance.isChallengeFile) return;
			MapHandler.Instance.SaveGame();
		} catch (Exception e) {
			Plugin.LogError(e.Message);
			throw;
		}
	}
}

[HarmonyPatch(typeof(StartOfRound))]
class SendMapsToClientPatch {
	[HarmonyPatch("OnClientConnect")]
	[HarmonyPrefix]
	public static void SendMaps(ulong clientId) {
		try {
			MapHandler.Instance.SendMapDataToClient(clientId);
		} catch (Exception e) {
			Plugin.LogError(e.Message);
			throw;
		}
	}
}

[HarmonyPatch(typeof(StartOfRound))]
class StartOfRoundPatch {
	[HarmonyPatch("OpenShipDoors")]
	[HarmonyPrefix]
	public static void ExcludeCompanyLevel() {
		if (StartOfRound.Instance.currentLevel.name == "CompanyBuildingLevel") {
			MapHandler.Instance.ClearActiveMap();
		}
	}
	
	[HarmonyPatch("ResetShip")]
	[HarmonyPostfix]
	public static void ResetMapHandler() {
		SaveManager.DeleteFile($"{SaveManager.CurrentSave}.dat");
		MapHandler.Instance.Clear();
	}
}

[HarmonyPatch(typeof(ES3))]
class DeleteFilePatch {
	[HarmonyPatch("DeleteFile", new Type[]{typeof(ES3Settings)})]
	[HarmonyPrefix]
	public static void DeleteSaveFile(ES3Settings settings) {
		try {
			if (
				settings.location == ES3.Location.File && 
				settings.FullPath.StartsWith(Application.persistentDataPath)
			) {
				SaveManager.DeleteFile(
					SaveManager.GetSaveNameFromPath(settings.FullPath)
				);
			}
		} catch (Exception e) {
			Plugin.LogError(e.Message);
			throw e;
		}
	}
	
	// LCBetterSaves Compatibility
	[HarmonyPatch("RenameFile", new Type[]{typeof(string), typeof(string)})]
	[HarmonyPrefix]
	public static void RenameSaveFile(string oldFilePath,string newFilePath) {
		try {
			if (oldFilePath.StartsWith("Temp") || newFilePath.StartsWith("Temp")) return;
			
			SaveManager.RenameFile(
				SaveManager.GetSaveNameFromPath(oldFilePath),
				SaveManager.GetSaveNameFromPath(newFilePath)
			);
		} catch (Exception e) {
			Plugin.LogError(e.Message);
			throw e;
		}
	}
}