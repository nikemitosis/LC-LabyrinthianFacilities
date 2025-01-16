// ^(\[.{7}:((LabyrinthianFacilities)|( Unity Log))\] .*$)
// (Logging regex)
namespace LabyrinthianFacilities;

using DgConversion;
using Serialization;
using Util;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
	public const string VERSION = "0.1.1";
	
	public const string SAVES_PATH = $"BepInEx/plugins/{NAME}/saves";
	
	private readonly Harmony harmony = new Harmony(GUID);
	private static new ManualLogSource Logger;
	
	private static bool initializedAssets = false;
	
	// for internal use, makes it so I can see my own debug/info logs without seeing everyone else's
	private static uint PROMOTE_LOG = 2;
	
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
	
}

public class MapHandler : NetworkBehaviour, ISerializable {
	public static MapHandler Instance {get; private set;}
	internal static GameObject prefab = null;
	
	private Dictionary<(SelectableLevel level, DungeonFlow flow),DGameMap> maps = null;
	private Dictionary<string,DGameMap> unresolvedMaps;
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
		maps = new();
		unresolvedMaps = new();
		LoadGame();
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
	
	public bool TryGetUnresolvedMap(string key, out DGameMap map) {
		if (unresolvedMaps.TryGetValue(key,out map)) {
			unresolvedMaps.Remove(key);
			return true;
		}
		return false;
	}
	
	public DGameMap GetMap(
		SelectableLevel moon, 
		DungeonFlow flow, 
		Action<GameMap> onComplete=null
	) {
		DGameMap map;
		if (
			!this.maps.TryGetValue((moon,flow), out map) 
			&& !TryGetUnresolvedMap($"map:{moon.name}:{flow.name}", out map)
		) {
			this.maps.Add((moon,flow), map=NewMap(onComplete));
			map.name = $"map:{moon.name}:{flow.name}";
		}
		return map;
	}
	
	public DGameMap NewMap(
		Action<GameMap> onComplete=null
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
		Action<GameMap> onComplete=null
	) {
		if (this.activeMap != null) this.activeMap.gameObject.SetActive(false);
		
		DGameMap map = GetMap(moon,tilegen.Flow); 
		this.activeMap = map;
		this.activeMap.gameObject.SetActive(true);
		
		map.GenerationCompleteEvent += onComplete;
		map.TileInsertionEvent += tilegen.FailedPlacementHandler;
		Plugin.LogInfo($"Generating tiles for {moon.name}, {tilegen.Flow}");
		yield return map.GenerateCoroutine(tilegen,seed);
		map.TileInsertionEvent -= tilegen.FailedPlacementHandler;
		map.GenerationCompleteEvent -= onComplete;
	}
	
	// Stop RoundManager from deleting scrap at the end of the day by hiding it
	// (Scrap is hidden by making it inactive; LC only looks for enabled GrabbableObjects)
	public void PreserveScrap() {
		this.activeMap?.PreserveScrap();
	}
	
	public void SaveGame() {
		if (!(base.IsServer || base.IsHost)) return;
		Plugin.LogInfo("Saving maps!");
		try {
			Directory.CreateDirectory(Plugin.SAVES_PATH);
		} catch (IOException) {
			Plugin.LogError("Could not load save; path to saves folder was interrupted by a file");
			throw;
		}
		
		string fileName = $"{Plugin.SAVES_PATH}/{GameNetworkManager.Instance.currentSaveFileName}.dat";
		using (FileStream fs = File.Open(fileName, FileMode.Create)) {
			var s = new Serializer();
			s.Serialize(this);
			s.SaveToFile(fs);
		}
	}
	public void LoadGame() {
		if (!(base.IsServer || base.IsHost)) return;
		Plugin.LogInfo($"Loading save data!");
		try {
			string fileName = $"{Plugin.SAVES_PATH}/{GameNetworkManager.Instance.currentSaveFileName}.dat";
			byte[] bytes = null;
			using (FileStream fs = File.Open(fileName, FileMode.Open)) {
				bytes = new byte[fs.Length];
				if (fs.Read(bytes,0,(int)fs.Length) != fs.Length) {
					Plugin.LogError($"Did not read as many bytes from file as were in the file?");
				}
			}
			new DeserializationContext(bytes).Deserialize(new MapHandlerDeserializer());
			foreach (var entry in this.unresolvedMaps) {
				entry.Value.gameObject.SetActive(false);
			}
		} catch (IOException) {
			Plugin.LogInfo($"No save data found for {GameNetworkManager.Instance.currentSaveFileName}");
		}
	}
	public void LoadMap(DGameMap m) {
		m.transform.parent = this.transform;
		m.transform.position -= Vector3.up * 200.0f;
		this.unresolvedMaps.Add(m.name,m);
	}
	
