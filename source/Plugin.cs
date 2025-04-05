namespace LabyrinthianFacilities;

using Serialization;
using Util;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

using DunGen.Graph;

// UnityEngine + System ambiguity
using Random = System.Random;
using Object = UnityEngine.Object;

// BepInEx + Netcode ambiguity
using LogLevel = BepInEx.Logging.LogLevel;

[BepInPlugin(Plugin.GUID, Plugin.NAME, Plugin.VERSION)]
[BepInIncompatibility("Zaggy1024.PathfindingLib")]
[BepInDependency("GGMD.GenericInteriors", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin {
	public const string GUID = "mitzapper2.LethalCompany.LabyrinthianFacilities";
	public const string NAME = "LabyrinthianFacilities";
	public const string VERSION = "0.7.0";
	
	private readonly Harmony harmony = new Harmony(GUID);
	private static new ManualLogSource Logger;
	private new Config Config {get => LabyrinthianFacilities.Config.Singleton;}
	
	private static bool initializedAssets = false;
	
	// for internal use, makes it so I can see my own debug/info logs without seeing everyone else's
	private static uint PROMOTE_LOG = 0;
	public static bool TreatWarningsAsErrors = false;
	
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
		LabyrinthianFacilities.Config.ConfigFile = base.Config;
		var cfg = LabyrinthianFacilities.Config.Singleton;
		Logger = base.Logger;
		
		if (!cfg.GlobalEnable) {
			LogInfo("{NAME} is disabled by its config; Skipping initialization");
			return;
		}
		DeserializationContext.Verbose = cfg.EnableVerboseDeserialization;
		SerializationContext.Verbose = cfg.EnableVerboseSerialization;
		
		SceneManager.sceneLoaded += (Scene scene, LoadSceneMode mode) => {
			if (scene.name == "SampleSceneRelay") InitializeAssets();
		};
		
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
	
	private static bool PluginLoaded(string guid) => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(guid);
	
	public static void LogDebug  (string message) {
		if (PROMOTE_LOG > 0) {
			LogInfo(message);
			return;
		}
		if ((Config.Singleton.LogLevels & LogLevel.Debug) != 0) Logger.LogDebug(message);
	}
	public static void LogInfo   (string message) {
		if (PROMOTE_LOG > 1) {
			LogMessage(message);
			return;
		}
		if ((Config.Singleton.LogLevels & LogLevel.Info) != 0) Logger.LogInfo(message);
	}
	public static void LogMessage(string message) {
		if (PROMOTE_LOG > 2) {
			LogWarning(message);
			return;
		}
		if ((Config.Singleton.LogLevels & LogLevel.Message) != 0) Logger.LogMessage(message);
	}
	public static void LogWarning(string message) {
		if (PROMOTE_LOG > 3 || TreatWarningsAsErrors) {
			LogError(message);
			return;
		}
		if ((Config.Singleton.LogLevels & LogLevel.Warning) != 0) Logger.LogWarning(message);
	}
	public static void LogError  (string message) {
		if (PROMOTE_LOG > 4) {
			LogFatal(message);
			return;
		}
		if ((Config.Singleton.LogLevels & LogLevel.Error) != 0) Logger.LogError(message);
	}
	public static void LogFatal  (string message) {
		if (PROMOTE_LOG > 5) return;
		if ((Config.Singleton.LogLevels & LogLevel.Fatal) != 0) Logger.LogFatal(message);
	}
	
	public static void LogException(Exception ex) => LogError($"{ex}");
	
	public static void InitializeAssets() {
		if (initializedAssets) return;
		initializedAssets = true;
		
		Plugin.LogInfo($"Initializing Miscellaneous Items");
		foreach (GameObject item in Resources.FindObjectsOfTypeAll(typeof(GameObject))) {
			if (item.name == "TunnelDeadEndBlockerB") {
				BoxCollider collider = item.transform.Find("Cube").GetComponent<BoxCollider>();
				
				Vector3 vec = collider.center;
				vec.y = 0;
				collider.center = vec;
				
				vec = collider.size;
				vec.y = 0;
				collider.size = vec;
				
				break;
			}
		}
		
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
			
			foreach (NetworkObject netObj in tile.GetComponentsInChildren<NetworkObject>(true)) {
				netObj.AutoObjectParentSync = false;
			}
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
				} else if (grabbable is ShotgunItem) {
					grabbable.gameObject.AddComponent<GunEquipment>();
				} else {
					grabbable.gameObject.AddComponent<Scrap>();
				}
			} else if (
				// exclude maneater (and hopefully catch custom enemies with a similar gimmick)
				grabbable.GetComponent<EnemyAI>() == null 
			) {
				if (grabbable.itemProperties.requiresBattery) {
					grabbable.gameObject.AddComponent<BatteryEquipment>();
				} else if (grabbable is SprayPaintItem || grabbable is TetraChemicalItem) {
					grabbable.gameObject.AddComponent<FueledEquipment>();
				} else {
					grabbable.gameObject.AddComponent<Equipment>();
				}
			}
		}
	
		Plugin.LogInfo("Initializing Company Cruiser");
		foreach (VehicleController vc in Resources.FindObjectsOfTypeAll(typeof(VehicleController))) {
			if (vc.name == "CompanyCruiser") {
				vc.gameObject.AddComponent<Cruiser>();
			} else {
				Plugin.LogWarning(
					$"Did not recognize vehicle '{vc.name}'. This vehicle will be ignored by {Plugin.NAME}"
				);
			}
		}
	
		Plugin.LogInfo("Initializing Hazards");
		Plugin.LogDebug("Initializing Turrets");
		foreach (Turret t in Resources.FindObjectsOfTypeAll<Turret>()) {
			t.transform.parent.gameObject.AddComponent<TurretHazard>()
				.GetComponent<NetworkObject>().AutoObjectParentSync = false;
		}
		Plugin.LogDebug("Initializing Landmines");
		foreach (Landmine t in Resources.FindObjectsOfTypeAll<Landmine>()) {
			t.transform.parent.gameObject.AddComponent<LandmineHazard>()
				.GetComponent<NetworkObject>().AutoObjectParentSync = false;
		}
		Plugin.LogDebug("Initializing Spike Traps");
		foreach (SpikeRoofTrap t in Resources.FindObjectsOfTypeAll<SpikeRoofTrap>()) {
			t.transform.parent.parent.parent.gameObject.AddComponent<SpikeTrapHazard>()
				.GetComponent<NetworkObject>().AutoObjectParentSync = false;
		}
		
		if (PluginLoaded("GGMD.GenericInteriors")) Compatibility.GenericInteriors.Initialize();
	}
	
}

