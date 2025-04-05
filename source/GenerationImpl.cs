namespace LabyrinthianFacilities;

using System;
using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine;

using DunGen.Graph;

using Util;

using Random = System.Random;


public class PropAction : GenerationAction {
	protected Prop target;
	protected bool enable;
	
	public Prop Prop {get {return target;}}
	public bool Enable {get {return enable;}}
	
	public PropAction(Prop t, bool e) {
		target = t;
		enable = e;
	}
}

internal sealed class ArchetypeRegionPlacer : IDisposable {
	
	public DungeonFlowConverter parent {get; private set;}
	
	
	// Parameters
	private GameMap map;
	private Doorway root;
	private Archetype archetype;
	
	// State
	private HashSet<DTile> tilesPlaced;
	public int iterationsSinceLastSuccess {get; private set;}
	private DoorwayManager doorwayManager;
	
	private WeightedChoiceList<DTile> possibleTiles;
	private Tile tile;
	private ChoiceList<Doorway> possibleDoorways;
	private Doorway doorway;
	private IChoice<Doorway,float> possibleLeaves;
	private Doorway leaf;
	
	private int NumLeaves {get => doorwayManager.Leaves.Count;}
	
	// Access Properties
	public Random Rng {get => parent.Rng;}
	public bool Exhausted {get => tile == null && possibleTiles.OpenCount == 0 || NumLeaves == 0;}
	
	
	public ArchetypeRegionPlacer(
		DungeonFlowConverter parent, 
		GameMap map, 
		Doorway root, 
		Archetype archetype, 
		HashSet<DTile> tilesPlaced
	) {
		if (parent      == null) throw new ArgumentNullException("parent"     );
		if (map         == null) throw new ArgumentNullException("map"        );
		if (root        == null) throw new ArgumentNullException("root"       );
		if (archetype   == null) throw new ArgumentNullException("archetype"  );
		if (tilesPlaced == null) throw new ArgumentNullException("tilesPlaced");
		
		this.parent = parent;
		this.map = map;
		this.root = root;
		this.archetype = archetype;
		this.tilesPlaced = tilesPlaced;
		
		this.doorwayManager = new(map);
		AddDoorway(root);
		
		this.possibleDoorways = null;
		this.possibleLeaves = null;
		
		iterationsSinceLastSuccess = 0;
		
		possibleTiles = new(archetype.Tiles);
		tile = null;
		doorway = leaf = null;
		
		map.TileInsertionEvent += Handler;
	}
	
	~ArchetypeRegionPlacer() {Dispose(false);}
	
	public void Dispose() {Dispose(true); GC.SuppressFinalize(this);}
	private void Dispose(bool isDisposing) {
		if (isDisposing) this.map.TileInsertionEvent -= Handler;
	}
	
	public PlacementInfo YieldAttempt() {
		do {GetNewLeaf();} while (leaf == null && !Exhausted);
		
		if (Exhausted) {
			return null;
		}
		if (leaf == null) throw new Exception("null leaf with no exhaustion?");
		
		var rt = new PlacementInfo(tile,Array.IndexOf(tile.Doorways,doorway),leaf);
		if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"Trying {rt}");
		return rt;
	}
	
	private void Handler(Tile t) {
		if (t != null) {
			foreach (Doorway d in t.Doorways) {
				AddDoorway(d);
			}
			
			if (tile.GetComponent<DunGen.Tile>().RepeatMode == DunGen.TileRepeatMode.Disallow) {
				this.tilesPlaced.Add((DTile)tile);
			}
			
			tile = null;
			doorway = leaf = null;
			possibleTiles.Reset();
			iterationsSinceLastSuccess = 0;
			
		} else {
			if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"Placement Failed");
			iterationsSinceLastSuccess++;
		}
	}
	
	private void GetNewLeaf() {
		if (doorway == null || possibleLeaves.OpenCount == 0) {
			GetNewDoorway();
			if (doorway == null || possibleLeaves.OpenCount == 0) {
				this.leaf = null;
				return;
			}
		}
		this.leaf = possibleLeaves.Yield((float)(possibleLeaves.OpenWidth*Rng.NextDouble()));
	}
	
	private void GetNewDoorway() {
		if (tile == null || possibleDoorways.OpenCount == 0) {
			do {
				GetNewTile();
			} while (this.tilesPlaced.Contains((DTile)this.tile) && !Exhausted);
			if (tile == null) {
				this.doorway = null;
				return;
			}
		}
		
		this.doorway = possibleDoorways.Yield(Rng.Next(possibleDoorways.OpenCount));
		
		this.possibleLeaves = doorwayManager.GetLeaves(
			(Doorway d) => ( (d.Size == this.doorway.Size) ? (doorwayManager.ClearanceRadius(d)) : (0.0f) )
		);
	}
	
	private void GetNewTile() {
		if (possibleTiles.OpenCount == 0) {
			tile = null;
			return;
		}
		this.tile = possibleTiles.Yield((float)(possibleTiles.OpenWidth*Rng.NextDouble()));
		
		this.possibleDoorways = new ChoiceList<Doorway>(this.tile.Doorways);
	}
	
	private void AddDoorway(Doorway leaf) {
		// don't bother with a leaf we can never use
		if (this.archetype.GetDoorwayCountBySize(leaf.Size) == 0) {
			return;
		}
		
		doorwayManager.Add(leaf);
		possibleLeaves = null;
	}
}

