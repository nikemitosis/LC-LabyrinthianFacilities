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
		
		Plugin.LogInfo($"Custom Generate! (Seed={__instance.Seed})");
		var flow = new DungeonFlowConverter(__instance.DungeonFlow);
		
		MapHandler.Instance.StartCoroutine(MapHandler.Instance.Generate(
			StartOfRound.Instance.currentLevel,
			flow,
			// 1293754012,
			__instance.Seed,
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
		Plugin.LogInfo("Hiding scrap!");
		MapHandler.Instance.PreserveScrap();
	}
}
