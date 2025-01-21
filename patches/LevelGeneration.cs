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
	public int SetSeed = 0;
	#endif
	
	[HarmonyPatch("Generate")]
	[HarmonyPrefix]
	public static bool CustomGenerate(DungeonGenerator __instance) {
		
		Plugin.LogInfo($"Custom Generate! (Seed={__instance.Seed})");
		var flow = new DungeonFlowConverter(__instance.DungeonFlow);
		
		MapHandler.Instance.StartCoroutine(MapHandler.Instance.Generate(
			StartOfRound.Instance.currentLevel,
			flow,
			#if SETSEED
			SetSeed,
			#else
			__instance.Seed,
			#endif
			(GameMap map) => GenerateLevel.ChangeStatus(__instance,GenerationStatus.Complete)
		));
		
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
		MapHandler.Instance.PreserveMapObjects();
		if (StartOfRound.Instance.allPlayersDead) {
			MapHandler.Instance.DestroyAllScrap();
		}
	}
}

[HarmonyPatch(typeof(StormyWeather))]
class FixLightningStrikingInactiveScrapPatch {
	[HarmonyPatch("GetMetalObjectsAfterDelay")]
	[HarmonyPrefix]
	public static void ResetMetalScrapBuffer(ref List<GrabbableObject> ___metalObjects) {
		___metalObjects.Clear();
	}
}

[HarmonyPatch(typeof(GameNetworkManager))]
class SaveMapsPatch {
	[HarmonyPatch("SaveGame")]
	[HarmonyPrefix]
	public static void SaveMaps() {
		MapHandler.Instance.SaveGame();
	}
}

[HarmonyPatch(typeof(StartOfRound))]
class SendMapsToClientPatch {
	[HarmonyPatch("OnClientConnect")]
	[HarmonyPrefix]
	public static void SendMaps(ulong clientId) {
		MapHandler.Instance.SendMapDataToClient(clientId);
	}
}

[HarmonyPatch(typeof(ES3))]
class DeleteFilePatch {
	[HarmonyPatch("DeleteFile", new Type[]{typeof(ES3Settings)})]
	[HarmonyPrefix]
	public static void DeleteSaveFile(ES3Settings settings) {
		if (
			settings.location == ES3.Location.File && 
			settings.FullPath.StartsWith(Application.persistentDataPath)
		) {
			SaveManager.DeleteFile(
				SaveManager.GetSaveNameFromPath(settings.FullPath)
			);
		}
	}
	
	// LCBetterSaves Compatibility
	[HarmonyPatch("RenameFile", new Type[]{typeof(string), typeof(string)})]
	[HarmonyPrefix]
	public static void RenameSaveFile(string oldFilePath,string newFilePath) {
		if (oldFilePath.StartsWith("Temp") || newFilePath.StartsWith("Temp")) return;
		
		SaveManager.RenameFile(
			SaveManager.GetSaveNameFromPath(oldFilePath),
			SaveManager.GetSaveNameFromPath(newFilePath)
		);
	}
}