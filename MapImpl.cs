namespace LabyrinthianFacilities.DgConversion;

using BoundsExtensions;
using Util;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

using DunGen.Graph;

using Random = System.Random;

public class DDoorway : Doorway {
	// Properties
	public bool IsBossDoor {get {return this.currentlyActiveRandomObject == null;}}
	
	// Protected/Private
	protected GameObject[] alwaysBlockers;
	protected GameObject[] alwaysDoors;
	protected GameObject[] randomBlockerSet; // entries should have weights
	protected GameObject[] randomDoorSet;
	private GameObject currentlyActiveRandomObject;
	
	// Helper Methods
	private void fixRotation() {
		Bounds bounds = Tile.BoundingBox;
		RectFace[] faces = bounds.GetFaces();
		
		// float lowest_dist = faces[0].bounds.SqrDistance(this.transform.position);
		// highest w component is highest cosine of 1/2*angle, which means lowest angle
		// (the angle is how much the door has to rotate to face whatever face)
		
		float lowest_dist = Vector3.Angle(
			faces[0].perpindicular, this.transform.rotation * Vector3.forward
		);
		RectFace closest_face = faces[0];
		
		for (uint i=1; i<6; i++) {
			float dist = (
				faces[i].perpindicular - this.transform.rotation * Vector3.forward
			).magnitude;
			if (dist < lowest_dist) {
				lowest_dist = dist;
				closest_face = faces[i];
			}
		}
		
		this.transform.rotation = Quaternion.LookRotation(closest_face.perpindicular);
	}
	
	private GameObject instantiateSubPart(GameObject o) {
		if (o == null) return null;
		
		// Do not reinstantiate subparts that already exist
		if (o.GetComponentInParent<Tile>(includeInactive: true) == this.Tile) {
			return o;
		}
		
		var newobj = GameObject.Instantiate(o);
		newobj.transform.SetParent(this.transform);
		newobj.transform.localPosition = Vector3.zero;
		newobj.transform.localRotation = Quaternion.identity;
		return newobj;
	}
	
	// Constructors/Initializers
	public DDoorway() {
		if (this.gameObject == null) return;
		InitSize();
	}
	
	private void Awake() {
		base.OnDisconnect += DDoorway.DisconnectAction;
	}
	
	public void InitSize() {
		var dg = this.GetComponent<DunGen.Doorway>();
		base.Size = dg.Socket.Size;
	}
	
	public override void Initialize() {
		if (base.Initialized) return;
		base.Initialize();
		this.InitSize();
		this.fixRotation();
		
		var dg = this.GetComponent<DunGen.Doorway>();
		List<GameObject> objs = new();
		
		for (int i=0; i<dg.BlockerSceneObjects.Count; i++) {
			var blocker = dg.BlockerSceneObjects[i];
			if (blocker == null) continue;
			var b = instantiateSubPart(blocker);
			var prop = b.AddComponent<TileProp>();
			prop.IsBlocker = true;
			prop.DoorwayReference = this;
			objs.Add(b);
			// b.SetActive(true); // (already active)
		}
		this.alwaysBlockers = objs.ToArray();
		
		objs = new();
		for (int i=0; i<dg.ConnectorSceneObjects.Count; i++) {
			var door = dg.ConnectorSceneObjects[i];
			if (door == null) continue;
			var d = instantiateSubPart(door);
			var prop = d.AddComponent<TileProp>();
			prop.IsConnector = true;
			prop.DoorwayReference = this;
			objs.Add(d);
			d.SetActive(false);
		}
		this.alwaysDoors = objs.ToArray();
		
		this.randomBlockerSet = new GameObject[dg.BlockerPrefabWeights?.Count ?? 0];
		for (int i=0; i<randomBlockerSet.Length; i++) {
			var blocker = dg.BlockerPrefabWeights[i].GameObject;
			var b = instantiateSubPart(blocker);
			var prop = b?.AddComponent<TileProp>();
			if (prop != null) {
				prop.IsConnector = true;
				prop.DoorwayReference = this;
			}
			this.randomBlockerSet[i] = b;
			b?.SetActive(false);
		}
		
		this.randomDoorSet = new GameObject[dg.ConnectorPrefabWeights?.Count ?? 0];
		for (int i=0; i<randomDoorSet.Length; i++) {
			var door = dg.ConnectorPrefabWeights[i].GameObject;
			var d = instantiateSubPart(door);
			this.randomDoorSet[i] = d;
			var prop = d?.AddComponent<TileProp>();
			if (prop != null) {
				prop.IsConnector = true;
				prop.DoorwayReference = this;
			}
			d?.SetActive(false);
		}
		
		currentlyActiveRandomObject = (
			this.randomBlockerSet.Length != 0
		) ? (
			this.randomBlockerSet[
				this.Tile.Map.rng.Next(this.randomBlockerSet.Length)
			]
		) : null;
		currentlyActiveRandomObject?.SetActive(true);
	}
	
