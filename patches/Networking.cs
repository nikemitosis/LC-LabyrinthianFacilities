namespace LabyrinthianFacilities.Patches;
using HarmonyLib;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

using Object = UnityEngine.Object;
using Tile = LabyrinthianFacilities.Tile;

[HarmonyPatch(typeof(GameNetworkManager))]
public class PrefabInitializer {
	[HarmonyPostfix]
	[HarmonyPatch("Start")]
	public static void InitMapHandlerPrefab() {
		try {
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
		} catch (Exception e) {
			Plugin.LogError(e.Message);
			throw;
		}
	}
}

[HarmonyPatch(typeof(StartOfRound))]
public class PrefabSpawner {
	[HarmonyPostfix]
	[HarmonyPatch("Start")]
	public static void SpawnMapHandler() {
		try {
			if (MapHandler.Instance != null) return;
			GameObject.Instantiate(MapHandler.prefab).GetComponent<NetworkObject>().Spawn();
		} catch (Exception e) {
			Plugin.LogError(e.Message);
			throw;
		}
	}
}

[HarmonyPatch(typeof(StartOfRound))]
class SendMapsToClientPatch {
	[HarmonyPatch("OnClientConnect")]
	[HarmonyPrefix]
	public static void SendMaps(ulong clientId) {
		try {
			MapHandler.Instance.SendMapDataToClient(clientId);
		} catch (Exception e) {
			Plugin.LogError(e.Message);
			throw;
		}
	}
}