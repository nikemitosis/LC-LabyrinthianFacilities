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
	public const string VERSION = "0.2.0";
	
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
		
		try {
			SaveManager.SyncSavesByWriteTime();
		} catch (Exception ex) {
			Plugin.LogError($"Error syncing saves: {ex.Message}");
		}
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
	
	public static void InitializeAssets() {
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
		
		Plugin.LogInfo("Creating Scrap & Equipment");
		foreach (GrabbableObject grabbable in Resources.FindObjectsOfTypeAll(typeof(GrabbableObject))) {
			// exclude corpses & clipboards?
			if (
				grabbable.GetType() == typeof(RagdollGrabbableObject)
				// || grabbable.GetType() == typeof(ClipboardItem)
			) continue;
			
			if (grabbable.itemProperties.isScrap) {
				grabbable.gameObject.AddComponent<Scrap>();
			} else if (
				// exclude maneater (and hopefully catch custom enemies with a similar gimmick)
				grabbable.GetComponent<EnemyAI>() == null 
			) {
				grabbable.gameObject.AddComponent<Equipment>();
			}
		}
	}
	
}

public class MapHandler : NetworkBehaviour {
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
		Plugin.InitializeAssets();
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
	
	public DGameMap GetMap(
		SelectableLevel moon, 
		DungeonFlow flow, 
		Action<GameMap> onComplete=null
	) {
		DGameMap map;
		if (!this.maps.TryGetValue((moon,flow), out map)) {
			string name = $"map:{moon.name}:{flow.name}";
			
			if (!unresolvedMaps.TryGetValue(name, out map)) {
				map = NewMap(onComplete);
			} else {
				unresolvedMaps.Remove(name);
			}
			
			map.name = name;
			this.maps.Add((moon,flow), map);
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
		// newmapobj.transform.position -= Vector3.up * 200.0f;
		
		newmap = newmapobj.AddComponent<DGameMap>();
		newmap.GenerationCompleteEvent += onComplete;
		
		return newmap;
	}
	
	public void ClearActiveMap() {
		this.activeMap = null;
	}
	
	public IEnumerator Generate(
		SelectableLevel moon, 
		DungeonFlowConverter tilegen, 
		Action<GameMap> onComplete=null
	) {
		if (this.activeMap != null) this.activeMap.gameObject.SetActive(false);
		
		DGameMap map = GetMap(moon,tilegen.Flow); 
		this.activeMap = map;
		this.activeMap.gameObject.SetActive(true);
		
		map.GenerationCompleteEvent += onComplete;
		map.TileInsertionEvent += tilegen.FailedPlacementHandler;
		Plugin.LogInfo($"Generating tiles for {moon.name}, {tilegen.Flow}");
		yield return map.GenerateCoroutine(tilegen);
		map.TileInsertionEvent -= tilegen.FailedPlacementHandler;
		map.GenerationCompleteEvent -= onComplete;
	}
	
	// Stop RoundManager from deleting scrap at the end of the day by hiding it
	// (Scrap is hidden by making it inactive; LC only looks for enabled GrabbableObjects)
	public void PreserveMapObjects() {
		Plugin.LogInfo("Hiding Map Objects!");
		this.activeMap?.PreserveMapObjects();
	}
	
	public void DestroyAllScrap() {
		Plugin.LogInfo("Destroying all scrap :(");
		foreach (var entry in maps) {
			entry.Value.DestroyAllScrap();
		}
		foreach (var entry in unresolvedMaps) {
			entry.Value.DestroyAllScrap();
		}
	}
	
	public void SaveGame() {
		if (!(base.IsServer || base.IsHost)) return;
		Plugin.LogInfo("Saving maps!");
		try {
			Directory.CreateDirectory(SaveManager.ModSaveDirectory);
		} catch (IOException) {
			Plugin.LogError("Could not load save; path to saves folder was interrupted by a file");
			throw;
		}
		
		using (FileStream fs = File.Open(SaveManager.CurrentSavePath, FileMode.Create)) {
			var s = new SerializationContext();
			s.Serialize(this, new MapHandlerSerializer());
			s.SaveToFile(fs);
		}
	}
	public void LoadGame() {
		if (!(base.IsServer || base.IsHost)) return;
		Plugin.LogInfo($"Loading save {SaveManager.CurrentSave}.dat!");
		if (SaveManager.SaveIsMissingOrNew(SaveManager.CurrentSave)) {
			Plugin.LogInfo($"Save is outdated, deleting");
			SaveManager.DeleteFile(SaveManager.CurrentSavePath);
		}
		
		try {
			byte[] bytes = null;
			using (FileStream fs = File.Open(SaveManager.CurrentSavePath, FileMode.Open)) {
				bytes = new byte[fs.Length];
				if (fs.Read(bytes,0,(int)fs.Length) != fs.Length) {
					Plugin.LogError($"Did not read as many bytes from file as were in the file?");
				}
			}
			new DeserializationContext(bytes).Deserialize(new MapHandlerSerializer());
		} catch (IOException) {
			Plugin.LogInfo($"No save data found for {GameNetworkManager.Instance.currentSaveFileName}");
		}
	}
	public void LoadMap(DGameMap m) {
		m.transform.parent = this.transform;
		// m.transform.position -= Vector3.up * 200.0f;
		this.unresolvedMaps.Add(m.name,m);
	}
	
	public void Clear() {
		if (!(base.IsServer || base.IsHost)) return;
		this.GetComponent<NetworkObject>().Despawn();
		GameObject.Instantiate(MapHandler.prefab).GetComponent<NetworkObject>().Spawn();
	}
	
	public void SendMapDataToClient(ulong clientId) {
		if (!(base.IsServer || base.IsHost)) return;
		Plugin.LogInfo($"Sending map data to client #{clientId}!");
		var cparams = new ClientRpcParams {
			Send = new ClientRpcSendParams {
				TargetClientIds = new ulong[]{clientId}
			}
		};
		var s = new SerializationContext();
		s.Serialize(this, new MapHandlerSerializer());
		Plugin.LogInfo($"({s.Output.Count} bytes)");
		byte[] b = new byte[s.Output.Count];
		s.Output.CopyTo(b,0);
		LoadMapsClientRpc(b, cparams);
	}
	
	[ClientRpc]
	protected void LoadMapsClientRpc(byte[] bytes, ClientRpcParams cparams=default) {
		Plugin.LogInfo($"Receiving maps from server! ({bytes.Length} bytes)");
		var rt = new DeserializationContext(bytes).Deserialize(new MapHandlerSerializer());
		if (!ReferenceEquals(rt, Instance)) Plugin.LogError($"Got a different MapHandler than Instance?");
		Plugin.LogInfo($"Requesting map objects from the server...");
		RequestMapObjectsServerRpc();
	}
	
	[ServerRpc(RequireOwnership=false)]
	protected void RequestMapObjectsServerRpc(ServerRpcParams sparams=default) {
		var cparams = new ClientRpcParams {
			Send = new ClientRpcSendParams {
				TargetClientIds = new ulong[1]{sparams.Receive.SenderClientId}
			}
		};
		Plugin.LogInfo($"Received request for map objects from client #{sparams.Receive.SenderClientId}");
		var s = new SerializationContext();
		s.Serialize(this,new MapHandlerNetworkSerializer());
		Plugin.LogInfo($"{s.Output.Count} bytes of mapObjects");
		byte[] b = new byte[s.Output.Count];
		s.Output.CopyTo(b,0);
		SendMapObjectsClientRpc(b,cparams);
	}
	
	[ClientRpc]
	protected void SendMapObjectsClientRpc(byte[] bytes, ClientRpcParams cparams=default) {
		Plugin.LogInfo($"Received map objects from server! ({bytes.Length} bytes)");
		var rt = new DeserializationContext(bytes).Deserialize(new MapHandlerNetworkSerializer());
		if (!ReferenceEquals(rt,Instance)) Plugin.LogError($"Got a different MapHandler than Instance?");
		Plugin.LogInfo($"Done syncing with server!");
	}
	
	internal void DebugSave() {
		var s = new SerializationContext();
		s.Serialize(this, new MapHandlerSerializer());
		byte[] bytes = new byte[s.Output.Count];
		s.Output.CopyTo(bytes,0);
		using (FileStream fs = File.Open($"{SaveManager.ModSaveDirectory}/debug.dat", FileMode.Create)) {
			foreach (byte b in bytes) {
				fs.WriteByte(b);
			}
		}
	}
}

internal static class SaveManager {
	
