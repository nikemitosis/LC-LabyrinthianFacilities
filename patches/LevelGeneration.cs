namespace LabyrinthianFacilities.Patches;

using System;

using HarmonyLib;

using DunGen;

using Tile = LabyrinthianFacilities.Tile;

// POTENTIAL ISSUE:
// OnGenerationStatusChanged invoked even though we don't check what
[HarmonyPatch(typeof(DungeonGenerator))]
class GenerateLevel {
	
	[HarmonyPatch(typeof(DungeonGenerator),"Generate")]
	[HarmonyPrefix]
	public static bool CustomGenerate(DungeonGenerator __instance) {
		
		Plugin.LogInfo("Custom Generate!");
		var flow = new DungeonFlowConverter(__instance.DungeonFlow);
		
		MapHandler.Instance.NewMap(
			StartOfRound.Instance.currentLevel,
			flow,
			__instance.ChosenSeed
		);
		
		GenerateLevel.ChangeStatus(__instance,GenerationStatus.Complete);
		return false;
	}
	
	[HarmonyReversePatch]
	[HarmonyPatch("Status",MethodType.Setter)]
	public static void ChangeStatus(object instance, GenerationStatus value) {
		throw new NotImplementedException("Reverse patch stub");
	}
}

/*
[HarmonyPatch(typeof(StartOfRound))]
class StartOfRoundPatch {
	
	[HarmonyPatch("ShipLeave")]
	[HarmonyPrefix]
	public static void SaveMatchUponLeaving() {
		if (Plugin.local_fatal_error) return;
		
		Plugin.LogInfo("Saving map!");
		MapHandler.Save();
	}
}
*/