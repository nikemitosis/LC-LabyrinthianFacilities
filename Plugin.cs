using System.Collections.Generic;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

namespace LabyrinthianFacilities;

[BepInPlugin(Plugin.GUID, Plugin.NAME, Plugin.VERSION)]
public class Plugin : BaseUnityPlugin {
	public const string GUID = "mitzapper2.lethal_company.labyrinth";
	public const string NAME = "LabyrinthianFacilities";
	public const string VERSION = "1.0.0";
	
	internal static bool local_fatal_error = false;
	internal static GameObject rootObject;
	
	public static Dictionary<int,GameObject> SavedDungeons {
		get; 
		internal set;
	} = new Dictionary<int,GameObject>();
	
	private readonly Harmony harmony = new Harmony(GUID);
	internal static new ManualLogSource Logger;
	
	private void Awake() {
		Logger = base.Logger;
		harmony.PatchAll();
		
		rootObject = newNetworkObject(Plugin.NAME);
		
		Logger.LogInfo($"Plugin {Plugin.GUID} is Awoken!");
	}
	
	public static GameObject newNetworkObject(string name,bool active=true) {
		var o = new GameObject(name);
		o.hideFlags = HideFlags.DontSave;
		o.SetActive(active);
		o.AddComponent<NetworkObject>();
		return o;
	}
	
	public static Scene GetLevelScene() {
		string name = RoundManager.Instance.currentLevel.sceneName;
		Scene scene = SceneManager.GetSceneByName(name);
		if (!scene.IsValid()) Logger.LogError("Could not find level scene! ");
		return scene;
	}
	
	public static SelectableLevel GetLevel() {
		return RoundManager.Instance.currentLevel;
	}
	
	public static GameObject GetSavedDungeon() {
		GameObject rt = null;
		SavedDungeons.TryGetValue(Plugin.GetLevel().levelID, out rt);
		return rt;
	}
	
	public static void SaveDungeon(GameObject dungeon) {
		GameObject savedDungeon = GetSavedDungeon();
		if (savedDungeon == null) {
			Plugin.SavedDungeons.Add(GetLevel().levelID,dungeon);
			return;
		}
		
		foreach (Transform child in dungeon.transform) {
			child.SetParent(savedDungeon.transform,true);
		}
		Plugin.SavedDungeons[GetLevel().levelID] = dungeon;
		savedDungeon.transform.SetParent(rootObject.transform,true);
	}
}