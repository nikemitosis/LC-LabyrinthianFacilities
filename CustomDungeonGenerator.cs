namespace LabyrinthianFacilities;

using BoundsExtensions;
using Serialization;
using Util;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;

// Ambiguity between System.Random and UnityEngine.Random
using Random = System.Random;

public class Doorway : MonoBehaviour {
	// Events
	// OnDestroy invokes OnDisconnectEvent first (if applicable), then OnDestroyEvent
	public event Action<Doorway> OnDisconnectEvent;
	public event Action<Doorway> OnDestroyEvent;
	public event Action<Doorway,Doorway> OnConnectEvent;
	
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
		this.OnConnectEvent?.Invoke(other,this);
	}
	
	public void Disconnect() {
		if (this.connection == null) return;
		var con = this.connection;
		this.connection = null;
		con.Disconnect();
		this.OnDisconnectEvent?.Invoke(this);
	}
}

public class Tile : MonoBehaviour {
	// Properties
	public bool Initialized {
		get { return initialized; }
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
	
	public bool IsPrefab {get {return !this.gameObject.scene.IsValid();}}
	
	public Doorway[] Doorways {
		get {return this.doorways ??= this.GetComponentsInChildren<Doorway>();}
	}
	public Bounds BoundingBox {get {return this.bounding_box;}}
	public GameMap Map {get {return this.GetComponentInParent<GameMap>(true);}}
	
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
	
	public static float intersection_tolerance = 1f/8;
	public bool Intersects(Tile other) {
		var tbb = this.bounding_box;
		var obb = other.bounding_box;
		
		// we dont want to include borders, and are ok with a little wiggle-room
		// hence the random offset of extents
		tbb.extents -= Vector3.one*intersection_tolerance;
		obb.extents -= Vector3.one*intersection_tolerance;
		
		tbb.size = new Vector3(
			tbb.size.x < 0 ? 0 : tbb.size.x,
			tbb.size.y < 0 ? 0 : tbb.size.y,
			tbb.size.z < 0 ? 0 : tbb.size.z
		);
		
		obb.size = new Vector3(
			obb.size.x < 0 ? 0 : obb.size.x,
			obb.size.y < 0 ? 0 : obb.size.y,
			obb.size.z < 0 ? 0 : obb.size.z
		);
		
		return tbb.Intersects(obb) && obb.size != Vector3.zero && tbb.size != Vector3.zero;
	}
	
	// WARNING: Bounding box increases *permanantly* with most calls. This should really only be 
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
		foreach (Tile t in this.Map.GetComponentsInChildren<Tile>()) {
			if (this.Intersects(t) && !object.ReferenceEquals(t,this)) {
				this.transform.SetParent(oldParent);
				return false;
			}
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
	
	public PlacementInfo(Tile nt, int ndi=0, Doorway ap=null) {
		newtile = nt;
		newDoorwayIdx = ndi;
		attachmentPoint = ap;
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
	
	public void FailedPlacementHandler(Tile tile) {
		return;
	}
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
		get {return rootTile;} 
		protected set {rootTile=value;}
	}
	
	protected virtual float LeafPrecision {get {return 1.0f / (1 << (int)leafPosPrecision);}}
	
	// Protected/Private
	protected Tile rootTile;
	
	private Dictionary<Vector2,List<Doorway>> leaves;
	private Dictionary<Vector3,List<Doorway>> leavesByPos;
	// if extraConnections[d1] = d2, d2 should not be a key in extraConnections, and d1 should not be a value
	private Dictionary<Doorway,Doorway> extraConnections;
	// Overly precise positions lead to leaves that *could* overlap not overlapping
	// 0 => round to nearest 1.00f
	// 1 => round to nearest 0.50f
	// 2 => round to nearest 0.25f, etc.
	// values above 31 are invalid due to the implementation of LeafPrecision, unless overridden
	public byte leafPosPrecision {get; set;} = 3;
	
	private int numLeaves = 0;
	protected NavMeshSurface navSurface;
	
	// MonoBehaviour Stuff
	protected virtual void Awake() {
		this.rootTile = null;
		this.leaves = new Dictionary<Vector2,List<Doorway>>();
		this.leavesByPos = new();
		this.extraConnections = new();
		
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
		} else if (action is ConnectAction connection)  {
			(Doorway d1, Doorway d2) = connection.Doorways;
			this.extraConnections.Add(d1,d2);
			d1.Connect(d2);
		} else if (action is DisconnectAction dc) {
			dc.d1.Disconnect();
			this.RemoveExtraConnection(dc.d1);
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
			yield return null;
		}
		GenerationCompleteEvent?.Invoke(this);
	}
	
	private void RemoveExtraConnection(Doorway d) {
		this.extraConnections.Remove(d);
	}
	
	public IList<Doorway> GetLeaves(Vector2 size) {
		if (!this.leaves.TryGetValue(size, out List<Doorway> ds)) return null;
		return ds.Count == 0 ? null : ds.AsReadOnly();
	}
	
	public IEnumerable<IList<Doorway>> GetOverlappingLeaves() {
		foreach (var entry in this.leavesByPos) {
			if (entry.Value.Count > 1) yield return entry.Value.AsReadOnly();
		}
	}
	public IEnumerable<(Doorway d1, Doorway d2)> GetExtraConnections() {
		foreach (var entry in this.extraConnections) {
			yield return (entry.Key,entry.Value);
		}
	}
	
	protected Vector3 LeafPosition(Doorway leaf) {
		if (leaf == null) {
			throw new NullReferenceException($"Cannot get the position of a destroyed leaf doorway. ");
		}
		Vector3 pos = leaf.transform.position;
		// coord ~= LeafPrecision * a  for some int a
		// coord / LeafPrecision ~= a
		// a = (int)((coord / LeafPrecision)+0.5f)
		// ^(add 0.5f to round in both directions instead of always rounding down)
		// rounded = LeafPrecision * (int)((coord / LeafPrecision)+0.5f)
		return new Vector3(
			LeafPrecision * (int)((pos.x / LeafPrecision)+0.5f),
			LeafPrecision * (int)((pos.y / LeafPrecision)+0.5f),
			LeafPrecision * (int)((pos.z / LeafPrecision)+0.5f)
		);
	}
	
	public void AddLeaf(Doorway d) {
		if (d == null) return;
		
		List<Doorway> leaves;
		if (!this.leaves.TryGetValue(d.Size, out leaves)) {
			leaves = new List<Doorway>();
			this.leaves.Add(d.Size,leaves);
		}
		if (leaves.Contains(d)) Plugin.LogFatal($"Duplicate leaf {d.Tile.name}.{d.name}");
		leaves.Add(d);
		
		Vector3 leafpos = LeafPosition(d);
		if (!this.leavesByPos.TryGetValue(leafpos, out leaves)) {
			leaves = new(2);
			this.leavesByPos.Add(leafpos,leaves);
		}
		if (leaves.Contains(d)) Plugin.LogFatal($"Duplicate leaf pos {d.Tile.name}.{d.name}");
		leaves.Add(d);
		
		numLeaves++;
	}
	
	public void RemoveLeaf(Doorway d) {
		if (d == null) {
			throw new NullReferenceException($"Cannot remove a leaf that has already been destroyed");
		}
		try {
			this.leaves[d.Size].Remove(d);
		} catch (KeyNotFoundException) {
			Plugin.LogFatal($"No leaves of size {d.Size}");
		}
		try {
			this.leavesByPos[LeafPosition(d)].Remove(d);
		} catch (KeyNotFoundException) {
			Plugin.LogFatal($"No leaves at {LeafPosition(d)}");
		}
		numLeaves--;
	}
	
	public virtual Tile AddTile(PlacementInfo placement) {
		Tile newTile = placement.NewTile;
		if (newTile.IsPrefab) {
			newTile = newTile.Instantiate(this.gameObject.transform);
		}
		if (this.RootTile == null) {
			this.RootTile = newTile;
			this.RootTile.PlaceAsRoot(this.gameObject.transform);
			foreach (Doorway d in this.RootTile.Doorways) {
				AddLeaf(d);
				subscribeToDoorwayEvents(d);
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
		
		RemoveLeaf(leaf);
		for (int i=0; i<newTile.Doorways.Length; i++) {
			if (i != newTileTargetDoorwayIdx) {
				this.AddLeaf(newTile.Doorways[i]);
			}
			subscribeToDoorwayEvents(newTile.Doorways[i]);
		}
		
		return newTile;
	}
	
	public virtual void RemoveTile(RemovalInfo removal) {
		foreach (Tile t in removal.Target.gameObject.GetComponentsInChildren<Tile>()) {
			this.TileRemovalEvent?.Invoke(t);
		}
		GameObject.Destroy(removal.Target.gameObject);
	}
	
	private void subscribeToDoorwayEvents(Doorway d) {
		d.OnDisconnectEvent += (Doorway d) => {this.AddLeaf(d); this.RemoveExtraConnection(d);};
		d.OnDestroyEvent += (Doorway d) => {this.RemoveLeaf(d); this.RemoveExtraConnection(d);};
		d.OnConnectEvent += (Doorway d,Doorway e) => this.RemoveLeaf(d);
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

public class GameMapSerializer<T,TTile,TTileSer> : Serializer<T> 
	where T : GameMap 
	where TTile : Tile 
	where TTileSer : ISerializer<TTile>, new()
{
	/* Format:
	 *   Name
	 *   RootTile
	*/
	public override void Serialize(SerializationContext sc, T map) {
		sc.Add(map.name.GetBytes());
		sc.Add(new byte[1]{0});
		
		sc.AddInline(map.RootTile, new TTileSer());
	}
	
	// Expects that ident has already been consumed
	// extraContext is TTileSer that deserializes tiles, or null for a default deserializer
	protected override T Deserialize(T map, DeserializationContext dc, object extraContext=null) {
		var tileDeserializer = ((TTileSer)extraContext) ?? new TTileSer();
		
		map.AddTile(new PlacementInfo((TTile)dc.ConsumeInline(tileDeserializer,map)));
		
		return map;
	}
	
	public override T Deserialize(DeserializationContext dc, object extraContext=null) {
		dc.ConsumeUntil(
			(byte b) => (b == 0)
		).CastInto(out string id);
		dc.Consume(1); // null terminator
		
		T rt = new GameObject(id).AddComponent<T>();
		return Deserialize(rt, dc, extraContext);
	}
}

public class TileSerializer<T> : Serializer<T> where T : Tile {
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
	
	protected override T Deserialize(
		T tile, DeserializationContext dc, object extraContext=null
	) {
		GameMap parentMap = (GameMap)extraContext;
		int address = dc.Address;
		bool hasConnected = false;
		
		dc.Consume(2).CastInto(out ushort numDoors);
		for (int didx=0; didx<numDoors; didx++) {
			int thisDoorIndex = didx;
			int tileConnection = dc.ConsumeReference(this, context: parentMap);
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
					parentMap.AddTile(
						new PlacementInfo(
							tile, 
							thisDoorIndex, 
							other.Doorways[(int)otherDoorIndex]
						)
					);
				} else {
					parentMap.PerformAction(
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
	public override T Deserialize(DeserializationContext dc, object extraContext=null) {
		int address = dc.Address;
		GameMap parentMap = (GameMap)extraContext;
		if (parentMap == null) {
			throw new InvalidCastException(
				$"{extraContext} could not be cast into a {nameof(GameMap)}"
			);
		}
		
		dc.ConsumeUntil(
			(byte b) => b == 0
		).CastInto(out string id);
		dc.Consume(1); // consume null terminator
		
		// The fact that I can't do T.GetPrefab is the dumbest shit. 
		// Why does C# hate static methods so much??
		T t = (T)Tile.GetPrefab(id)?.Instantiate(parentMap.transform);
		if (t == null) {
			throw new NullReferenceException($"Could not find a prefab with id '{id}'");
		}
		return Deserialize(t, dc, parentMap);
		
	}
}

