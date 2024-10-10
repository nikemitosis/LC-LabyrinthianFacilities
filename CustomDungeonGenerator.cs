namespace LabyrinthianFacilities;

using System;
using System.Collections.Generic;

using UnityEngine;
using Unity.Netcode;

public class DoorConnectionType {
	protected DoorConnectionType[] accepts;
	
	public bool Accepts(DoorConnectionType other) {
		foreach (var i in accepts) {
			if (other == i) return true;
		}
		return false;
	}
}

public class DoorwayType {
	protected DoorConnectionType doorConnectionType;
	protected GameObject[] prefabs;
	protected GameObject[] antifabs;
	
}
public class Doorway {
	protected DoorwayType doorwayType
	protected GameObject gameObject;
	public bool isAntifab {
		get {tiles.t2 == null;}
	}
	protected bool isLocked;
	protected (Tile t1,Tile t2) tiles;
	
	public Doorway(Tile t, DoorwayType doorwayType) {
		this.doorwayType = doorwayType;
		this.gameObject = doorwayType.antifabs[
			MapHandler.Instance.Rng.Next(doorwayType.antifabs.Length)
		];
		this.tiles = (t,null);
	}
}

public struct QtyBounds {
	uint lower;
	uint? upper;
}

public class TileType {
	public GameObject prefab {get; protected set;}
	
	protected DoorwayType[] doorways {get; protected set;}
}


public struct TileRequirements {
	QtyBounds global_limits = QtyBounds{0,null};
	QtyBounds iter_limits = QtyBounds{0,null};
	bool global_required = false;
	bool iter_required = false;
}

// Represents an actual, placed tile
public class Tile {
	protected TileType tileType;
	protected Doorway[] doors;
	protected List<Doorway> unused_doors;
	protected List<Doorway> used_doors;
	protected GameObject prefabInstance;
	
	public Tile(TileType prefab) {
		this.tileType = prefab;
		this.doors = new Doorway[prefab.doorways.Length]{};
		this.prefabInstance = GameObject.Instantiate(prefab.prefab);
		this.unused_doors = new List<Doorway>();
	}
	
	
	public Tile(TileType prefab) {
		this.prefabInstance = GameObject.Instantiate(prefab.prefab);
		this.doors = new Doorway[prefab.doorways.Length];
	}
}

public class Submap {
	Dict<TileType,TileRequirements> tiles;
	Dict<Submap,TileType[]> transitions;
}

public struct SubmapTransition {
	Submap A,B;
	TileType[] transition;
}

public class MapGenerationRules {
	protected SubmapTransition[] subMapTransitions;
	protected Dictionary<Submap,TileType[]> starts;
	public float TerminationRate {get; protected set;} = 0.3;
	protected float ChangeSubmapChance {get; protected set;} = 0.1;
	
	public MapGenerationRules(DunGen.Graph.DungeonFlow flow) {
		flow.Length;
		flow.DoorwayConnectionChance;
		flow.RestrictConnectionToSameSection?;
	}
	
	public (Submap,TileType) GetStart() {
		Submap startmap = rules.starts.Keys[MapHandler.Instance.Rng.Next(rules.starts.Count)];
		return (
			startmap,
			rules.starts[startmap][MapHandler.Instance.Rng.Next(possibleStartRooms.Length)]
		);
	}
}

public class GameSubmap {
	protected Submap submap;
	protected List<Tile> leaves;
	protected List<(Tile t, GameSubmap sm)> edgeNodes;
	
	public IsLeaf {get {this.leaves.Length != 0;}}
	
	public GameSubmap(Submap sm, TileType startTile) {
		this.submap = sm;
		this.leaves = new List<Tile>();
		this.edgeNodes = new List();
		Tile tile = new Tile(startTile);
		
		this.leaves.Add(tile);
	}
	
	public bool AddTile(MapGenerationRules rules) {
		Tile leaf = this.leaves[MapHandler.Instance.Rng.Next(this.leaves.Length)];
		return leaf.AddTile(sm,rules);
	}
}

public class GameMap {
	protected (GameSubmap sm,Tile t) root = null;
	protected List<GameSubmap> leaves = new List<GameSubmap>();
	
	public void Init(MapGenerationRules rules,int? seed=null) {
		this.GenerateNew(rules,seed);
	}
	
	private void GenerateNew(MapGenerationRules rules,int? seed=null) {
		if (seed != null) MapHandler.Instance.Seed = seed;
		
		this.Start(rules.GetStart());
		
		while (MapHandler.Instance.Rng.NextSingle() >= rules.terminationRate) {
			tgtSubmap = this.leaves[MapHandler.Instance.Rng.Next(this.leaves.Length)];
			tgtSubmap.AddTile(rules);
		}
	}
	
	private void Start((Submap sm,TileType t) start) {
		this.root = (new GameSubmap(start.sm,start.t), new Tile(start.t));
		this.leaves.Add(root.sm);
	}
	
}

public class MapHandler : NetworkBehavior {
	public MapHandler Instance {get; private set;}
	internal static GameObject prefab = null;
	
	private int _seed;
	public int Seed {
		get {
			this._seed
		} set {
			this.Rng = new Random(value);
			this._seed = value;
		}
	}
	
	internal Random Rng {get; private set;} = Random(Environment.TickCount);
	
	private Dictionary<ulong,GameObject> maps = null;
	
	public override void OnNetworkSpawn() {
		if (Instance != null) return;
		map_container = new GameObject(Plugin.NAME);
		map_container.AddComponent<NetworkObject>();
	}
	
	public void NewMap(ulong mapId, MapGenerationRules rules, int? seed=null) {
		GameObject newmap;
		if (maps.TryGetValue(mapId,newmap)) return;
		newmap = new GameObject($"map:{mapId}");
		newmap.transform.SetParent(map_container);
		newmap.AddComponent<GameMap>().Init(rules,seed);
	}
}