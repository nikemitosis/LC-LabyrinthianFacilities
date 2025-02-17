namespace LabyrinthianFacilities;

using BoundsExtensions;
using Serialization;
using Util;

using System;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;

// Ambiguity between System.Random and UnityEngine.Random
using Random = System.Random;

public interface IDoorwayManager : ICollection<Doorway> {
	public IChoice<Doorway,    float>  GetLeaves               (Func<Doorway,   float> weightFunction);
	public IChoice<Connection, float>  GetPotentialConnections (Func<Connection,float> weightFunction);
	public IChoice<Connection, float>  GetActiveConnections    (Func<Connection,float> weightFunction);
}

// Does not support doorways being moved after they have been added
// Worst case, you can remove and readd them
public class DoorwayManager : IDoorwayManager {
	
	public enum DoorType {
		LEAF,
		ACTIVE_CONNECTOR,
		POTENTIAL_CONNECTOR,
		FALSE_POTENTIAL_CONNECTOR
	}
	
	// <External>
	protected GameMap map;
	public GameMap Map {get => map;}
	public virtual BoundsMap<Tile> BoundsMap {get => Map.BoundsMap;}
	// </External>
	
	// <Metadata>
	public int Count {get => doorways.Count;}
	public virtual bool IsReadOnly {get => false;}
	// </Metadata>
	
	// <Parameters>
	
	// Overly precise positions lead to leaves that *could* overlap not overlapping
	// 0 => round to nearest 1.00f, 1 => 0.50f, 2 => 0.25f, etc.
	// values above 31 are invalid due to the implementation of LeafPrecision, unless overridden
	public virtual byte DoorPosPrecision {get => 3;}
	public virtual float DoorPrecision {get => 1.0f / (1 << (int)DoorPosPrecision);}
	
	// Must be >0.0f
	protected virtual float clearancePrecision {get => 1f;}
	// Should be >clearancePrecision
	protected virtual float maxClearance {get => clearancePrecision * 256f;}
	
	// </Parameters>
	
	// <Data>
	private Dictionary<Doorway, DoorType> doorways;
	
	private Dictionary<Vector3,Doorway> leaves;
	private HashSet<Connection> activeConnections;
	private Dictionary<Vector3,Connection> potentialConnections;
	private Dictionary<Vector3,Connection> falsePotentialConnections;
	// </Data>
	
	
	// yes this is circumventable with a cast. Probably dont do that?
	public IReadOnlyCollection<Doorway>    Leaves {get => leaves.Values;}
	public IReadOnlyCollection<Connection> ActiveConnections {get => activeConnections;}
	public IReadOnlyCollection<Connection> PotentialConnections {get => potentialConnections.Values;}
	public IReadOnlyCollection<Connection> FalsePotentialConnections {get => falsePotentialConnections.Values;}
	
	public DoorwayManager(GameMap map) {
		this.map = map;
		
		doorways = new();
		leaves = new();
		activeConnections = new();
		potentialConnections = new();
		falsePotentialConnections = new();
	}
	
	public bool Validate() {
		foreach (var entry in doorways) {
			Doorway d = entry.Key;
			if (d == null) return false;
			DoorType dt = entry.Value;
			Vector3 pos = ApproxPosition(d);
			switch (dt) {
				case DoorType.LEAF:
					if (!leaves.TryGetValue(pos,out Doorway e) || d != e) return false;
				break; case DoorType.ACTIVE_CONNECTOR:
					if (!activeConnections.Contains(new Connection(d,d.Connection))) return false;
				break; case DoorType.POTENTIAL_CONNECTOR:
					if (
						!potentialConnections.TryGetValue(pos,out Connection c) 
						|| (c.d1 != d && c.d2 != d)
					) return false;
				break; case DoorType.FALSE_POTENTIAL_CONNECTOR:
					if (
						!falsePotentialConnections.TryGetValue(pos,out c)
						|| (c.d1 != d && c.d2 != d)
					) return false;
				break; default:
					return false;
				// break;
			}
		}
		return true;
	}
	
