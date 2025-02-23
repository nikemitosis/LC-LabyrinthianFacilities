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
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

using UnityEngine;
using Unity.Netcode;

using DunGen.Graph;

// UnityEngine + System ambiguity
using Random = System.Random;
using Object = UnityEngine.Object;

[BepInPlugin(Plugin.GUID, Plugin.NAME, Plugin.VERSION)]
public sealed class Plugin : BaseUnityPlugin {
	public const string GUID = "mitzapper2.LethalCompany.LabyrinthianFacilities";
	public const string NAME = "LabyrinthianFacilities";
	public const string VERSION = "0.4.1";
	
	public static Plugin Singleton {get; private set;}
	
	private readonly Harmony harmony = new Harmony(GUID);
	private static new ManualLogSource Logger;
	
	private static bool initializedAssets = false;
	
	// for internal use, makes it so I can see my own debug/info logs without seeing everyone else's
	private static uint PROMOTE_LOG = 0;
	
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
		if (Singleton != null) throw new InvalidOperationException("Singleton violation");
		Singleton = this;
		Logger = base.Logger;
		
		var cfg = LabyrinthianFacilities.Config.Singleton;
		if (!cfg.GlobalEnable) {
			LogInfo("{NAME} is disabled by its config; Skipping initialization");
			return;
		}
		DeserializationContext.Verbose = cfg.EnableVerboseDeserialization;
		SerializationContext.Verbose = cfg.EnableVerboseSerialization;
		
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
		LogInfo($"{NAME} is Awoken!");
	}
	
	public static void LogDebug  (string message) {
		if (MIN_LOG > 0) return;
		if (PROMOTE_LOG > 0) {
			LogInfo(message);
			return;
		}
		Logger.LogDebug(message);
	}
	public static void LogInfo   (string message) {
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
	public static void LogError  (string message) {
		if (MIN_LOG > 4) return;
		if (PROMOTE_LOG > 4) {
			LogFatal(message);
			return;
		}
		Logger.LogError(message);
	}
	public static void LogFatal  (string message) {
		if (MIN_LOG > 5 || PROMOTE_LOG > 5) return;
		Logger.LogFatal(message);
	}
	
	public static void InitializeAssets() {
		if (initializedAssets) return;
		initializedAssets = true;
		
		Plugin.LogInfo($"Initializing Tiles");
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
		
		Plugin.LogInfo("Initializing Doorways");
		foreach (DunGen.Doorway doorway in Resources.FindObjectsOfTypeAll(typeof(DunGen.Doorway))) {
			doorway.gameObject.AddComponent<DDoorway>();
		}
		
		Plugin.LogInfo("Initializing Scrap & Equipment");
		foreach (GrabbableObject grabbable in Resources.FindObjectsOfTypeAll(typeof(GrabbableObject))) {
			// exclude corpses & clipboards
			if (
				grabbable.GetType() == typeof(RagdollGrabbableObject)
				|| grabbable.GetType() == typeof(ClipboardItem)
			) continue;
			
			if (grabbable.itemProperties.isScrap) {
				if (grabbable.name == "RedLocustHive") {
					grabbable.gameObject.AddComponent<Beehive>();
				} else {
					grabbable.gameObject.AddComponent<Scrap>();
				}
			} else if (
				// exclude maneater (and hopefully catch custom enemies with a similar gimmick)
				grabbable.GetComponent<EnemyAI>() == null 
			) {
				grabbable.gameObject.AddComponent<Equipment>();
			}
		}
	
		Plugin.LogInfo("Initializing Company Cruiser");
		foreach (VehicleController vc in Resources.FindObjectsOfTypeAll(typeof(VehicleController))) {
			if (vc.name == "CompanyCruiser") {
				vc.gameObject.AddComponent<Cruiser>();
			} else if (vc.name == "CompanyCruiser(Clone)") { // Clients already loaded spawned NetworkObjects
				vc.gameObject.AddComponent<Cruiser>();
				vc.gameObject.AddComponent<DummyFlag>();
			} else {
				Plugin.LogWarning(
					$"Did not recognize vehicle '{vc.name}'. This vehicle will be ignored by {Plugin.NAME}"
				);
			}
		}
	}
}

