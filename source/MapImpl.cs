namespace LabyrinthianFacilities;

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

using Object = UnityEngine.Object;
using Random = System.Random;

public class DDoorway : Doorway {
	// Properties
	public float VerticalOffset => this.Tile.IntersectionTolerance + 0.125f;
	public Vector3 PositionOffset => this.transform.up * VerticalOffset;
	public Prop ActiveRandomObject {
		get => activeRandomObject;
		protected set => activeRandomObject=value;
	}
	public IEnumerable<Prop> Blockers {get {
		foreach (Prop p in alwaysBlockers) yield return p;
		foreach (Prop p in randomBlockerSet) yield return p;
	}}
	// does not include any borrowed activeRandomObject
	public IEnumerable<Prop> Connectors {get {
		foreach (Prop p in alwaysDoors) yield return p;
		foreach (Prop p in randomDoorSet) yield return p;
	}}
	public bool OwnsActiveRandomObject {
		get => (IsVacant ? randomBlockerSet : randomDoorSet).Contains(ActiveRandomObject);
	}
	
	// Protected/Private
	protected List<Prop> alwaysBlockers;
	protected List<Prop> alwaysDoors;
	protected WeightedList<Prop> randomBlockerSet;
	protected WeightedList<Prop> randomDoorSet;
	
	protected Prop activeRandomObject = null;
	
	// Helper Methods
	// Round door rotation to nearest 90 degrees
	private void fixRotation() {
		Vector3 old = this.transform.rotation.eulerAngles;
		this.transform.rotation = Quaternion.Euler(new Vector3(
			(int)(old.x+0.5f),
			(int)(old.y+0.5f),
			(int)(old.z+0.5f)
		));
	}
	
	private Prop instantiateSubPart(GameObject o, bool isblocker) {
		if (o == null) return null;
		
		// Reinstantiate subparts that do not already exist
		if (o.GetComponentInParent<Tile>(includeInactive: true) != this.Tile) {
			o = GameObject.Instantiate(o);
			o.transform.SetParent(this.transform);
			o.transform.localPosition = -this.PositionOffset;
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
				$"DDoorway has no parent tile. "
			);
		}
		// make sure doorway's position is not on edge of bounding box (as opposed to just on a face)
		this.transform.position += PositionOffset;
		
		var dg = this.GetComponent<DunGen.Doorway>();
		
		List<Prop> objs = new(dg.BlockerSceneObjects.Count);
		for (int i=0; i<dg.BlockerSceneObjects.Count; i++) {
			var blocker = dg.BlockerSceneObjects[i];
			if (blocker == null) continue;
			var b = instantiateSubPart(blocker,true);
			b.Enable();
			objs.Add(b);
		}
		this.alwaysBlockers = objs;
		
		objs = new(dg.ConnectorSceneObjects.Count);
		for (int i=0; i<dg.ConnectorSceneObjects.Count; i++) {
			var door = dg.ConnectorSceneObjects[i];
			if (door == null) continue;
			var d = instantiateSubPart(door,false);
			d.Disable();
			objs.Add(d);
		}
		this.alwaysDoors = objs;
		
		this.randomBlockerSet = new(dg.BlockerPrefabWeights.Count);
		foreach (var entry in dg.BlockerPrefabWeights) {
			var blocker = entry.GameObject;
			if (blocker == null) continue;
			var b = instantiateSubPart(blocker,true);
			b.Disable();
			this.randomBlockerSet.Add(b,entry.Weight);
		}
		