	protected virtual void UnsetDoorway(Doorway d,Vector3 approxPos=default) {
		if (!doorways.TryGetValue(d,out DoorType dt)) return;
		if (approxPos == default) approxPos = ApproxPosition(d);
		switch (dt) {
			case DoorType.LEAF:
				if (
					!leaves.Remove(approxPos,out Doorway door)
					|| door != d
				) throw new InvalidOperationException(
					$"Desync between leaves and doorways"
				);
			break; case DoorType.ACTIVE_CONNECTOR:
				activeConnections.Remove(new Connection(d,d.Connection));
			break; case DoorType.POTENTIAL_CONNECTOR:
				if (
					potentialConnections.Remove(approxPos,out Connection con) 
					&& (con.d1 != d && con.d2 != d)
				) throw new InvalidOperationException(
					$"Desync between potentialConnections and doorways"
				);
			break; case DoorType.FALSE_POTENTIAL_CONNECTOR:
				if (
					falsePotentialConnections.Remove(approxPos, out con)
					&& (con.d1 != d && con.d2 != d)
				) throw new InvalidOperationException(
					$"Desync between falsePotentialConnections and doorways"
				);
			break;
		}
	}
	
	protected virtual void SetLeaf(Doorway d, Vector3 approxPos=default) {
		if (approxPos == default) approxPos = ApproxPosition(d);
		UnsetDoorway(d);
		doorways[d] = DoorType.LEAF;
		leaves.Add(approxPos,d);
	}
	
	protected virtual void SetActiveConnector(Doorway d) {
		UnsetDoorway(d);
		UnsetDoorway(d.Connection);
		doorways[d] = DoorType.ACTIVE_CONNECTOR;
		if (doorways.ContainsKey(d.Connection)) doorways[d.Connection] = DoorType.ACTIVE_CONNECTOR;
		// activeConnections is hashset, no need to worry about duplicates
		activeConnections.Add(new Connection(d,d.Connection));
	}
	
	protected virtual void SetPotentialConnection(Connection c,Vector3 approxPos=default) {
		if (approxPos == default) approxPos = ApproxPosition(c.d1);
		UnsetDoorway(c.d1);
		UnsetDoorway(c.d2);
		if (c.d1.Fits(c.d2)) {
			doorways[c.d1] = DoorType.POTENTIAL_CONNECTOR;
			if (doorways.ContainsKey(c.d2)) doorways[c.d2] = DoorType.POTENTIAL_CONNECTOR;
			potentialConnections.Add(approxPos,c);
		} else {
			doorways[c.d1] = DoorType.FALSE_POTENTIAL_CONNECTOR;
			if (doorways.ContainsKey(c.d2)) doorways[c.d2] = DoorType.FALSE_POTENTIAL_CONNECTOR;
			falsePotentialConnections.Add(approxPos,c);
		}
	}
	
	public void Add(Doorway d) {
		if (d == null) {
			Debug.LogException(new ArgumentNullException("Doorway d"));
			return;
		}
		if (Contains(d)) return;
		Subscribe(d);
		if (d.IsVacant) {
			Vector3 approxPos = ApproxPosition(d);
			if (leaves.TryGetValue(approxPos, out Doorway other)) {
				SetPotentialConnection(new Connection(d,other),approxPos);
			} else {
				SetLeaf(d,approxPos);
			}
		} else {
			SetActiveConnector(d);
		}
	}
	
