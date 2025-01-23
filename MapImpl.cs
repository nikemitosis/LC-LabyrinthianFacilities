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
	public bool IsBossDoor {get {return this.randomDoorSet.Range == (1,1);}}
	public PropSet[] PropSets {get {
		return new PropSet[4]{alwaysBlockers,alwaysDoors,randomBlockerSet,randomDoorSet};
	}}
	
	// Protected/Private
	protected PropSet alwaysBlockers;
	protected PropSet alwaysDoors;
	protected PropSet randomBlockerSet;
	protected PropSet randomDoorSet;
	
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
	
	private void RegisterProp(GameObject b, PropSet ps, float weight=1.0f) {
		bool isBlocker = ReferenceEquals(ps,alwaysBlockers) || ReferenceEquals(ps,randomBlockerSet);
		
		var prop = b.GetComponent<TileProp>() ?? b.AddComponent<TileProp>();
		ps.Add(prop,weight);
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
		
		this.alwaysBlockers = new((DGameMap)this.Tile.Map, default);
		for (int i=0; i<dg.BlockerSceneObjects.Count; i++) {
			var blocker = dg.BlockerSceneObjects[i];
			if (blocker == null) continue;
			var b = instantiateSubPart(blocker);
			RegisterProp(b,this.alwaysBlockers);
		}
		this.alwaysBlockers.Range = (this.alwaysBlockers.Count,this.alwaysBlockers.Count);
		
		this.alwaysDoors = new((DGameMap)this.Tile.Map, (0,0));
		for (int i=0; i<dg.ConnectorSceneObjects.Count; i++) {
			var door = dg.ConnectorSceneObjects[i];
			if (door == null) continue;
			var d = instantiateSubPart(door);
			RegisterProp(d,this.alwaysDoors);
		}
		
		this.randomBlockerSet = new((DGameMap)this.Tile.Map, (1,1));
		foreach (var entry in dg.BlockerPrefabWeights) {
			var blocker = entry.GameObject;
			if (blocker == null) continue;
			var b = instantiateSubPart(blocker);
			RegisterProp(b,this.randomBlockerSet, entry.Weight);
		}
		
		this.randomDoorSet = new((DGameMap)this.Tile.Map, (0,0));
		foreach (var entry in dg.ConnectorPrefabWeights) {
			var door = entry.GameObject;
			if (door == null) continue;
			var d = instantiateSubPart(door);
			RegisterProp(d,this.randomDoorSet, entry.Weight);
		}
	}
	
	// Overrides
	public override void Connect(Doorway other) {
		base.Connect(other);
		bool choice = this.Tile.Map.rng.Next(2) == 0;
		this.OnConnect(choice);
		((DDoorway)other).OnConnect(!choice);
	}
	
	// Native Methods
	// Boss Doorway in charge of random door object
	protected virtual void OnConnect(bool isBossDoor) {
		this.alwaysBlockers.Range = (0,0);
		this.alwaysDoors.Range = (this.alwaysDoors.Count, this.alwaysDoors.Count);
		
		if (isBossDoor) this.randomDoorSet.Range = (1,1);
		this.randomBlockerSet.Range = (0,0);
	}
	private static void DisconnectAction(Doorway door) {
		var d = (DDoorway)door;
		if (d == null) return;
		d.alwaysBlockers.Range = (d.alwaysBlockers.Count, d.alwaysBlockers.Count);
		d.alwaysDoors.Range = (0,0);
		d.randomDoorSet.Range = (0,0);
		d.randomBlockerSet.Range = (1,1);
	}
}

public class TileProp : MonoBehaviour, ISerializable {
	public event Action<TileProp> OnEnableEvent;
	public event Action<TileProp> OnDisableEvent;
	public event Action<TileProp> OnDestroyEvent;
	
	protected List<PropSet> propSets = new();
	
