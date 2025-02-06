namespace LabyrinthianFacilities.DgConversion;

using BoundsExtensions;
using Serialization;
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
	public Prop ActiveRandomObject {get {return activeRandomObject;}}
	public IEnumerable<Prop> Blockers {get {
		foreach (Prop p in alwaysBlockers) yield return p;
		foreach (Prop p in randomBlockerSet) yield return p;
	}}
	// does not include any borrowed activeRandomObject
	public IEnumerable<Prop> Connectors {get {
		foreach (Prop p in alwaysDoors) yield return p;
		foreach (Prop p in randomDoorSet) yield return p;
	}}
	
	// Protected/Private
	protected Prop[] alwaysBlockers;
	protected Prop[] alwaysDoors;
	protected WeightedList<Prop> randomBlockerSet;
	protected WeightedList<Prop> randomDoorSet;
	
	protected Prop activeRandomObject = null;
	
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
	
	private Prop instantiateSubPart(GameObject o, bool isblocker) {
		if (o == null) return null;
		
		// Reinstantiate subparts that do not already exist
		if (o.GetComponentInParent<Tile>(includeInactive: true) != this.Tile) {
			o = GameObject.Instantiate(o);
			o.transform.SetParent(this.transform);
			o.transform.localPosition = Vector3.zero;
			o.transform.localRotation = Quaternion.identity;
		}
		
		var prop = o.GetComponent<Prop>() ?? o.AddComponent<Prop>();
		if (isblocker) {
			prop.IsBlocker = true;
		} else {
			prop.IsConnector = true;
		}
		return prop;
	}
	
	// Constructors/Initializers
	public DDoorway() {
		if (this.gameObject == null) return;
		InitSize();
	}
	
	private void Awake() {
		base.OnDisconnectEvent += (Doorway) => this.OnDisconnect();
		base.OnConnectEvent += (Doorway d1, Doorway d2) => this.OnConnect();
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
		
		if (this.Tile == null) {
			throw new NullReferenceException(
				$"DDoorway has no parent tile. Parent tile is needed for Tile.Map for new PropSet()"
			);
		}
		
		var dg = this.GetComponent<DunGen.Doorway>();
		
		List<Prop> objs = new();
		for (int i=0; i<dg.BlockerSceneObjects.Count; i++) {
			var blocker = dg.BlockerSceneObjects[i];
			if (blocker == null) continue;
			var b = instantiateSubPart(blocker,true);
			b.Enable();
			objs.Add(b);
		}
		this.alwaysBlockers = objs.ToArray();
		
		objs.Clear();
		for (int i=0; i<dg.ConnectorSceneObjects.Count; i++) {
			var door = dg.ConnectorSceneObjects[i];
			if (door == null) continue;
			var d = instantiateSubPart(door,false);
			d.Disable();
			objs.Add(d);
		}
		this.alwaysDoors = objs.ToArray();
		
		this.randomBlockerSet = new();
		foreach (var entry in dg.BlockerPrefabWeights) {
			var blocker = entry.GameObject;
			if (blocker == null) continue;
			var b = instantiateSubPart(blocker,true);
			b.Disable();
			this.randomBlockerSet.Add(b,entry.Weight);
		}
		
		this.randomDoorSet = new();
		foreach (var entry in dg.ConnectorPrefabWeights) {
			var door = entry.GameObject;
			if (door == null) continue;
			var d = instantiateSubPart(door,false);
			d.Disable();
			this.randomDoorSet.Add(d,entry.Weight);
		}
	}
	
	// Native Methods
	protected virtual void OnConnect() {
		foreach (Prop obj in this.alwaysBlockers) {
			obj.Disable();
		}
		foreach (Prop obj in this.alwaysDoors) {
			obj.Enable();
		}
		this.activeRandomObject?.Disable();
		this.activeRandomObject = null;
	}
	
	public virtual void SetActiveObject(float idx) {
		this.activeRandomObject?.Disable();
		DDoorway con = (DDoorway)this.connection;
		if (IsVacant) {
			if (this.randomBlockerSet.Count == 0) {
				this.activeRandomObject = null;
			} else {
				this.activeRandomObject = this.randomBlockerSet[this.randomBlockerSet.SummedWeight * idx];
			}
		} else {
			if (this.randomDoorSet.Count == 0) {
				this.activeRandomObject = null;
				if (con.randomDoorSet.Count != 0) {
					con.SetActiveObject(idx);
				}
				return;
			} else {
				this.activeRandomObject = this.randomDoorSet[this.randomDoorSet.SummedWeight * idx];
			}
		}
		this.activeRandomObject?.Enable();
		if (con != null) {
			con.activeRandomObject?.Disable(); 
			con.activeRandomObject = this.activeRandomObject;
		}
	}
	
	public virtual IList<Prop> GetProps() {
		List<Prop> rt = new();
		var enumerable = this.alwaysBlockers.Concat(
			this.alwaysDoors
		).Concat(
			this.randomBlockerSet
		).Concat(
			this.randomDoorSet
		);
		foreach (Prop p in enumerable) {
			if (!rt.Contains(p)) rt.Add(p);
		}
		return rt;
	}
	
	protected virtual void OnDisconnect() {
		if (this == null) return;
		foreach (Prop obj in this.alwaysBlockers) {
			obj.Enable();
		}
		foreach (Prop obj in this.alwaysDoors) {
			obj.Disable();
		}
		this.activeRandomObject?.Disable();
		this.activeRandomObject = null;
	}
}