	private void RemoveAction(Doorway d) => Remove(d);
	public bool Remove(Doorway d) {
		if (!doorways.ContainsKey(d)) return false;
		Unsubscribe(d);
		switch (doorways[d]) {
			case DoorType.LEAF:
				if (!leaves.Remove(ApproxPosition(d), out Doorway e) || d != e) {
					Debug.LogWarning(
						"DoorwayManager: Desync between doorways and leaves, this *will* become a problem"
					);
				}
			break; case DoorType.ACTIVE_CONNECTOR:
				if (!Contains(d.Connection)) {
					activeConnections.Remove(new Connection(d,d.Connection));
				}
			break; case DoorType.POTENTIAL_CONNECTOR:
				potentialConnections.Remove(ApproxPosition(d),out Connection con);
				Doorway other = con.GetOther(d);
				if (Contains(other)) {
					SetLeaf(other);
				}
			break; case DoorType.FALSE_POTENTIAL_CONNECTOR:
				falsePotentialConnections.Remove(ApproxPosition(d),out con);
				other = con.GetOther(d);
				if (Contains(other)) {
					SetLeaf(other);
				}
			break;
		}
		doorways.Remove(d);
		
		return true;
	}
	
	public bool Contains(Doorway d) => doorways.ContainsKey(d);
	
	public virtual void Clear() {
		doorways.Clear();
		leaves.Clear();
		activeConnections.Clear();
		potentialConnections.Clear();
		falsePotentialConnections.Clear();
	}
	
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public IEnumerator<Doorway> GetEnumerator() => this.doorways.Keys.GetEnumerator();
	
	public void CopyTo(Doorway[] arr, int startIdx) => throw new NotImplementedException();
	
	protected virtual void Subscribe(Doorway d) {
		d.OnDestroyEvent    += RemoveAction;
		d.OnDisconnectEvent += DisconnectDoor;
		d.OnConnectEvent    += ConnectDoor;
	}
	protected virtual void Unsubscribe(Doorway d) {
		d.OnDestroyEvent    -= RemoveAction;
		d.OnDisconnectEvent -= DisconnectDoor;
		d.OnConnectEvent    -= ConnectDoor;
	}
	
	public virtual Vector3 ApproxPosition(Doorway door) {
		if (door == null) {
			throw new NullReferenceException($"Cannot get the position of a destroyed doorway. ");
		}
		Vector3 pos = door.transform.position;
		// coord ~= LeafPrecision * a  for some int a
		// coord / LeafPrecision ~= a
		// a = (int)((coord / LeafPrecision)+0.5f)
		// ^(add 0.5f to round in both directions instead of always rounding down)
		// rounded = LeafPrecision * (int)((coord / LeafPrecision)+0.5f)
		return new Vector3(
			DoorPrecision * (int)((pos.x / DoorPrecision)+0.5f),
			DoorPrecision * (int)((pos.y / DoorPrecision)+0.5f),
			DoorPrecision * (int)((pos.z / DoorPrecision)+0.5f)
		);
	}
	
	public virtual float ClearanceRadius(Doorway d) {
		float radius = clearancePrecision;
		Bounds bounds;
		while (true) {
			bounds = new Bounds(
				d.transform.position + radius*d.transform.forward, 
				2*radius*Vector3.one
			);
			if (BoundsMap.Intersects(bounds)) break;
			radius *= 2.0f;
			if (radius >= maxClearance) break;
		}
		return radius;
	}
	
	public IChoice<Doorway,float> GetLeaves(Func<Doorway,float> weightFunc) {
		WeightedChoiceList<Doorway> rt = new();
		foreach (Doorway d in leaves.Values) {
			rt.Add(d,weightFunc(d));
		}
		return rt;
	}
	
	public IChoice<Connection,float> GetActiveConnections(Func<Connection,float> weightFunc) {
		WeightedChoiceList<Connection> rt = new();
		foreach (Connection c in activeConnections) {
			rt.Add(c,weightFunc(c));
		}
		return rt;
	}
	
	public IChoice<Connection,float> GetPotentialConnections(Func<Connection,float> weightFunc) {
		WeightedChoiceList<Connection> rt = new();
		foreach (Connection c in potentialConnections.Values) {
			rt.Add(c,weightFunc(c));
		}
		return rt;
	}
	
	public void DisconnectDoor(Doorway d) {
		if (leaves.TryGetValue(ApproxPosition(d),out Doorway other)) {
			SetPotentialConnection(new Connection(d,other));
		} else {
			SetLeaf(d);
		}
	}
	