public class MapHandler : NetworkBehaviour {
	public static MapHandler Instance {get; private set;}
	internal static GameObject prefab = null;
	
	public static event Action<Moon> OnNewMoon;
	
	private Dictionary<string,byte[]> serializedMoons = null;
	
	public Moon ActiveMoon {get; private set;}
	public ReadOnlyDictionary<string,byte[]> SerializedMoons {get; private set;}
	
	public override void OnNetworkSpawn() {
		if (Instance != null) {
			this.GetComponent<NetworkObject>().Despawn(true);
			return;
		}
		Instance = this;
		
		NetworkManager.OnClientStopped += MapHandler.OnDisconnect;
		if (IsServer) NetworkManager.OnClientConnectedCallback += OnConnect;
		serializedMoons = new();
		SerializedMoons = new(serializedMoons);
		Plugin.InitializeAssets();
		
		if (this.IsServer) {
			LoadGame();
		} else {
			MapHandler.Instance.RequestConfigServerRpc(NetworkManager.Singleton.LocalClientId);
			// clear old history
			if (Config.Singleton.EnableHistory) {
				FileInfo file = new FileInfo($"{SaveManager.ModSaveDirectory}/serverHistory.log");
				if (file.Exists) file.Delete();
			}
		}
		
	}
	public override void OnNetworkDespawn() {
		if (IsServer) NetworkManager.OnClientConnectedCallback -= OnConnect;
		MapHandler.Instance = null;
		GameObject.Destroy(this.gameObject);
	}
	