	public bool IsForcedOff {get {
		foreach (PropSet ps in propSets) {
			if (ps.Range.max == 0) return true;
		}
		return false;
	}}
	public bool IsForcedOn {get {
		foreach (PropSet ps in propSets) {
			if (ps.Range.min == ps.Count) return true;
		}
		return false;
	}}
	public bool Ok {get {
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
		if (this.isActiveAndEnabled) return false;
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
	
	public Tile Tile {get {
		return this.GetComponentInParent<Tile>();
	}}
	
	public void RegisterSet(PropSet ps) {
		this.propSets.Add(ps);
	}
	public void UnregisterSet(PropSet ps) {
		this.propSets.Remove(ps);
	}
	
	public IEnumerable<SerializationToken> Serialize() {
		yield return new SerializationToken(
			this.isActiveAndEnabled.GetBytes(),
			isStartOf: this
		);
	}
	
	private void OnEnable()  {this.OnEnableEvent ?.Invoke(this);}
	private void OnDisable() {this.OnDisableEvent?.Invoke(this);}
	private void OnDestroy() {this.OnDestroyEvent?.Invoke(this);}
}

// Do not need serialization support for propset, since the data for each propset is stored in the tile asset, 
// and the state of its props are stored in the props themselves
public class PropSet : ICollection<TileProp> {
	// Protected/Private
	protected (int min, int max) range;
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
	public bool Ok {get {return !(High || Low);}}
	public int NumActive {get {
		if (activeProps == null) {
			throw new NullReferenceException($"PropSet has not had its activity lists initialized");
		}
		return activeProps.Count;
	}}
	public (int min,int max) Range {
		get {return range;} 
		set {
			(int min, int max) = value;
			if (min < 0) throw new ArgumentOutOfRangeException($"Range min < 0 ({min} < 0)");
			if (max < 0) throw new ArgumentOutOfRangeException($"Range max < 0 ({max} < 0)");
			if (min > max) throw new ArgumentException($"Range min > max ({min} > {max})"); 
			range=value;
		}
	}
	public int Count {get {return props.Count;}}
	public bool IsReadOnly {get {return false;}}
	
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
	
	public void Add(TileProp p) {this.Add(p,1.0f);}
	public void Add(TileProp p, float weight) {
		this.props.Add(p,weight);
		p.gameObject.SetActive(false);
		p.OnDisableEvent += this.PropDisabled;
		p.OnEnableEvent += this.PropEnabled; 
		p.OnDestroyEvent += (TileProp p) => this.Remove(p);
		p.RegisterSet(this);
	}
	public bool Remove(TileProp p) {
		p.OnDisableEvent -= this.PropDisabled;
		p.OnEnableEvent -= this.PropEnabled;
		p.OnDestroyEvent -= (TileProp p) => this.Remove(p);
		p.UnregisterSet(this);
		if (this.activeProps   != null) this.activeProps.Remove(p);
		if (this.inactiveProps != null) this.inactiveProps.Remove(p);
		return this.props.Remove(p);
	}
	
	public void Clear() {
		props.Clear();
		activeProps.Clear();
		inactiveProps.Clear();
	}
	public bool Contains(TileProp p) {
		return props.Contains(p);
	}
	
	IEnumerator IEnumerable.GetEnumerator() {return this.props.GetEnumerator();}
	public IEnumerator<TileProp> GetEnumerator() {
		return this.props.GetEnumerator();
	}
	
	public void CopyTo(TileProp[] arr,int idx) {
		if (arr == null) throw new NullReferenceException("arr == null");
		if (idx + this.Count > arr.Length) {
			throw new ArgumentException("Insufficient array bounds");
		}
		if (idx < 0) throw new ArgumentOutOfRangeException($"idx < 0 ({idx} < 0)");
		
		int i=0;
		foreach (TileProp item in this) {
			arr[idx + i++] = item;
		}
		
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
	
	// Possible reason that lower bounds are violated: parent not enabled, so propset cannot 
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
		#if VERBOSE_GENERATION
		Plugin.LogDebug(
			$"{iterationCount} iterations: \n"
			+ $"\tok: {ok.Count}, low: {low.Count}, high: {high.Count}"
		);
		#endif
		
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
				#if VERBOSE_GENERATION
				if (iterationCount % 100 == 0) {
					Plugin.LogDebug(
						$"{iterationCount} iterations: \n"
						+ $"\tok: {ok.Count}, low: {low.Count}, high: {high.Count}"
					);
				}
				#endif
				if (iterationCount == 1000) {
					Plugin.LogWarning("Aborted prop spawning after 1000 iterations");
					Plugin.LogWarning(
						$"(Propsets: {ok.Count} ok, {low.Count} low, {high.Count} high)"
					);
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
			#if VERBOSE_GENERATION
			Plugin.LogDebug(
				$"{iterationCount} iterations: \n"
				+ $"\tok: {ok.Count}, low: {low.Count}, high: {high.Count}"
			);
			#endif
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
		#if VERBOSE_GENERATION
		Plugin.LogDebug($"Tile {this.gameObject} had no predefined bounds");
		#endif
		
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
			#if VERBOSE_GENERATION
			Plugin.LogDebug($"Unable to find easy meshcollider for {this}");
			#endif
			
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
				#if VERBOSE_GENERATION
				Plugin.LogDebug($"Using first collider found: {collider}");
				#endif
				if (collider == null) {
					Plugin.LogError($"Could not find a collider to infer bounds for tile {this}");
				}
			}
		}
		if (bounds.extents == Vector3.zero && collider != null) bounds = collider.bounds;
		#if VERBOSE_GENERATION
		Plugin.LogDebug($"{this.gameObject.name} extents: {bounds.extents}");
		#endif
		
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
		if (bounds.size == Vector3.zero) {
			Plugin.LogError(
				$"Tile '{this}' has zero-size. Tile will allow others to encroach on its area."
			);
		}
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
			PropSet propset = new PropSet(
				(DGameMap)this.Map, 
				(localPropSet.PropCount.Min, localPropSet.PropCount.Max)
			);
			psets.Add(propset);
			foreach (var entry in localPropSet.Props.Weights) {
				GameObject propObject = entry.Value;
				if (propObject == null) continue;
				var prop = propObject.GetComponent<TileProp>() ?? propObject.AddComponent<TileProp>();
				propset.Add(prop, (entry.MainPathWeight + entry.BranchPathWeight)/2.0f);
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
			pset.Add(prop, (globalProp.MainPathWeight + globalProp.BranchPathWeight)/2.0f);
		}
	}
	
	public TileProp[] GetProps() {
		TileProp[] allprops = this.GetComponentsInChildren<TileProp>(includeInactive: true);
		TileProp[] filtered = new TileProp[allprops.Length];
		int j = 0;
		for (int i=0; i<allprops.Length; i++) {
			TileProp p = allprops[i]; 
			if (p.GetComponentInParent<DTile>(includeInactive: true) == this) {
				filtered[j++] = p;
			}
		}
		Array.Resize(ref filtered, j);
		return filtered;
	}
	
	public override IEnumerable<SerializationToken> Serialize() {
		foreach (var token in base.Serialize()) {
			yield return token;
		}
		TileProp[] props = this.GetProps();
		yield return ((ushort)props.Length).GetBytes();
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"Found {props.Length} props");
		#endif
		foreach (TileProp prop in props) {
			foreach (var token in prop.Serialize()) {
				yield return token;
			}
		}
	}
}