	public void ConnectDoor(Doorway d1, Doorway d2) {
		SetActiveConnector(d1);
	}
}

public struct Connection : IEquatable<Connection>, IEquatable<ITuple> {
	public Doorway d1;
	public Doorway d2;
	
	public Connection(Doorway d1, Doorway d2) {
		this.d1 = d1;
		this.d2 = d2;
	}
	
	public Doorway GetOther(Doorway d) {
		if (d != d1 && d != d2) throw new ArgumentException(
			"Doorway expected to be one of the doorways in the connection"
		);
		return d == d1 ? d2 : d1;
	}
	
	public void Deconstruct(out Doorway d1, out Doorway d2) {
		d1 = this.d1;
		d2 = this.d2;
	}
	public override bool Equals(object other) {
		if (other is ITuple tup) {
			return Equals(tup);
		} else if (other is Connection con) {
			return Equals(con);
		} else {
			return false;
		}
	}
	public bool Equals(Connection con) {
		var (od1, od2) = con;
		return (d1 == od1 && d2 == od2) || (d1 == od2 && d2 == od1);
	}
	public bool Equals(ITuple tup) {
		if (tup.Length != 2) return false;
		Doorway od1,od2;
		try {
			od1 = (Doorway)tup[0];
			od2 = (Doorway)tup[1];
		} catch (InvalidCastException) {
			return false;
		}
		return (d1 == od1 && d2 == od2) || (d1 == od2 && d2 == od1);
	}
	
	public override int GetHashCode() {
		return d1.GetHashCode() ^ d2.GetHashCode();
	}
}

public class Doorway : MonoBehaviour {
	// Events
	// OnDestroy invokes OnDisconnectEvent first (if applicable), then OnDestroyEvent
	public event Action<Doorway> OnDisconnectEvent; // occurs before disconnection
	public event Action<Doorway> OnDestroyEvent; // as with OnDestroy, occurs before destruction
	public event Action<Doorway,Doorway> OnConnectEvent; // occurs *after* connection
	
	// Properties
	public Tile Tile {get {
		return this.tile ??= this.GetComponentInParent<Tile>(includeInactive: true);
	}}
	public Vector2 Size {get {return this.size;} protected set {this.size = value;}}
	public bool IsVacant {get {return this.connection == null;}}
	public bool Initialized {
		get {return this.initialized;} 
		protected set {this.initialized = value;}
	}
	public Doorway Connection {get {return this.connection;}}
	
	// Protected/Private
	protected bool initialized;
	protected Tile tile;
	protected Vector2 size;
	protected Doorway connection;
	
	// Monobehaviour Stuff
	private void OnDestroy() {
		this.Disconnect();
		
		this.OnDestroyEvent?.Invoke(this);
	}
	
	// Native Stuff
	public virtual void Initialize() {
		if (this.Initialized) return;
		this.Initialized = true;
		
		this.connection = null;
	}
	
	public virtual bool Fits(Doorway other) {
		return this.size == other.size;
	}
	
	public void Connect(Doorway other) {
		if (!Fits(other)) {
			string msg = "Cannot connect doors that do not fit together\n"
				+ $"\t(Tiles {this.Tile} & {other.Tile})";
			throw new ArgumentException(msg);
		}
		this.connection = other;
		other.connection = this;
		this.OnConnectEvent?.Invoke(this,other);
		other.OnConnectEvent?.Invoke(other,this);
	}
	
	public void Disconnect() {
		if (this.connection == null) return;
		this.OnDisconnectEvent?.Invoke(this);
		var con = this.connection;
		this.connection = null;
		con.Disconnect();
	}
}

public class Tile : MonoBehaviour {
	// Properties
	public bool Initialized {
		get => initialized;
		protected set { initialized = value; }
	}
	
	public bool HasLeafDoorway {
		get {
			foreach (var doorway in this.Doorways) {
				if (doorway.IsVacant) {
					return true;
				}
			}
			return false;
		}
	}
	
	public bool IsPrefab {get => !this.gameObject.scene.IsValid();}
	