public class Archetype {
	public WeightedList<DTile> Tiles;
	public Dictionary<Vector2,int> DoorwayCountBySize;
	public float Length;
	
	public Archetype(float length) {
		Tiles = new();
		DoorwayCountBySize = new();
		Length = length;
	}
	
	public void AddTile(DTile tile, float weight) {
		this.Tiles.Add(tile,weight);
		foreach (Doorway d in tile.Doorways) {
			try {
				DoorwayCountBySize[d.Size]++;
			} catch (KeyNotFoundException) {
				DoorwayCountBySize.Add(d.Size,1);
			}
		}
	}
	
	public int GetDoorwayCountBySize(Vector2 size) {
		if (!DoorwayCountBySize.TryGetValue(size, out int count)) return 0;
		return count;
	}
}

public class DungeonFlowConverter : ITileGenerator {
	
	protected DunGen.Graph.DungeonFlow flow;
	
	protected DTile StartRoom;
	
	protected List<List<Archetype>> archetypes;
	
	// tile prefabs with DunGen flag TileRepeatMode.Disallow that have been placed
	protected HashSet<DTile> tilesPlaced;
	
	public int TileCountLowerBound {get; protected set;}
	public int TileCountUpperBound {get; protected set;}
	public int TileCountAverage {get => (TileCountLowerBound + TileCountUpperBound)/2;}
	
	public int PlacementLowerBound {get => (int)(Config.Singleton.LowerIterationMultiplier*TileCountLowerBound);}
	public int PlacementUpperBound {get => (int)(Config.Singleton.UpperIterationMultiplier*TileCountLowerBound);}
	
	// if a tile fails to place within MAX_ATTEMPTS, a new tile is chosen
	private const int MAX_ATTEMPTS=10;
	private int iterationsSinceLastSuccess = 0;
	
	private int seed;
	private Random rng;
	public int Seed {
		get => this.seed;
		set {
			this.rng = new Random(value);
			this.seed = value;
		}
	}
	public Random Rng {get => rng;}
	public DunGen.Graph.DungeonFlow Flow {get => flow;}
	
	// reduce chance because each doorway pair will likely get multiple chances to connect
	// and we dont want loops to feel too chaotic
	public float DoorwayConnectionChance {get => flow.DoorwayConnectionChance / 2.0f;}
	public float DoorwayDisconnectChance {get => flow.DoorwayConnectionChance / 2.0f;}
	
