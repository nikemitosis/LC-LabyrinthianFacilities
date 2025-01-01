namespace LabyrinthianFacilities.Patches;
using HarmonyLib;

using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;


using Tile = LabyrinthianFacilities.Tile;

[HarmonyPatch(typeof(GameNetworkManager))]
public class PrefabInitializer {
	[HarmonyPostfix]
	[HarmonyPatch("Start")]
	public static void InitMapHandlerPrefab() {
		if (MapHandler.prefab != null) return;
		
		var prefab = new GameObject($"{Plugin.NAME}::MapHandler");
		var netobj = prefab.AddComponent<NetworkObject>();
		
		netobj.ActiveSceneSynchronization = false;
		netobj.AlwaysReplicateAsRoot = false;
		netobj.AutoObjectParentSync = true;
		netobj.DontDestroyWithOwner = false;
		netobj.SynchronizeTransform = false;
		netobj.DestroyWithScene = true;
		
		prefab.AddComponent<MapHandler>();
		prefab.hideFlags = HideFlags.HideAndDontSave;
		
		NetworkManager.Singleton.AddNetworkPrefab(prefab);
		MapHandler.prefab = prefab;
	}
}

[HarmonyPatch(typeof(StartOfRound))]
public class PrefabSpawner {
	[HarmonyPostfix]
	[HarmonyPatch("Start")]
	public static void SpawnMapHandler() {
		if (MapHandler.Instance != null) return;
		GameObject.Instantiate(MapHandler.prefab).GetComponent<NetworkObject>().Spawn();
	}
}