	public Doorway[] Doorways {
		get => this.doorways ??= this.GetComponentsInChildren<Doorway>();
	}
	public virtual float IntersectionTolerance {get => 5f/8;}
	public Bounds LooseBoundingBox {
		get => new Bounds(BoundingBox.center, BoundingBox.size - 2*IntersectionTolerance*Vector3.one);
	}
	public Bounds BoundingBox {get => this.bounding_box;}
	public GameMap Map {get => this.GetComponentInParent<GameMap>(true);}
	
	// Protected/Private
	protected Doorway[] doorways = null;
	protected Bounds bounding_box = new Bounds(Vector3.zero,Vector3.zero);
	protected bool initialized = false;
	
	// Native Methods
	public Tile Instantiate(Transform parent=null) {
		var newtile = GameObject.Instantiate(this.gameObject).GetComponent<Tile>();
		newtile.bounding_box = new Bounds(Vector3.zero,Vector3.zero);
		
		newtile.transform.parent = parent;
		newtile.Initialize();
		return newtile;
	}
	
	// Do placement-independent initializtion here (including bounds, since bounds are 
	// affected by MoveTo and RotateBy
	public virtual void Initialize() {
		if (initialized) return;
		initialized = true;
		return;
	}
	
	// WARNING: Bounding box increases *permanantly* with certain calls. This should really only be 
	// called once per tile. Should eventually move away from AABB anyway... 
	// the only exception to this warning is 90x degree rotations
	public void RotateBy(Quaternion quat) {
		Vector3 diff = this.bounding_box.center - this.transform.position;
		
		this.bounding_box.center = this.transform.position + quat * diff;
		this.bounding_box.extents = quat * this.bounding_box.extents;
		this.bounding_box.FixExtents();
		this.transform.Rotate(quat.eulerAngles,Space.World);
	}
	
	public void MoveTo(Vector3 pos) {
		Vector3 diff = this.transform.position - this.bounding_box.center;
		
		this.bounding_box.center = pos;
		this.transform.position = pos + diff;
	}
	
	public bool PlaceAsRoot(Transform parent) {
		this.transform.parent = parent;
		this.MoveTo(parent.position);
		return true;
	}
		
	public bool Place(int thisDoorwayIdx, Doorway other) {
		if (thisDoorwayIdx >= this.Doorways.Length) {
			throw new IndexOutOfRangeException(
				$"Door index of new tile out of range. Tried to select idx {thisDoorwayIdx} with only {this.Doorways.Length} doors. "
			);
		}
		if (this == null) {
			throw new ArgumentNullException($"Cannot place a tile that is being destroyed");
		}
		if (other == null) {
			throw new ArgumentNullException($"No doorway provided to connect to");
		}
		if (other.Tile.Map == null) {
			throw new ArgumentException($"Tile must connect to a doorway that is placed within a map");
		}
		
		Transform oldParent = this.transform.parent;
		this.transform.parent = other.transform;
		var thisDoorway = this.Doorways[thisDoorwayIdx];
		
		// Undo this rotation, do other rotation, do 180 about vertical axis
		Quaternion rotation = (
			Quaternion.Inverse(thisDoorway.transform.rotation) 
			* other.transform.rotation
			* new Quaternion(0,1,0,0)
		);
		
		
		this.RotateBy(rotation);
		
		// local position accounts for parent rotation, which makes sense, 
		// but confused me for so long
		Vector3 doorLocalPos = thisDoorway.transform.position - this.bounding_box.center;
		this.MoveTo(
			other.transform.position - doorLocalPos
		);
		if (!this.Map.VerifyTilePlacement(this)) {
			this.transform.SetParent(oldParent);
			return false;
		}
		
		thisDoorway.Connect(other);
		return true;
	}
	
	// Should be null-terminated! (Unless deserialization is overridden)
	public virtual byte[] GetSerializationId() {
		return (this.name.Substring(0,this.name.Length - "(Clone)".Length) + "\0").GetBytes();
	}
	