public class DGameMap : GameMap {
	protected DungeonFlow flow;
	protected Dictionary<int, PropSet> globalPropSets;
	
	// Constructors/Initialization
	protected override void Awake() {
		base.Awake();
		
		this.transform.position -= Vector3.up * 200f;
		
		this.GenerationCompleteEvent += DGameMap.GenerationCompleteHandler;
		this.TileInsertionEvent += DGameMap.TileInsertionFail;
		globalPropSets = new();
	}
	
	// Native Methods
	private (int min,int max) GetGlobalPropRange(int id) {
		if (this.flow == null) {
			Plugin.LogError($"GameMap has no flow to derive GlobalProp bounds from");
			return (1,1);
		}
		foreach (var settings in this.flow.GlobalProps) {
			if (settings.ID == id) return (settings.Count.Min,settings.Count.Max);
		}
		Plugin.LogError($"Global Prop bounds not found for id {id}");
		return (1,1);
	}
	
	public static void TileInsertionFail(Tile t) {
		#if VERBOSE_GENERATION
		if (t == null) Plugin.LogDebug($"Failed to place tile {t}");
		#endif
	}
	
	protected void HandleProps() {
		IEnumerable<PropSet> propsets = (
			(IEnumerable<DTile>)this.GetComponentsInChildren<DTile>()
		).SelectMany<DTile,PropSet,PropSet>(
			(DTile t) => t.LocalPropSets,
			(DTile t,PropSet pset) => pset
		).Concat(
			this.globalPropSets.Values
		).Concat(
			(
				(IEnumerable<DDoorway>)this.GetComponentsInChildren<DDoorway>()
			).SelectMany<DDoorway,PropSet,PropSet>(
				(DDoorway d) => d.PropSets,
				(DDoorway d,PropSet pset) => pset
			)
		);
		
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
		var foo = (GameMap m) => HandleProps();
		this.GenerationCompleteEvent += foo;
		yield return StartCoroutine(base.GenerateCoroutine(tilegen,seed));
		this.GenerationCompleteEvent -= foo;
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

	public void DestroyAllScrap() {
		foreach (Scrap s in this.GetComponentsInChildren<Scrap>(includeInactive:true)) {
			GameObject.Destroy(s.gameObject);
		}
	}
	
	public void InitializeLoadedMapObjects() {
		foreach (MapObject s in this.GetComponentsInChildren<MapObject>(includeInactive: true)) {
			var netObj = s.GetComponent<NetworkObject>();
			if (!netObj.IsSpawned) netObj.Spawn();
			s.FindParent(this);
		}
	}
	
	public override IEnumerable<SerializationToken> Serialize() {
		foreach (var token in base.Serialize()) {
			yield return token;
		}
		
		foreach (SerializationToken t in GetSerializedMapObjects<Scrap>()) {
			yield return t;
		}
		foreach (SerializationToken t in GetSerializedMapObjects<Equipment>()) {
			yield return t;
		}
	}
	
	private IEnumerable<SerializationToken> GetSerializedMapObjects<T>() where T : MapObject {
		T[] objs = this.GetComponentsInChildren<T>(includeInactive: true);
		yield return ((ushort)objs.Length).GetBytes();
		foreach (T o in objs) {
			foreach (var token in o.Serialize()) {
				yield return token;
			}
		}
	}
}

public class DungeonFlowConverter : ITileGenerator {
	protected DunGen.Graph.DungeonFlow flow;
	protected uint tile_demand;
	