		this.randomDoorSet = new(dg.ConnectorPrefabWeights.Count);
		foreach (var entry in dg.ConnectorPrefabWeights) {
			var door = entry.GameObject;
			if (door == null) continue;
			var d = instantiateSubPart(door,false);
			d.Disable();
			this.randomDoorSet.Add(d,entry.Weight);
		}
	}
	
	// Native Methods
	protected bool CheckDunGenRule(DunGen.TileConnectionRule rule, DDoorway other) {
		return rule.Delegate?.Invoke(
			this.Tile.GetComponent<DunGen.Tile>(),
			other.Tile.GetComponent<DunGen.Tile>(),
			this.GetComponent<DunGen.Doorway>(),
			other.GetComponent<DunGen.Doorway>()
		) != DunGen.TileConnectionRule.ConnectionResult.Deny;
	}
	public override bool Fits(Doorway o) {
		if (!(o is DDoorway other)) return false;
		if (!base.Fits(other)) return false;
		foreach (var rule in DunGen.DoorwayPairFinder.CustomConnectionRules) {
			if (!CheckDunGenRule(rule,other)) return false;
		}
		return true;
	}
	
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
	
	public virtual void SetActiveObject(Prop prop) {
		this.activeRandomObject?.Disable();
		DDoorway con = (DDoorway)this.connection;
		if (IsVacant) {
			if (!this.randomBlockerSet.Contains(prop)) {
				throw new ArgumentException(
					$"Provided prop '{prop.name}' is not a randomBlocker of the door "
					+$"'{this.Tile.name}:{this.name}'"
				);
			}
		} else {
			if (!this.randomDoorSet.Contains(prop)) {
				throw new ArgumentException(
					"Provided prop '{prop.name}' is not a randomDoor of the door "
					+$"'{this.Tile.name}:{this.name}'"
				);
			}
		}
		this.activeRandomObject = prop;
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
	
	public bool RemoveProp(Prop p) {
		return (
			alwaysBlockers.Remove(p) 
			|| alwaysDoors.Remove(p) 
			|| randomBlockerSet.Remove(p) 
			|| randomDoorSet.Remove(p)
		);
	}
}

public class Prop : MonoBehaviour {
	public bool IsBlocker  {get; set;} = false;
	public bool IsConnector{get; set;} = false;
	public bool IsTileProp {get; set;} = false;
	public bool IsMapProp  {get; set;} = false;
	
	public bool IsDoorProp => IsBlocker || IsConnector;
	public DTile Tile => this.GetComponentInParent<DTile>(this.gameObject.activeInHierarchy);
	public DDoorway Doorway => this.GetComponentInParent<DDoorway>(this.gameObject.activeInHierarchy);
	public DGameMap Map => this.GetComponentInParent<DGameMap>(this.gameObject.activeInHierarchy);
	public MonoBehaviour Parent => IsDoorProp ? Doorway : Tile;
	
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
	
	protected virtual void OnDestroy() {
		if (IsDoorProp) {
			var d = this.Doorway;
			if (d != null) d.RemoveProp(this);
		} 
		var t = this.Tile;
		if (t != null) t.RemoveProp(this);
		var map = this.Map;
		if (map != null) map.RemoveProp(this);
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
	
	public override bool Add(Prop p, float weight) {
		if (p == null) return false;
		if (this.Remove(p,out float oldWeight)) {
			return this.Add(p,weight + oldWeight);
		} else {
			return base.Add(p,weight);
		}
	}
}

public class DTile : Tile {
	// Properties
	internal IList<PropSet> LocalPropSets => (
		(IList<PropSet>)localPropSets?.AsReadOnly() ?? (IList<PropSet>)new PropSet[0]
	);
	internal IList<(Prop prop, int id, float weight)> GlobalProps => (
		(IList<(Prop prop, int id, float weight)>)globalProps?.AsReadOnly() 
		?? (IList<(Prop prop, int id, float weight)>)new (Prop prop, int id, float weight)[0]
	);
	
	// Public
	public List<Func<bool,PlacementInfo>> ValidatePlacement;
	
	// Protected/Private
	protected List<PropSet> localPropSets;
	protected List<(Prop prop, int id, float weight)> globalProps;
	