	// Overrides
	public override void Connect(Doorway other) {
		base.Connect(other);
		DDoorway d1,d2;
		if (this.Tile.Map.rng.Next(2) == 0) {
			d1 = (DDoorway)other; d2 = this;
		} else {
			d1 = this; d2 = (DDoorway)other;
		}
		d1.OnConnect(true);
		d2.OnConnect(false);
	}
	
	// Native Methods
	// Boss Doorway in charge of random door object
	private void OnConnect(bool isBossDoor) {
		foreach (GameObject o in this.alwaysBlockers) {
			o.SetActive(false);
		}
		foreach (GameObject o in this.alwaysDoors) {
			o.SetActive(true);	
		}
		
		this.currentlyActiveRandomObject?.SetActive(false);
		if (isBossDoor && this.randomDoorSet.Length != 0) {
			this.currentlyActiveRandomObject = this.randomDoorSet[
				this.Tile.Map.rng.Next(this.randomDoorSet.Length)
			];
			this.currentlyActiveRandomObject?.SetActive(true);
		} else {
			this.currentlyActiveRandomObject = null;
		}
	}
	private static void DisconnectAction(Doorway door) {
		var d = (DDoorway)door;
		foreach (GameObject o in d.alwaysBlockers) {
			o.SetActive(true);
		}
		foreach (GameObject o in d.alwaysDoors) {
			o.SetActive(false);
		}
		
		d.currentlyActiveRandomObject?.SetActive(false);
		d.currentlyActiveRandomObject = (
			d.randomBlockerSet.Length != 0
		) ? (
			d.randomBlockerSet[
				d.Tile.Map.rng.Next(d.randomBlockerSet.Length)
			]
		) : null;
		d.currentlyActiveRandomObject?.SetActive(true);
	}
}

public class TileProp : MonoBehaviour {
	public bool IsBlocker {get; internal set;} = false;
	public bool IsConnector {get; internal set;} = false;
	public DDoorway DoorwayReference {get; internal set;} = null;
	
	public event Action<TileProp> OnEnableEvent;
	public event Action<TileProp> OnDisableEvent;
	public event Action<TileProp> OnDestroyEvent;
	
	protected List<PropSet> propSets = new();
	
	public bool IsForcedOff {get {
		return IsBlocker && !DoorwayReference.IsVacant || IsConnector && DoorwayReference.IsVacant;
	}}
	public bool Ok {get {
		if (this.isActiveAndEnabled && IsForcedOff) return false;
		foreach (var pset in propSets) {
			if (!pset.Ok) return false;
		}
		return true;
	}}
	public bool CanToggle {get {
		if (this.gameObject.activeSelf) return CanDisable;
		else return CanEnable;
	}}
	public bool CanEnable {get {
		if (this.isActiveAndEnabled || IsForcedOff) return false;
		foreach (var pset in propSets) {
			if (pset.NumActive + 1 > pset.Range.max) return false;
		}
		return true;
	}}
	public bool CanDisable {get {
		if (!this.isActiveAndEnabled) return false;
		foreach (var pset in propSets) {
			if (pset.NumActive - 1 < pset.Range.min) return false;
		}
		return true;
	}}
	
	private void OnEnable()  {this.OnEnableEvent ?.Invoke(this);}
	private void OnDisable() {this.OnDisableEvent?.Invoke(this);}
	private void OnDestroy() {this.OnDestroyEvent?.Invoke(this);}
}