	// This should be a virtual static method. Too bad!
	public virtual Tile GetPrefab(object ident) {
		return Tile.GetPrefab((string)ident);
	}
	public static Tile GetPrefab(string id) {
		foreach (Tile t in Resources.FindObjectsOfTypeAll(typeof(Tile))) {
			if (t.name == id && !t.gameObject.scene.IsValid()) {
				return t;
			}
		}
		return null;
	}
}

public abstract class GenerationAction {}

public class YieldFrame : GenerationAction {}

public class PlacementInfo : GenerationAction {
	private Tile newtile;
	private int newDoorwayIdx;
	private Doorway attachmentPoint;
	
	public Tile NewTile {get {return newtile;} set {newtile = value;}}
	public int NewDoorwayIdx {
		get {
			return newDoorwayIdx;
		} set {
			// should have a bounds check here
			newDoorwayIdx = value;
		}
	}
	public Doorway AttachmentPoint {
		get {
			return attachmentPoint;
		} set {
			attachmentPoint = value;
		}
	}
	
	public PlacementInfo(Tile newTile, int newDoorwayIdx=0, Doorway attachmentPoint=null) {
		this.newtile = newTile;
		this.newDoorwayIdx = newDoorwayIdx;
		this.attachmentPoint = attachmentPoint;
	}
}

public class RemovalInfo : GenerationAction {
	private Tile target;
	public Tile Target {get {return target;}}
	
	public RemovalInfo(Tile target) {
		this.target = target;
	}
}

public abstract class ConnectionAction : GenerationAction {
	public Doorway d1 {get; private set;}
	public Doorway d2 {get; private set;}
	public (Doorway d1, Doorway d2) Doorways {get {return (d1,d2);}}
	
	public ConnectionAction(Doorway d1, Doorway d2) {
		this.d1 = d1;
		this.d2 = d2;
		if (!d1.Fits(d2)) throw new ArgumentException($"Doors {d1} and {d2} do not fit together");
	}
}

public class ConnectAction : ConnectionAction {
	public ConnectAction(Doorway d1,Doorway d2) : base(d1,d2) {}
}

// d1 here must be the same as the d1 in the corresponding ConnectAction!
// (it will be the same as the d1 yielded by GetExtraConnections)
public class DisconnectAction : ConnectionAction {
	public DisconnectAction(Doorway d1,Doorway d2) : base(d1,d2) {}
}

public interface ITileGenerator {
	public IEnumerable<GenerationAction> Generator(GameMap map);
}

public class GameMap : MonoBehaviour {
	
	// Delegates & Events
	
	// In the event of a failed insertion, tile is null 
	public event Action<Tile> TileInsertionEvent;
	// TileRemovalEvent Invoked before removal actually occurs
	public event Action<Tile> TileRemovalEvent;
	
	public event Action<GameMap> GenerationCompleteEvent;
	
	// Properties
	public Tile RootTile {
		get => rootTile;
		protected set {rootTile=value;}
	}
	public int TileCount {get => this.GetComponentsInChildren<Tile>().Length;}
	public BoundsMap<Tile> BoundsMap {get => boundsMap; private set => boundsMap = value;}
	
	public virtual IDoorwayManager DoorwayManager {get; protected set;} = null;
	
	// Protected/Private
	protected Tile rootTile;
	
	private BoundsMap<Tile> boundsMap;
	
	protected NavMeshSurface navSurface;
	
	// MonoBehaviour Stuff
	protected virtual void Awake() {
		this.rootTile = null;
		if (this.DoorwayManager == null) this.DoorwayManager = new DoorwayManager(this);
		
		this.boundsMap = new BoundsMap<Tile>(
			center: Vector3.zero,
			radius: 1024f,
			itemBounds: (Tile t) => t.LooseBoundingBox
		);
		
		this.navSurface = this.gameObject.AddComponent<NavMeshSurface>();
		this.navSurface.collectObjects = CollectObjects.Children;
		this.navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
		// ^NavmeshCollectGeometry.RenderMeshes causes the manor start tile to be unenterable/exitable
	}
	