public class Prop : MonoBehaviour {
	public bool IsBlocker  {get; set;} = false;
	public bool IsConnector{get; set;} = false;
	public bool IsTileProp {get; set;} = false;
	public bool IsMapProp  {get; set;} = false;
	
	
	public bool IsDoorProp => IsBlocker || IsConnector;
	
	public void SetActive(bool value) {if (value) Enable(); else Disable();}
	
	public virtual void Enable() {
		if (this == null) {
			Plugin.LogError("Cannot enable a prop which has been destroyed");
			return;
		}
		this.gameObject.SetActive(true);
	}
	
	public virtual void Disable() {
		if (this == null) {
			Plugin.LogError("Cannot disable a prop which has been destroyed");
			return;
		}
		this.gameObject.SetActive(false);
	}
}

public class PropSet : WeightedList<Prop> {
	public (int min, int max) Range {get; set;} = (0,0);
	public WeightedList<Prop> Props {get {return this;}}
	
	public PropSet() {}
	public PropSet(DunGen.LocalPropSet pset) {
		foreach (var entry in pset.Props.Weights) {
			if (entry.Value == null) continue;
			Prop p = entry.Value.GetComponent<Prop>() ?? entry.Value.AddComponent<Prop>();
			p.IsTileProp = true;
			this.Add(p, (entry.MainPathWeight + entry.BranchPathWeight)/2.0f);
		}
		this.Range = (pset.PropCount.Min,pset.PropCount.Max);
	}
	
	public override void Add(Prop p, float weight) {
		if (this.Remove(p,out float oldWeight)) {
			this.Add(p,weight + oldWeight);
		} else {
			base.Add(p,weight);
		}
	}
}

public class DTile : Tile {
	// Properties
	internal PropSet[] LocalPropSets {get {return localPropSets;}}
	internal (Prop prop, int id)[] GlobalProps {get {return globalProps;}}
	
	// Protected/Private
	protected List<GrabbableObject> ownedObjects;
	protected PropSet[] localPropSets;
	protected (Prop prop, int id)[] globalProps;
	
	private void OnDestroy() {
		if (this.Map != null) {
			((DGameMap)this.Map).RemoveTileProps(this);
		}
	}
	
	// Helper Methods
	private Bounds DeriveBounds() {
		Bounds bounds;
		var dungenTile = this.GetComponent<DunGen.Tile>();
		if (dungenTile.OverrideAutomaticTileBounds && dungenTile.TileBoundsOverride.extents != Vector3.zero) {
			return dungenTile.TileBoundsOverride;
		}
		#if VERBOSE_GENERATION
		Plugin.LogDebug($"Tile {this.gameObject} had no predefined bounds");
		#endif
		
		if (!this.gameObject.activeInHierarchy) {
			throw new InvalidOperationException($"Tile {this.name} is not active; cannot derive bounds");
		}
		
		bounds = new Bounds(Vector3.zero,Vector3.zero);
		// manor tiles all use mesh
		// factory (typically) uses variety of these 3 (belt room is weird af)
		Collider collider = (
			this.transform.Find("mesh") 
			?? this.transform.Find("Mesh") 
			?? this.transform.Find("Wall") 
		)?.GetComponent<MeshCollider>();
		
		if (collider == null) {
			#if VERBOSE_GENERATION
			Plugin.LogDebug($"Unable to find easy meshcollider for {this}");
			#endif
			
			var colliders = this.transform.Find("Meshes")?.GetComponentsInChildren<MeshCollider>(true);
			foreach (Collider c in colliders ?? (Collider[])[]) {
				bounds.Encapsulate(c.bounds);
			}
			
			if (bounds.extents == Vector3.zero) {
				// cave tiles all have first meshcollider as room bounds (I think)
				collider = (
					this.GetComponentInChildren<MeshCollider>(true)
					?? this.GetComponentInChildren<Collider>(true)
				);
				#if VERBOSE_GENERATION
				Plugin.LogDebug($"Using first collider found: {collider}");
				#endif
				if (collider == null) {
					Plugin.LogError($"Could not find a collider to infer bounds for tile {this}");
				}
			}
		}
		if (bounds.extents == Vector3.zero && collider != null) bounds = collider.bounds;
		
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
			break; default:
				if (bounds.extents == Vector3.zero) {
					Plugin.LogError($"Tile {this} has zero bounds");
				}
			break;
			
		}
		
