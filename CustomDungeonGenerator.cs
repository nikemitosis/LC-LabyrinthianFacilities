namespace LabyrinthianFacilities;

using BoundsExtensions;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

// Ambiguity between System.Random and UnityEngine.Random
using Random = System.Random;

public class Doorway : MonoBehaviour {
	// Properties
	public Tile Tile {get {return this.tile;}}
	public Vector2 Size {get {return this.size;} protected set {this.size = value;}}
	public bool IsVacant {get {return this.connection == null;}}
	public bool Initialized {
		get {return this.initialized;} 
		protected set {this.initialized = value;}
	}
	
	// Protected/Private
	protected bool initialized;
	protected Tile tile;
	protected Vector2 size;
	protected Doorway connection;
	
	
	// Native Stuff
	public virtual void Initialize() {
		if (this.Initialized) return;
		this.Initialized = true;
		
		this.connection = null;
		this.tile = this.GetComponentInParent<Tile>(includeInactive: true);
		if (this.tile == null) {
			throw new NullReferenceException("Doorway cannot find its parent tile");
		}
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
	
	
}

public class Tile : MonoBehaviour {
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
	
	public NavMeshBuildSource[] Navigables {get {return this.navigables;}}
	public NavMeshLinkData[] Links {get {return this.links;}}
	
	// Protected/Private
	protected Doorway[] doorways = null;
	protected Bounds bounding_box = new Bounds(Vector3.zero,Vector3.zero);
	protected bool initialized = false;
	
	protected NavMeshBuildSource[] navigables = new NavMeshBuildSource[0];
	protected NavMeshLinkData[] links = new NavMeshLinkData[0];
	
	
	// Native Methods
	public Tile Instantiate(Transform parent=null) {
		var newtile = GameObject.Instantiate(this.gameObject).GetComponent<Tile>();
		newtile.bounding_box = new Bounds(Vector3.zero,Vector3.zero);
		
		// rotation & position handled internally, do not inherit from parent
		Vector3 diff = this.bounding_box.center - this.transform.position;
		newtile.gameObject.transform.SetParent(parent,worldPositionStays: true);
		newtile.bounding_box.center = newtile.transform.position + diff;
		
		newtile.gameObject.transform.rotation = Quaternion.LookRotation(Vector3.forward);
		
		newtile.Initialize();
		TileInstantiatedEvent?.Invoke(newtile);
		return newtile;
	}
	
	// Do placement-independent initializtion here (including bounds, since bounds are 
	// affected by MoveTo and RotateBy
	// Do *not* do navmesh stuff here
	protected virtual void Initialize() {
		if (initialized) return;
		initialized = true;
		return;
	}
	
	public bool Intersects(Tile other) {
		// we dont want to include borders, and are ok with a little wiggle-room
		// hence the random offset of extents
		this.bounding_box.extents -= Vector3.one*1/2; 
		other.bounding_box.extents -= Vector3.one*1/2;
		bool rt = this.bounding_box.Intersects(other.bounding_box);
		
		//restore bounding boxes
		this.bounding_box.extents += Vector3.one*1/2;
		other.bounding_box.extents += Vector3.one*1/2;
		
		return rt;
	}
	
	// WARNING: Bounding box increases *permanantly* with most calls. This should only be called once
	// per tile. Should eventually move away from AABB anyway... 
	// the only exception to this warning is 90x degree rotations
	public void RotateBy(Quaternion quat) {
		Vector3 diff = this.bounding_box.center - this.transform.position;
		
		this.bounding_box.center = this.transform.position + quat * diff;
		this.bounding_box.extents = quat * this.bounding_box.extents;
		this.bounding_box.FixExtents();
		this.transform.rotation *= quat;
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
		
		var tile = this.Instantiate(parent: other.gameObject.transform);
		var thisDoorway = tile.Doorways[thisDoorwayIdx];
		
		// Undo this rotation, do other rotation, do 180 about vertical axis
		Quaternion rotation = (
			Quaternion.Inverse(thisDoorway.gameObject.transform.rotation) 
			* other.gameObject.transform.rotation
			* new Quaternion(0,1,0,0)
		);
		
		tile.RotateBy(rotation);
		
		//local position accounts for parent rotation, which makes sense, 
		// but fucking confused me for so long
		Vector3 doorLocalPos = thisDoorway.transform.position - tile.bounding_box.center;
		tile.MoveTo(
			other.transform.position - doorLocalPos
		);
		foreach (Tile t in tile.GetComponentInParent<GameMap>().GetComponentsInChildren<Tile>()) {
			if (tile.Intersects(t) && !object.ReferenceEquals(t,tile)) {
				tile.transform.SetParent(null);
				GameObject.Destroy(tile.gameObject);
				return null;
			}
		}
		thisDoorway.Connect(other);
		this.OnConnect?.Invoke(this);
		return tile;
	}
}

public struct PlacementInfo {
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

public interface ITileGenerator {
	public IEnumerable<PlacementInfo> Generator(GameMap map);
	
