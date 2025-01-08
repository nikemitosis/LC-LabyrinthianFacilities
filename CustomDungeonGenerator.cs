namespace LabyrinthianFacilities;

using BoundsExtensions;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;

// Ambiguity between System.Random and UnityEngine.Random
using Random = System.Random;

// [Serializable]
public class Doorway : MonoBehaviour {
	// Events
	public event Action<Doorway> OnDisconnect;
	public event Action<Doorway> OnDestroyEvent;
	
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
	
	// Protected/Private
	protected bool initialized;
	protected Tile tile;
	// [SerializeField]
	protected Vector2 size;
	// [SerializeField]
	protected Doorway connection;
	
	// Monobehaviour Stuff
	protected virtual void OnDestroy() {
		if (connection != null) this.Disconnect();
		
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
	
	public virtual void Connect(Doorway other) {
		if (!Fits(other)) {
			string msg = "Cannot connect doors that do not fit together\n"
				+ $"\t(Tiles {this.Tile} & {other.Tile})";
			throw new ArgumentException(msg);
		}
		this.connection = other;
		other.connection = this;
	}
	
	public virtual void Disconnect() {
		if (this.connection == null) return;
		var con = this.connection;
		this.connection = null;
		con.Disconnect();
		this.OnDisconnect?.Invoke(this);
	}
	
	// Serialization
	
}

// [Serializable]
public class Tile : MonoBehaviour/* , ISerializationCallbackReceiver */ {
	// Delegates & Events
	public delegate void TileReactionDelegate(Tile tile);
	public event TileReactionDelegate TileInstantiatedEvent;
	public event TileReactionDelegate OnMove;
	public event TileReactionDelegate OnConnect;
	
	
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
	
	public Doorway[] Doorways {
		get {return this.doorways ??= this.GetComponentsInChildren<Doorway>();}
	}
	public Bounds BoundingBox {get {return this.bounding_box;}}
	public GameMap Map {get {return this.GetComponentInParent<GameMap>();}}
	
	// Protected/Private
	protected Doorway[] doorways = null;
	protected Bounds bounding_box = new Bounds(Vector3.zero,Vector3.zero);
	protected bool initialized = false;
	
	// Native Methods
	public Tile Instantiate(Transform parent=null) {
		var newtile = GameObject.Instantiate(this.gameObject).GetComponent<Tile>();
		newtile.bounding_box = new Bounds(Vector3.zero,Vector3.zero);
		
		// rotation & position handled internally, do not inherit from parent
		newtile.transform.SetParent(parent,worldPositionStays: true);
		
		newtile.Initialize();
		TileInstantiatedEvent?.Invoke(newtile);
		return newtile;
	}
	
	// Do placement-independent initializtion here (including bounds, since bounds are 
	// affected by MoveTo and RotateBy
	protected virtual void Initialize() {
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
	
	// WARNING: Bounding box increases *permanantly* with most calls. This should only be called once
	// per tile. Should eventually move away from AABB anyway... 
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
		
		this.OnMove?.Invoke(this);
	}
	
	public Tile PlaceAsRoot(Transform parent) {
		Tile rt = this.Instantiate(parent);
		rt.MoveTo(parent.position);
		return rt;
	}
	
	
	public Tile Place(int thisDoorwayIdx, Doorway other) {
		if (thisDoorwayIdx >= this.Doorways.Length) {
			throw new IndexOutOfRangeException(
				$"Door index of new tile out of range. Tried to select idx {thisDoorwayIdx} with only {this.Doorways.Length} doors. "
			);
		}
		
		var tile = this.Instantiate(parent: other.transform);
		var thisDoorway = tile.Doorways[thisDoorwayIdx];
		
		// Undo this rotation, do other rotation, do 180 about vertical axis
		Quaternion rotation = (
			Quaternion.Inverse(thisDoorway.transform.rotation) 
			* other.transform.rotation
			* new Quaternion(0,1,0,0)
		);
		
		
		tile.RotateBy(rotation);
		
		// local position accounts for parent rotation, which makes sense, 
		// but fucking confused me for so long
		Vector3 doorLocalPos = thisDoorway.transform.position - tile.bounding_box.center;
		tile.MoveTo(
			other.transform.position - doorLocalPos
		);
		foreach (Tile t in tile.GetComponentInParent<GameMap>().GetComponentsInChildren<Tile>()) {
			if (tile.Intersects(t) && !object.ReferenceEquals(t,tile)) {
				// tile.transform.SetParent(null);
				GameObject.Destroy(tile.gameObject);
				return null;
			}
		}
		thisDoorway.Connect(other);
		this.OnConnect?.Invoke(this);
		return tile;
	}
	
	// Serialization
	/* private string tileName;
	public override void OnBeforeSerialize() {
		// ensure field is initialized
		var _ = this.Doorways;
		this.tileName = this.name.Substring(0,this.name.Count - 7);
	}
	
	public override void OnAfterSerialize() {
		return;
	} */
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
	