		#if VERBOSE_GENERATION
		Plugin.LogDebug($"{this.gameObject.name} extents: {bounds.extents}");
		#endif
		
		return bounds;
	}
	
	public override void Initialize() {
		if (this.Initialized) return;
		base.Initialize();
		this.Initialized = true;
		
		if (this.Map == null) {
			throw new NullReferenceException($"DTile has no parent map");
		}
		
		// Bounds
		#if VERBOSE_GENERATION
		Plugin.LogDebug("Getting bounds...");
		#endif
		Bounds bounds = this.DeriveBounds();
		bounds.FixExtents();
		this.bounding_box = bounds;
		
		// Doorways
		#if VERBOSE_GENERATION
		Plugin.LogDebug("Initializing Doorways...");
		#endif
		foreach (DDoorway d in this.Doorways) {
			d.Initialize();
		}
		
		// Props
		#if VERBOSE_GENERATION
		Plugin.LogDebug("Retrieving Props...");
		#endif
		// Local Props
		var localPropSets = this.GetComponentsInChildren<DunGen.LocalPropSet>(includeInactive:true);
		List<PropSet> psets = new();
		foreach (var localPropSet in localPropSets) {
			psets.Add(new PropSet(localPropSet));
		}
		this.localPropSets = psets.ToArray();
		
		// Global Props
		var globs = this.GetComponentsInChildren<DunGen.GlobalProp>(includeInactive:true);
		this.globalProps = new (Prop prop, int id)[globs.Length];
		for (int i=0; i<globs.Length; i++) {
			DunGen.GlobalProp globalProp = globs[i];
			if (globalProp?.gameObject == null) continue;
			
			((DGameMap)this.Map).AddGlobalProp(globalProp);
			this.globalProps[i] = (globalProp.GetComponent<Prop>(), globalProp.PropGroupID);
		}
	}
	
	public IList<Prop> GetProps() {
		List<Prop> rt = new();
		foreach ((Prop prop, int id) in this.globalProps) {
			if (!rt.Contains(prop)) rt.Add(prop);
		}
		foreach (PropSet ps in this.LocalPropSets) {
			foreach (Prop prop in ps) {
				if (!rt.Contains(prop)) rt.Add(prop);
			}
		}
		foreach (DDoorway d in this.Doorways) {
			foreach (Prop prop in d.GetProps()) {
				if (!rt.Contains(prop)) rt.Add(prop);
			}
		}
		return rt;
	}
}

public class DGameMap : GameMap {
	
	protected Dictionary<int, PropSet> globalPropSets;
	protected Dictionary<int, PropSet> uninitializedGlobalPropSets;
	
	public Moon Moon {get {return this.transform.parent.GetComponent<Moon>();}}
	public IReadOnlyCollection<PropSet> GlobalPropSets {get {return globalPropSets.Values;}}
	
	// Constructors/Initialization
	protected override void Awake() {
		base.Awake();
		
		this.transform.position = new Vector3(0,-200,0);
		
		this.GenerationCompleteEvent += DGameMap.GenerationCompleteHandler;
		this.TileInsertionEvent += DGameMap.TileInsertionFail;
		globalPropSets = new();
		uninitializedGlobalPropSets = new();
	}
	
	// Native Methods
	public static void TileInsertionFail(Tile t) {
		#if VERBOSE_GENERATION
		if (t == null) Plugin.LogDebug($"Failed to place tile {t}");
		#endif
	}
	
	public PropSet GetGlobalPropSet(int id) {
		PropSet propSet;
		if (!globalPropSets.TryGetValue(id, out propSet)) {
			if (!uninitializedGlobalPropSets.TryGetValue(id, out propSet)) {
				propSet = new PropSet();
				uninitializedGlobalPropSets.Add(id,propSet);
			}
		}
		return propSet;
	}
	