	// Helper Methods
	private Bounds DeriveBounds() {
		Bounds bounds;
		var dungenTile = this.GetComponent<DunGen.Tile>();
		if (dungenTile.OverrideAutomaticTileBounds && dungenTile.TileBoundsOverride.extents != Vector3.zero) {
			return dungenTile.TileBoundsOverride;
		}
		#if VERBOSE_TILE_INIT
		Plugin.LogDebug($"Tile {this.gameObject} had no predefined bounds");
		#endif
		
		if (!this.gameObject.activeInHierarchy) {
			throw new InvalidOperationException($"Tile {this.name} is not active; cannot derive bounds");
		}
		
		bounds = new Bounds(this.transform.position,Vector3.zero);
		
		var renders = this.GetComponentsInChildren<Renderer>();
		foreach (Renderer r in renders ?? (Renderer[])[]) {
			if (bounds.extents == Vector3.zero) {
				bounds = r.bounds;
			} else {
				bounds.Encapsulate(r.bounds);
			}
		}
		if (bounds.extents == Vector3.zero) {
			var colliders = this.GetComponentsInChildren<Collider>();
			foreach (Collider c in colliders ?? (Collider[])[]) {
				if (bounds.extents == Vector3.zero) {
					bounds = c.bounds;
				} else {
					bounds.Encapsulate(c.bounds);
				}
			}
		}
		// The (horizontal) doorways should form a rectangle because of DunGen's rules with doors 
		// (Doorways must be on AABB) (also we are ignoring the y axis)
		// Constrain bounding box until all doors lay on it
		
		// !! THIS IGNORES THE FACT THAT SOME DOORS ARE VERTICAL !!
		// 1. Find smallest rectangle containing all doors
		// 2. Change bounds s.t. one edge runs along the corresponding edge in the rectangle formed by the doors
		//    Choose the edge(s) that require the least change in perimeter of the bounds
		// 3. Repeat until all doors are on the edge of the bounding box
		//    (this should only take two iterations, since there are only two dimensions to control)
		Vector2 min = new Vector2(Single.PositiveInfinity,Single.PositiveInfinity);
		Vector2 max = new Vector2(Single.NegativeInfinity,Single.NegativeInfinity);
		foreach (Doorway d in this.Doorways) {
			// ignore ceiling/floor doors. Yes this will probably become an issue
			if (d.transform.up != Vector3.up) continue; 
			Vector3 pos = d.transform.position;
			
			if (pos.x < min.x) min.x = pos.x;
			if (pos.x > max.x) max.x = pos.x;
			if (pos.z < min.y) min.y = pos.z;
			if (pos.z > max.y) max.y = pos.z;
		}
		int attemptCtr = 0;
		do {
			bool done = true;
			foreach (Doorway d in this.Doorways) {
				if (d.transform.up != Vector3.up) continue;
				Vector3 pos = d.transform.position;
				if (bounds.ClosestFace(pos).bounds.SqrDistance(pos) > 0.0625f) {done = false; break;}
			}
			if (done) break;
			
			float dminx = Math.Abs(bounds.min.x - min.x);
			float dmaxx = Math.Abs(bounds.max.x - max.x);
			float dminz = Math.Abs(bounds.min.z - min.y);
			float dmaxz = Math.Abs(bounds.max.z - max.y);
			
			if (
				dminx != 0
				&& dminx < dmaxx
				&& dminx < dminz
				&& dminx < dmaxz
			) {
				bounds.min = new Vector3(min.x,bounds.min.y,bounds.min.z);
			} else if (
				dmaxx != 0
				&& dmaxx < dminz
				&& dmaxx < dmaxz
			) {
				bounds.max = new Vector3(max.x,bounds.max.y,bounds.max.z);
			} else if (
				dminz != 0
				&& dminz < dmaxz
			) {
				bounds.min = new Vector3(bounds.min.x,bounds.min.y,min.y);
			} else if (
				dmaxz != 0
			) {
				bounds.max = new Vector3(bounds.max.x,bounds.max.y,max.y);
			} else {
				Plugin.LogWarning($"No non-zero change in bounds to accomodate doors?");
			}
			
			if (++attemptCtr > 10) break;
		} while (true);
		
		/* foreach (Doorway d in this.Doorways) {
			// if the doorway is completely within the bounds, 
			// shrink the bounds whichever way requires the least change
			float x = d.transform.position.x;
			float z = d.transform.position.z;
			
			// filter out doorways that aren't in trouble
			// "in trouble" means engulfed by tile bounds
			if (!(x > bounds.min.x && x < bounds.max.x && z > bounds.min.z && z < bounds.max.z)) continue;
			
			// choose dimension to change based on door's direction
			byte mode=0;
			float yrot = d.transform.rotation.eulerAngles.y;
			float diff = 0 - yrot;
			if (diff < 0) diff += 360;
			if (diff > 180) diff = 360-diff;
			
			float difftemp = 90 - yrot;
			if (difftemp < 0) difftemp += 360;
			if (difftemp > 180) difftemp = 360-difftemp;
			if (difftemp < diff) {
				mode = 1;
				diff = difftemp;
			}
			difftemp = 180 - yrot;
			if (difftemp < 0) difftemp += 360;
			if (difftemp > 180) difftemp = 360-difftemp;
			if (difftemp < diff) {
				mode = 2;
				diff = difftemp;
			}
			difftemp = 270 - yrot;
			if (difftemp < 0) difftemp += 360;
			if (difftemp > 180) difftemp = 360-difftemp;
			if (difftemp < diff) {
				mode = 3;
				diff = difftemp;
			}
			switch (mode) {
				case 0:
					bounds.max = new Vector3(bounds.max.x,bounds.max.y,z);
				break; case 1:
					bounds.max = new Vector3(x,bounds.max.y,bounds.max.z);
				break; case 2:
					bounds.min = new Vector3(bounds.min.x,bounds.min.y,z);
				break; case 3:
					bounds.min = new Vector3(x,bounds.min.y,bounds.min.z);
				break; default:
					throw new NotImplementedException("Invalid case - how did we get here?");
				// break;
			}
		} */
		
		// Special rules
		switch (this.gameObject.name) {
			case "ElevatorConnector(Clone)":
				bounds = new Bounds(Vector3.zero,Vector3.zero);
			break; case "StartRoom(Clone)":
				bounds.Encapsulate(
					this.transform.Find("VisualMesh").Find("StartRoomElevator")
						.GetComponent<MeshFilter>().sharedMesh.bounds
				);
			break; case "DoubleDoorRoom(Clone)":
				// copy Mesh (1) MeshRenderer bounds
				bounds = new Bounds(new Vector3(7.12f,-2.94f,19.88f),new Vector3(9.13f,2.69f,14.25f));
			break; default:
				if (bounds.extents == Vector3.zero) {
					Plugin.LogError($"Tile {this} has zero bounds");
				}
			break;
			
		}
		
		#if VERBOSE_TILE_INIT
		Plugin.LogDebug($"{this.name} extents: {bounds.extents}");
		#endif
		
		return bounds;
	}
	
