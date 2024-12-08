namespace LabyrinthianFacilities.DgConversion;

using BoundsExtensions;

using System;
using System.Collections;
using System.Collections.Generic;

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
		
		float lowest_dist = faces[0].bounds.SqrDistance(this.transform.position);
		RectFace closest_face = faces[0];
		for (uint i=2; i<6; i++) {
			if (/* i == 1 || */ i == 4) continue;
			float dist = faces[i].bounds.SqrDistance(this.transform.position);
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
		
		// Vector3 relpos = o.transform.localPosition;
		// Quaternion relrot = o.transform.localRotation;
		
		var newobj = GameObject.Instantiate(o);
		newobj.transform.SetParent(this.transform);
		// newobj.transform.localPosition = relpos;
		newobj.transform.localPosition = Vector3.zero;
		// newobj.transform.localRotation = relrot;
		newobj.transform.localRotation = Quaternion.identity;
		return newobj;
	}
	
	// Constructors/Initializers
	public DDoorway() {
		if (this.gameObject == null) return;
		InitSize();
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
			var b = instantiateSubPart(blocker);
			objs.Add(b);
			// b.SetActive(true); // (already active)
		}
		this.alwaysBlockers = objs.ToArray();
		
		objs = new();
		for (int i=0; i<dg.ConnectorSceneObjects.Count; i++) {
			var door = dg.ConnectorSceneObjects[i];
			if (door == null) continue;
			var d = instantiateSubPart(door);
			objs.Add(d);
			d.SetActive(false);
		}
		this.alwaysDoors = objs.ToArray();
		
		this.randomBlockerSet = new GameObject[dg.BlockerPrefabWeights.Count];
		for (int i=0; i<randomBlockerSet.Length; i++) {
			var blocker = dg.BlockerPrefabWeights[i].GameObject;
			var b = instantiateSubPart(blocker);
			this.randomBlockerSet[i] = b;
			b?.SetActive(false);
		}
		
		this.randomDoorSet = new GameObject[dg.ConnectorPrefabWeights.Count];
		for (int i=0; i<randomDoorSet.Length; i++) {
			var door = dg.ConnectorPrefabWeights[i].GameObject;
			var d = instantiateSubPart(door);
			this.randomDoorSet[i] = d;
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
	// Boss Doorway in charge of door
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
}

public class DTile : Tile {
	// Properties
	internal DunGen.GlobalProp[] GlobalProps {get {return globalProps;}}
	
	// Protected/Private
	protected DunGen.LocalPropSet[] localProps;
	protected DunGen.GlobalProp[] globalProps;
	protected List<GrabbableObject> ownedObjects;
	
	// Private Helper Methods
	private Bounds DeriveBounds() {
		//subject to change, because why have consistency with what is the actual mesh of the room
		//ajfshdlfjqew
		Bounds bounds = new Bounds(Vector3.zero,Vector3.zero);
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
		
		return bounds;
	}
	
	private void InitNavStuff(GameMap map) {
		if (this == null) {
			map.GenerationCompleteEvent -= InitNavStuff;
			return;
		}
		
		List<NavMeshBuildSource> navigables = new();
		List<NavMeshLinkData> links = new();
		
		// navmesh sources
		
		foreach (MeshFilter c in this.GetComponentsInChildren<MeshFilter>(includeInactive: false)) {
			NavMeshBuildSource src = new(); {
				src.area = 0;
				src.component = c;
				src.generateLinks = true;
				src.shape = NavMeshBuildSourceShape.Mesh; 
				src.size = c.sharedMesh.bounds.size;
				src.sourceObject = c.sharedMesh;
				src.transform = c.transform.localToWorldMatrix;
			}
			navigables.Add(src);
		}
		
		// links
		foreach (OffMeshLink link in this.GetComponentsInChildren<OffMeshLink>()) {
			var newlink = new NavMeshLinkData(); {
				newlink.agentTypeID = 0;
				newlink.area = 0;
				newlink.bidirectional = link.biDirectional;
				newlink.costModifier = link.costOverride; // assuming this is correct
				newlink.endPosition = link.endTransform.position;
				newlink.startPosition = link.startTransform.position;
				newlink.width = 1;
			}
			links.Add(newlink);
		}
		
		this.navigables = navigables.ToArray();
		this.links = links.ToArray();
	}
	
	protected override void Initialize() {
		if (this.Initialized) return;
		base.Initialize();
		this.Initialized = true;
		
		//Bounds
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
		
		// Local Props
		Plugin.LogDebug("Spawning LocalProps...");
		this.localProps = this.GetComponentsInChildren<DunGen.LocalPropSet>(includeInactive: true);
		this.DecideLocalProps();
		
		// Global Props (Not initialized here, just fetched for later initialization)
		Plugin.LogDebug("Fetching GlobalProps...");
		this.globalProps = this.GetComponentsInChildren<DunGen.GlobalProp>(includeInactive: true);
		
		// Plugin.LogDebug("Initializing Navigation");
		this.Map.GenerationCompleteEvent += this.InitNavStuff;
	}
	
