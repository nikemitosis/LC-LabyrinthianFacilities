// Fire exit doesn't spawn on offense mineshaft?

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
	
	private const uint PROMOTE_LOG = 0;
	// if other modders want to make this thing shut the fuck up, set this higher
	// (0=Debug, 1=Info, 2=Message, 3=Warning, 4=Error, 5=Fatal)
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
		try {
			NetcodePatch();
		} catch (Exception e) {
			LogMessage($"NetcodePatch failed - {e.Message}");
		}
		
		harmony.PatchAll();
		
		LogInfo($"Plugin {Plugin.GUID} is Awoken!");
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
		initializedAssets = true;
		
		Plugin.LogInfo($"Creating Tiles");
		foreach (DunGen.Tile tile in Resources.FindObjectsOfTypeAll(typeof(DunGen.Tile))) {
			// Surely there's a better way to fix +z being the vertical axis on these tiles...
			switch (tile.gameObject.name) {
				// manor 
				case "CloverTile":
				// mineshaft
				case "CaveCrampedIntersectTile":
				case "CaveSmallIntersectTile":
				case "DeepShaftTile":
				case "CaveWaterTile":
				case "CaveLongRampTile":
				case "CaveYTile":
					var rotation = Quaternion.Euler(270,0,0);
					tile.transform.rotation *= rotation;
					
					var bounds = tile.TileBoundsOverride;
					tile.TileBoundsOverride = new Bounds(
						rotation * bounds.center,
						rotation * bounds.size
					);
				break;
			}
			tile.gameObject.AddComponent<DTile>();
		}
		
		Plugin.LogInfo("Creating Doorways");
		foreach (DunGen.Doorway doorway in Resources.FindObjectsOfTypeAll(typeof(DunGen.Doorway))) {
			doorway.gameObject.AddComponent<DDoorway>();
		}
		
		Plugin.LogInfo("Creating Scrap");
		foreach (GrabbableObject scrap in Resources.FindObjectsOfTypeAll(typeof(GrabbableObject))) {
			if (scrap.itemProperties.isScrap) {
				scrap.gameObject.AddComponent<Scrap>();
			}
		}
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
	
	private Dictionary<SelectableLevel,Dictionary<DungeonFlow,DGameMap>> maps = null;
	private DGameMap activeMap = null;
	
	public DGameMap ActiveMap {get {return activeMap;}}
	
	public override void OnNetworkSpawn() {
		if (Instance != null) {
			this.GetComponent<NetworkObject>().Despawn(true);
			return;
		}
		Plugin.InitializeCustomGenerator();
		Instance = this;
		NetworkManager.OnClientStopped += MapHandler.OnDisconnect;
		maps = new Dictionary<SelectableLevel, Dictionary<DungeonFlow,DGameMap>>();
		
	}
	public override void OnNetworkDespawn() {
		MapHandler.Instance = null;
		GameObject.Destroy(this.gameObject);
	}
	
	public static void OnDisconnect(bool isHost) {
		Plugin.LogInfo($"Disconnecting: Destroying local instance of MapHandler");
		Instance.NetworkManager.OnClientStopped -= MapHandler.OnDisconnect;
		Instance.OnNetworkDespawn();
	}
	
	public static void TileInsertionFail(Tile t) {
		if (t == null) Plugin.LogDebug($"Failed to place tile {t}");
	}
	
	public DGameMap GetMap(
		SelectableLevel moon, 
		DungeonFlow flow, 
		GameMap.GenerationCompleteDelegate onComplete=null
	) {
		Dictionary<DungeonFlow,DGameMap> flowmaps;
		DGameMap map;
		if (!this.maps.TryGetValue(moon, out flowmaps)) {
			this.maps.Add(moon,flowmaps=new Dictionary<DungeonFlow,DGameMap>());
		}
		if (!flowmaps.TryGetValue(flow,out map)) {
			flowmaps.Add(flow, map=NewMap(onComplete));
			map.name = $"map:{moon.name}:{flow.name}";
		}
		return map;
	}
	
	public DGameMap NewMap(
		GameMap.GenerationCompleteDelegate onComplete=null
	) {
		GameObject newmapobj;
		DGameMap newmap;
		
		newmapobj = new GameObject();
		newmapobj.transform.SetParent(this.gameObject.transform);
		newmapobj.transform.position -= Vector3.up * 200.0f;
		
		newmap = newmapobj.AddComponent<DGameMap>();
		newmap.GenerationCompleteEvent += onComplete;
		
		newmap.TileInsertionEvent += MapHandler.TileInsertionFail;
		
		return newmap;
	}
	
	public IEnumerator Generate(
		SelectableLevel moon, 
		DungeonFlowConverter tilegen, 
		int? seed=null,
		GameMap.GenerationCompleteDelegate onComplete=null
	) {
		if (this.activeMap != null) this.activeMap.gameObject.SetActive(false);
		
		DGameMap map = GetMap(moon,tilegen.Flow); 
		Plugin.LogInfo($"Generating tiles for {moon.name}, {tilegen.Flow}");
		map.gameObject.SetActive(true);
		this.activeMap = map;
		
		map.GenerationCompleteEvent += onComplete;
		map.TileInsertionEvent += tilegen.FailedPlacementHandler;
		yield return map.GenerateCoroutine(tilegen,seed);
		map.TileInsertionEvent -= tilegen.FailedPlacementHandler;
		map.GenerationCompleteEvent -= onComplete;
		
		// every indoor enemy *appears* to use agentId 0
		map.GenerateNavMesh(agentId: 0);
	}
	
	// Stop RoundManager from deleting scrap at the end of the day by hiding it
	// (Scrap is hidden by making it inactive; LC only looks for enabled GrabbableObjects)
	public void PreserveScrap() {
		this.activeMap.PreserveScrap();
	}
}

// May be refactored as an extension of GrabbableObject, since we want to include equipment, too
// (but not maneater :P)
public class Scrap : MonoBehaviour {
	
	public GrabbableObject Grabbable {get {return this.GetComponent<GrabbableObject>();}}
	
	public void FindParent() {
		if (this.Grabbable.isInShipRoom) return;
		
		bool noparentfound = true;
		foreach (Tile t in MapHandler.Instance.ActiveMap.GetComponentsInChildren<Tile>()) {
			if (t.BoundingBox.Contains(this.transform.position)) {
				this.transform.parent = t.transform;
				noparentfound = false; break;
			}
		} if (noparentfound) {
			this.transform.parent = MapHandler.Instance.ActiveMap.transform;
		}
		this.Grabbable.targetFloorPosition 
			= this.Grabbable.startFallingPosition 
			= this.transform.localPosition;
	}
	
	public void Preserve() {
		this.FindParent();
		this.gameObject.SetActive(false);
	}
	
	public void Restore() {
		this.gameObject.SetActive(true);
	}
}