	protected virtual void OnDestroy() {
		this.navSurface.RemoveData();
	}
	
	public virtual bool PerformAction(GenerationAction action) {
		if (action is PlacementInfo placement) {
			this.TileInsertionEvent?.Invoke(this.AddTile(placement));
		} else if (action is RemovalInfo removal) {
			this.RemoveTile(removal);
		} else if (action is ConnectAction connection) {
			(Doorway d1, Doorway d2) = connection.Doorways;
			d1.Connect(d2);
		} else if (action is DisconnectAction dc) {
			dc.d1.Disconnect();
		} else if (action is YieldFrame) {
			// noop here
		} else {
			return false;
		}
		return true;
	}
	
	// Native Methods
	public virtual IEnumerator GenerateCoroutine(ITileGenerator tileGen) {
		foreach (GenerationAction action in tileGen.Generator(this)) {
			if (!this.PerformAction(action)) {
				throw new InvalidOperationException($"Unknown GenerationAction: {action}");
			}
			if (action is YieldFrame) {
				yield return null;
			}
		}
		GenerationCompleteEvent?.Invoke(this);
	}
	
	protected void AddDoorway(Doorway d) {
		if (d == null) {
			Debug.LogError($"Cannot add null doorway to {this.GetType().Name}. Is it destroyed?",this);
			return;
		}
		DoorwayManager.Add(d);
	}
	
	protected void RemoveDoorway(Doorway d) {
		if (d == null) {
			throw new NullReferenceException($"Cannot remove a leaf that has already been destroyed");
		}
		if (!this.DoorwayManager.Remove(d)) {
			Debug.LogError($"Doorway {d.Tile.name}:{d.name} not found/removed",this);
		}
	}
	
	public virtual bool VerifyTilePlacement(Tile tile) {
		if (tile == null) {
			Debug.LogException(new ArgumentNullException("Tile tile"), this);
			return false;
		}
		return !this.boundsMap.Intersects(tile);
	}
	
	public virtual Tile AddTile(PlacementInfo placement) {
		Tile newTile = placement.NewTile;
		if (newTile.IsPrefab) {
			newTile = newTile.Instantiate(this.transform);
		}
		if (this.RootTile == null) {
			this.RootTile = newTile;
			this.RootTile.PlaceAsRoot(this.transform);
			foreach (Doorway d in this.RootTile.Doorways) {
				this.AddDoorway(d);
			}
			return this.RootTile;
		}
		
		Doorway leaf = placement.AttachmentPoint;
		int newTileTargetDoorwayIdx = placement.NewDoorwayIdx;
		
		bool success = newTile.Place(newTileTargetDoorwayIdx,leaf);
		if (!success) {
			GameObject.Destroy(newTile.gameObject);
			return null;
		}
		
		foreach (Doorway d in newTile.Doorways) {
			this.AddDoorway(d);
		}
		this.boundsMap.Add(newTile);
		
		return newTile;
	}
	
	public virtual void RemoveTile(RemovalInfo removal) {
		foreach (Tile t in removal.Target.gameObject.GetComponentsInChildren<Tile>()) {
			this.TileRemovalEvent?.Invoke(t);
		}
		this.boundsMap.Remove(removal.Target);
		GameObject.Destroy(removal.Target.gameObject);
	}
	
	// Possible optimization if necessary:
	// Update navmesh instead of completely reconstructing it
	public void GenerateNavMesh(int agentId) {
		this.navSurface.RemoveData();
		this.navSurface.agentTypeID = agentId;
		this.navSurface.BuildNavMesh();
		this.navSurface.AddData();
	}
	
}

