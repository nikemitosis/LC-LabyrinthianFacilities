namespace LabyrinthianFacilities;

using DgConversion;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

using UnityEngine;
using Unity.Netcode;

using DunGen.Graph;

// UnityEngine + System ambiguity
using Random = System.Random;

[BepInPlugin(Plugin.GUID, Plugin.NAME, Plugin.VERSION)]
public class Plugin : BaseUnityPlugin {
	public const string GUID = "mitzapper2.LethalCompany.LabyrinthianFacilities";
	public const string NAME = "LabyrinthianFacilities";
	public const string VERSION = "0.0.1";
	
	private readonly Harmony harmony = new Harmony(GUID);
	private static new ManualLogSource Logger;
	
	private static bool initializedAssets = false;
	
	private const uint PROMOTE_LOG = 1;
	public static uint MIN_LOG = 0;
	
	// From and for UnityNetcodePatcher
	private void NetcodePatch() {
		var types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (var type in types) {
			var methods = type.GetMethods(
				BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
			);
			foreach (var method in methods) {
				var attributes = method.GetCustomAttributes(
					typeof(RuntimeInitializeOnLoadMethodAttribute),
					false
				);
				if (attributes.Length > 0) method.Invoke(null,null);
			}
		}
	}
	
	private void Awake() {
		Logger = base.Logger;
		harmony.PatchAll();
		
		Logger.LogInfo($"Plugin {Plugin.GUID} is Awoken!");
	}
	
	public static void LogDebug(string message) {
		if (MIN_LOG > 0) return;
		if (PROMOTE_LOG > 0) {
			LogInfo(message);
			return;
		}
		Logger.LogDebug(message);
	}
	public static void LogInfo(string message) {
		if (MIN_LOG > 1) return;
		if (PROMOTE_LOG > 1) {
			LogMessage(message);
			return;
		}
		Logger.LogInfo(message);
	}
	public static void LogMessage(string message) {
		if (MIN_LOG > 2) return;
		if (PROMOTE_LOG > 2) {
			LogWarning(message);
			return;
		}
		Logger.LogMessage(message);
	}
	public static void LogWarning(string message) {
		if (MIN_LOG > 3) return;
		if (PROMOTE_LOG > 3) {
			LogError(message);
			return;
		}
		Logger.LogWarning(message);
	}
	public static void LogError(string message) {
		if (MIN_LOG > 4) return;
		if (PROMOTE_LOG > 4) {
			LogFatal(message);
			return;
		}
		Logger.LogError(message);
	}
	public static void LogFatal(string message) {
		if (MIN_LOG > 5 || PROMOTE_LOG > 5) return;
		Logger.LogFatal(message);
	}
	
	public static void InitializeCustomGenerator() {
		if (initializedAssets) return;
		
		Plugin.LogInfo($"Creating Tiles");
		foreach (DunGen.Tile tile in Resources.FindObjectsOfTypeAll(typeof(DunGen.Tile))) {
			var newtile = tile.gameObject.AddComponent<DTile>();
			// newtile.TileInstantiatedEvent += DunGenConverter.InstantiationHandler;
		}
		
		Plugin.LogInfo("Creating Doorways");
		foreach (DunGen.Doorway doorway in Resources.FindObjectsOfTypeAll(typeof(DunGen.Doorway))) {
			var newdoorway = doorway.gameObject.AddComponent<DDoorway>();
		}
		
		// Plugin.LogInfo("Creating Doors");
		// foreach (DunGen.Door door in Resources.FindObjectsOfTypeAll(typeof(DunGen.Door))) {
			// Plugin.LogInfo($"Adding Door for '{door.gameObject}'");
			// var newdoor = door.gameObject.AddComponent<Door<bool>>();
			// newdoor.InitFromDunGen(door);
		// }
	}
	
	
	public static IEnumerator DebugWait() {
		Transform body = StartOfRound.Instance.localPlayerController.thisPlayerBody;
		while (body.position[1] > -100) yield return new WaitForSeconds(0.5f);
		while (body.position[1] < -100) yield return new WaitForSeconds(0.05f);
	}
	
	public static Tile DebugGetTile(string name) {
		foreach (Tile t in Resources.FindObjectsOfTypeAll<Tile>()) {
			if (t.gameObject.name == name) return t;
		}
		return null;
	}
}

public class MapHandler : NetworkBehaviour {
	public static MapHandler Instance {get; private set;}
	internal static GameObject prefab = null;
	
	private Dictionary<SelectableLevel,GameMap> maps = null;
	
	public override void OnNetworkSpawn() {
		if (Instance != null) return;
		Instance = this;
		
		maps = new Dictionary<SelectableLevel,GameMap>();
	}
	
	public static void TileInsertionFail(Tile t) {
		if (t == null) Plugin.LogDebug($"Failed to place tile {t}");
	}
	
	public GameMap NewMap(
		SelectableLevel moon, 
		GameMap.GenerationCompleteDelegate onComplete=null
	) {
		GameObject newmapobj;
		GameMap newmap;
		if (maps.TryGetValue(moon,out newmap)) {
			Plugin.LogWarning(
				"Attempted to generate new moon on moon that was already generated"
			);
			return null;
		}
		Plugin.LogInfo($"Generating new moon for {moon.name}!");
		newmapobj = new GameObject($"map:{moon.name}");
		newmapobj.transform.SetParent(this.gameObject.transform);
		newmapobj.transform.position -= Vector3.up * 200.0f;
		
		newmap = newmapobj.AddComponent<GameMap>();
		newmap.GenerationCompleteEvent += onComplete;
		maps.Add(moon,newmap);
		
		newmap.TileInsertionEvent += MapHandler.TileInsertionFail;
		
		return newmap;
	}
	
	public IEnumerator Generate(
		SelectableLevel moon, 
		ITileGenerator tilegen, 
		int? seed=null,
		GameMap.GenerationCompleteDelegate onComplete=null
	) {
		GameMap map = null; 
		if (!maps.TryGetValue(moon,out map)) {
			map = NewMap(moon);
		}
		Plugin.LogInfo($"Generating tiles for {moon.name}");
		
		map.GenerationCompleteEvent += onComplete;
		map.TileInsertionEvent += tilegen.FailedPlacementHandler;
		yield return map.GenerateCoroutine(tilegen.Generator,seed);
		map.TileInsertionEvent -= tilegen.FailedPlacementHandler;
		map.GenerationCompleteEvent -= onComplete;
		
		// every indoor enemy *appears* to use agentId 0
		map.GenerateNavMesh(agentId: 0);
	}
}