	public void AddGlobalProp(DunGen.GlobalProp globProp) {
		Prop prop = globProp.GetComponent<Prop>() ?? globProp.gameObject.AddComponent<Prop>();
		prop.IsMapProp = true;
		GetGlobalPropSet(globProp.PropGroupID).Add(
			prop, (globProp.MainPathWeight + globProp.BranchPathWeight) / 2.0f
		);
	}
	
	public void RemoveTileProps(DTile tile) {
		foreach ((Prop prop,int id) in tile.GlobalProps) {
			if (!globalPropSets.TryGetValue(id, out PropSet pset)) {
				if (!uninitializedGlobalPropSets.TryGetValue(id, out pset)) {
					throw new KeyNotFoundException($"GlobalPropId {id}");
				}
			}
			pset.Remove(prop);
		}
	}
	
	public void InitializeGlobalPropSets(DungeonFlowConverter flowConverter) {
		foreach (var entry in uninitializedGlobalPropSets) {
			int id = entry.Key;
			PropSet pset = entry.Value;
			pset.Range = flowConverter.GetGlobalPropRange(id);
			globalPropSets[id] = pset;
		}
		uninitializedGlobalPropSets.Clear();
	}
	
	public override IEnumerator GenerateCoroutine(ITileGenerator tilegen) {
		var dtilegen = (DungeonFlowConverter)tilegen;
		yield return StartCoroutine(base.GenerateCoroutine(tilegen));
	}
	
	private static void GenerationCompleteHandler(GameMap m) {
		var map = (DGameMap)m;
		// every indoor enemy *appears* to use agentId 0
		map.GenerateNavMesh(agentId: 0);
		map.RestoreMapObjects();
	}
	
	public void PreserveMapObjects() {
		foreach (MapObject obj in GameObject.FindObjectsByType<MapObject>(FindObjectsSortMode.None)) {
			obj.Preserve();
		}
	}
	
	public void RestoreMapObjects() {
		foreach (MapObject obj in this.GetComponentsInChildren<MapObject>(includeInactive: true)) {
			obj.Restore();
		}
	}
	
	public void InitializeLoadedMapObjects() {
		foreach (MapObject s in this.GetComponentsInChildren<MapObject>()) {
			var netObj = s.GetComponent<NetworkObject>();
			s.FindParent(map: this);
		}
	}
	
	public override bool PerformAction(GenerationAction action) {
		if (action is PropAction propA) {
			propA.Prop.SetActive(propA.Enable);
		} else {
			return base.PerformAction(action);
		}
		return true;
	}
}

public class PropAction : GenerationAction {
	protected Prop target;
	protected bool enable;
	
	public Prop Prop {get {return target;}}
	public bool Enable {get {return enable;}}
	
	public PropAction(Prop t, bool e) {
		target = t;
		enable = e;
	}
}

public class DungeonFlowConverter : ITileGenerator {
	private int seed;
	private Random rng;
	
	protected DunGen.Graph.DungeonFlow flow;
	protected uint tile_demand;
	
	protected List<(DTile tile,float weight)> tile_freqs;
	
	protected float freq_range;
	// Don't fully count branch tiles because they often get cut off. 
	// This is obviously an imperfect representation of how many tiles get generated by DunGen, 
	// but we're not trying to be dungen anyway, we're just trying to make dungen compatible
	// public float BranchCountMultiplier = 0.6f;
	
	// if a tile fails to place within MAX_ATTEMPTS, a new tile is chosen
	private const int MAX_ATTEMPTS=10;
	
	public int Seed {
		get {
			return this.seed;
		} set {
			this.rng = new Random(value);
			this.seed = value;
		}
	}
	public Random Rng {get {return rng;}}
	public DunGen.Graph.DungeonFlow Flow {get {return flow;}}
	// reduce chance because each doorway pair will likely get multiple chances to connect
	// and we dont want loops to feel too chaotic
	public float DoorwayConnectionChance {get {return flow.DoorwayConnectionChance / 2.0f;}}
	public float DoorwayDisconnectChance {get {return flow.DoorwayConnectionChance / 2.0f;}}
	