	public DungeonFlowConverter(DungeonFlow flow, int seed) {
		this.Seed = seed;
		
		this.tilesPlaced = new();
		
		this.flow = flow;
		DunGen.Graph.GraphNode node = null;
		foreach (var n in flow.Nodes) {
			if (n.NodeType == DunGen.Graph.NodeType.Start) {node = n; break;}
		}
		FindStartRoom(node);
		
		int baseTileCount = (int)(
			(
				(flow.Length.Min + flow.Length.Max) / 2.0f
				* RoundManager.Instance.mapSizeMultiplier
				* RoundManager.Instance.currentLevel.factorySizeMultiplier
			)
		);
		
		this.TileCountLowerBound = (int)(baseTileCount * Config.Singleton.MinimumTileMultiplier);
		this.TileCountUpperBound = (int)(baseTileCount * Config.Singleton.MaximumTileMultiplier);
		
		float summedLength = 0.0f;
		
		this.archetypes = new();
		// Note that nodes arent included here... for now(?)
		flow.Lines.Sort(
			(DunGen.Graph.GraphLine x, DunGen.Graph.GraphLine y) => {
				float val = x.Position - y.Position;
				if (val == 0.0f) return 0;
				if (val > 0.0f) return 1;
				return -1;
			}
		);
		foreach (var line in flow.Lines) {
			List<Archetype> archetypeList = new();
			this.archetypes.Add(archetypeList);
			summedLength += line.Length;
			foreach (var archetype in line.DungeonArchetypes) {
				
				var arch = new Archetype(line.Length);
				archetypeList.Add(arch);
				
				float tset_freq = 1.0f / archetype.TileSets.Count;
				foreach (var tileset in archetype.TileSets) {
					foreach (var tile in tileset.TileWeights.Weights) {
						float tile_freq = (tile.MainPathWeight + tile.BranchPathWeight) / 2.0f;
						DTile dtile = tile.Value.GetComponent<DTile>();
						if (dtile == null) {
							Plugin.LogError("Bad dtile");
						}
						if (
							dtile == StartRoom 
							|| dtile.name == "StartRoom" 
							|| dtile.name == "ManorStartRoom"
						) continue;
						
						float freq = tset_freq * tile_freq;
						arch.AddTile(dtile, weight: freq);
					}
				}
			}
		}
		
		// normalize lengths so we can use them w.r.t the amount of tiles we're placing
		for (int i=0; i<this.archetypes.Count; i++) {
			for (int j=0; j<this.archetypes[i].Count; j++) {
				this.archetypes[i][j].Length /= summedLength;
			}
		}
	}
	
	private void FindStartRoom(DunGen.Graph.GraphNode node) {
		foreach (var tileset in node.TileSets) {
			foreach (var gameObjectChance in tileset.TileWeights.Weights) {
				foreach (
					SpawnSyncedObject subObject 
					in gameObjectChance.Value.GetComponentsInChildren<SpawnSyncedObject>(true)
				) {
					if (subObject.spawnPrefab.GetComponent<EntranceTeleport>()?.name == "EntranceTeleportA") {
						this.StartRoom = gameObjectChance.Value.GetComponent<DTile>();
						return;
					}
				}
				
			}
		}
		
		Plugin.LogError(
			$"Could not find a start room with an EntranceTeleportA. Defaulting to first tile we find."
		);
		this.StartRoom = (node
			?.TileSets[0]
			?.TileWeights
			?.Weights[0]
			?.Value
			?.GetComponent<DTile>()
		);
	}
	
	public virtual (int min,int max) GetGlobalPropRange(int id) {
		foreach (var settings in this.flow.GlobalProps) {
			if (settings.ID == id) return (settings.Count.Min,settings.Count.Max);
		}
		Plugin.LogError($"Global Prop bounds not found for id {id}");
		return (1,1);
	}
	