public class PropSet {
	// Protected/Private
	private (int min, int max) range;
	private DGameMap map;
	
	private WeightedList<TileProp> props;
	private WeightedList<TileProp> activeProps;
	private WeightedList<TileProp> inactiveProps;
	
	private void PropEnabled(TileProp p) {
		if (inactiveProps != null && activeProps != null) {
			float weight;
			inactiveProps.Remove(p,out weight);
			activeProps.Add(p,weight);
		}
	}
	private void PropDisabled(TileProp p) {
		if (inactiveProps != null && activeProps != null) {
			float weight;
			activeProps.Remove(p,out weight);
			inactiveProps.Add(p,weight);
		}
	}
	
	// Properties
	public bool High {get {return NumActive > Range.max;}}
	public bool Low {get {return NumActive < Range.min;}}
	public bool Ok {get {return !(High || Low);}} // neither high nor low
	public int NumActive {get {return activeProps.Count;}}
	public (int min,int max) Range {get {return range;}}
	
	public PropSet(DGameMap map, (int min, int max) range) {
		this.map = map;
		this.range = range;
		
		this.props = new();
		this.activeProps = null;
		this.inactiveProps = null;
		
	}
	
	public void InitActivityLists() {
		this.activeProps = new();
		this.inactiveProps = new();
		foreach (TileProp prop in this.props) {
			if (prop == null) {
				this.props.Remove(prop);
				continue;
			}
			if (prop.isActiveAndEnabled) {
				this.activeProps.Add(prop, this.props[prop]);
			} else {
				this.inactiveProps.Add(prop, this.props[prop]);
			}
		}
	}
	
	public void Display() {
		Plugin.LogDebug($"Active props: ");
		foreach (var prop in this.activeProps) {
			Plugin.LogDebug(
				$"{prop} ({prop.gameObject.activeSelf}, {prop.gameObject.activeInHierarchy})"
			);
		}
		Plugin.LogDebug($"Inactive props: ");
		foreach (var prop in this.inactiveProps) {
			Plugin.LogDebug(
				$"{prop} ({prop.gameObject.activeSelf}, {prop.gameObject.activeInHierarchy})"
			);
		}
		Plugin.LogDebug("");
	}
	
	public void AddProp(TileProp p, float weight) {
		this.props.Add(p,weight);
		p.OnDisableEvent += this.PropDisabled;
		p.OnEnableEvent += this.PropEnabled; 
		p.OnDestroyEvent += this.RemoveProp;
	}
	public void RemoveProp(TileProp p) {
		this.props.Remove(p);
		p.OnDisableEvent -= this.PropDisabled;
		p.OnEnableEvent -= this.PropEnabled;
		p.OnDestroyEvent -= this.RemoveProp;
		if (this.activeProps   != null) this.activeProps.Remove(p);
		if (this.inactiveProps != null) this.inactiveProps.Remove(p);
	}
	
	// returns false when randomly chosen prop cannot be enabled or no prop is found
	// does *not* necessarily mean there are no props available to be enabled!
	public bool EnableRandomProp() {
		if (this.inactiveProps.Count == 0) return false;
		
		Random rng = this.map.rng;
		float index = this.inactiveProps.SummedWeight * (float)rng.NextDouble();
		
		TileProp prop = this.inactiveProps[index];
		if (!prop.CanEnable) return false;
		
		prop.gameObject.SetActive(true);
		
		return prop.isActiveAndEnabled;
	}
	public bool DisableRandomProp() {
		if (this.activeProps.Count == 0) return false;
		
		Random rng = this.map.rng;
		float index = this.activeProps.SummedWeight * (float)rng.NextDouble();
		
		TileProp prop = this.activeProps[index];
		if (!prop.CanDisable) return false;
		
		prop.gameObject.SetActive(false);
		
		return !prop.isActiveAndEnabled;
	}
	