	public DungeonFlowConverter(DungeonFlow flow, int seed) {
		this.Seed = seed;
		
		this.flow = flow;
		this.tile_demand = (uint)(
			/* 0.5f *  */(
				(flow.Length.Min + flow.Length.Max) / 2.0f
				* RoundManager.Instance.mapSizeMultiplier
				* RoundManager.Instance.currentLevel.factorySizeMultiplier
			)/*  + AverageBranchTileCount(flow) * BranchCountMultiplier */
		);
		
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
	
	public virtual (int min,int max) GetGlobalPropRange(int id) {
		foreach (var settings in this.flow.GlobalProps) {
			if (settings.ID == id) return (settings.Count.Min,settings.Count.Max);
		}
		Plugin.LogError($"Global Prop bounds not found for id {id}");
		return (1,1);
	}
	
	// Does not take into account the early termination of branches!
	public static float AverageBranchTileCount(DungeonFlow flow) {
		float total = 0.0f;
		foreach (var line in flow.Lines) {
			float line_total = 0.0f;
			foreach (var archetype in line.DungeonArchetypes) {
				line_total += (
					  (archetype.BranchingDepth.Min + archetype.BranchingDepth.Max) / 2.0f
					* (archetype.BranchCount.Min + archetype.BranchCount.Max) / 2.0f
				);
			}
			total += line_total / line.DungeonArchetypes.Count;
		}
		return total;
	}
	
	public void FailedPlacementHandler(Tile tile) {
		if (tile == null) {
			#if VERBOSE_GENERATION
			Plugin.LogDebug($"Placement failure - {tile}");
			#endif
			this.tile_demand++;
		}
	}
	
	protected Tile GetStartTile() {
		DunGen.Graph.GraphNode node = null;
		foreach (var n in flow.Nodes) {
			if (n.NodeType == DunGen.Graph.NodeType.Start) {node = n; break;}
		}
		return node
			?.TileSets[0]
			?.TileWeights
			?.Weights[0]
			?.Value
			?.GetComponent<Tile>();
	}
	
	protected PlacementInfo PlaceRoot() {
		Tile start = GetStartTile();
		if (start == null) {
			Plugin.LogError("Start tile not found D:");
			return null;
		}
		#if VERBOSE_GENERATION
			Plugin.LogDebug($"{this.tile_demand}: Using '{start.gameObject.name}' as start room");
		#endif
		
		this.tile_demand--;
		return new PlacementInfo(start);
	}
	protected IEnumerable<RemovalInfo> RemoveTiles(GameMap map) {
		Plugin.LogInfo($"Removing tiles...");
		for (int i=0; i<5; i++) {
			Tile[] tiles = map.GetComponentsInChildren<Tile>();
			if (tiles.Length <= 1) break;
			Tile selected;
			do {
				selected = tiles[Rng.Next(tiles.Length)];
			} while (selected == map.RootTile);
			#if VERBOSE_GENERATION
				Plugin.LogDebug($"Removing {selected.name}");
			#endif
			yield return new RemovalInfo(selected);
		}
		#if VERBOSE_GENERATION
			Plugin.LogDebug($"Done removing tiles!");
		#endif
	}
	protected IEnumerable<PlacementInfo> PlaceTiles(GameMap map) {
		Plugin.LogInfo($"Placing tiles...");
		uint iterationsSinceLastSuccess = 0;
		PlacementInfo rt = new PlacementInfo(null,0,null);
		while (tile_demand > 0) {
			bool startRoomExists = map.transform.Find(
				"ElevatorConnector(Clone)/ElevatorDoorway/StartRoom(Clone)"
			) || map.transform.Find(
				"Level2StartRoomConnector(Clone)/ElevatorDoorway/ManorStartRoom(Clone)"
			);
			Tile start = GetStartTile();
			do {
				int i = -1;
				float rand_point = (float)(this.freq_range * Rng.NextDouble());
				while (rand_point > 0) {
					i++;
					rand_point -= this.tile_freqs[i].weight;
				}
				rt.NewTile = this.tile_freqs[i].tile;
			} while (
				rt.NewTile == start 
				|| (
					(
						rt.NewTile.gameObject.name == "StartRoom"
						|| rt.NewTile.gameObject.name == "ManorStartRoom"
					) && startRoomExists
				)
			);
			// RHS of condition is temporary fix to stop start room from spawning multiple times
			// since it *technically* isn't the start room in the factory/manor layout
			// This won't be necessary if we enforce the rule that certain rooms can only spawn once 
			// in a map, but I'm still a little unsure if I actually want to use that rule, since maps
			// will often be larger than in vanilla in order to keep them more interesting. 
			
			#if VERBOSE_GENERATION
			Plugin.LogDebug($"Attempting to place {rt.NewTile}");
			#endif
			bool forelse = true;
			for (int i=0; i<MAX_ATTEMPTS; i++) {
				rt.NewDoorwayIdx = Rng.Next(rt.NewTile.Doorways.Length);
				var d = rt.NewTile.Doorways[rt.NewDoorwayIdx];
				var leaves = map.GetLeaves(d.Size);
				if (leaves == null) continue;
				rt.AttachmentPoint = leaves[rng.Next(leaves.Count)];
				if (rt.AttachmentPoint != null) {
					#if VERBOSE_GENERATION
					Plugin.LogDebug(
						$"Size connections: {rt.NewTile?.Doorways?[rt.NewDoorwayIdx]?.Size} - {rt.AttachmentPoint?.Size}"
					);
					#endif
					iterationsSinceLastSuccess = 0;
					forelse = false; break;
				}
			} if (forelse) {
				#if VERBOSE_GENERATION
				Plugin.LogDebug("Exceeded max spawn attempts, getting new tile");
				#endif
				iterationsSinceLastSuccess++;
				if (iterationsSinceLastSuccess >= 500) {
					Plugin.LogError(
						$"Unable to generate map D: ({this.tile_demand} tiles were never placed)"
					);
					goto FailureCondition;
				}
				continue;
			}
			#if VERBOSE_GENERATION
			Plugin.LogDebug($"{this.tile_demand}: Yielding '{rt.NewTile.gameObject.name}'");
			#endif
			this.tile_demand--;
			yield return rt;
		}
		FailureCondition:
		Plugin.LogInfo($"Done Generating Tiles!");
	}
	
	protected IEnumerable<ConnectionAction> HandleConnections(GameMap map) {
		// buffer actions so a connection can't be made and immediately disconnected 
		// before the player ever sees it
		List<ConnectionAction> actions = new();
		foreach (var action in ConnectTiles(map)) {
			actions.Add(action);
		}
		foreach (var action in DisconnectTiles(map)) {
			actions.Add(action);
		}
		return actions;
	}
	
	private IEnumerable<ConnectAction> ConnectTiles(GameMap map) {
		Plugin.LogInfo($"Queueing making some loops...");
		foreach (IList<Doorway> doorways in map.GetOverlappingLeaves()) {
			if (doorways[0].Fits(doorways[1]) && Rng.NextDouble() < DoorwayConnectionChance) {
				#if VERBOSE_GENERATION
				Plugin.LogDebug(
					$"C {doorways[0].Tile.name}.{doorways[0].name} | {doorways[1].Tile.name}.{doorways[1].name}"
				);
				#endif
				yield return new ConnectAction(doorways[0], doorways[1]);
			}
		}
	}
	private IEnumerable<DisconnectAction> DisconnectTiles(GameMap map) {
		Plugin.LogInfo($"Queueing removing some loops...");
		foreach ((Doorway d1,Doorway d2) in map.GetExtraConnections()) {
			if (Rng.NextDouble() < DoorwayDisconnectChance) {
				#if VERBOSE_GENERATION
				Plugin.LogDebug(
					$"D {d1.Tile.name}.{d1.name} | {d2.Tile.name}.{d2.name}"
				);
				#endif
				yield return new DisconnectAction(d1,d2);
			}
		}
	}
	
	protected virtual IEnumerable<PropAction> HandleProps(DGameMap map) {
		map.InitializeGlobalPropSets(this);
		foreach (var i in HandleDoorProps(map)) yield return i;
		foreach (var i in HandleTileProps(map)) yield return i;
		foreach (var i in HandleMapProps (map)) yield return i;
	}
	
	private IEnumerable<PropAction> HandleDoorProps(DGameMap map) {
		#if VERBOSE_GENERATION
		Plugin.LogDebug($"Handling random door objects...");
		#endif
		var doorways = map.GetComponentsInChildren<DDoorway>();
		foreach (DDoorway door in doorways) {
			if (door.ActiveRandomObject != null ) continue;
			
			DDoorway tgt = ((door.IsVacant || Rng.Next(2) == 0) ? (door) : ((DDoorway)door.Connection));
			
			tgt.SetActiveObject((float)Rng.NextDouble());
		}
		// Disable any blocker which should not be in use
		foreach (DDoorway door in doorways) {
			if (!door.IsVacant) {
				foreach (Prop p in door.Blockers) {
					yield return new PropAction(p, false);
				}
			}
		}
		yield break;
	}
	
	private IEnumerable<PropAction> HandleMapProps(DGameMap map) {
		#if VERBOSE_GENERATION
		Plugin.LogDebug($"Handling global props...");
		#endif
		foreach (PropSet propset in map.GlobalPropSets) {
			#if VERBOSE_GENERATION
			Plugin.LogDebug(
				$"Handling propset w/ {(propset.Count > 0 ? propset[0.0f].name : "nothing in it")} "
				+$"({propset.Count} props)"
			);
			#endif
			foreach (var action in HandlePropSetPos(propset)) {
				#if VERBOSE_GENERATION
				Plugin.LogDebug($"+{action.Prop.name}");
				#endif
				yield return action;
			}
			foreach (var action in HandlePropSetNeg(propset,true)) {
				#if VERBOSE_GENERATION
				Plugin.LogDebug($"-{action.Prop.name}");
				#endif
				yield return action;
			}
		}
	}
	
	private IEnumerable<PropAction> HandleTileProps(DGameMap map) {
		#if VERBOSE_GENERATION
		Plugin.LogDebug($"Handling local props...");
		#endif
		foreach (DTile tile in map.GetComponentsInChildren<DTile>()) {
			foreach (PropSet propset in tile.LocalPropSets) {
				foreach (var action in HandlePropSetPos(propset)) {
					#if VERBOSE_GENERATION
					Plugin.LogDebug($"+{action.Prop.name}");
					#endif
					yield return action;
				}
			}
			foreach (PropSet propset in tile.LocalPropSets) {
				foreach (var action in HandlePropSetNeg(propset)) {
					#if VERBOSE_GENERATION
					Plugin.LogDebug($"-{action.Prop.name}");
					#endif
					yield return action;
				}
			}
		}
	}
	
	private IEnumerable<PropAction> HandlePropSetPos(PropSet propset) {
		WeightedList<Prop> copy = new();
		int numActive = 0;
		int numEnable = Rng.Next(propset.Range.min,propset.Range.max+1);
		if (numEnable > propset.Count) numEnable = propset.Count;
		
		foreach ((Prop prop,float weight) in propset.Entries) {
			if (prop.gameObject.activeSelf) numActive++;
			else if (!prop.IsDoorProp) copy.Add(prop,weight);
		}
		for (int i=numActive; i<numEnable; i++) {
			Prop tgt;
			do {
				if (copy.Count == 0) {tgt = null; break;}
				tgt = copy[copy.SummedWeight*(float)Rng.NextDouble()];
				copy.Remove(tgt);
			} while (
				tgt.gameObject.activeSelf 
				|| !tgt.transform.parent.gameObject.activeInHierarchy
			);
			if (tgt == null) break;
			yield return new PropAction(tgt,true);
		}
	}
	
	private IEnumerable<PropAction> HandlePropSetNeg(PropSet propset, bool globalPropSet=false) {
		WeightedList<Prop> copy = new();
		int numActive = 0;
		int numEnable = propset.Range.max;
		if (numEnable > propset.Count) numEnable = propset.Count;
		
		foreach ((Prop prop,float weight) in propset.Entries) {
			if (prop.gameObject.activeInHierarchy) {
				numActive++;
				copy.Add(prop,weight);
			}
		}
		for (int i=numActive; i>numEnable; i--) {
			if (copy.Count == 0) break;
			Prop tgt = copy[copy.SummedWeight*(float)Rng.NextDouble()];
			copy.Remove(tgt);
			yield return new PropAction(tgt,false);
		}
	}
	
	public virtual IEnumerable<GenerationAction> Generator(GameMap m) {
		DGameMap map = (DGameMap)m;
		Tile start = GetStartTile();
		
		if (map.RootTile == null) {
			this.tile_demand = (uint)(1.5f * tile_demand);
			var action = PlaceRoot();
			if (action == null) yield break;
			yield return action;
		} else {
			foreach (var action in RemoveTiles(map)) {
				yield return action;
			}
		}
		foreach (var action in PlaceTiles(map)) {
			yield return action;
		}
		
		foreach (var action in HandleConnections(map)) {
			yield return action;
		}
		
		foreach (var action in HandleProps(map)) {
			yield return action;
		}
	}
	
}

public class DGameMapSerializer : GameMapSerializer<DGameMap, DTile, DTileSerializer> {
	private void DeserializeMapObjects<T,U>(DGameMap map, DeserializationContext dc)
		where T : MapObject
		where U : MapObjectSerializer<T>, new()
	{
		dc.Consume(sizeof(ushort)).CastInto(out ushort count);
		#if VERBOSE_DESERIALIZE
			Plugin.LogDebug(
				$"Loading {count} {typeof(T)} objects for DGameMap '{map.name}' from address 0x{dc.Address:X}"
			);
		#endif
		U ds = new();
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(ds,map);
		}
	}
	
