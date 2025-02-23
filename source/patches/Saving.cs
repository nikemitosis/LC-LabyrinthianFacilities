namespace LabyrinthianFacilities.Patches;

using System;

using UnityEngine;

using HarmonyLib;

using Object=UnityEngine.Object;

[HarmonyPatch(typeof(GameNetworkManager))]
class SaveMapsPatch {
	[HarmonyPatch("SaveGame")]
	[HarmonyPrefix]
	public static void SaveMaps() {
		try {
			if (!StartOfRound.Instance.inShipPhase || StartOfRound.Instance.isChallengeFile) return;
			MapHandler.Instance.SaveGame();
		} catch (Exception e) {
			Plugin.LogError($"{e}");
			throw;
		}
	}
}

[HarmonyPatch(typeof(StartOfRound))]
class LoseSavePatch {
	
	[HarmonyPatch("ResetShip")]
	[HarmonyPostfix]
	public static void LoseSave() {
		SaveManager.DeleteFile($"{SaveManager.CurrentSave}.dat");
	}
}

[HarmonyPatch(typeof(ES3))]
class ModifyFilePatch {
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
			Plugin.LogError($"{e}");
			throw;
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
			Plugin.LogError($"{e}");
			throw;
		}
	}
}

[HarmonyPatch(typeof(VehicleController))]
public class CruiserLoadPatch {
	private static bool OldState;
	[HarmonyPatch("Start")]
	[HarmonyPrefix]
	public static void DontMagnetMoonCruisers(VehicleController __instance) {
		if (__instance.GetComponent<DummyFlag>() == null) return;
		
		OldState = StartOfRound.Instance.inShipPhase;
		StartOfRound.Instance.inShipPhase = false;
	}
	
	[HarmonyPatch("Start")]
	[HarmonyPostfix]
	public static void RestoreInShipPhase(VehicleController __instance) {
		DummyFlag flag = __instance.GetComponent<DummyFlag>();
		if (flag == null) return;
		
		Object.Destroy(flag);
		StartOfRound.Instance.inShipPhase = OldState;
	}
}