	public static double SaveTimeTolerance = 20.0d;
	public static string ModSaveDirectory;
	public static string NativeSaveDirectory;
	
	public static string CurrentSave {get {return GameNetworkManager.Instance.currentSaveFileName;}}
	public static string CurrentSavePath {get {
		return $"{ModSaveDirectory}/{GameNetworkManager.Instance.currentSaveFileName}.dat";
	}}
	public static string CurrentSavePathNative {get {
		return $"{Application.persistentDataPath}/{GameNetworkManager.Instance.currentSaveFileName}";
	}}
	
	static SaveManager() {
		ModSaveDirectory = $"{Application.persistentDataPath}/{Plugin.NAME}";
		NativeSaveDirectory = Application.persistentDataPath;
	}
	
	// path does not have to be fully qualified, as long as it has the name of the save file
	// appends .dat to the end of the filename! (because this mod uses .dat as the file extension for its save data)
	public static string GetSaveNameFromPath(string nativePath) {
		int fileIdx = nativePath.LastIndexOf('/') + 1; // deliberate masking of -1 error
		string fileName = nativePath.Substring(fileIdx);
		return $"{fileName}.dat";
	}
	
	// Use to detect whether the savefile we have is actually meant for the current save
	// (as opposed to a save where this mod wasn't active)
	public static bool SaveIsMissingOrNew(string saveName) {
		if (saveName.EndsWith(".dat")) saveName = saveName.Substring(0,saveName.Length-4);
		FileInfo thisFile = new FileInfo($"{ModSaveDirectory}/{saveName}.dat");
		FileInfo nativeFile = new FileInfo($"{NativeSaveDirectory}/{saveName}");
		if (!nativeFile.Exists) return true;
		return FileTimeDiff(nativeFile,thisFile) > SaveTimeTolerance;
	}
	