	private void SerializeMapObjects<T>(
		SerializationContext sc, 
		DGameMap map, 
		ISerializer<T> ser
	) where T : MapObject {
		T[] objs = map.GetComponentsInChildren<T>(includeInactive: true);
		sc.Add(((ushort)objs.Length).GetBytes());
		foreach (T o in objs) {
			sc.AddInline(o, ser);
		}
	}
	
	public override void Serialize(SerializationContext sc, DGameMap tgt) {
		base.Serialize(sc,tgt);
		
		SerializeMapObjects<Scrap>(sc,tgt,new ScrapSerializer());
		SerializeMapObjects<Equipment>(sc,tgt,new EquipmentSerializer());
	}
	
	// extraContext is a DTileSerializer or null for a new instance
	protected override DGameMap Deserialize(
		DGameMap rt, DeserializationContext dc, object extraContext=null
	) {
		base.Deserialize(rt,dc,extraContext);
		DeserializeMapObjects<Scrap, ScrapSerializer>(rt,dc);
		DeserializeMapObjects<Equipment, EquipmentSerializer>(rt,dc);
		
		return rt;
	}
	
	public override void Finalize(DGameMap map) {
		map.InitializeLoadedMapObjects();
		map.gameObject.SetActive(false);
	}
}

public class DTileSerializer : TileSerializer<DTile> {
	