public class GameMapSerializer<T,TTile> : Serializer<T> 
	where T : GameMap 
	where TTile : Tile 
{
	
	protected ISerializer<TTile> TileSer;
	
	public GameMapSerializer(ISerializer<TTile> tileSerializer) {
		TileSer = tileSerializer;
	}
	
	/* Format:
	 *   Name
	 *   RootTile
	*/
	public override void Serialize(SerializationContext sc, T map) {
		sc.Add(map.name.GetBytes());
		sc.Add(new byte[1]{0});
		
		sc.AddInline(map.RootTile, TileSer);
	}
	
	// Expects that ident has already been consumed
	protected override T Deserialize(T map, DeserializationContext dc) {
		
		map.AddTile(new PlacementInfo((TTile)dc.ConsumeInline(TileSer)));
		
		return map;
	}
	
	public override T Deserialize(DeserializationContext dc) {
		dc.ConsumeUntil(
			(byte b) => (b == 0)
		).CastInto(out string id);
		dc.Consume(1); // null terminator
		
		T rt = new GameObject(id).AddComponent<T>();
		return Deserialize(rt, dc);
	}
}

public class TileSerializer<T> : Serializer<T> where T : Tile {
	
	protected GameMap ParentMap;
	
	public TileSerializer(GameMap map) {
		this.ParentMap = map;
	}
	
	/* Format:
	 *  string prefabIdentifier
	 *      Defaulting to prefab name, cuz I don't know a more reliably unique identifier
	 *      that's also consistent with different tiles being added/removed
	 *  (Tile*, int)[] doorways
	 *		{Tile*	connection
	 *		int		doorwayIndex}
	*/
	public override void Serialize(SerializationContext sc, T tgt) {
		// prefabIdentifier
		sc.Add(tgt.GetSerializationId());
		
		// ushort doorways.length
		sc.Add(((ushort)tgt.Doorways.Length).GetBytes());
		foreach (Doorway d in tgt.Doorways) {
			// Tile* connection
			sc.AddReference(d.Connection?.Tile, this);
			
			// int doorwayIndex
			if (d.Connection == null) {
				sc.Add(((ushort)0).GetBytes());
			} else {
				sc.Add(
					((ushort)Array.IndexOf(
						d.Connection.Tile.Doorways, d.Connection
					)).GetBytes()
				);
			}
		}
	}
	
	protected override T Deserialize(T tile, DeserializationContext dc) {
		int address = dc.Address;
		bool hasConnected = false;
		
		dc.Consume(2).CastInto(out ushort numDoors);
		for (int didx=0; didx<numDoors; didx++) {
			int thisDoorIndex = didx;
			int tileConnection = dc.ConsumeReference(this);
			dc.Consume(2).CastInto(out ushort otherDoorIndex);
			
			if (tileConnection == 0) continue;
			if (tileConnection < address) { // tileConnection is closer to root
				// Place this tile onto other tile
				T other = (T)dc.GetReference(tileConnection);
				#if VERBOSE_DESERIALIZE
				Plugin.LogDebug($"Attaching {tile.name}:{thisDoorIndex} to {other.name}:{otherDoorIndex}");
				#endif
				if (!hasConnected) {
					hasConnected = true;
					ParentMap.AddTile(
						new PlacementInfo(
							tile, 
							thisDoorIndex, 
							other.Doorways[(int)otherDoorIndex]
						)
					);
				} else {
					ParentMap.PerformAction(
						new ConnectAction(
							tile.Doorways[thisDoorIndex],
							other.Doorways[(int)otherDoorIndex]
						)
					);
				}
			} else if (tileConnection == address) {
				throw new Exception($"Tile connects to itself??");
			} else { // tileConnection is farther from root
				// noop - let other tile connect itself
			}
		}
		return tile;
	}
	
	// extraContext is parent map
	public override T Deserialize(DeserializationContext dc) {
		int address = dc.Address;
		
		dc.ConsumeUntil(
			(byte b) => b == 0
		).CastInto(out string id);
		dc.Consume(1); // consume null terminator
		
		// The fact that I can't do T.GetPrefab is the dumbest shit. 
		// Why does C# hate static methods so much??
		T t = (T)Tile.GetPrefab(id)?.Instantiate(ParentMap.transform);
		if (t == null) {
			throw new NullReferenceException($"Could not find a prefab with id '{id}'");
		}
		return Deserialize(t, dc);
		
	}
}