	// Most common reason that lower bounds are violated: parent not enabled, so propset cannot 
	// use SetActive to enable the prop
	public static void Resolve(DGameMap map, IEnumerable<PropSet> psets) {
		List<PropSet> ok = new();
		List<PropSet> low = new();
		List<PropSet> high = new();
		foreach (PropSet pset in psets) {
			pset.InitActivityLists();
			if (pset.Low) {
				low.Add(pset);
			} else if (pset.High) {
				high.Add(pset);
			} else {
				ok.Add(pset);
			}
		}
		
		uint iterationCount = 0;
		Random rng = map.rng;
		
		// guarantee that easily-resolvable propsets are resolved
		int idx=0;
		while (idx < high.Count) {
			var propset = high[idx];
			
			while (propset.High && propset.DisableRandomProp());
			if (propset.Ok) {
				high[idx] = high[high.Count-1];
				high.RemoveAt(high.Count-1);
				ok.Add(propset);
			} else {
				idx++;
			}
		}
		idx=0;
		while (idx < low.Count) {
			var propset = low[idx];
			
			while (propset.Low && propset.EnableRandomProp());
			if (propset.Ok) {
				low[idx] = low[low.Count-1];
				low.RemoveAt(low.Count-1);
				ok.Add(propset);
			} else {
				idx++;
			}
		}
		
		// Check if any other sets have gone out of range
		List<(PropSet pset, List<PropSet> dst)> moves = new();
		foreach (PropSet pset in ok) {
			if (pset.Low) {
				moves.Add((pset,low));
			} else if (pset.High) {
				moves.Add((pset,high));
			}
		}
		foreach (var move in moves) {
			move.dst.Add(move.pset);
			ok.Remove(move.pset);
		}
		Plugin.LogDebug(
			$"{iterationCount} iterations: \n"
			+ $"\tok: {ok.Count}, low: {low.Count}, high: {high.Count}"
		);
		
		while (low.Count != 0 || high.Count != 0) {
			// Resolve current issues
			do {
				bool chooseHigh;
				if (low.Count == 0) {
					chooseHigh = true;
				} else if (high.Count == 0) {
					chooseHigh = false;
				} else {
					chooseHigh = rng.Next(2) == 0;
				}
				
				PropSet target;
				if (chooseHigh) {
					target = high[rng.Next(high.Count)];
					while (target.High && target.DisableRandomProp());
					if (target.Ok) {
						high.Remove(target);
						ok.Add(target);
					}
				} else {
					target = low[rng.Next(low.Count)];
					while (target.Low && target.EnableRandomProp());
					if (target.Ok) {
						low.Remove(target);
						ok.Add(target);
					}
				}
				iterationCount++;
				if (iterationCount % 1000 == 0) {
					Plugin.LogDebug(
						$"{iterationCount} iterations: \n"
						+ $"\tok: {ok.Count}, low: {low.Count}, high: {high.Count}"
					);
				}
				if (iterationCount == 10000) {
					Plugin.LogWarning("Aborted prop spawning after 10,000 iterations");
					Plugin.LogWarning(
						$"(Propsets: {ok.Count} ok, {low.Count} low, {high.Count} high)"
					);
					// Plugin.LogDebug($"low:");
					// foreach (PropSet p in low) {
						// p.Display();
					// }
					// Plugin.LogDebug("");
					// Plugin.LogDebug($"high:");
					// foreach (PropSet p in high) {
						// p.Display();
					// }
					return;
				}
			} while (low.Count != 0 || high.Count != 0);
			
			// Check if any other sets have gone out of range
			moves = new();
			foreach (PropSet pset in ok) {
				if (pset.Low) {
					moves.Add((pset,low));
				} else if (pset.High) {
					moves.Add((pset,high));
				}
			}
			foreach (var move in moves) {
				move.dst.Add(move.pset);
				ok.Remove(move.pset);
			}
			Plugin.LogDebug(
				$"{iterationCount} iterations: \n"
				+ $"\tok: {ok.Count}, low: {low.Count}, high: {high.Count}"
			);
		}
	}
}

public class DTile : Tile {
	// Properties
	internal PropSet[] LocalPropSets {get {return localPropSets;}}
	
	// Protected/Private
	protected List<GrabbableObject> ownedObjects;
	protected PropSet[] localPropSets;
	