public class MapHandler : NetworkBehaviour {
	public static MapHandler Instance {get; private set;}
	internal static GameObject prefab = null;
	
	private Dictionary<SelectableLevel,Moon> moons = null;
	private Dictionary<string,Moon> unresolvedMoons = null;
	
	public Moon ActiveMoon {get; private set;}
	
	public override void OnNetworkSpawn() {
		if (Instance != null) {
			this.GetComponent<NetworkObject>().Despawn(true);
			return;
		}
		Instance = this;
		
		NetworkManager.OnClientStopped += MapHandler.OnDisconnect;
		moons = new();
		unresolvedMoons = new();
		Plugin.InitializeAssets();
		
		if (this.IsHost || this.IsServer) {
			LoadGame();
		} else {
			MapHandler.Instance.RequestMapDataServerRpc(NetworkManager.Singleton.LocalClientId);
		}
		
	}
	public override void OnNetworkDespawn() {
		MapHandler.Instance = null;
		GameObject.Destroy(this.gameObject);
	}
	
	public static void OnDisconnect(bool isHost) {
		Plugin.LogInfo($"Disconnecting: Destroying local instance of MapHandler");
		
		// Dont need to include cruisers not parented to MapHandler because they are in active play, 
		// and not being despawned
		foreach (Cruiser cruiser in MapHandler.Instance.GetComponentsInChildren<Cruiser>(true)) {
			cruiser.DoneWithOldCruiserServerRpc(disconnect: true);
		}
		
		Instance.NetworkManager.OnClientStopped -= MapHandler.OnDisconnect;
		Instance.OnNetworkDespawn();
		
	}
	
	public Moon GetMoon(SelectableLevel level) {
		Moon moon;
		if (!this.moons.TryGetValue(level, out moon)) {
			string name = $"moon:{level.name}";
			
			if (!unresolvedMoons.TryGetValue(name, out moon)) {
				moon = NewMoon();
				moon.name = name;
			} else {
				unresolvedMoons.Remove(name);
			}
			
			moon.name = name;
			this.moons.Add(level, moon);
		}
		return moon;
	}
	
	public Moon NewMoon() {
		GameObject g = new GameObject();
		g.transform.parent = this.transform;
		return g.AddComponent<Moon>();
	}
	
	public DGameMap GetMap(
		SelectableLevel level, 
		DungeonFlow flow, 
		Action<GameMap> onComplete=null
	) {
		return GetMoon(level).GetMap(flow,onComplete);
	}
	
	public void ClearActiveMoon() {
		if (this.ActiveMoon != null) this.ActiveMoon.gameObject.SetActive(false);
		this.ActiveMoon = null;
	}
	
	public IEnumerator Generate(
		SelectableLevel level, 
		DungeonFlowConverter tilegen, 
		Action<GameMap> onComplete=null
	) {
		if (this.ActiveMoon != null) this.ActiveMoon.gameObject.SetActive(false);
		
		Moon moon = GetMoon(level);
		this.ActiveMoon = moon;
		this.ActiveMoon.gameObject.SetActive(true);
		
		yield return this.ActiveMoon.Generate(tilegen, onComplete);
		
		if (Config.Singleton.EnableHistory) {
			RecordDay(tilegen.Seed);
		}
	}
	
	// Stop RoundManager from deleting scrap at the end of the day by hiding it
	// (Scrap is hidden by making it inactive; LC only looks for enabled GrabbableObjects)
	public void PreserveMapObjects() {
		this.ActiveMoon?.PreserveMapObjects();
	}
	
