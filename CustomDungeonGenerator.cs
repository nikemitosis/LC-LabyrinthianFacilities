namespace LabyrinthianFacilities;

using BoundsExtensions;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using Unity.Netcode;

// Ambiguity between System.Random and UnityEngine.Random
using Random = System.Random;

/*public abstract class Door : MonoBehaviour {
	// Properties
	public bool IsInUse {get;}
	
	// Private/Protected
	protected (Doorway d1, Doorway d2) doorways;
	
	// Monobehaviour Stuff
	public void Awake() {
		this.doorways.d1 = null;
		this.doorways.d2 = null;
	}
}

public class Door<DoorState> : Door {
	// Delegates & Events
	public delegate void DoorMoveDelegate(Door<DoorState> door, DoorState open);
	public event DoorMoveDelegate DoorMoveEvent;
	
	// Properties
	public new bool IsInUse {get {return doorways.d1 != null && doorways.d2 != null;}}
	
	// Private/Proteced
	protected DoorState state;
	
	// Native methods
	public void DoorMove(DoorState state) {
		this.state = state;
		DoorMoveEvent?.Invoke(this,state);
	}
}*/

public class Doorway : MonoBehaviour {
	// Properties
	public Tile Tile {get {return this.tile;}}
	public Vector2 Size {get {return this.size;} set {this.size = value;}}
	public bool IsVacant {get {return this.connection == null;}}
	public bool Initialized {get {return this.initialized;}}
	
	// Protected/Private
	protected bool initialized;
	protected Tile tile;
	[SerializeField]
	protected Vector2 size;
	protected Doorway connection;
	
	// Monobehaviour Stuff
	public void Awake() {
		this.connection = null;
		this.tile = this.GetComponentInParent<Tile>(includeInactive: true);
		if (this.tile == null) {
			Plugin.LogError("Doorway cannot find its parent tile");
		}
	}
	
	// Native Stuff
	public bool Fits(Doorway other) {
		return this.size == other.size;
	}
	
	public void Connect(Doorway other) {
		if (!Fits(other)) {
			throw new ArgumentException("Cannot connect doors that do not fit together");
		}
		this.connection = other;
		other.connection = this;
	}
	
	public void FixRotation() {
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
}

public class Tile : MonoBehaviour {
	// Delegates & Events
	public delegate void TileInstantiatedEventDelegate(Tile tile);
	public event TileInstantiatedEventDelegate TileInstantiatedEvent;
	
	// Properties
	public bool Initialized {
		get { return initialized; }
		protected set {
			initialized = value; 
			if (initialized == false) {
				Plugin.LogWarning($"Why did we uninitialize tile {this.gameObject}?");
			}
		}
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
	
	// Protected/Private
	protected Doorway[] doorways = null;
	protected Bounds bounding_box = new Bounds(Vector3.zero,Vector3.zero);
	protected bool initialized = false;	
	
	// Native Methods
	public Tile Instantiate(Transform parent=null) {
		
		var newtile = GameObject.Instantiate(this.gameObject).GetComponent<Tile>();
		newtile.bounding_box = new Bounds(Vector3.zero,Vector3.zero);
		
		// rotation & position handled internally, do not inherit from parent
		Vector3 diff = this.bounding_box.center - this.transform.position;
		newtile.gameObject.transform.SetParent(parent,worldPositionStays: true);
		newtile.bounding_box.center = newtile.gameObject.transform.position + diff;
		
		newtile.gameObject.transform.rotation = Quaternion.LookRotation(Vector3.forward);
		
		TileInstantiatedEvent?.Invoke(newtile);
		return newtile;
	}
	
	public void Initialize(Bounds bounds) {
		if (initialized) return;
		initialized = true;
		
		this.bounding_box = bounds;
		this.bounding_box.FixExtents();
	}
	
	public bool Intersects(Tile other) {
		// we dont want to include borders, and are ok with a little wiggle-room
		// hence the random offset of extents
		this.bounding_box.extents -= Vector3.one*1/2; 
		other.bounding_box.extents -= Vector3.one*1/2;
		bool rt = this.bounding_box.Intersects(other.bounding_box);
		
		for (int i=0; i<3; i++) {
			if (this.bounding_box.extents[i] < 0) {
				Plugin.LogError($"Bad bounding box on {this}");
			}
			if (other.bounding_box.extents[i] < 0) {
				Plugin.LogError($"Bad bounding box on {other}");
			}
		}
		
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
	}
	
	public Tile PlaceAsRoot(Transform parent) {
		Tile rt = this.Instantiate(parent);
		rt.MoveTo(parent.position);
		return rt;
	}
	
	
	public Tile Place(int thisDoorwayIdx, Doorway other) {
		if (thisDoorwayIdx >= this.Doorways.Length) {
			Plugin.LogWarning($"Cannot find new tile's door; dropping... ({this.gameObject})");
			return null;
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
				Plugin.LogDebug($"Tile {tile.gameObject.name} would collide with {t.gameObject.name}");
				Plugin.LogDebug($"Connector: \t{tile.GetComponentInParent<Tile>().gameObject.name}");
				Plugin.LogDebug($"OtherBounds\t{t.BoundingBox}");
				Plugin.LogDebug($"TileBounds \t{tile.BoundingBox}");
				tile.transform.SetParent(null);
				GameObject.Destroy(tile.gameObject);
				return null;
			}
		}
		thisDoorway.Connect(other);
		return tile;
	}
}

public class GameMap : MonoBehaviour {
	
