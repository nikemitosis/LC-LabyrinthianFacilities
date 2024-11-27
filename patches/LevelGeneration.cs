namespace LabyrinthianFacilities.Patches;
using DgConversion;

using System;

using HarmonyLib;

using DunGen;

using Tile = LabyrinthianFacilities.Tile;

[HarmonyPatch(typeof(DungeonGenerator))]
class GenerateLevel {
	
	[HarmonyPatch("Generate")]
	[HarmonyPrefix]
	public static bool CustomGenerate(DungeonGenerator __instance) {
		
		Plugin.LogInfo("Custom Generate!");
		var flow = new DungeonFlowConverter(__instance.DungeonFlow);
		
		MapHandler.Instance.StartCoroutine(MapHandler.Instance.Generate(
			StartOfRound.Instance.currentLevel,
			flow,
			0,// __instance.Seed,
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