	public void FailedPlacementHandler(Tile tile) {
		if (tile == null) {
			iterationsSinceLastSuccess++;
		} else {
			iterationsSinceLastSuccess = 0;
		}
	}
	
	
	protected IEnumerable<PlacementInfo> PlaceRoot(GameMap map) {
		if (StartRoom == null) {
			Plugin.LogError("Start tile not found D:");
			yield return null;
			yield break;
		}
		if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug(
			$"Using '{StartRoom.name}' as start room"
		);
		
		yield return new PlacementInfo(StartRoom);
		string startname = "";
		switch (StartRoom.name) {
			case "ElevatorConnector":
				startname = "StartRoom";
			break; case "Level2StartRoomConnector":
				startname = "ManorStartRoom";
			break;
		}
		DTile fakestartroom = null;
		foreach (DTile t in Resources.FindObjectsOfTypeAll<DTile>()) {
			if (t.name == startname) {
				fakestartroom = t;
				break;
			}
		}
		if (fakestartroom != null) {
			int didx;
			for (didx=0; didx<fakestartroom.Doorways.Length; didx++) {
				if (fakestartroom.Doorways[didx].Size == map.RootTile.Doorways[0].Size) break;
			}
			Action<Tile> foo = (Tile t) => {
				if (t == null) {
					Plugin.LogFatal($"Failed to place start room - the map will fail to generate");
				}
			};
			map.TileInsertionEvent += foo;
			yield return new PlacementInfo(fakestartroom,didx,map.RootTile.Doorways[0]);
			map.TileInsertionEvent -= foo;
		}
	}
	protected IEnumerable<RemovalInfo> RemoveTiles(GameMap map) {
		var timer = new Stopwatch();
		timer.Start();
		
		Plugin.LogInfo($"Removing tiles...");
		
		int tileCount = map.TileCount;
		
		// We want at most TileCountUpperBoundTiles, but we need room for TileCountLowerBound more tiles, 
		// because that's the minimum number of tiles to add
		// We can't get more tiles than we already have by removing them, though. 
		int upperBound = Math.Min(TileCountUpperBound - PlacementLowerBound, tileCount);
		
		// We want to keep at least 80% of tiles to make sure we don't nuke too much of the map
		// but we don't want to dip below the minimum number of tiles
		int lowerBound = Math.Max(4*tileCount/5, TileCountLowerBound);
		
		if (lowerBound > upperBound) lowerBound = upperBound;
		
		int targetCount = Rng.Next(lowerBound,upperBound+1);
		int numTilesToDelete = tileCount - targetCount;
		
		if (Config.Singleton.EnableVerboseGeneration) {
			Plugin.LogDebug($"Removing {numTilesToDelete} tiles...");
		}
		
		while (numTilesToDelete > 0) {
			Tile[] tiles = map.GetComponentsInChildren<Tile>();
			if (tiles.Length <= 1) break;
			Tile selected;
			int numTilesUnderSelection = 0;
			do {
				selected = tiles[Rng.Next(tiles.Length)];
				numTilesUnderSelection = selected.GetComponentsInChildren<DTile>().Length;
			} while (numTilesUnderSelection > numTilesToDelete);
			if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"Removing {selected.name}");
			yield return new RemovalInfo(selected);
			numTilesToDelete -= numTilesUnderSelection;
		}
		