	// Ensure that late joiners (like via LateCompany) don't have a desynced moon
	public void OnConnect(ulong clientId) => ClearActiveMoonClientRpc();
	
	public static void OnDisconnect(bool isHost) {
		Plugin.LogInfo($"Disconnecting: Destroying local instance of MapHandler");
		
		// Dont need to include cruisers not parented to MapHandler because they are in active play, 
		// and not being despawned
		foreach (Cruiser cruiser in MapHandler.Instance.GetComponentsInChildren<Cruiser>(true)) {
			cruiser.DoneWithOldCruiserServerRpc(disconnect: true);
		}
		
		// Reset config (in case client had to change its config to sync with server)
		Config.Singleton.InitFromConfigFile();
		
		Instance.NetworkManager.OnClientStopped -= MapHandler.OnDisconnect;
		Instance.OnNetworkDespawn();
		
	}
	
	[ClientRpc]
	public void ClearActiveMoonClientRpc() => ClearActiveMoon();
	public void ClearActiveMoon() => StartCoroutine(ChangeActiveMoon(null));
	
	public void SaveMoon(Moon moon=null) {
		moon ??= this.ActiveMoon;
		if (moon == null) return;
		
		var ser = new SerializationContext();
		ser.Serialize(moon,new MoonSerializer());
		byte[] b = new byte[ser.Output.Count];
		ser.Output.CopyTo(b,0);
		this.serializedMoons[moon.name] = b;
	}
	
	// (When actually changing moons)
	// 1. SERVER - Save & Despawn/Destroy current moon
	//    CLIENT - Destroy current moon & orphan network objects
	// 
	// 2. SERVER - Load new moon from memory & send + network serialization
	//    CLIENT - nothing
	// 
	// 3. SERVER - done
	//    CLIENT - Load new moon from server (2x Deserialization)
	public IEnumerator ChangeActiveMoon(SelectableLevel level) {
		if ( // exit early if no change
			this.ActiveMoon == null && level == null
			|| this.ActiveMoon?.name == $"moon:{level?.name}"
		) yield break;
		
		// Destroy old moon
		if (this.ActiveMoon != null) {
			if (IsServer) SaveMoon();
			foreach (NetworkObject netobj in this.ActiveMoon?.GetComponentsInChildren<NetworkObject>(true)) {
				if (netobj.IsSpawned) {
					if (IsServer) {
						netobj.Despawn(true);
					} else {
						netobj.transform.parent = null;
					}
				} else {
					GameObject.Destroy(netobj.gameObject);
				}
			}
			GameObject.Destroy(this.ActiveMoon.gameObject);
		}
		this.ActiveMoon = null;
		
		if (level == null) yield break;
		string moonName = $"moon:{level.name}";
		if (!IsServer) {
			while (this.ActiveMoon?.name != moonName) yield return new WaitForSeconds(0.125f);
			yield break;
		}
		// Below is server-only
		
		// Load new moon
		Moon moon;
		if (!this.serializedMoons.TryGetValue(moonName, out byte[] moonData)) {
			moon = NewMoon(moonName);
			SaveMoon(moon);
			moonData = this.serializedMoons[moonName];
		} else {
			DeserializationContext dc = new(moonData);
			moon = (Moon)dc.Deserialize(new MoonSerializer());
			moon.transform.parent = this.transform;
			yield return new WaitForSeconds(3f);
		}
		
		SerializationContext sc = new();
		sc.Serialize(moon,new MoonNetworkSerializer());
		byte[] netData = new byte[sc.Output.Count];
		sc.Output.CopyTo(netData,0);
		
		this.ActiveMoon = moon;
		
		ChangeActiveMoonClientRpc(moonData,netData);
		yield break;
	}
	
	[ClientRpc]
	private void ChangeActiveMoonClientRpc(byte[] serializedMoon, byte[] netSerializedMoon) {
		if (IsServer) return;
		StartCoroutine(ChangeActiveMoonClientRoutine(serializedMoon,netSerializedMoon));
	}
	private IEnumerator ChangeActiveMoonClientRoutine(byte[] serializedMoon, byte[] netSerializedMoon) {
		// Await current moon destruction
		while (this.ActiveMoon != null) yield return null;
		
		((Moon)(
			new DeserializationContext(serializedMoon).Deserialize(new MoonSerializer())
		)).transform.parent = MapHandler.Instance.transform;
		this.ActiveMoon = (Moon)(
			new DeserializationContext(netSerializedMoon).Deserialize(new MoonNetworkSerializer())
		);
		yield break;
	}
	