	// Private Helper Methods
	private Bounds DeriveBounds() {
		Bounds bounds;
		var dungenTile = this.GetComponent<DunGen.Tile>();
		if (dungenTile.OverrideAutomaticTileBounds) {
			return dungenTile.TileBoundsOverride;
		}
		Plugin.LogDebug($"Tile {this.gameObject} had no predefined bounds");
		
		//subject to change, because why have consistency with what is the actual mesh of the room
		//ajfshdlfjqew
		bounds = new Bounds(Vector3.zero,Vector3.zero);
		// manor tiles all use mesh
		// factory (typically) uses variety of these 3 (belt room is weird af)
		Collider collider = (
			this.transform.Find("mesh") 
			?? this.transform.Find("Mesh") 
			?? this.transform.Find("Wall") 
		)?.GetComponent<MeshCollider>();
		
		if (collider == null) {
			Plugin.LogDebug($"Unable to find easy meshcollider for {this}");
			
			var colliders = this.transform.Find("Meshes")?.GetComponentsInChildren<MeshCollider>();
			foreach (Collider c in colliders ?? (Collider[])[]) {
				bounds.Encapsulate(c.bounds);
			}
			
			if (bounds.extents == Vector3.zero) {
				// cave tiles all have first meshcollider as room bounds (I think)
				collider = (
					this.GetComponentInChildren<MeshCollider>()
					?? this.GetComponentInChildren<Collider>()
				);
				Plugin.LogDebug($"Using first collider found: {collider}");
				if (collider == null) {
					Plugin.LogError($"Could not find a collider to infer bounds for tile {this}");
				}
			}
		}
		if (bounds.extents == Vector3.zero && collider != null) bounds = collider.bounds;
		Plugin.LogDebug($"{this.gameObject.name} extents: {bounds.extents}");
		
		// Special rules
		switch (this.gameObject.name) {
			case "ElevatorConnector(Clone)":
				bounds = new Bounds(Vector3.zero,Vector3.zero);
			break; case "StartRoom(Clone)":
				bounds.Encapsulate(
					this.transform.Find("VisualMesh").Find("StartRoomElevator")
						.GetComponent<MeshFilter>().sharedMesh.bounds
				);
			break; case "MineshaftStartTile(Clone)":
				// Do not include the entire start room
				// (makes bounds at bottom of elevator stick out way too far)
				bounds = new Bounds(new Vector3(3.4f,20f,3.4f), new Vector3(6.8f,60f,6.8f));
			break;
		}
		
		return bounds;
	}
	
	protected override void Initialize() {
		if (this.Initialized) return;
		base.Initialize();
		this.Initialized = true;
		
		// Bounds
		Plugin.LogDebug("Getting bounds...");
		Bounds bounds = this.DeriveBounds();
		if (bounds.size == Vector3.zero) {
			Plugin.LogError(
				$"Tile '{this}' has zero-size. Tile will allow others to encroach on its area."
			);
		}
		bounds.FixExtents();
		this.bounding_box = bounds;
		
		// Doorways
		Plugin.LogDebug("Initializing Doorways...");
		foreach (DDoorway d in this.Doorways) {
			d.Initialize();
		}
		
		// Props
		Plugin.LogDebug("Retrieving Props...");
		// Local Props
		var localPropSets = this.GetComponentsInChildren<DunGen.LocalPropSet>(includeInactive:true);
		List<PropSet> psets = new();
		foreach (var localPropSet in localPropSets) {
			PropSet propset = new PropSet(
				(DGameMap)this.Map, 
				(localPropSet.PropCount.Min, localPropSet.PropCount.Max)
			);
			psets.Add(propset);
			foreach (var entry in localPropSet.Props.Weights) {
				GameObject propObject = entry.Value;
				if (propObject == null) continue;
				var prop = propObject.GetComponent<TileProp>() ?? propObject.AddComponent<TileProp>();
				propset.AddProp(prop, (entry.MainPathWeight + entry.BranchPathWeight)/2.0f);
			}
		}
		this.localPropSets = psets.ToArray();
		
		// Global Props
		foreach (
			var globalProp in this.GetComponentsInChildren<DunGen.GlobalProp>(includeInactive:true)
		) {
			PropSet pset = ((DGameMap)this.Map).GetGlobalPropSet(globalProp.PropGroupID);
			GameObject propObject = globalProp.gameObject;
			var prop = propObject.GetComponent<TileProp>() ?? propObject.AddComponent<TileProp>();
			pset.AddProp(prop, (globalProp.MainPathWeight + globalProp.BranchPathWeight)/2.0f);
		}
	}
}