		timer.Stop();
		Plugin.LogDebug($"Finished removing tiles in {timer.Elapsed.TotalSeconds} seconds");
	}
	
	protected IEnumerable<GenerationAction> AttemptArchetypePlacement(
		GameMap map, Doorway root, Archetype archetype, int length, Action<int> reaction
	) {
		int progress = 0;
		using (
			var arp = new ArchetypeRegionPlacer(
				parent: this, map: map, root: root, archetype: archetype, tilesPlaced: tilesPlaced
			)
		) {
			int numAttempts = 0;
			while (progress != length) { // root attempts
				PlacementInfo attempt = arp.YieldAttempt();
				if (attempt != null) yield return attempt;
				if (attempt != null && arp.iterationsSinceLastSuccess == 0) {
					progress++;
					if (Config.Singleton.EnableVerboseGeneration) {
						Plugin.LogDebug(
							$"+ ({numAttempts}) {attempt.NewTile.name} | {attempt.AttachmentPoint.Tile.name}"
						);
					}
				} else {
					if (attempt == null /* || arp.iterationsSinceLastSuccess >= 30 */) {
						if (progress < 0.7f*length) {
							if (Config.Singleton.EnableVerboseGeneration) {
								Plugin.LogDebug(
									$"Root failure: Generated {progress}/{length} tiles from root {root.Tile}"
								);
								if (attempt == null) {
									Plugin.LogDebug($"Reason: Exhausted all options");
								} else {
									Plugin.LogDebug(
										$"Reason: Exceeded 30 attempts between placements"
									);
								}
							}
							if (progress != 0) {
								progress = 0;
								// allow props to initialize so they may be properly disposed
								yield return new YieldFrame();
								yield return new RemovalInfo(root.Connection.Tile);
								yield return new YieldFrame();
							}
						}
						break;
					}
				}
				if (Config.Singleton.EnableVerboseGeneration) numAttempts = arp.iterationsSinceLastSuccess;
			}
		}
		if (Config.Singleton.EnableVerboseGeneration) {
			Plugin.LogDebug($"Generated {progress}/{length} tiles from root {root.Tile}");
		}
		reaction(progress);
		yield break;
	}
	protected IEnumerable<GenerationAction> PlaceTiles(GameMap map) {
		var timer = new Stopwatch();
		timer.Start();
		
		Plugin.LogInfo($"Placing tiles...");
		
		int mapTileCount = map.TileCount;
		
		// we need at least TileCountLowerBound tiles, but we might already have enough
		int lowerBound = Math.Max(mapTileCount, TileCountLowerBound);
		int target = Rng.Next(lowerBound, TileCountUpperBound+1);
		int tile_demand = target - mapTileCount;
		
		tile_demand = Math.Max(tile_demand,PlacementLowerBound);
		tile_demand = Math.Min(tile_demand,PlacementUpperBound);
		
		if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"Queueing {tile_demand} tiles...");
		
		int tile_demand_theory = tile_demand;
		
		List<(Archetype a,int len)> archetypeSizes = new();
		for (int i=0; i<this.archetypes.Count; i++) {
			Archetype archetype = this.archetypes[i][Rng.Next(this.archetypes[i].Count)];
			
			int tileCount = (int)(archetype.Length * tile_demand + 0.5f);
			int lower = (int)(0.9f * tileCount);
			if (lower <= 0) lower = 1;
			int upper = (int)(1.1f * tileCount);
			if (upper > tile_demand_theory) upper = tile_demand_theory;
			if (upper < lower) {
				break;
			}
			tileCount = Rng.Next(lower,upper);
			tile_demand_theory -= tileCount;
			
			archetypeSizes.Add((archetype, tileCount));
		}
		
		if (tile_demand_theory != 0) {
			if (archetypeSizes.Count == 0) {
				Plugin.LogError($"Was not able to select a single archetypes to generate this round");
			}
			for (int i=0; i<archetypeSizes.Count; i++) {
				archetypeSizes[i] = (
					archetypeSizes[i].a, 
					archetypeSizes[i].len + tile_demand_theory/archetypeSizes.Count
				);
			}
		}
		
		if (Config.Singleton.EnableVerboseGeneration) {
			string msg = $"Archetype sizes: ({archetypeSizes.Count}) - ";
			foreach ((Archetype a,int len) in archetypeSizes) {
				msg += $"{len}, "; // yes theres an extra comma. It's special and his name is jerry
			}
			Plugin.LogDebug(msg.Substring(0,msg.Length-2));
		}
		
		foreach ((Archetype archetype, int length) in archetypeSizes) {
			
			IDoorwayManager doorwayManager = map.DoorwayManager;
			
			// this makes a *doorway's* chance of being chosen proportional to how many doorways in the 
			// archetype share its size
			// It does *not* make a given *size* have a chance of being chosen proportional to how many 
			// doorways in the archetype have that size
			// e.g. if you have a million doors of size A and one door of size B, you're *still* 
			// going to get a door of size A most of the time, even if the archetype's doors are 99% size B
			IChoice<Doorway,float> roots = doorwayManager.GetLeaves(
				(Doorway d) => (float)archetype.GetDoorwayCountBySize(d.Size)
			);
			
			foreach (var entry in ((WeightedList<Doorway>)roots).Entries) {
				if (entry.item == null) {
					Plugin.LogError($"null leaf");
					break;
				}
			}
			
			bool forelse = true;
			for (int i=0; i<50; i++) { // archetype attempts
				
				if (roots.OpenCount == 0) break;
				Doorway root = roots.Yield(roots.OpenWidth*(float)Rng.NextDouble());
				if (root == null) {
					Plugin.LogError("null root");
					foreach (var entry in ((WeightedList<Doorway>)roots).Entries) {
						Plugin.LogError($"{(entry.item == null ? "null" : entry.item.name)} | {entry.weight}");
					}
				}
				
				Action<int> reaction = (int p) => {
					tile_demand -= p;
					if (p != 0) forelse = false;
				};
				foreach (
					GenerationAction a in AttemptArchetypePlacement(
						map, root, archetype, length, reaction
					)
				) {
					yield return a;
				}
				if (!forelse) break;
				yield return new YieldFrame();
			} if (forelse) {
				Plugin.LogError($"Failed to generate an archetype of {length} tiles");
			}
		}
		Plugin.LogInfo($"Failed to place {tile_demand} tiles");
		
		timer.Stop();
		Plugin.LogDebug($"Finished generating tiles in {timer.Elapsed.TotalSeconds} seconds");
	}
	
	protected IEnumerable<ConnectionAction> HandleConnections(GameMap map) {
		// buffer actions so a connection can't be made and immediately disconnected 
		// before the player ever sees it
		List<ConnectionAction> actions = new();
		foreach (var action in ConnectTiles(map)) {
			actions.Add(action);
		}
		foreach (var action in DisconnectTiles(map)) {
			actions.Add(action);
		}
		return actions;
	}
	
	private IEnumerable<ConnectAction> ConnectTiles(GameMap map) {
		Plugin.LogInfo($"Queueing making some loops...");
		foreach (Connection con in map.DoorwayManager.GetPotentialConnections((Connection c) => 1.0f)) {
			if (Rng.NextDouble() < DoorwayConnectionChance) {
				if (Config.Singleton.EnableVerboseGeneration) {
					Plugin.LogDebug(
						$"C {con.d1.Tile.name}.{con.d1.name} | {con.d2.Tile.name}.{con.d2.name}"
					);
				}
				yield return new ConnectAction(con.d1, con.d2);
			}
		}
	}
	private IEnumerable<DisconnectAction> DisconnectTiles(GameMap map) {
		Plugin.LogInfo($"Queueing removing some loops...");
		foreach ((Doorway d1,Doorway d2) in (
			map.DoorwayManager.GetActiveConnections((Connection c) => 1.0f)
		)) {
			if (
				// do not delete "main path" (we don't have a way to resolve isolated parts of the map yet)
				d1.Tile.transform.parent != d2.transform
				&& d2.Tile.transform.parent != d1.transform 
				&& Rng.NextDouble() < DoorwayDisconnectChance
			) {
				if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug(
					$"D {d1.Tile.name}.{d1.name} | {d2.Tile.name}.{d2.name}"
				);
				yield return new DisconnectAction(d1,d2);
			}
		}
	}
	
	protected virtual IEnumerable<PropAction> HandleProps(DGameMap map) {
		var timer = new Stopwatch();
		timer.Start();
		
		map.InitializeGlobalPropSets(this);
		foreach (var i in HandleDoorProps(map)) yield return i;
		foreach (var i in HandleTileProps(map)) yield return i;
		foreach (var i in HandleMapProps (map)) yield return i;
		
		if (Config.Singleton.ForbiddenPassages) {
			foreach (DDoorway d in map.GetComponentsInChildren<DDoorway>()) {
				if (d.IsVacant && d.ActiveRandomObject != null && Rng.Next(100) == 0) {
					d.ActiveRandomObject.SetActive(false);
				}
			}
		}
		
		timer.Stop();
		Plugin.LogDebug($"Finished prop handling in {timer.Elapsed.TotalSeconds} seconds");
	}
	
	private IEnumerable<PropAction> HandleDoorProps(DGameMap map) {
		if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"Handling door props...");
		
		var timer = new Stopwatch();
		timer.Start();
		
		var doorways = map.GetComponentsInChildren<DDoorway>();
		foreach (DDoorway door in doorways) {
			if (door.ActiveRandomObject != null ) continue;
			
			DDoorway tgt = ((door.IsVacant || Rng.Next(2) == 0) ? (door) : ((DDoorway)door.Connection));
			
			tgt.SetActiveObject((float)Rng.NextDouble());
		}
		// Disable any blocker/connector which should not be in use
		foreach (DDoorway door in doorways) {
			foreach (Prop p in (door.IsVacant ? door.Connectors : door.Blockers)) {
				yield return new PropAction(p, false);
			}
		}
		
		timer.Stop();
		Plugin.LogDebug($"\tFinished door props in {timer.Elapsed.TotalSeconds} seconds");
		
		yield break;
	}
	
	private IEnumerable<PropAction> HandleMapProps(DGameMap map) {
		if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"Handling global props...");
		
		Stopwatch timer = new();
		timer.Start();
		
		foreach (PropSet propset in map.GlobalPropSets) {
			if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug(
				$"Handling propset w/ {(propset.Count > 0 ? propset[0.0f].name : "nothing in it")} "
				+$"({propset.Count} props)"
			);
			foreach (var action in HandlePropSetPos(propset)) {
				if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"+{action.Prop.name}");
				yield return action;
			}
			foreach (var action in HandlePropSetNeg(propset,true)) {
				if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"-{action.Prop.name}");
				yield return action;
			}
		}
		
		timer.Stop();
		Plugin.LogDebug($"\tFinished handling global props in {timer.Elapsed.TotalSeconds} seconds");
	}
	
	private IEnumerable<PropAction> HandleTileProps(DGameMap map) {
		if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"Handling local props...");
		
		var timer = new Stopwatch();
		timer.Start();
		
		foreach (DTile tile in map.GetComponentsInChildren<DTile>()) {
			foreach (PropSet propset in tile.LocalPropSets) {
				foreach (var action in HandlePropSetPos(propset)) {
					if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"+{action.Prop.name}");
					
					yield return action;
				}
			}
			foreach (PropSet propset in tile.LocalPropSets) {
				foreach (var action in HandlePropSetNeg(propset)) {
					if (Config.Singleton.EnableVerboseGeneration) Plugin.LogDebug($"-{action.Prop.name}");
					yield return action;
				}
			}
		}
		
		timer.Stop();
		Plugin.LogDebug($"\tFinished handling tile props in {timer.Elapsed.TotalSeconds} seconds");
	}
	
	private IEnumerable<PropAction> HandlePropSetPos(PropSet propset) {
		if (propset == null) {
			Plugin.LogException(new ArgumentNullException($"propset"));
			yield break;
		}
		WeightedList<Prop> copy = new();
		int numActive = 0;
		int numEnable = Rng.Next(propset.Range.min,propset.Range.max+1);
		if (numEnable > propset.Count) numEnable = propset.Count;
		foreach ((Prop prop,float weight) in propset.Entries) {
			if (prop == null && !ReferenceEquals(prop,null)) {
				Plugin.LogError($"Destroyed prop in propset");
			} else {
				if (prop.gameObject.activeSelf) numActive++;
				else if (!prop.IsDoorProp) copy.Add(prop,weight);
			}
		}
		for (int i=numActive; i<numEnable; i++) {
			Prop tgt;
			do {
				if (copy.Count == 0) {tgt = null; break;}
				tgt = copy[copy.SummedWeight*(float)Rng.NextDouble()];
				copy.Remove(tgt);
			} while (
				tgt.gameObject.activeSelf 
				|| !tgt.transform.parent.gameObject.activeInHierarchy
			);
			if (tgt == null) break;
			yield return new PropAction(tgt,true);
		}
	}
	
	private IEnumerable<PropAction> HandlePropSetNeg(PropSet propset, bool globalPropSet=false) {
		if (propset == null) {
			Plugin.LogException(new ArgumentNullException($"propset"));
			yield break;
		}
		WeightedList<Prop> copy = new();
		int numActive = 0;
		int numEnable = propset.Range.max;
		if (numEnable > propset.Count) numEnable = propset.Count;
		
		foreach ((Prop prop,float weight) in propset.Entries) {
			if (prop == null && !ReferenceEquals(prop,null)) {
				Plugin.LogError($"Destroyed prop in propset");
			} else {
				if (prop.gameObject.activeInHierarchy) {
					numActive++;
					copy.Add(prop,weight);
				}
			}
		}
		for (int i=numActive; i>numEnable; i--) {
			if (copy.Count == 0) break;
			Prop tgt = copy[copy.SummedWeight*(float)Rng.NextDouble()];
			copy.Remove(tgt);
			yield return new PropAction(tgt,false);
		}
	}
	
	public virtual IEnumerable<GenerationAction> Generator(GameMap m) {
		
		var timer = new Stopwatch();
		timer.Start();
		
		DGameMap map = (DGameMap)m;
		
		if (map.RootTile == null) {
			foreach (var action in PlaceRoot(map)) {
				if (action == null) yield break;
				yield return action;
			}
		} else {
			foreach (var action in RemoveTiles(map)) {
				yield return action;
			}
		}
		yield return new YieldFrame();
		foreach (var action in PlaceTiles(map)) {
			yield return action;
		}
		
		var conTimer = new Stopwatch();
		conTimer.Start();
		foreach (var action in HandleConnections(map)) {
			yield return action;
		}
		
		conTimer.Stop();
		Plugin.LogDebug($"Finished connections in {conTimer.Elapsed.TotalSeconds} seconds");
		
		foreach (var action in HandleProps(map)) {
			yield return action;
		}
		
		yield return new YieldFrame();
		
		timer.Stop();
		Plugin.LogDebug($"Finished generation in {timer.Elapsed.TotalSeconds} seconds");
	}
	
}