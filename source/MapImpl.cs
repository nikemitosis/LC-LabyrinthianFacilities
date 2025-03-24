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

using Object = UnityEngine.Object;
using Random = System.Random;

public class DDoorway : Doorway {
	// Properties
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
				$"DDoorway has no parent tile. "
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
	
	public override bool Add(Prop p, float weight) {
		if (this.Remove(p,out float oldWeight)) {
			return this.Add(p,weight + oldWeight);
		} else {
			return base.Add(p,weight);
		}
	}
}

public class DTile : Tile {
	// Properties
	internal PropSet[] LocalPropSets {get {return localPropSets;}}
	internal (Prop prop, int id, float weight)[] GlobalProps {get {return globalProps;}}
	
	// Protected/Private
	protected PropSet[] localPropSets;
	protected (Prop prop, int id, float weight)[] globalProps;
	
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
		#if VERBOSE_TILE_INIT
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
			#if VERBOSE_TILE_INIT
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
				#if VERBOSE_TILE_INIT
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
		
		#if VERBOSE_TILE_INIT
		Plugin.LogDebug($"{this.gameObject.name} extents: {bounds.extents}");
		#endif
		
		return bounds;
	}
	
	public override void Initialize() {
		if (this.Initialized) return;
		base.Initialize();
		this.Initialized = true;
		
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
		List<PropSet> psets = new();
		foreach (var localPropSet in localPropSets) {
			psets.Add(new PropSet(localPropSet));
		}
		this.localPropSets = psets.ToArray();
		
		// Global Props
		var globs = this.GetComponentsInChildren<DunGen.GlobalProp>(includeInactive:true);
		this.globalProps = new (Prop prop, int id, float weight)[globs.Length];
		for (int i=0; i<globs.Length; i++) {
			DunGen.GlobalProp globalProp = globs[i];
			if (globalProp?.gameObject == null) continue;
			
			Prop prop = globalProp.GetComponent<Prop>() ?? globalProp.gameObject.AddComponent<Prop>();
			prop.IsMapProp = true;
			this.globalProps[i] = (
				prop, 
				globalProp.PropGroupID, 
				(globalProp.MainPathWeight + globalProp.BranchPathWeight) / 2.0f
			);
		}
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
	
	protected Dictionary<int, PropSet> globalPropSets;
	protected Dictionary<int, PropSet> uninitializedGlobalPropSets;
	
	internal List<GameObject> CaveLights = new();
	
	public Moon Moon {get {return this.transform.parent.GetComponent<Moon>();}}
	public IReadOnlyCollection<PropSet> GlobalPropSets {get {return globalPropSets.Values;}}
	
	// Constructors/Initialization
	protected override void Awake() {
		base.Awake();
		
		this.transform.position = new Vector3(0,-200,0);
		
		this.GenerationCompleteEvent += DGameMap.GenerationCompleteHandler;
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
	
	public override Tile AddTile(PlacementInfo placement) {
		Tile tile = base.AddTile(placement);
		if (tile != null) this.AddGlobalProps((DTile)tile);
		return tile;
	}
	
	public void RemoveTileProps(DTile tile) {
		foreach ((Prop prop,int id,float weight) in tile.GlobalProps) {
			if (!globalPropSets.TryGetValue(id, out PropSet pset)) {
				if (!uninitializedGlobalPropSets.TryGetValue(id, out pset)) {
					// Some propsets might not be recognized because the tile was never actually placed, 
					// and so never had its props registered
					// throw new KeyNotFoundException($"GlobalPropId {id}");
					continue;
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