	// positive = f1 is more recent
	public static double FileTimeDiff(FileInfo f1, FileInfo f2) {
		var rt = (f1.LastWriteTimeUtc - f2.LastWriteTimeUtc).TotalSeconds;
		return rt;
	}
	
	public static void RenameFile(string oldName, string newName) {
		if (oldName.Contains("..")) throw new IOException("Miss me with that shit");
		var file = new FileInfo($"{ModSaveDirectory}/{oldName}");
		if (!file.Exists) return;
		try {
			file.MoveTo($"{ModSaveDirectory}/{newName}");
		} catch (Exception ex) {
			Plugin.LogError($"Error when renaming file {oldName} to {newName}: \n{ex.Message}");
		}
	}
	
	public static void DeleteFile(string saveName) {
		if (saveName.Contains("..")) throw new IOException("Miss me with that shit");
		var file = new FileInfo($"{ModSaveDirectory}/{saveName}");
		if (!file.Exists) {
			Plugin.LogInfo($"Could not delete file {saveName}; file does not exist. ");
			return;
		}
		Plugin.LogInfo($"Deleting save '{saveName}'!");
		file.Delete();
	}
	
	public static void SyncSavesByWriteTime() {
		Plugin.LogInfo($"Syncing LF save data with LC! (Compatibility with LCBetterSaves)");
		
		var moddir = new DirectoryInfo(ModSaveDirectory);
		List<(int id, FileInfo file)> badSaves = new();
		List<int> usedSaves = new();
		foreach (FileInfo modsave in moddir.EnumerateFiles()) {
			int id;
			try {
				id = int.Parse(modsave.Name.Substring(10, modsave.Name.Length-14));
			} catch (FormatException) {
				Plugin.LogWarning(
					$"{typeof(SaveManager)} does not know how to sync save '{modsave.Name}'"
				);
				continue;
			}
			
			if (SaveIsMissingOrNew(modsave.Name)) {
				badSaves.Add((id,modsave));
			} else {
				usedSaves.Add(id);
			}
		}
		uint renamed=0;
		foreach ((int id, FileInfo file) in badSaves) {
			bool forelse = true;
			for (int nativeId=1; nativeId<id; nativeId++) {
				if (usedSaves.Contains(nativeId)) continue;
				var natsave = new FileInfo($"{NativeSaveDirectory}/LCSaveFile{nativeId}");
				if (Math.Abs(FileTimeDiff(natsave,file)) < SaveTimeTolerance) {
					RenameFile($"LCSaveFile{id}.dat",$"LCSaveFile{nativeId}.dat");
					usedSaves.Add(nativeId);
					renamed++;
					forelse = false; break;
				}
			} if (forelse) {
				Plugin.LogError(
					$"Unable to find matching LC save file for save '{file.Name}'. "
					+$"Deleting file as it seems the corresponding save has been deleted. "
				);
				DeleteFile(file.Name);
			}
		}
		if (renamed != 0) Plugin.LogInfo($"Resynced {renamed} saves!");
	}
}

public class MapHandlerSerializer : Serializer<MapHandler> {
	public override void Serialize(SerializationContext sc, object o) {
		if (!ReferenceEquals(o,MapHandler.Instance)) {
			throw new ArgumentException($"MapHandler singleton violated");
		}
		var maps = MapHandler.Instance.GetComponentsInChildren<DGameMap>(true);
		sc.Add((ushort)(maps.Length));
		var serializer = new DGameMapSerializer();
		foreach (DGameMap map in maps) {
			sc.AddReference(map, serializer);
		}
	}
	