	protected List<(DTile tile,float weight)> tile_freqs;
	
	protected float freq_range;
	// Don't fully count branch tiles because they often get cut off. 
	// This is obviously an imperfect representation of how many tiles get generated by DunGen, 
	// but we're not trying to be dungen anyway, we're just trying to make dungen compatible
	// public float BranchCountMultiplier = 0.6f;
	
	// max attempts to place each given tile
	// NOT the max attempts to place any tile. 
	//   i.e. if a tile fails to place within MAX_ATTEMPTS, a new tile is chosen
	private const int MAX_ATTEMPTS=10;
	
	public DunGen.Graph.DungeonFlow Flow {get {return flow;}}
	
	public DungeonFlowConverter(DungeonFlow flow) {
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
			#if VERBOSE_GENERATION
				Plugin.LogDebug($"{this.tile_demand}: Using '{start.gameObject.name}' as start room");
			#endif
			
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
		
		Plugin.LogInfo($"Placing tiles...");
		uint iterationsSinceLastSuccess = 0;
		PlacementInfo rt = new PlacementInfo(null);
		while (tile_demand > 0) {
			bool startRoomExists = map.transform.Find(
				"ElevatorConnector(Clone)/ElevatorDoorway/StartRoom(Clone)"
			) || map.transform.Find(
				"Level2StartRoomConnector(Clone)/ElevatorDoorway/ManorStartRoom(Clone)"
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
				rt.NewDoorwayIdx = map.rng.Next(rt.NewTile.Doorways.Length);
				var d = rt.NewTile.Doorways[rt.NewDoorwayIdx];
				rt.AttachmentPoint = map.GetLeaf(d.Size);
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
	
}

public class DGameMapDeserializer : GameMapDeserializer<DGameMap, DTile> {
	private void DeserializeMapObjects<T,U>(DGameMap map, DeserializationContext dc)
		where T : MapObject
		where U : MapObjectDeserializer<T>, new()
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
	
	public override DGameMap Deserialize(
		object baseObj, DeserializationContext dc, object extraContext=null
	) {
		DGameMap rt = base.Deserialize(baseObj,dc,extraContext);
		
		DeserializeMapObjects<Scrap, ScrapDeserializer>(rt,dc);
		DeserializeMapObjects<Equipment, EquipmentDeserializer>(rt,dc);
		
		return rt;
	}
	
	public override void Finalize(object x) {
		DGameMap map = (DGameMap)x;
		map.InitializeLoadedMapObjects();
		map.gameObject.SetActive(false);
	}
}

public class DTileDeserializer : TileDeserializer<DTile> {
	public override DTile Deserialize(
		object baseObj, DeserializationContext dc, object extraContext=null
	) {
		var tile = (DTile)base.Deserialize(baseObj,dc,extraContext);
		
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"Deserializing {tile.name}");
		#endif
		
		dc.Consume(2).CastInto(out ushort propCount);
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"Found {propCount} props");
		#endif
		TileProp[] props = tile.GetComponentsInChildren<TileProp>(includeInactive: true);
		for (ushort i=0; i<propCount; i++) {
			dc.Consume(1).CastInto(out bool flag);
			props[i].gameObject.SetActive(flag);
		}
		return tile;
	}
}

public class DGameMapNetworkSerializer : IDeSerializer<DGameMap> {
	private IEnumerable<SerializationToken> SerializeMapObjects<T>(DGameMap map, ISerializer<T> serializer) 
		where T : MapObject 
	{
		T[] objs = map.GetComponentsInChildren<T>(includeInactive: true);
		yield return ((ushort)objs.Length).GetBytes();
		foreach (T o in objs) {
			foreach (var token in serializer.Serialize(o)) {
				yield return token;
			}
		}
	}
	
