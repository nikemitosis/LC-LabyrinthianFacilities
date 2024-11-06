namespace LabyrinthianFacilities;

using BoundsExtensions;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using Unity.Netcode;

using DunGen.Graph;

static class DunGenConverter {
	
	public static void InstantiationHandler(Tile t) {
		t.InitFromDunGen(t.gameObject.GetComponent<DunGen.Tile>());
	}
	
	public static void InitFromDunGen(this Tile ths, DunGen.Tile dg) {
		//subject to change, because why have consistency with what is the actual mesh of the room
		//ajfshdlfjqew
		Bounds bounds = new Bounds(Vector3.zero,Vector3.zero);
		// manor tiles all use mesh
		// factory (typically) uses variety of these 3 (belt room is weird af)
		Collider collider = (
			ths.transform.Find("mesh") 
			?? ths.transform.Find("Mesh") 
			?? ths.transform.Find("Wall") 
		)?.GetComponent<MeshCollider>();
		
		if (collider == null) {
			Plugin.LogWarning($"Unable to find easy meshcollider for {ths}");
			
			var colliders = ths.transform.Find("Meshes")?.GetComponentsInChildren<MeshCollider>();
			foreach (Collider c in colliders ?? (Collider[])[]) {
				bounds.Encapsulate(c.bounds);
			}
			
			if (bounds.extents == Vector3.zero) {
				// cave tiles all have first meshcollider as room bounds (I think)
				collider = (
					ths.GetComponentInChildren<MeshCollider>()
					?? ths.GetComponentInChildren<Collider>()
				);
				Plugin.LogInfo($"Using first collider found: {collider}");
				if (collider == null) {
					Plugin.LogError($"Could not find a collider to infer bounds for tile {ths}");
				}
			}
		}
		if (bounds.extents == Vector3.zero && collider != null) bounds = collider.bounds;
		
		Plugin.LogDebug($"{ths.gameObject.name} extents: {bounds.extents}");
		ths.Initialize(bounds);
		if (bounds.size == Vector3.zero) {
			Plugin.LogError(
				$"Tile '{ths}' has zero-size. Tile will allow others to encroach on its area."
			);
		} else {
			foreach (Doorway d in ths.Doorways) {
				d.FixRotation();
			}
		}
	}
	
	public static void InitFromDunGen(this Doorway ths, DunGen.Doorway dg) {
		ths.Size = dg.Socket.Size;
		return;
	}
	
	// public static void InitFromDunGen(this Door ths, DunGen.Door dg) {
		// return;
	// }
}

public abstract class TileGenerator {
	public abstract IEnumerable<Tile> Generator(GameMap map);
	
	public virtual void FailedPlacementHandler(GameMap map,Tile tile) {
		return;
	}
}

public class DungeonFlowConverter : TileGenerator {
	private DunGen.Graph.DungeonFlow flow;
	private uint tile_demand;
	
	public override void FailedPlacementHandler(GameMap map,Tile tile) {
		this.tile_demand++;
	}
	
	public DungeonFlowConverter(DungeonFlow flow) {
		this.flow = flow;
		this.tile_demand = 30;
	}
	
	public override IEnumerable<Tile> Generator(GameMap map) {
		Tile start = flow?.Lines[0]
			?.DungeonArchetypes[0]
			?.TileSets[0]
			?.TileWeights
			?.Weights[0]
			?.Value
			?.GetComponent<Tile>();
		
		if (start == null) {
			Plugin.LogError("Start tile not found D:");
			yield break;
		}
		Plugin.LogInfo($"{this.tile_demand}: Using '{start.gameObject.name}' as start room");
		
		yield return start;
		this.tile_demand--;
		
		Tile rt;
		while (tile_demand > 0) {
			var lines = flow?.Lines;
			var archetypes = lines?[map.rng.Next(lines.Count)]?.DungeonArchetypes;
			var tilesets = archetypes?[map.rng.Next(archetypes.Count)].TileSets;
			var weights = tilesets?[map.rng.Next(tilesets.Count)]?.TileWeights?.Weights;
			rt = weights[map.rng.Next(weights.Count)]?.Value?.GetComponent<Tile>();
			if (rt != start) {
				Plugin.LogInfo($"{this.tile_demand}: Yielding '{rt.gameObject.name}'");
				this.tile_demand--;
				yield return rt;
			}
		}
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
	
	public static void TileInsertionFail(GameMap gm, Tile t) {
		Plugin.LogError($"Failed to place tile {t}");
	}
	
	public void NewMap(SelectableLevel moon, TileGenerator tilegen, int? seed=null) {
		GameObject newmapobj;
		GameMap newmap;
		if (maps.TryGetValue(moon,out newmap)) {
			Plugin.LogWarning(
				"Attempted to generate new moon on moon that was already generated"
			);
			return;
		}
		Plugin.LogInfo($"Generating new moon for {moon.name}!");
		newmapobj = new GameObject($"map:{moon.name}");
		newmapobj.transform.SetParent(this.gameObject.transform);
		newmapobj.transform.position -= Vector3.up * 200.0f;
		
		newmap = newmapobj.AddComponent<GameMap>();
		maps.Add(moon,newmap);
		newmap.TileInsertionFailEvent += MapHandler.TileInsertionFail;
		newmap.TileInsertionFailEvent += tilegen.FailedPlacementHandler;
		StartCoroutine(newmap.GenerateCoroutine(tilegen.Generator,seed));
	}
}