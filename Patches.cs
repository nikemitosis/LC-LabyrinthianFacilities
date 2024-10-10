namespace LabyrinthianFacilities.Patches;

using System.Collections;

using HarmonyLib;
using DunGen;
using UnityEngine;
using UnityEngine.SceneManagement;

// [HarmonyPatch(typeof(PreInitSceneScript))]
// class PreInitSceneScriptPatch {
	// [HarmonyPatch("loadSceneDelayed")]
	// [HarmonyPostfix]
	// public static void TriggerPrefabRetrieval() {
		// if (Plugin.local_fatal_error) return;
		// Plugin.Logger.LogInfo("Retrieving Prefabs! (PreInit Postfix patch)");
		// Plugin.RetrieveTilePrefabs();
	// }
// }

[HarmonyPatch(typeof(RoundManager))]
class RoundManagerPatch {
	[HarmonyPatch("GenerateNewFloor")]
	[HarmonyPrefix]
	public static bool UseSavedDungeon() {
		if (Plugin.local_fatal_error) return true;
		
		if (Plugin.GetSavedDungeon() != null) {
			Plugin.Logger.LogInfo("Loading map! What could go wrong?");
			LabyrinthGenerator.Load();
			DungeonGeneratorPatch.enable = true;
			return false;
		} else {
			Plugin.Logger.LogInfo("No savedDungeon; proceeding with vanilla generation");
			return true;
		}
	}
}

// For eventually overridding the DungeonGenerator
[HarmonyPatch(typeof(DungeonGenerator))]
class DungeonGeneratorPatch {
	
	internal static bool enable = false;
	
	[HarmonyPatch("Status",MethodType.Getter)]
	[HarmonyPostfix]
	public static void StatusAlwaysComplete(ref GenerationStatus __result) {
		if (enable) __result = GenerationStatus.Complete;
	}
	
	[HarmonyPatch("GenerateBranchPaths")]
	[HarmonyPrefix]
	public static bool DisableBranches(ref IEnumerator __result) {
		__result = "".GetEnumerator();
		return false;
	}
}

[HarmonyPatch(typeof(StartOfRound))]
class StartOfRoundPatch {
	
	[HarmonyPatch("ShipLeave")]
	[HarmonyPrefix]
	public static void SaveMatchUponLeaving() {
		if (Plugin.local_fatal_error) return;
		
		Plugin.Logger.LogInfo("Saving map!");
		LabyrinthGenerator.Save();
	}
}