	protected override MapHandler Deserialize(
		MapHandler baseObj, DeserializationContext dc, object extraContext=null
	) {
		if (!ReferenceEquals(baseObj, MapHandler.Instance)) {
			Plugin.LogError($"Deserialzed instance is not MapHandler singleton!");
			((MapHandler)baseObj).GetComponent<NetworkObject>().Despawn(true);
		}
		dc.Consume(2).CastInto(out ushort numMaps);
		
		// Captures for lambda
		var rt = MapHandler.Instance;
		var mapDeserializer = new DGameMapSerializer();
		var tileDeserializer = new DTileSerializer();
		for (int i=0; i<numMaps; i++) {
			dc.ConsumeReference(
				mapDeserializer,
				(object s) => rt.LoadMap((DGameMap)s),
				tileDeserializer
			);
		}
		
		return rt;
	}
	public override MapHandler Deserialize(
		DeserializationContext dc, object extraContext=null
	) {
		return Deserialize(MapHandler.Instance,dc,extraContext);
	}
}

public class MapHandlerNetworkSerializer : Serializer<MapHandler> {
	public override void Serialize(SerializationContext sc, object obj) {
		MapHandler item = (MapHandler)obj;
		
		DGameMap[] maps = item.GetComponentsInChildren<DGameMap>(true);
		sc.Add((ushort)maps.Length);
		var mapSerializer = new DGameMapNetworkSerializer();
		foreach (DGameMap map in maps) {
			sc.AddInline(map,mapSerializer);
		}
	}
	
	protected override MapHandler Deserialize(
		MapHandler tgt, DeserializationContext dc, object extraContext=null
	) {
		dc.Consume(sizeof(ushort)).CastInto(out ushort numMaps);
		var ds = new DGameMapNetworkSerializer();
		for (ushort i=0; i<numMaps; i++) {
			dc.ConsumeInline(ds);
		}
		return tgt;
	}
	
	public override MapHandler Deserialize(DeserializationContext dc, object extraContext=null) {
		return Deserialize(MapHandler.Instance,dc,extraContext);
	}
}