	public override void Serialize(SerializationContext sc, DTile tgt) {
		base.Serialize(sc,tgt);
		
		IList<Prop> props = tgt.GetProps();
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"Found {props.Count} props");
		#endif
		sc.Add((ushort)props.Count);
		
		ulong total = sc.AddBools<Prop>(
			props,
			(Prop p) => p.gameObject.activeSelf
		);
		if (total != (ulong)props.Count) {
			throw new Exception(
				$"Iterating over props had != props.Count iterations! ({total} != {props.Count})"
			);
		}
	}
	
	
	protected override DTile Deserialize(
		DTile tile, DeserializationContext dc, object extraContext=null
	) {
		base.Deserialize(tile,dc,extraContext);
		
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"Deserializing {tile.name}");
		#endif
		
		dc.Consume(2).CastInto(out ushort propCount);
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"Found {propCount} props");
		#endif
		
		IList<Prop> props = tile.GetProps();
		if (propCount != props.Count) {
			throw new Exception(
				 $"The amount of props stored in file for {tile.name} is not the same as "
				+$"the number of props the tile actually has. ({propCount} != {props.Count})"
			);
		}
		
		int i=0;
		foreach (bool flag in dc.ConsumeBools(propCount)) {
			props[i++].SetActive(flag);
		}
		
		return tile;
	}
}