	public void SendMapDataToClient(ulong clientId) {
		if (!(base.IsServer || base.IsHost)) return;
		var cparams = new ClientRpcParams {
			Send = new ClientRpcSendParams {
				TargetClientIds = new ulong[]{clientId}
			}
		};
		var s = new Serializer();
		s.Serialize(this);
		byte[] b = new byte[s.Output.Count];
		s.Output.CopyTo(b,0);
		LoadMapsClientRpc(b, cparams);
	}
	
	[ClientRpc]
	protected void LoadMapsClientRpc(byte[] bytes, ClientRpcParams cparams=default) {
		new DeserializationContext(bytes).Deserialize(new MapHandlerDeserializer());
	}
	
	public IEnumerable<SerializationToken> Serialize() {
		yield return new SerializationToken(
			((ushort)(this.maps.Count)).GetBytes(), 
			isStartOf: this
		);
		foreach (var entry in this.maps) {
			var map = entry.Value;
			yield return new SerializationToken(
				referenceTo: map
			);
		}
	}
}

// May be refactored as an extension of GrabbableObject, since we want to include equipment, too
// (but not maneater :P)
public class Scrap : MonoBehaviour {
	
	public GrabbableObject Grabbable {get {
		return this.GetComponent<GrabbableObject>();
	}}
	
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
		var grabbable = this.Grabbable;
		grabbable.isInShipRoom = StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(
			grabbable.transform.position
		); // fix isInShipRoom for people joining partway through a save
		
		this.FindParent();
		
		if (!grabbable.isInShipRoom) this.gameObject.SetActive(false);
		if (grabbable.radarIcon != null && grabbable.radarIcon.gameObject != null) {
			grabbable.radarIcon.gameObject.SetActive(false);
		}
	}
	
	public void Restore() {
		this.gameObject.SetActive(true);
		var grabbable = this.Grabbable;
		if (
			!grabbable.isInShipRoom 
			&& grabbable.radarIcon != null 
			&& grabbable.radarIcon.gameObject != null
		) {
			grabbable.radarIcon.gameObject.SetActive(true);
		}
	}
}

public class MapHandlerDeserializer : IDeserializer<MapHandler> {
	public virtual MapHandler Deserialize(
		ISerializable baseObj, DeserializationContext dc, object extraContext=null
	) {
		if (!ReferenceEquals(baseObj, MapHandler.Instance)) {
			((MapHandler)baseObj).GetComponent<NetworkObject>().Despawn(true);
		}
		dc.Consume(2).CastInto(out ushort numMaps);
		
		// Captures for lambda
		var rt = MapHandler.Instance;
		var mapDeserializer = new DGameMapDeserializer();
		var tileDeserializer = new TileDeserializer<DTile>();
		for (int i=0; i<numMaps; i++) {
			dc.Consume(4).CastInto(out int refAddress);
			dc.EnqueueDependency(
				refAddress, 
				mapDeserializer, 
				(ISerializable s) => rt.LoadMap((DGameMap)s),
				tileDeserializer
			);
		}
		
		return rt;
	}
	public virtual MapHandler Deserialize(
		DeserializationContext dc, object extraContext=null
	) {
		return Deserialize(MapHandler.Instance,dc,extraContext);
	}
}