	public Moon NewMoon(string name) {
		GameObject g = new GameObject(name);
		g.transform.parent = this.transform;
		Moon rt = g.AddComponent<Moon>();
		OnNewMoon?.Invoke(rt);
		return rt;
	}
	
	public IEnumerator Generate(
		SelectableLevel level, 
		DungeonFlowConverter tilegen, 
		Action<GameMap> onComplete=null
	) {
		StartCoroutine(ChangeActiveMoon(level));
		// await ChangeActiveMoon
		while (this.ActiveMoon?.name != $"moon:{level.name}") yield return new WaitForSeconds(0.125f);
		
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
	
	public void PreserveEarlyObjects() {
		this.ActiveMoon?.PreserveEarlyObjects();
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
	public void LoadMoon(byte[] data) {
		int len = 0;
		for (int i=0; i<data.Length; i++) {
			if (data[i] == 0) {
				len = i;
				break;
			}
		}
		string name = System.Text.Encoding.UTF8.GetString(data,0,len);
		this.serializedMoons[name] = data;
	}
	
	public void Clear() {
		if (!base.IsServer) return;
		this.GetComponent<NetworkObject>().Despawn();
		SaveManager.DeleteFile($"{SaveManager.CurrentSave}.dat",true);
		GameObject.Instantiate(MapHandler.prefab).GetComponent<NetworkObject>().Spawn();
	}
	
	[ServerRpc(RequireOwnership=false)]
	public void RequestMapDataServerRpc(ulong clientId) {
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
	}
	
	[ServerRpc(RequireOwnership=false)]
	protected void RequestConfigServerRpc(ulong clientId) {
		var cparams = new ClientRpcParams {
			Send = new ClientRpcSendParams {
				TargetClientIds = new ulong[1]{clientId}
			}
		};
		Plugin.LogInfo($"Received request for config from client #{clientId}");
		var s = new SerializationContext();
		s.Serialize(Config.Singleton,new ConfigNetworkSerializer<Config>());
		Plugin.LogInfo($"{s.Output.Count} bytes of config");
		byte[] b = new byte[s.Output.Count];
		s.Output.CopyTo(b,0);
		SendConfigClientRpc(b,cparams);
	}
	
	[ClientRpc]
	protected void SendConfigClientRpc(byte[] bytes, ClientRpcParams cparams=default) {
		Plugin.LogInfo($"Received config from server! ({bytes.Length} bytes)");
		var rt = new DeserializationContext(bytes).Deserialize(new ConfigNetworkSerializer<Config>());
		if (!ReferenceEquals(rt,Config.Singleton)) Plugin.LogError($"Got a different config than singleton?");
	}
	
	private void RecordDay(int modSeed) {
		string savename = this.IsServer ? SaveManager.CurrentSave : "server";
		using (FileStream fs = File.Open(
			$"{SaveManager.ModSaveDirectory}/{savename}History.log", FileMode.Append
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
	
	public IEnumerator EnableBouncyCruisers() {
		Cruiser[] cruisers = Object.FindObjectsByType<Cruiser>(FindObjectsSortMode.None);
		foreach (Cruiser c in cruisers) {
			c.gameObject.SetActive(false);
			c.transform.position += Vector3.down;
		}
		yield return new WaitForSeconds(1f);
		foreach (Cruiser c in cruisers) {
			c.gameObject.SetActive(true);
		}
	}
}

public class Moon : MonoBehaviour {
	
	public static event Action<DGameMap> OnNewMap;
	
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
			OnNewMap?.Invoke(map);
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
		if (map == null) throw new NullReferenceException("map");
		this.unresolvedMaps.Add(map.name,map);
		map.transform.parent = this.transform;
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
		Plugin.LogInfo("Preserving Map Objects!");
		this.ActiveMap?.PreserveMapObjects();
	}
	
	public void PreserveEarlyObjects() {
		Plugin.LogInfo("(Early Stage) Preserving Map Objects");
		if (!Config.Singleton.SaveMaps && Config.Singleton.UseCustomGeneration) {
			GameObject.Destroy(ActiveMap.RootTile);
		}
		
		// Hazards are early because they are in the level's scene, not SampleSceneRelay. 
		// This scene is unloaded between UnloadSceneObjectsEarly and DespawnPropsAtEndOfRound
		// Config options are handled within Preserve function
		foreach (HazardBase obj in Object.FindObjectsByType<HazardBase>(FindObjectsSortMode.None)) {
			obj.Preserve();
		}
		
		if (Config.Singleton.SaveHives) {
			foreach (RedLocustBees bee in Object.FindObjectsByType<RedLocustBees>(FindObjectsSortMode.None)) {
				bee.hive.GetComponent<Beehive>().SaveBees(bee);
			}
		}
		if (Config.Singleton.SaveCruisers) {
			foreach (Cruiser cruiser in Object.FindObjectsByType<Cruiser>(FindObjectsSortMode.None)) {
				cruiser.Preserve();
			}
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
		
		// rename history, if it exists
		file = new FileInfo($"{ModSaveDirectory}/{oldName.Substring(0,oldName.Length-4)}History.log");
		if (!file.Exists) return;
		try {
			file.MoveTo($"{ModSaveDirectory}/{newName.Substring(0,newName.Length-4)}History.log");
		} catch (Exception ex) {
			Plugin.LogError($"Error when renaming file history {oldName} to {newName}: \n{ex.Message}");
		}
	}
	
	public static void DeleteFile(string saveName, bool isGameOver=false) {
		if (saveName.Contains("..")) throw new IOException("Miss me with that shit");
		var file = new FileInfo($"{ModSaveDirectory}/{saveName}");
		if (!file.Exists) {
			Plugin.LogInfo($"Could not delete file {saveName}; file does not exist. ");
			return;
		}
		Plugin.LogInfo($"Deleting save '{saveName}'!");
		file.Delete();
		
		// delete history, if it exists
		if (isGameOver) return;
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
				if (
					modsave.Name != "serverHistory.log"
					&& modsave.Name.Substring(11) != "History.log"
				) {
					Plugin.LogWarning(
						$"{typeof(SaveManager)} does not know how to sync save '{modsave.Name}'"
					);
				}
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
		
		o.SaveMoon();
		
		sc.Add((ushort)o.SerializedMoons.Count);
		foreach (var kvpair in o.SerializedMoons) {
			sc.Add((int)kvpair.Value.Length);
			sc.Add(kvpair.Value);
		}
	}
	
	protected override MapHandler Deserialize(MapHandler baseObj, DeserializationContext dc) {
		if (!ReferenceEquals(baseObj, MapHandler.Instance)) {
			Plugin.LogError($"Deserialzed instance is not MapHandler singleton!");
			((MapHandler)baseObj).GetComponent<NetworkObject>().Despawn(true);
		}
		dc.Consume(2).CastInto(out ushort numMoons);
		
		var rt = MapHandler.Instance;
		for (int i=0; i<numMoons; i++) {
			int startAddress = dc.Address;
			
			dc.Consume(sizeof(int)).CastInto(out int len);
			rt.LoadMoon(dc.Consume(len));
			
			if (DeserializationContext.Verbose) {
				Plugin.LogDebug($"moon #{i}: 0x{startAddress:X}-0x{dc.Address:X}");
			}
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
	
	private bool Generate;
	public MoonSerializer(bool generate=true) {this.Generate = generate;}
	
	
	public override void Serialize(SerializationContext sc, Moon moon) {
		sc.Add(moon.name + "\0");
		
		// Serialize MapObjects
		List<GrabbableMapObject> mapObjects = new(moon.transform.childCount);
		foreach (Transform child in moon.transform) {
			GrabbableMapObject item = child.GetComponent<GrabbableMapObject>();
			if (item != null) mapObjects.Add(item);
		}
		new MapObjectCollection(mapObjects).Serialize(
			sc,
			new ScrapSerializer           <Scrap           >(moon),
			new EquipmentSerializer       <Equipment       >(moon),
			new BatteryEquipmentSerializer<BatteryEquipment>(moon),
			new GunEquipmentSerializer    <GunEquipment    >(moon),
			new BatteryEquipmentSerializer<FueledEquipment >(moon)
		);
		
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
	
	protected override Moon Deserialize(Moon moon, DeserializationContext dc) {
		
		MapObjectCollection.Deserialize(
			dc,
			new ScrapSerializer           <Scrap>           (moon),
			new EquipmentSerializer       <Equipment>       (moon),
			new BatteryEquipmentSerializer<BatteryEquipment>(moon),
			new GunEquipmentSerializer    <GunEquipment>    (moon),
			new BatteryEquipmentSerializer<FueledEquipment> (moon)
		);
		
		dc.Consume(2).CastInto(out ushort numCruisers);
		if (DeserializationContext.Verbose) {
			Plugin.LogDebug($"{moon?.name ?? "null"}: Found {numCruisers} cruisers");
		}
		var cruiserSerializer = new CruiserSerializer(moon);
		for (int i=0; i<numCruisers; i++) {
			dc.ConsumeInline(cruiserSerializer);
		}
		
		dc.Consume(2).CastInto(out ushort numMaps);
		var ser = new DGameMapSerializer(moon);
		if (DeserializationContext.Verbose) {
			Plugin.LogDebug($"{moon?.name ?? "null"}: Found {numMaps} maps");
		}
		for (int i=0; i<numMaps; i++) {
			DGameMap map = (DGameMap)dc.ConsumeInline(ser);
			moon?.LoadMap(map);
		}
		return moon;
	}
	
	public override Moon Deserialize(DeserializationContext dc) {
		dc.ConsumeUntil(
			(byte b) => (b == 0)
		).CastInto(out string id);
		dc.Consume(1); // null terminator
		
		Moon rt = Generate ? (new GameObject(id).AddComponent<Moon>()) : null;
		return Deserialize(rt, dc);
	}
}

public class MoonNetworkSerializer : Serializer<Moon> {
	
	public override void Serialize(SerializationContext sc, Moon moon) {
		sc.Add(moon.name+"\0");
		
		// MapObjects
		List<GrabbableMapObject> mapObjects = new(moon.transform.childCount);
		foreach (Transform child in moon.transform) {
			GrabbableMapObject item = child.GetComponent<GrabbableMapObject>();
			if (item != null) mapObjects.Add(item);
		}
		new MapObjectCollection(mapObjects).Serialize(
			sc,
			new ScrapNetworkSerializer             <Scrap>           (moon),
			new GrabbableMapObjectNetworkSerializer<Equipment>       (moon),
			new BatteryEquipmentNetworkSerializer  <BatteryEquipment>(moon),
			new GunEquipmentNetworkSerializer      <GunEquipment>    (moon),
			new BatteryEquipmentNetworkSerializer  <FueledEquipment> (moon)
		);
		
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
	
	protected override Moon Deserialize(Moon moon, DeserializationContext dc) {
		// MapObjects
		MapObjectCollection.Deserialize(
			dc,
			new ScrapNetworkSerializer             <Scrap>           (moon),
			new GrabbableMapObjectNetworkSerializer<Equipment>       (moon),
			new BatteryEquipmentNetworkSerializer  <BatteryEquipment>(moon),
			new GunEquipmentNetworkSerializer      <GunEquipment>    (moon),
			new BatteryEquipmentNetworkSerializer  <FueledEquipment> (moon)
		);
		
		// Cruisers
		dc.Consume(2).CastInto(out ushort numCruisers);
		if (DeserializationContext.Verbose) Plugin.LogDebug(
			$"Loading {numCruisers} cruisers for {moon?.name} from address 0x{dc.Address:X}"
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
		
		Moon moon = MapHandler.Instance.transform.Find(id)?.GetComponent<Moon>();
		if (moon == null) {
			Plugin.LogError($"Couldn't find moon '{id ?? "null"}'");
			return null;
		}
		
		return Deserialize(moon, dc);
	}
}