	public override void Initialize(Tile prefab) {
		if (this.Initialized) return;
		base.Initialize(prefab);
		this.Initialized = true;
		
		// Do not allow nested tiles
		foreach (Tile t in this.GetComponentsInChildren<Tile>(true)) {
			if (t != this) {
				UnityEngine.Object.Destroy(t);
			}
		}
		
		// Bounds
		#if VERBOSE_TILE_INIT
		Plugin.LogDebug("Getting bounds...");
		#endif
		Bounds bounds = this.DeriveBounds();
		bounds.FixExtents();
		this.bounding_box = bounds;
		
		// Doorways
		#if VERBOSE_TILE_INIT
		Plugin.LogDebug("Initializing Doorways...");
		#endif
		foreach (DDoorway d in this.Doorways) {
			d.Initialize();
		}
		
		// Props
		#if VERBOSE_TILE_INIT
		Plugin.LogDebug("Retrieving Props...");
		#endif
		
		// Local Props
		var localPropSets = this.GetComponentsInChildren<DunGen.LocalPropSet>(includeInactive:true);
		List<PropSet> psets = new(localPropSets.Length);
		foreach (var localPropSet in localPropSets) {
			psets.Add(new PropSet(localPropSet));
		}
		this.localPropSets = psets;
		
		// Global Props
		var globs = this.GetComponentsInChildren<DunGen.GlobalProp>(includeInactive:true);
		this.globalProps = new List<(Prop prop, int id, float weight)>(globs.Length);
		for (int i=0; i<globs.Length; i++) {
			DunGen.GlobalProp globalProp = globs[i];
			if (globalProp?.gameObject == null) continue;
			
			Prop prop = globalProp.GetComponent<Prop>() ?? globalProp.gameObject.AddComponent<Prop>();
			prop.IsMapProp = true;
			this.globalProps.Add((
				prop, 
				globalProp.PropGroupID, 
				(globalProp.MainPathWeight + globalProp.BranchPathWeight) / 2.0f
			));
		}
	}
	