	public void PreserveBees() {
		this.ActiveMoon?.PreserveBees();
	}
	
	public void DestroyAllScrap() {
		Plugin.LogInfo("Destroying all scrap :(");
		foreach (Scrap s in this.GetComponentsInChildren<Scrap>(includeInactive:true)) {
			GameObject.Destroy(s.gameObject);
		}
	}
	
	public void SaveGame() {
		if (!(base.IsServer || base.IsHost)) return;
		Plugin.LogInfo("Saving moons!");
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
	public void LoadMoon(Moon m) {
		m.transform.parent = this.transform;
		this.unresolvedMoons.Add(m.name,m);
	}
	
	public void Clear() {
		if (!base.IsServer) return;
		this.GetComponent<NetworkObject>().Despawn();
		SaveManager.DeleteFile($"{SaveManager.CurrentSave}.dat");
		GameObject.Instantiate(MapHandler.prefab).GetComponent<NetworkObject>().Spawn();
	}
	
	[ServerRpc(RequireOwnership=false)]
	public void RequestMapDataServerRpc(ulong clientId) {
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
	
	internal void RecordDay(int modSeed) {
		using (FileStream fs = File.Open(
			$"{SaveManager.ModSaveDirectory}/{SaveManager.CurrentSave}History.log", FileMode.Append
		)) {
			fs.Write((
				$"Q{TimeOfDay.Instance.timesFulfilledQuota+1} D{4-TimeOfDay.Instance.daysUntilDeadline}\n"
			).GetBytes());
			fs.Write($"StartOfRound.randomMapSeed: {StartOfRound.Instance.randomMapSeed}\n".GetBytes());
			fs.Write($"Active moon: {this.ActiveMoon?.name ?? "Company"}\n".GetBytes());
			if (this.ActiveMoon != null) {
				fs.Write($"Active map: {this.ActiveMoon.ActiveMap?.name ?? "Company"}\n".GetBytes());
			}
			fs.Write($"Mod Seed: {modSeed}\n\n".GetBytes());
		}
	}
}

public class Moon : MonoBehaviour {
	protected Dictionary<DungeonFlow,DGameMap> maps = new();
	protected Dictionary<string,DGameMap> unresolvedMaps = new();
	
	public DGameMap ActiveMap {get; private set;}
	
	private void OnDisable() {
		// Prevent ActiveMap from getting briefly enabled when swapping to this moon but a different interior
		// (To prevent OnEnable of children from executing when they will be immediately disabled)
		this.ActiveMap?.gameObject?.SetActive(false);
		this.ActiveMap = null;
	}
	
	public DGameMap GetMap(DungeonFlow flow, Action<GameMap> onComplete=null) {
		if (!maps.TryGetValue(flow, out DGameMap map)) {
			string name = $"interior:{flow.name}";
			if (!unresolvedMaps.TryGetValue(name, out map)) {
				map = NewMap(onComplete);
				map.name = name;
			} else {
				unresolvedMaps.Remove(name);
			}
			maps.Add(flow,map);
		}
		return map;
	}
	
	public DGameMap NewMap(
		Action<GameMap> onComplete=null
	) {
		GameObject newmapobj;
		DGameMap newmap;
		
		newmapobj = new GameObject();
		newmapobj.transform.SetParent(this.transform);
		
		newmap = newmapobj.AddComponent<DGameMap>();
		newmap.GenerationCompleteEvent += onComplete;
		
		return newmap;
	}
	
	public void LoadMap(DGameMap map) {
		this.unresolvedMaps.Add(map.name,map);
		map.transform.parent = this.transform;
	}
	
	public void LoadCruiser(Cruiser cruiser) {
		cruiser.SetMoon(this);
	}
	
	public IEnumerator Generate(
		DungeonFlowConverter tilegen, 
		Action<GameMap> onComplete=null
	) {
		if (this.ActiveMap != null) this.ActiveMap.gameObject.SetActive(false);
		this.ActiveMap = GetMap(tilegen.Flow);
		this.ActiveMap.gameObject.SetActive(true);
		
		this.ActiveMap.GenerationCompleteEvent += onComplete;
		this.ActiveMap.TileInsertionEvent += tilegen.FailedPlacementHandler;
		Plugin.LogInfo($"Generating tiles for {this.name}:{this.ActiveMap.name}:{tilegen.Flow.name}");
		yield return this.ActiveMap.GenerateCoroutine(tilegen);
		this.ActiveMap.TileInsertionEvent -= tilegen.FailedPlacementHandler;
		this.ActiveMap.GenerationCompleteEvent -= onComplete;
		
		this.RestoreMapObjects();
	}
	
	public void PreserveMapObjects() {
		Plugin.LogInfo("Hiding Map Objects!");
		this.ActiveMap?.PreserveMapObjects();
		
		foreach (var cruiser in Object.FindObjectsByType<Cruiser>(FindObjectsSortMode.None)) {
			cruiser.Preserve();
		}
	}
	
	public void PreserveBees() {
		if (!Config.Singleton.SaveHives) return;
		foreach (RedLocustBees bee in Object.FindObjectsByType<RedLocustBees>(FindObjectsSortMode.None)) {
			bee.hive.GetComponent<Beehive>().SaveBees(bee);
		}
	}
	
	public void RestoreMapObjects() {
		foreach (Transform child in this.transform) {
			MapObject mapObj = child.GetComponent<MapObject>();
			if (mapObj != null) {
				mapObj.Restore();
			} else {
				var cruiser = child.GetComponent<Cruiser>();
				if (cruiser != null) cruiser.Restore();
			}
		}
		this.ActiveMap.RestoreMapObjects();
	}
}

internal static class SaveManager {
	
	public static double SaveTimeTolerance = 20.0d;
	public static string NativeSaveDirectory {
		get => Application.persistentDataPath;
	}
	public static string ModSaveDirectory {
		get => $"{NativeSaveDirectory}/{Plugin.NAME}";
	}
	
	public static string CurrentSave {get => GameNetworkManager.Instance.currentSaveFileName;}
	public static string CurrentSavePath {get => $"{ModSaveDirectory}/{CurrentSave}.dat";}
	public static string CurrentSavePathNative {
		get => $"{NativeSaveDirectory}/{GameNetworkManager.Instance.currentSaveFileName}";
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
		
		// delete history, if it exists
		file = new FileInfo($"{ModSaveDirectory}/{saveName.Substring(0,saveName.Length-4)}History.log");
		if (!file.Exists) return;
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
	public override void Serialize(SerializationContext sc, MapHandler o) {
		if (!ReferenceEquals(o,MapHandler.Instance)) {
			throw new ArgumentException($"MapHandler singleton violated");
		}
		var moons = MapHandler.Instance.GetComponentsInChildren<Moon>(true);
		sc.Add((ushort)(moons.Length));
		var serializer = new MoonSerializer();
		foreach (Moon moon in moons) {
			sc.AddInline(moon, serializer);
		}
	}
	
	protected override MapHandler Deserialize(MapHandler baseObj, DeserializationContext dc) {
		if (!ReferenceEquals(baseObj, MapHandler.Instance)) {
			Plugin.LogError($"Deserialzed instance is not MapHandler singleton!");
			((MapHandler)baseObj).GetComponent<NetworkObject>().Despawn(true);
		}
		dc.Consume(2).CastInto(out ushort numMoons);
		
		// Captures for lambda
		var rt = MapHandler.Instance;
		var moonDeserializer = new MoonSerializer();
		for (int i=0; i<numMoons; i++) {
			rt.LoadMoon((Moon)dc.ConsumeInline(moonDeserializer));
		}
		
		return rt;
	}
	public override MapHandler Deserialize(DeserializationContext dc) {
		return Deserialize(MapHandler.Instance,dc);
	}
}

public class MapHandlerNetworkSerializer : Serializer<MapHandler> {
	public override void Serialize(SerializationContext sc, MapHandler item) {
		Moon[] moons = item.GetComponentsInChildren<Moon>(true);
		sc.Add((ushort)moons.Length);
		var serializer = new MoonNetworkSerializer();
		foreach (Moon moon in moons) {
			sc.AddInline(moon,serializer);
		}
	}
	
	protected override MapHandler Deserialize(MapHandler tgt, DeserializationContext dc) {
		dc.Consume(sizeof(ushort)).CastInto(out ushort numMoons);
		var ds = new MoonNetworkSerializer();
		for (ushort i=0; i<numMoons; i++) {
			dc.ConsumeInline(ds);
		}
		return tgt;
	}
	
	public override MapHandler Deserialize(DeserializationContext dc) {
		return Deserialize(MapHandler.Instance,dc);
	}
}

public class MoonSerializer : Serializer<Moon> {
	private void SerializeMapObjects<T>(
		SerializationContext sc, 
		Moon moon, 
		Serializer<T> ser
	) where T : MapObject {
		List<T> objs = new(moon.transform.childCount);
		foreach (Transform child in moon.transform) {
			T item = child.GetComponent<T>();
			if (item != null) objs.Add(item);
		}
		sc.Add((ushort)objs.Count);
		foreach (T o in objs) {
			sc.AddInline(o, ser);
		}
	}
	
	public override void Serialize(SerializationContext sc, Moon moon) {
		sc.Add(moon.name + "\0");
		
		// Serialize MapObjects
		SerializeMapObjects<Scrap>(sc,moon,new ScrapSerializer(moon));
		SerializeMapObjects<Equipment>(sc,moon,new EquipmentSerializer(moon));
		
		// Serialize cruisers
		Cruiser[] cruisers = moon.GetComponentsInChildren<Cruiser>(true);
		sc.Add((ushort)cruisers.Length);
		var cruiserSerializer = new CruiserSerializer(moon);
		foreach (Cruiser cruiser in cruisers) {
			sc.AddInline(cruiser,cruiserSerializer);
		}
		
		// Serialize maps
		DGameMap[] maps = moon.GetComponentsInChildren<DGameMap>(true);
		sc.Add((ushort)maps.Length);
		var ser = new DGameMapSerializer();
		foreach (DGameMap map in maps) {
			sc.AddInline(map,ser);
		}
	}
	
	private void DeserializeMapObjects<T>(
		DeserializationContext dc, MapObjectSerializer<T> ds
	)
		where T : MapObject
	{
		dc.Consume(sizeof(ushort)).CastInto(out ushort count);
		if (DeserializationContext.Verbose) Plugin.LogDebug(
			$"Loading {count} {typeof(T)} objects for Moon '{ds.Parent.name}' from address 0x{dc.Address:X}"
		);
		
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(ds);
		}
	}
	
	protected override Moon Deserialize(Moon moon, DeserializationContext dc) {
		
		DeserializeMapObjects<Scrap    >(dc,new ScrapSerializer    (moon));
		DeserializeMapObjects<Equipment>(dc,new EquipmentSerializer(moon));
		
		dc.Consume(2).CastInto(out ushort numCruisers);
		var cruiserSerializer = new CruiserSerializer(moon);
		for (int i=0; i<numCruisers; i++) {
			dc.ConsumeInline(cruiserSerializer);
		}
		
		dc.Consume(2).CastInto(out ushort numMaps);
		var ser = new DGameMapSerializer();
		for (int i=0; i<numMaps; i++) {
			moon.LoadMap((DGameMap)dc.ConsumeInline(ser));
		}
		
		return moon;
	}
	
	public override Moon Deserialize(DeserializationContext dc) {
		dc.ConsumeUntil(
			(byte b) => (b == 0)
		).CastInto(out string id);
		dc.Consume(1); // null terminator
		
		Moon rt = new GameObject(id).AddComponent<Moon>();
		return Deserialize(rt, dc);
	}
	
	public override void Finalize(Moon moon) {
		moon.gameObject.SetActive(false);
	}
}

public class MoonNetworkSerializer : Serializer<Moon> {
	private void SerializeMapObjects<T>(
		SerializationContext sc, 
		Moon moon, 
		Serializer<T> ser
	) where T : MapObject {
		List<T> objs = new(moon.transform.childCount);
		foreach (Transform child in moon.transform) {
			T item = child.GetComponent<T>();
			if (item != null) objs.Add(item);
		}
		sc.Add((ushort)objs.Count);
		foreach (T o in objs) {
			sc.AddInline(o, ser);
		}
	}
	
	public override void Serialize(SerializationContext sc, Moon moon) {
		sc.Add(moon.name+"\0");
		
		// MapObjects
		SerializeMapObjects<Scrap    >(sc,moon, new ScrapNetworkSerializer(moon));
		SerializeMapObjects<Equipment>(sc,moon, new EquipmentNetworkSerializer(moon));
		
		// Cruisers
		Cruiser[] cruisers = moon.GetComponentsInChildren<Cruiser>(true);
		var ser = new CruiserNetworkSerializer(moon);
		sc.Add((ushort)cruisers.Length);
		foreach (var cruiser in cruisers) {
			sc.AddInline(cruiser, ser);
		}
		
		// Maps
		var mapSer = new DGameMapNetworkSerializer(null);
		DGameMap[] maps = moon.GetComponentsInChildren<DGameMap>(true);
		sc.Add((ushort)maps.Length);
		foreach (DGameMap map in maps) {
			sc.AddInline(map,mapSer);
		}
	}
	
	private void DeserializeMapObjects<T>(
		DeserializationContext dc, MapObjectNetworkSerializer<T> ds
	)
		where T : MapObject
	{
		dc.Consume(sizeof(ushort)).CastInto(out ushort count);
		if (DeserializationContext.Verbose) Plugin.LogDebug(
			$"Loading {count} {typeof(T)} objects for Moon '{ds.Parent.name}' from address 0x{dc.Address:X}"
		);
		
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(ds);
		}
	}
	
	protected override Moon Deserialize(Moon moon, DeserializationContext dc) {
		// MapObjects
		DeserializeMapObjects<Scrap    >(dc, new ScrapNetworkSerializer    (moon));
		DeserializeMapObjects<Equipment>(dc, new EquipmentNetworkSerializer(moon));
		
		// Cruisers
		dc.Consume(2).CastInto(out ushort numCruisers);
		if (DeserializationContext.Verbose) Plugin.LogDebug(
			$"Loading {numCruisers} cruisers for {moon.name} from address 0x{dc.Address:X}"
		);
		var cruiserSerializer = new CruiserNetworkSerializer(moon);
		for (ushort i=0; i<numCruisers; i++) {
			dc.ConsumeInline(cruiserSerializer);
		}
		
		// DGameMaps
		dc.Consume(2).CastInto(out ushort numMaps);
		var ds = new DGameMapNetworkSerializer(moon);
		for (ushort i=0; i<numMaps; i++) {
			dc.ConsumeInline(ds);
		}
		
		return moon;
	}
	
	public override Moon Deserialize(DeserializationContext dc) {
		dc.ConsumeUntil(
			(byte b) => (b == 0)
		).CastInto(out string id);
		dc.Consume(1); // null terminator
		
		Moon moon = MapHandler.Instance.transform.Find(id).GetComponent<Moon>();
		if (moon == null) {
			Plugin.LogError($"Couldn't find moon '{id}'");
			return null;
		}
		
		return Deserialize(moon, dc);
	}
	
	public override void Finalize(Moon moon) {
		moon.gameObject.SetActive(false);
	}
}