public class DGameMapNetworkSerializer : Serializer<DGameMap> {
	
	private void SerializeMapObjects<T>(SerializationContext sc, DGameMap map, ISerializer<T> serializer) 
		where T : MapObject 
	{
		T[] objs = map.GetComponentsInChildren<T>(includeInactive: true);
		sc.Add((ushort)objs.Length);
		foreach (T o in objs) {
			sc.AddInline(o,serializer);
		}
	}
	
	public override void Serialize(SerializationContext sc, DGameMap m) {
		sc.Add(m.name+"\0");
		
		SerializeMapObjects<Scrap>(sc, m, new ScrapNetworkSerializer());
		SerializeMapObjects<Equipment>(sc, m, new EquipmentNetworkSerializer());
	}
	
	private void DeserializeMapObjects<T>(
		DGameMap map, DeserializationContext dc, ISerializer<T> ds
	)
		where T : MapObject
	{
		dc.Consume(sizeof(ushort)).CastInto(out ushort count);
		#if VERBOSE_DESERIALIZE
			Plugin.LogDebug(
				$"Loading {count} {typeof(T)} objects for DGameMap '{map.name}' from address 0x{dc.Address:X}"
			);
		#endif
		
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(ds,map);
		}
	}
	
	protected override DGameMap Deserialize(DGameMap map, DeserializationContext dc, object extraContext=null) {
		DeserializeMapObjects<Scrap    >(map, dc, new ScrapNetworkSerializer());
		DeserializeMapObjects<Equipment>(map, dc, new EquipmentNetworkSerializer());
		
		return map;
	}
	
	public override DGameMap Deserialize(DeserializationContext dc, object extraContext=null) {
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string id);
		dc.Consume(1);
		
		Moon moon = (Moon)extraContext; 
		DGameMap map = moon.transform.Find(id).GetComponent<DGameMap>();
		return Deserialize(map,dc,extraContext);
	}
}