public class DGameMap : GameMap {
	protected DungeonFlow flow;
	protected Dictionary<int, PropSet> globalPropSets;
	
	// Constructors/Initialization
	protected override void Awake() {
		base.Awake();
		
		this.GenerationCompleteEvent += this.RestoreScrap;
		globalPropSets = new();
	}
	
	// Native Methods
	private (int min,int max) GetGlobalPropRange(int id) {
		foreach (var settings in this.flow.GlobalProps) {
			if (settings.ID == id) return (settings.Count.Min,settings.Count.Max);
		}
		Plugin.LogError($"Global Prop bounds not found for id {id}");
		return (0,1);
	}
	
	protected void HandleProps() {
		IEnumerable<PropSet> propsets = (
			(IEnumerable<DTile>)this.GetComponentsInChildren<DTile>()
		).SelectMany<DTile,PropSet,PropSet>(
			(DTile t) => t.LocalPropSets,
			(DTile t,PropSet pset) => pset
		).Concat(this.globalPropSets.Values);
		
		PropSet.Resolve(this, propsets);
	}
	
	public PropSet GetGlobalPropSet(int id) {
		PropSet propSet;
		if (!globalPropSets.TryGetValue(id, out propSet)) {
			propSet = new PropSet(this, GetGlobalPropRange(id));
			globalPropSets.Add(id,propSet);
		}
		return propSet;
	}
	
	public override IEnumerator GenerateCoroutine(ITileGenerator tilegen, int? seed) {
		var dtilegen = (DungeonFlowConverter)tilegen;
		this.flow = dtilegen.Flow;
		var foo = (GameMap.GenerationCompleteDelegate)(
			// (GameMap m) => HandleGlobalProps(dtilegen.Flow)
			(GameMap m) => HandleProps()
		);
		this.GenerationCompleteEvent += foo;
		yield return StartCoroutine(base.GenerateCoroutine(tilegen,seed));
		this.GenerationCompleteEvent -= foo;
	}
	
	public void PreserveScrap() {
		foreach (Scrap obj in GameObject.FindObjectsByType<Scrap>(FindObjectsSortMode.None)) {
			obj.Preserve();
		}
	}
	
	public void RestoreScrap(GameMap m) {
		foreach (Scrap obj in this.GetComponentsInChildren<Scrap>(includeInactive: true)) {
			obj.Restore();
		}
	}
}

public class DungeonFlowConverter : ITileGenerator {
	protected DunGen.Graph.DungeonFlow flow;
	protected uint tile_demand;
	
	protected List<(DTile tile,float weight)> tile_freqs;
	
	protected float freq_range;
	
	// max attempts to place each given tile
	// NOT the max attempts to place any tile. 
	//   i.e. if a tile fails to place within MAX_ATTEMPTS, a new tile is chosen
	private const int MAX_ATTEMPTS=10;
	
	public DunGen.Graph.DungeonFlow Flow {get {return flow;}}
	
	public DungeonFlowConverter(DungeonFlow flow) {
		this.flow = flow;
		this.tile_demand = 30;
		
		freq_range = 0.0f;
		this.tile_freqs = new();
		// Note that nodes arent included here... for now(?)
		foreach (var line in flow.Lines) {
			float line_freq = line.Length;
			float arch_freq = 1.0f / line.DungeonArchetypes.Count;
			foreach (var archetype in line.DungeonArchetypes) {
				float tset_freq = 1.0f / archetype.TileSets.Count;
				foreach (var tileset in archetype.TileSets) {
					foreach (var tile in tileset.TileWeights.Weights) {
						float tile_freq = (tile.MainPathWeight + tile.BranchPathWeight) / 2.0f;
						DTile dtile = tile.Value.GetComponent<DTile>();
						if (dtile == null) {
							Plugin.LogError("Bad dtile");
							Plugin.LogError($"{flow}");
							Plugin.LogError($"{line}");
							Plugin.LogError($"{archetype}");
							Plugin.LogError($"{tileset}");
							Plugin.LogError($"{tile}");
							Plugin.LogError($"{tile.Value}");
							Plugin.LogError("");
						}
						
						float freq = line_freq * arch_freq * tset_freq * tile_freq;
						
						tile_freqs.Add((dtile,freq));
						freq_range += freq;
					}
				}
			}
		}
	}
	
