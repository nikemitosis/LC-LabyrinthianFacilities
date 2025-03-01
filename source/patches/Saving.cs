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

public class FuelAccess {
	private static Traverse<float> field(TetraChemicalItem i) => new Traverse(i).Field<float>("fuel");
	public static float Get(TetraChemicalItem item) => field(item).Value;
	public static void Set(TetraChemicalItem item, float value) => field(item).Value = value;
	
	private static Traverse<float> field(SprayPaintItem i) => new Traverse(i).Field<float>("sprayCanTank");
	public static float Get(SprayPaintItem item) => field(item).Value;
	public static void Set(SprayPaintItem item, float value) => field(item).Value = value;
	
	public static float Get(GrabbableObject item) {
		if (item is TetraChemicalItem tc) return Get(tc);
		if (item is SprayPaintItem sp) return Get(sp);
		throw new InvalidCastException($"Supposed FueledEquipment is neither TZP nor Spraypaint/Weedkiller");
	}
	public static void Set(GrabbableObject item, float value) {
		if (item is TetraChemicalItem tc) {
			Set(tc,value);
			return;
		}
		if (item is SprayPaintItem sp) {
			Set(sp,value);
			return;
		}
		throw new InvalidCastException($"Supposed FueledEquipment is neither TZP nor Spraypaint/Weedkiller");
	}
	
	private static Traverse<int> field(VehicleController i) => new Traverse(i).Field<int>("turboBoosts");
	public static int Get(VehicleController i) => field(i).Value;
	public static void Set(VehicleController i, int value) => field(i).Value = value;
}