	// Delegates & Events
	public delegate IEnumerable<Tile> TileGenerator(GameMap gamemap);
	
	public delegate void TileInsertionFailDelegate(GameMap map,Tile tile);
	public event TileInsertionFailDelegate TileInsertionFailEvent;
	
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
	protected GameObject rootObj;
	protected Tile rootTile;
	
	protected Dictionary<Vector2,List<Doorway>> leaves;
	protected uint MAX_ATTEMPTS=5;
	
	private int _seed;
	public Random rng {get; private set; }
	
	// MonoBehaviour Stuff
	public virtual void Awake() {
		this.rootTile = null;
		this.leaves = new Dictionary<Vector2,List<Doorway>>();
		this.rng = new Random(Environment.TickCount);
	}
	
	// Native Methods
	public IEnumerator GenerateCoroutine(TileGenerator tileGen,int? seed=null) {
		
		if (seed != null) this.Seed = (int)seed;
		Plugin.LogInfo($"Seed: {Seed}");
		
		foreach (Tile tile in tileGen(this)) {
			Plugin.LogInfo($"Attempting to insert tile {tile}..");
			if (!this.AddTile(tile)) {
				this.TileInsertionFailEvent?.Invoke(this,tile);
			}
			// yield return null;
			// yield return Plugin.DebugWait();
		}
		yield break;
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
	}
	
	public bool AddTile(Tile tile) {
		if (RootTile == null) {
			Plugin.LogInfo($"Placing root tile '{tile.gameObject}'");
			this.RootTile = tile.PlaceAsRoot(this.gameObject.transform);
			foreach (Doorway d in this.RootTile.Doorways) {
				AddLeaf(d);
			}
			return true;
		}
		
		List<Doorway> leafset = null;
		int leafIdx = 0; // List indices cant be uint T_T
		
		Tile newTile = null;
		int newTileTargetDoorwayIdx = 0;
		
		bool forElse = true;
		for (uint i=0; i<MAX_ATTEMPTS; i++) {
			newTileTargetDoorwayIdx = rng.Next(tile.Doorways.Length);
			Vector2 newTileTargetDoorwaySize = tile.Doorways[newTileTargetDoorwayIdx].Size;
			
			if (!this.leaves.TryGetValue(newTileTargetDoorwaySize,out leafset)) {
				Plugin.LogInfo($"Door size not present in leaves: {newTileTargetDoorwaySize}");
				continue;
			}
			leafIdx = rng.Next(leafset.Count);
			
			newTile = tile.Place(newTileTargetDoorwayIdx,leafset[leafIdx]);
			
			if (newTile != null) {
				forElse = false;
				break;
			}
		} if (forElse) {
			Plugin.LogWarning("Exceeded maximum number of attempts in GameMap.AddTile");
			return false;
		}
		
		leafset.RemoveAt(leafIdx);
		for (int i=0; i<newTile.Doorways.Length; i++) {
			if (i != newTileTargetDoorwayIdx) {
				this.AddLeaf(newTile.Doorways[i]);
			}
		}
		
		return true;
	}
}