	public bool RemoveProp(Prop p) {
		bool rt = false;
		foreach (PropSet ps in this.localPropSets) {
			rt = rt || ps.Remove(p);
		}
		for (int i=0; i<this.globalProps.Count;) {
			if (this.globalProps[i].prop == p) {
				rt = true;
				this.globalProps.RemoveAt(i);
			} else {
				i++;
			}
		}
		foreach (Doorway d in this.Doorways) {
			rt = rt || ((DDoorway)d).RemoveProp(p);
		}
		return rt;
	}
	
	public IList<Prop> GetProps() {
		List<Prop> rt = new();
		foreach ((Prop prop, int id, float weight) in this.globalProps) {
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
	
	public delegate bool TilePlacementVerifier(
		DTile newTile, DTile oldTile, DDoorway newDoorway, DDoorway oldDoorway
	);
	public event TilePlacementVerifier TilePlacementVerifiers;
	
	protected Dictionary<int, PropSet> globalPropSets;
	protected Dictionary<int, PropSet> uninitializedGlobalPropSets;
	
	internal List<GameObject> CaveLights = new();
	
	// Properties
	public Moon Moon => this.transform.parent.GetComponent<Moon>();
	public IReadOnlyCollection<PropSet> GlobalPropSets => globalPropSets.Values;
	
	// Constructors/Initialization
	protected override void Awake() {
		base.Awake();
		
		this.transform.position = new Vector3(0,-200,0);
		
		this.GenerationCompleteEvent += DGameMap.GenerationCompleteHandler;
		this.TileRemovalEvent += this.RemoveTileAction;
		globalPropSets = new();
		uninitializedGlobalPropSets = new();
	}
	
	// Native Methods
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
	
	public void AddGlobalProps(DTile tile) {
		foreach ((Prop globalProp, int id, float weight) in tile.GlobalProps) {
			GetGlobalPropSet(id).Add(globalProp, weight);
		}
	}
	
	public override Result<Tile,string> AddTile(PlacementInfo placement) {
		var result = base.AddTile(placement);
		if (result.isOk) {
			DTile tile = (DTile)result.Ok;
			this.AddGlobalProps(tile);
		}
		return result;
	}
	
	public void RemoveTileProps(DTile tile) {
		if (tile.GlobalProps == null) return;
		foreach ((Prop prop,int id,float weight) in tile.GlobalProps) {
			if (!globalPropSets.TryGetValue(id, out PropSet pset)) {
				if (!uninitializedGlobalPropSets.TryGetValue(id, out pset)) {
					continue;
				}
			}
			pset.Remove(prop);
		}
	}
	public bool RemoveProp(Prop p) {
		bool rt = false;
		foreach (PropSet ps in globalPropSets.Values.Concat(uninitializedGlobalPropSets.Values)) {
			rt = rt || ps.Remove(p);
		}
		return rt;
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
		foreach (GameObject light in CaveLights) {
			GameObject.Destroy(light);
		}
		CaveLights.Clear();
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
		foreach (GrabbableMapObject obj in Object.FindObjectsByType<GrabbableMapObject>(FindObjectsSortMode.None)) {
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
	
	public override bool VerifyTilePlacement(PlacementInfo placement) {
		if (!base.VerifyTilePlacement(placement)) return false;
		
		if (TilePlacementVerifiers == null) return true;
		
		DTile    thisTile     = (DTile   )placement.NewTile;
		DDoorway thisDoorway  = (DDoorway)thisTile.Doorways[placement.NewDoorwayIdx];
		DDoorway otherDoorway = (DDoorway)placement.AttachmentPoint;
		DTile    otherTile    = (DTile   )otherDoorway.Tile;
		
		foreach (TilePlacementVerifier verifier in TilePlacementVerifiers.GetInvocationList()) {
			if (!verifier(thisTile,otherTile,thisDoorway,otherDoorway)) {
				return false;
			}
		}
		
		return true;
	}
	
	private void RemoveTileAction(Tile t) => RemoveTileProps((DTile)t);
}

public sealed class DGameMapSerializer : GameMapSerializer<DGameMap, DTile> {
	
	private Moon parent;
	public Moon Parent {get => parent;}
	
	public DGameMapSerializer(Moon p) : base(null) {parent = p;}
	public DGameMapSerializer() : this(null) {}
	
	public override void Serialize(SerializationContext sc, DGameMap tgt) {
		base.TileSer = new DTileSerializer(tgt);
		base.Serialize(sc,tgt);
		
		MapObject[] allMapObjects = tgt.GetComponentsInChildren<MapObject>(true);
		Dictionary<string,List<MapObject>> mapObjLists = new();
		foreach (var mapObj in allMapObjects) {
			if (!mapObjLists.TryGetValue(mapObj.name, out List<MapObject> list)) {
				list = new(allMapObjects.Length);
				mapObjLists.Add(mapObj.name, list);
			}
			list.Add(mapObj);
		}
		sc.Add((ushort)mapObjLists.Count);
		
		var groupSerializer = new MapObjectGroupSerializer<MapObject>(tgt);
		foreach (List<MapObject> mapObjList in mapObjLists.Values) {
			sc.AddInline(mapObjList, groupSerializer);
		}
		
	}
	
	protected override DGameMap Deserialize(
		DGameMap rt, DeserializationContext dc
	) {
		base.TileSer = new DTileSerializer(rt);
		base.Deserialize(rt,dc);
		if (Parent == null) { // probably the most stupid way to do this
			GameObject.Destroy(rt.gameObject);
			rt = null;
		}
		
		dc.Consume(sizeof(ushort)).CastInto(out ushort numMapObjTypes);
		
		var groupSer = new MapObjectGroupSerializer<MapObject>(rt);
		
		for (ushort i=0; i<numMapObjTypes; i++) {
			dc.ConsumeInline(groupSer);
		}
		
		return rt;
	}
	
	public override void Finalize(DGameMap map) {
		if (map == null) return;
		map.InitializeLoadedMapObjects();
		map.gameObject.SetActive(false);
	}
}

public sealed class DTileSerializer : TileSerializer<DTile> {
	
	private Queue<bool[]> doorwaysHaveActiveProp;
	private Queue<Prop[]> activeProps;
	public DTileSerializer(GameMap p) : base(p) {
		doorwaysHaveActiveProp = new();
		activeProps = new();
	}
	
	public override void Serialize(SerializationContext sc, DTile tgt) {
		base.Serialize(sc,tgt);
		
		IList<Prop> props = tgt.GetProps();
		if (SerializationContext.Verbose) Plugin.LogDebug($"Found {props.Count} props");
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
		
		Func<Doorway,bool> foo = (Doorway d) => (
			((DDoorway)d).ActiveRandomObject != null 
			&& ((DDoorway)d).ActiveRandomObject.gameObject.activeSelf
			&& ((DDoorway)d).OwnsActiveRandomObject
		);
		total = sc.AddBools<Doorway>(tgt.Doorways, foo);
		if (total != (ulong)tgt.Doorways.Length) {
			throw new Exception(
				$"Iterating over doorways had != Doorways.Length iterations! ({total} != {tgt.Doorways.Length})"
			);
		}
		foreach (Doorway d in tgt.Doorways) {
			if (foo(d)) sc.Add((ushort)props.IndexOf(((DDoorway)d).ActiveRandomObject));
		}
	}
	
	
	protected override DTile Deserialize(
		DTile tile, DeserializationContext dc
	) {
		base.Deserialize(tile,dc);
		
		if (DeserializationContext.Verbose) Plugin.LogDebug($"Deserializing {tile.name}");
		
		dc.Consume(2).CastInto(out ushort propCount);
		if (DeserializationContext.Verbose) Plugin.LogDebug($"Found {propCount} props");
		
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
		
		bool[] flags = [..dc.ConsumeBools((ulong)tile.Doorways.Length)];
		Prop[] randomProps = new Prop[flags.Length];
		for (i=0; i<tile.Doorways.Length; i++) {
			if (!flags[i]) {
				randomProps[i] = null;
				continue;
			}
			
			dc.Consume(sizeof(ushort)).CastInto(out ushort propIdx);
			randomProps[i] = props[propIdx];
		}
		doorwaysHaveActiveProp.Enqueue(flags);
		activeProps.Enqueue(randomProps);
		
		return tile;
	}
	
	public override void Finalize(DTile tile) {
		bool[] flags = doorwaysHaveActiveProp.Dequeue();
		Prop[] props = activeProps.Dequeue();
		if (flags.Length != props.Length) throw new Exception(
			$"Desync between flags.Length and props.Length ({flags.Length} != {props.Length})"
		);
		if (flags.Length != tile.Doorways.Length) throw new Exception(
			$"Num. Doorways does not match the number of flags ({tile.Doorways.Length} != {flags.Length})\n"
			+$"Tile: {tile.name}"
		);
		for (int i=0; i<flags.Length; i++) {
			if (!flags[i]) continue;
			((DDoorway)tile.Doorways[i]).SetActiveObject(props[i]);
		}
	}
}

public sealed class DGameMapNetworkSerializer : Serializer<DGameMap> {
	
	private Moon Moon;
	
	public DGameMapNetworkSerializer(Moon m) {
		this.Moon = m;
	}
	
	public override void Serialize(SerializationContext sc, DGameMap m) {
		sc.Add(m.name+"\0");
		
		new MapObjectCollection(m).Serialize(
			sc,
			new ScrapNetworkSerializer             <Scrap>           (m),
			new GrabbableMapObjectNetworkSerializer<Equipment>       (m),
			new BatteryEquipmentNetworkSerializer  <BatteryEquipment>(m),
			new GunEquipmentNetworkSerializer      <GunEquipment>    (m),
			new BatteryEquipmentNetworkSerializer  <FueledEquipment> (m)
		);
		
		HazardBase[] hazards = m.GetComponentsInChildren<HazardBase>(true);
		sc.Add((ushort)hazards.Length);
		var ser = new HazardNetworkSerializer<HazardBase>(m);
		foreach (var hazard in hazards) {
			sc.Add((ushort)1); // prep for array of arrays (so we don't use so much space on identifiers)
			sc.AddInline(hazard,ser);
		}
	}
	
	protected override DGameMap Deserialize(DGameMap map, DeserializationContext dc) {
		MapObjectCollection.Deserialize(
			dc,
			new ScrapNetworkSerializer             <Scrap>           (map),
			new GrabbableMapObjectNetworkSerializer<Equipment>       (map),
			new BatteryEquipmentNetworkSerializer  <BatteryEquipment>(map),
			new GunEquipmentNetworkSerializer      <GunEquipment>    (map),
			new BatteryEquipmentNetworkSerializer  <FueledEquipment> (map)
		);
		
		dc.Consume(sizeof(ushort)).CastInto(out ushort numHazardTypes);
		var ds = new HazardNetworkSerializer<HazardBase>(map);
		for (ushort i=0; i<numHazardTypes; i++) {
			dc.Consume(sizeof(ushort)).CastInto(out ushort numHazardsOfType); // placeholder
			dc.ConsumeInline(ds);
		}
		
		return map;
	}
	
	public override DGameMap Deserialize(DeserializationContext dc) {
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string id);
		dc.Consume(1);
		
		DGameMap map = Moon.transform.Find(id).GetComponent<DGameMap>();
		return Deserialize(map,dc);
	}
}