	public IEnumerable<SerializationToken> Serialize(object o) {
		DGameMap m = (DGameMap)o;
		yield return new SerializationToken((m.name+"\0").GetBytes(), isStartOf: m);
		
		foreach (var t in SerializeMapObjects<Scrap>(m, new ScrapNetworkSerializer())) {
			yield return t;
		}
		foreach (var t in SerializeMapObjects<Equipment>(m, new EquipmentNetworkSerializer())) {
			yield return t;
		}
	}
	
	private void DeserializeMapObjects<T>(
		DGameMap map, DeserializationContext dc, IDeserializer<T> ds
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
	
	public DGameMap Deserialize(object baseObj, DeserializationContext dc, object extraContext=null) {
		var map = (DGameMap)baseObj;
		DeserializeMapObjects<Scrap>(map, dc, new ScrapNetworkSerializer());
		DeserializeMapObjects<Equipment>(map, dc, new EquipmentNetworkSerializer());
		
		return map;
	}
	
	public DGameMap Deserialize(DeserializationContext dc, object extraContext=null) {
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string id);
		dc.Consume(1);
		DGameMap map = MapHandler.Instance.transform.Find(id).GetComponent<DGameMap>();
		return Deserialize(map,dc,extraContext);
	}

	public virtual void Finalize(object obj) {}
}