	public void FailedPlacementHandler(Tile tile) {
		if (tile == null) {
			Plugin.LogDebug($"Placement failure - {tile}");
			this.tile_demand++;
		}
	}
	
	public IEnumerable<GenerationAction> Generator(GameMap map) {
		DunGen.Graph.GraphNode node = null;
		foreach (var n in flow.Nodes) {
			if (n.NodeType == DunGen.Graph.NodeType.Start) {node = n; break;}
		}
		Tile start = node
			?.TileSets[0]
			?.TileWeights
			?.Weights[0]
			?.Value
			?.GetComponent<Tile>();
		
		if (map.RootTile == null) {
			if (start == null) {
				Plugin.LogError("Start tile not found D:");
				yield break;
			}
			Plugin.LogDebug($"{this.tile_demand}: Using '{start.gameObject.name}' as start room");
			
			yield return new PlacementInfo(start);
			this.tile_demand--;
		} else {
			Plugin.LogInfo($"Removing tiles...");
			for (int i=0; i<5; i++) {
				Tile[] tiles = map.GetComponentsInChildren<Tile>();
				if (tiles.Length <= 1) break;
				Tile selected;
				do {
					selected = tiles[map.rng.Next(tiles.Length)];
				} while (selected == start);
				yield return new RemovalInfo(selected);
			}
			Plugin.LogInfo($"Done removing tiles!");
		}
		
		PlacementInfo rt = new PlacementInfo();
		while (tile_demand > 0) {
			bool factoryStartRoomExists = map.transform.Find(
				"ElevatorConnector(Clone)/ElevatorDoorway/StartRoom(Clone)"
			);
			do {
				int i = -1;
				float rand_point = (float)(this.freq_range * map.rng.NextDouble());
				while (rand_point > 0) {
					i++;
					rand_point -= this.tile_freqs[i].weight;
				}
				rt.NewTile = this.tile_freqs[i].tile;
			} while (
				rt.NewTile == start 
				|| (
					factoryStartRoomExists
					&& rt.NewTile.gameObject.name == "StartRoom"
				)
			);
			// RHS of condition is temporary fix to stop start room from spawning multiple times
			// since it *technically* isn't the start room in the factory layout
			// This won't be necessary if we enforce the rule that certain rooms can only spawn once 
			// in a map, but I'm still a little unsure if I actually want to use that rule, since maps
			// will often be larger than in vanilla in order to keep them more interesting. 
			
			Plugin.LogDebug($"Attempting to place {rt.NewTile}");
			bool forelse = true;
			for (int i=0; i<MAX_ATTEMPTS; i++) {
				rt.NewDoorwayIdx = map.rng.Next(rt.NewTile.Doorways.Length);
				var d = rt.NewTile.Doorways[rt.NewDoorwayIdx];
				rt.AttachmentPoint = map.GetLeaf(d.Size);
				if (rt.AttachmentPoint != null) {
					Plugin.LogDebug(
						$"Size connections: {rt.NewTile?.Doorways?[rt.NewDoorwayIdx]?.Size} - {rt.AttachmentPoint?.Size}"
					);
					forelse = false; break;
				}
			} if (forelse) {
				Plugin.LogDebug("Exceeded max spawn attempts, getting new tile");
				continue;
			}
			
			Plugin.LogDebug($"{this.tile_demand}: Yielding '{rt.NewTile.gameObject.name}'");
			this.tile_demand--;
			yield return rt;
		}
		Plugin.LogInfo($"Done Generating Tiles!");
	}
	
}