	public void FailedPlacementHandler(Tile tile) {
		return;
	}
}

public class GameMap : MonoBehaviour {
	
	// Delegates & Events
	
	// In the event of a failed insertion, tile is null 
	public delegate void TileInsertionDelegate(Tile tile);
	public event TileInsertionDelegate TileInsertionEvent;
	
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
	protected Tile rootTile;
	
	private Dictionary<Vector2,List<Doorway>> leaves;
	private int numLeaves = 0;
	protected NavMeshDataInstance navmesh;
	protected List<NavMeshLinkInstance> links;
	
	private int _seed;
	public Random rng {get; private set; }
	
	// MonoBehaviour Stuff
	protected virtual void Awake() {
		this.rootTile = null;
		this.leaves = new Dictionary<Vector2,List<Doorway>>();
		this.rng = new Random(Environment.TickCount);
		this.links = new List<NavMeshLinkInstance>();
	}
	
	protected virtual void OnDestroy() {
		NavMesh.RemoveNavMeshData(navmesh);
	}
	
	// Native Methods
	public virtual IEnumerator GenerateCoroutine(ITileGenerator tileGen, int? seed=null) {
		
		if (seed != null) this.Seed = (int)seed;
		
		foreach (PlacementInfo placement in tileGen.Generator(this)) {
			this.TileInsertionEvent?.Invoke(this.AddTile(placement));
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
			
			//order not guaranteed...
			foreach (var kvpair in this.leaves) {
				if (idx < kvpair.Value.Count) return kvpair.Value[idx];
				idx -= kvpair.Value.Count;
			}
			throw new SystemException("numLeaves is incorrect!");
		} else {
			List<Doorway> ds;
			if (!this.leaves.TryGetValue((Vector2)size,out ds)) return null;
			
			int idx = idxn ?? this.rng.Next(ds.Count);
			return ds[idx];
		}
	}
	
	public void AddLeaf(Doorway d) {
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
	
	public virtual Tile AddTile(PlacementInfo placement) {
		Tile tile = placement.NewTile;
		if (RootTile == null) {
			this.RootTile = tile.PlaceAsRoot(this.gameObject.transform);
			foreach (Doorway d in this.RootTile.Doorways) {
				AddLeaf(d);
			}
			return RootTile;
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
		}
		
		return newTile;
	}
	
	public void GenerateNavMesh(int agentId) {
		// Clear old data
		NavMesh.RemoveNavMeshData(this.navmesh);
		foreach (var link in this.links) {
			NavMesh.RemoveLink(link);
		}
		links.Clear();
		
		List<NavMeshBuildSource> sources = new();
		foreach (Tile t in this.GetComponentsInChildren<Tile>()) {
			sources.AddRange(t.Navigables);
			foreach (NavMeshLinkData link in t.Links) {
				this.links.Add(NavMesh.AddLink(link));
			}
		}
		
		var navmeshdata = NavMeshBuilder.BuildNavMeshData(
			NavMesh.GetSettingsByID(agentId),
			sources,
			new Bounds(Vector3.zero,Vector3.one * float.PositiveInfinity),
			transform.position,
			transform.rotation
		);
		this.navmesh = NavMesh.AddNavMeshData(navmeshdata);
		this.navmesh.owner = this;
	}
}