	public PlacementInfo(Tile nt=null, int ndi=0, Doorway ap=null) {
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

public interface ITileGenerator {
	public IEnumerable<GenerationAction> Generator(GameMap map);
	
	public void FailedPlacementHandler(Tile tile) {
		return;
	}
}

// [Serializable]
public class GameMap : MonoBehaviour {
	
	// Delegates & Events
	
	// In the event of a failed insertion, tile is null 
	public event Action<Tile> TileInsertionEvent;
	// TileRemovalEvent Invoked before removal actually occurs
	public event Action<Tile> TileRemovalEvent;
	
	public delegate void GenerationCompleteDelegate(GameMap map);
	public event GenerationCompleteDelegate GenerationCompleteEvent;
	
	// Properties
	public Tile RootTile {
		get {return rootTile;} 
		protected set {rootTile=value;}
	}
	public int Seed {
		get {
			return this._seed;
		} set {
			this.rng = new Random(value);
			this._seed = value;
		}
	}
	
	
	// Protected/Private
	// [SerializeField]
	protected Tile rootTile;
	
	private Dictionary<Vector2,List<Doorway>> leaves;
	private int numLeaves = 0;
	protected NavMeshSurface navSurface;
	
	private int _seed;
	public Random rng {get; private set; }
	
	// MonoBehaviour Stuff
	protected virtual void Awake() {
		this.rootTile = null;
		this.leaves = new Dictionary<Vector2,List<Doorway>>();
		this.rng = new Random(Environment.TickCount);
		
		this.navSurface = this.gameObject.AddComponent<NavMeshSurface>();
		this.navSurface.collectObjects = CollectObjects.Children;
		// this.navSurface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
		this.navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
		// ^NavMeshCollectGeometry.PhysicsColliders cause some scrap from previous days to block 
		// navigation (e.g. V-Type Engine)
		// NavmeshCollectGeometry.RenderMeshes causes the manor start tile to be unenterable/exitable
	}
	
	protected virtual void OnDestroy() {
		this.navSurface.RemoveData();
	}
	
	// Native Methods
	public virtual IEnumerator GenerateCoroutine(ITileGenerator tileGen, int? seed=null) {
		
		if (seed != null) this.Seed = (int)seed;
		
		foreach (GenerationAction action in tileGen.Generator(this)) {
			if (action is PlacementInfo) {
				this.TileInsertionEvent?.Invoke(this.AddTile((PlacementInfo)action));
			} else if (action is RemovalInfo) {
				var removal = (RemovalInfo)action;
				this.RemoveTile(removal);
			} else {
				throw new InvalidCastException($"Unknown GenerationAction: {action}");
			}
			yield return null;
			// yield return Plugin.DebugWait();
		}
		GenerationCompleteEvent?.Invoke(this);
	}
	
	public Doorway GetLeaf(Vector2? size=null, int? idxn=null) {
		if (numLeaves == 0) return null;
		if (idxn != null && (int)idxn >= numLeaves) {
			throw new ArgumentOutOfRangeException("idx >= number of leaves");
		}
		
		if (size == null) {
			int idx = idxn ?? this.rng.Next(numLeaves);
			
			// order not guaranteed...
			foreach (var kvpair in this.leaves) {
				if (idx < kvpair.Value.Count) return kvpair.Value[idx];
				idx -= kvpair.Value.Count;
			}
			throw new SystemException("numLeaves is incorrect!");
		} else {
			List<Doorway> ds;
			if (!this.leaves.TryGetValue((Vector2)size,out ds)) return null;
			
			int idx = idxn ?? this.rng.Next(ds.Count);
			return idx < ds.Count ? ds[idx] : null;
		}
	}
	
	public void AddLeaf(Doorway d) {
		if (d == null) return;
		
		List<Doorway> leaf;
		if (this.leaves.TryGetValue(d.Size, out leaf)) {
			leaf.Add(d);
		} else {
			leaf = new List<Doorway>();
			leaf.Add(d);
			this.leaves.Add(d.Size,leaf);
		}
		numLeaves++;
	}
	
	public void RemoveLeaf(Doorway d) {
		try {
			this.leaves[d.Size].Remove(d);
		} catch (KeyNotFoundException _) {}
		numLeaves--;
	}
	
	public virtual Tile AddTile(PlacementInfo placement) {
		Tile tile = placement.NewTile;
		if (this.RootTile == null) {
			this.RootTile = tile.PlaceAsRoot(this.gameObject.transform);
			foreach (Doorway d in this.RootTile.Doorways) {
				AddLeaf(d);
				subscribeToDoorwayEvents(d);
			}
			return this.RootTile;
		}
		
		List<Doorway> leafset = null;
		Doorway leaf = placement.AttachmentPoint;
		
		Tile newTile = null;
		int newTileTargetDoorwayIdx = placement.NewDoorwayIdx;
		Vector2 newTileTargetDoorwaySize = tile.Doorways[newTileTargetDoorwayIdx].Size;
		
		if (!this.leaves.TryGetValue(newTileTargetDoorwaySize,out leafset)) {
			return null;
		}
		
		newTile = tile.Place(newTileTargetDoorwayIdx,leaf);
		if (newTile == null) return null;
		
		leafset.Remove(leaf);
		numLeaves--;
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
		d.OnDisconnect += (Doorway d) => this.AddLeaf(d);
		d.OnDestroyEvent += (Doorway d) => this.RemoveLeaf(d);
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