	// Native Methods
	public void DecideLocalProps() {
		foreach (var propset in this.localProps) {
			Plugin.LogDebug($"Handling propset {propset}");
			
			// Disable by default
			foreach (DunGen.GameObjectChance propAndChance in propset.Props.Weights) {
				propAndChance.Value?.SetActive(false);
			}
			
			// Choose & Enable props
			int spawnCount = this.Map.rng.Next(propset.PropCount.Min,propset.PropCount.Max+1);
			DunGen.GameObjectChanceTable proptable = propset.Props;
			
			// List of props to choose from
			List<(GameObject prop, float chance)> props = new();
			foreach (var item in proptable.Weights) {
				float chance = (item.MainPathWeight + item.BranchPathWeight) / 2.0f;
				if (chance > 0 && item.Value != null) {
					props.Add((item.Value,chance));
				}
			}
			
			// Infinite-loop prevention
			if (spawnCount > props.Count) {
				Plugin.LogDebug(
					$"Not enough props. Max of {propset.PropCount.Max+1}, only had {spawnCount}"
				);
				spawnCount = props.Count;
			}
			
			for (int i=0; i<spawnCount; i++) {
				while (true) {
					int idx = this.Map.rng.Next(props.Count);
					(GameObject prop, float chance) = props[idx];
					
					// System.Random.NextSingle isn't defined????
					if (chance > (float)this.Map.rng.NextDouble()) { 
						prop.SetActive(true);
						props.RemoveAt(idx);
						break;
					}
				}
			}
		}
	}
}

public class DGameMap : GameMap {
	protected Dictionary<int, List<DunGen.GlobalProp>> globalPropSpawns;
	protected Dictionary<int, List<DunGen.GlobalProp>> activeGlobalProps;
	
	// Constructors/Initialization
	public DGameMap() {
		this.globalPropSpawns = new();
		this.activeGlobalProps = new();
		
		this.TileInsertionEvent += RegisterTileProps;
		this.GenerationCompleteEvent += this.RestoreScrap;
	}
	
	// Native Methods
	private void AddProp(DunGen.GlobalProp p) {
		List<DunGen.GlobalProp> propList = null;
		if (!globalPropSpawns.TryGetValue(p.PropGroupID,out propList)) {
			propList = new();
			globalPropSpawns.Add(p.PropGroupID,propList);
		}
		propList.Add(p);
	}
	
	private (int min,int max) GetPropRange(DunGen.Graph.DungeonFlow flow, int id) {
		foreach (var settings in flow.GlobalProps) {
			if (settings.ID == id) return (settings.Count.Min,settings.Count.Max);
		}
		Plugin.LogError($"Global Prop bounds not found for id {id}");
		return (0,1);
	}
	
	protected void RegisterTileProps(Tile tile) {
		if (tile == null) return;
		
		DTile t = (DTile)tile;
		foreach (DunGen.GlobalProp p in t.GlobalProps ?? []) {
			this.AddProp(p);
			p.gameObject.SetActive(false);
		}
	}
	
	protected void HandleGlobalProps(DunGen.Graph.DungeonFlow flow) {
		foreach (var entry in globalPropSpawns) {
			int id = entry.Key;
			var options = entry.Value;
			List<DunGen.GlobalProp> actives = null; activeGlobalProps.TryGetValue(id,out actives);
			
			foreach (DunGen.GlobalProp prop in actives ?? []) {
				prop.gameObject.SetActive(false);
			}
			activeGlobalProps[id] = new();
			
			(int min,int max) = GetPropRange(flow,id);
			int count = this.rng.Next(min,max);
			for (int i=0; i<count; i++) {
				if (options.Count == 0) break;
				int idx = this.rng.Next(options.Count);
				var prop = options[idx];
				options.RemoveAt(idx);
				prop.gameObject.SetActive(true);
				activeGlobalProps[id].Add(prop);
			}
		}
	}
	
	public override IEnumerator GenerateCoroutine(ITileGenerator tilegen, int? seed) {
		var dtilegen = (DungeonFlowConverter)tilegen;
		var foo = (GameMap.GenerationCompleteDelegate)(
			(GameMap m) => HandleGlobalProps(dtilegen.Flow)
		);
		this.GenerationCompleteEvent += foo;
		yield return StartCoroutine(base.GenerateCoroutine(tilegen,seed));
		this.GenerationCompleteEvent -= foo;
	}
	
	public void PreserveScrap() {
		Plugin.LogFatal($"{this} Preserve");
		foreach (Scrap obj in GameObject.FindObjectsByType<Scrap>(FindObjectsSortMode.None)) {
			Plugin.LogFatal($"{obj} Preserve");
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
	// NOT the max attempts to place any tile; if a tile fails to place within MAX_ATTEMPTS, a new tile
	// is chosen
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
		if (tile == null) this.tile_demand++;
	}
	
	public IEnumerable<PlacementInfo> Generator(GameMap map) {
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
			
			bool forelse = true;
			for (int i=0; i<MAX_ATTEMPTS; i++) {
				rt.NewDoorwayIdx = map.rng.Next(rt.NewTile.Doorways.Length);
				rt.AttachmentPoint = map.GetLeaf(rt.NewTile.Doorways[rt.NewDoorwayIdx].Size);
				if (rt.AttachmentPoint != null) {
					Plugin.LogMessage($"Size connections: {rt.NewTile?.Doorways?[rt.NewDoorwayIdx]?.Size} - {rt.AttachmentPoint?.Size}");
					forelse = false; break;
				}
			} if (forelse) {
				continue;
			}
			
			Plugin.LogDebug($"{this.tile_demand}: Yielding '{rt.NewTile.gameObject.name}'");
			this.tile_demand--;
			yield return rt;
		}
		Plugin.LogInfo($"Done Generating Tiles!");
	}
	
}