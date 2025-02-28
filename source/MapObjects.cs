namespace LabyrinthianFacilities;
using Patches;

using System;
using System.Collections.Generic;

using UnityEngine;
using Unity.Netcode;

using DgConversion;
using Serialization;
using Util;

using Object=UnityEngine.Object;


// maybe revert to using GrabbableObject directly? 
// We could differentiate the different types the same way lethal does, including during serialization
// It would eliminate the need to have all these separate arrays in serialization for all the different 
// kinds of mapObjects. 
// It would also allow for things like a shotgun with a battery. The base game doesn't have that, but a mod 
// sure could?
// I *do* dislike how lethal handles consumable items, though (tetra, spraypaint)

public class MapObject : NetworkBehaviour {
	public GrabbableObject Grabbable {get {
		return this.GetComponent<GrabbableObject>();
	}}
	
	public Transform FindParent(DGameMap map=null) {
		Moon moon = map?.Moon ?? MapHandler.Instance.ActiveMoon;
		map ??= moon?.ActiveMap;
		if (map == null) {
			if (moon != null) {
				this.transform.parent = moon.transform;
				return this.transform.parent;
			}
			
			throw new NullReferenceException($"No active/provided map to parent MapObject");
		}
		
		Tile closestTile = null;
		float closestDist = Single.PositiveInfinity;
		foreach (Tile t in map.GetComponentsInChildren<Tile>(
			map.gameObject.activeInHierarchy
		)) {
			float dist = (
				t.BoundingBox.ClosestPoint(this.transform.position) - this.transform.position
			).sqrMagnitude;
			if (dist < closestDist) {
				closestDist = dist;
				closestTile = t;
				if (dist == 0) break;
			}
		} if (closestDist > 100f) {
			this.transform.parent = moon.transform;
		} else {
			this.transform.parent = closestTile.transform;
		}
		this.Grabbable.targetFloorPosition 
			= this.Grabbable.startFallingPosition 
			= this.transform.localPosition;
		
		return this.transform.parent;
	}
	
	public virtual void Preserve() {
		if (!Config.Singleton.SaveMapObjects) return;
		
		var grabbable = this.Grabbable;
		grabbable.isInShipRoom = (
			grabbable.isInShipRoom
			|| StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(
				grabbable.transform.position
			)
		); // fix isInShipRoom for people joining partway through a save
		
		if (
			grabbable.isInShipRoom
			|| this.transform.parent?.GetComponent<Cruiser>() != null // exclude things on cruiser
		) return; 
		
		this.FindParent();
		this.gameObject.SetActive(false);
	}
	
	public virtual void Restore() {
		this.Grabbable.startFallingPosition = (
			this.Grabbable.targetFloorPosition = this.transform.localPosition
		);
		this.gameObject.SetActive(true);
	}
	
}

public class Scrap : MapObject {
	
	public static Scrap GetPrefab(string name) {
		foreach (Scrap s in Resources.FindObjectsOfTypeAll(typeof(Scrap))) {
			if (s.name == name && !s.gameObject.scene.IsValid()) return s;
		}
		Plugin.LogError($"Unable to find scrap prefab '{name}'");
		return null;
	}
	
	public override void OnDestroy() {
		base.OnDestroy();
		if (this.Grabbable == null) return;
		if (this.Grabbable.radarIcon != null) GameObject.Destroy(this.Grabbable.radarIcon.gameObject);
	}
	
	public override void Preserve() {
		if (!Config.Singleton.SaveMapObjects || !Config.Singleton.SaveScrap) return;
		
		base.Preserve();
		var grabbable = this.Grabbable;
		if (grabbable.radarIcon != null && grabbable.radarIcon.gameObject != null) {
			grabbable.radarIcon.gameObject.SetActive(false);
		}
	}
	
	public override void Restore() {
		base.Restore();
		var grabbable = this.Grabbable;
		if (grabbable is LungProp apparatus) {
			apparatus.isLungDocked = false;
			apparatus.GetComponent<AudioSource>().Stop();
		}
		if (!grabbable.isInShipRoom) {
			if (grabbable.radarIcon == null) {
				grabbable.radarIcon = GameObject.Instantiate(
					StartOfRound.Instance.itemRadarIconPrefab, 
					RoundManager.Instance.mapPropsContainer.transform
				).transform;
			} else {
				grabbable.radarIcon.gameObject.SetActive(true);
			}
		}
	}
}

// The purpose of this is to mark an object that is being spawned by the mod, and should not use the 
// normal initialization
internal class DummyFlag : MonoBehaviour {}

public class Beehive : Scrap {
	
	public struct BeeInfo {
		public Vector3 position;
		public int currentBehaviourStateIndex;
		
		public bool IsInvalid {get {return currentBehaviourStateIndex < 0;}}
		
		public BeeInfo(Vector3 position, int currentBehaviourStateIndex) {
			this.position = position;
			this.currentBehaviourStateIndex = currentBehaviourStateIndex;
		}
	}
	
	protected BeeInfo beeInfo = new BeeInfo(Vector3.zero, -1);
	protected RedLocustBees bees = null;
	
	protected GameObject beesPrefab = null;
	protected virtual GameObject BeesPrefab {
		get {
			if (this.beesPrefab == null) {
				foreach (RedLocustBees bees in Resources.FindObjectsOfTypeAll<RedLocustBees>()) {
					if (bees.name == "RedLocustBees") {
						this.beesPrefab = bees.gameObject; 
						break;
					}
				}
			}
			return this.beesPrefab;
		}
	}
	
	protected virtual void OnEnable() {
		if (this.IsServer && this.GetComponentInParent<Moon>() != null) {
			SpawnBees();
		}
	}
	
	protected virtual void OnDisable() {
		if (
			IsServer
			&& this.bees != null 
			&& this.bees.IsSpawned
		) {
			this.bees.GetComponent<NetworkObject>().Despawn();
			this.bees = null;
		}
	}
	
	[ClientRpc]
	protected virtual void SendBeesClientRpc(NetworkObjectReference beeNetObj, int behaviourStateIndex) {
		if (IsServer) return;
		this.bees = ((NetworkObject)beeNetObj).GetComponent<RedLocustBees>();
		this.bees.hive = this.Grabbable;
		this.bees.lastKnownHivePosition = this.transform.position;
		this.bees.currentBehaviourStateIndex = behaviourStateIndex;
		this.bees.gameObject.AddComponent<DummyFlag>();
	}
	
	protected virtual void SpawnBees() {
		if (!IsServer) return;
		
		if (this.beeInfo.IsInvalid) {
			this.beeInfo = new BeeInfo(position: this.transform.position, currentBehaviourStateIndex: 0);
		}
		GameObject g = GameObject.Instantiate(BeesPrefab, this.beeInfo.position, Quaternion.identity);
		
		this.bees = g.GetComponent<RedLocustBees>();
		
		// These need to be synced with the client
		this.bees.currentBehaviourStateIndex = this.beeInfo.currentBehaviourStateIndex;
		this.bees.hive = this.Grabbable;
		this.bees.lastKnownHivePosition = this.transform.position;
		g.AddComponent<DummyFlag>();
		
		// Only server receives call to SpawnHiveNearEnemy, so only server needs DummyFlag
		NetworkObject netObj = g.GetComponent<NetworkObject>();
		netObj.Spawn();
		this.SendBeesClientRpc(netObj,this.bees.currentBehaviourStateIndex);
	}
	
	public void SaveBees(RedLocustBees bees) {
		this.bees = bees;
		if (bees != null) {
			this.beeInfo = new BeeInfo(
				position: this.bees.transform.position, 
				currentBehaviourStateIndex: this.bees.currentBehaviourStateIndex
			);
		} else {
			this.beeInfo = new BeeInfo(Vector3.zero, -1);
		}
	}
	
	public override void Preserve() {
		if (!Config.Singleton.SaveMapObjects || !Config.Singleton.SaveHives) return;
		base.Preserve();
		
		if (this.Grabbable.isInShipRoom) {
			this.beeInfo = new BeeInfo(Vector3.zero, -1);
		}
	}
	
}

public class Equipment : MapObject {
	// yes, this is basically a copy-paste from Scrap
	public static Equipment GetPrefab(string name) {
		foreach (Equipment s in Resources.FindObjectsOfTypeAll(typeof(Equipment))) {
			if (s.name == name && !s.gameObject.scene.IsValid()) return s;
		}
		Plugin.LogError($"Unable to find equipment prefab '{name}'");
		return null;
	}
	
	public override void Preserve() {
		if (!Config.Singleton.SaveMapObjects || !Config.Singleton.SaveEquipment) return;
		base.Preserve();
	}
}

public class BatteryEquipment : Equipment {
	public virtual float Charge {
		get => this.Grabbable.insertedBattery.charge;
		set => this.Grabbable.insertedBattery.charge = value;
	}
}
public class GunEquipment : Equipment {
	public virtual bool Safety {
		get => ((ShotgunItem)this.Grabbable).safetyOn;
		set => ((ShotgunItem)this.Grabbable).safetyOn = value;
	}
	public virtual int NumShells {
		get => ((ShotgunItem)this.Grabbable).shellsLoaded;
		set => ((ShotgunItem)this.Grabbable).shellsLoaded = value;
	}
}
public class FueledEquipment : BatteryEquipment {
	public override float Charge {
		get => FuelAccess.Get(this.Grabbable);
		set => FuelAccess.Set(this.Grabbable,value);
	}
}

// Bypassing Netcode's requirement that NetworkObjects must be parented under other NetworkObjects
// (I didn't want moons to be server-managed just to take advantage of unity's parenting for cruisers)
public class Cruiser : NetworkBehaviour {
	
	public Moon Moon {get => this.transform.parent.GetComponent<Moon>();}
	
	private static GameObject prefab = null;
	public static GameObject Prefab {get {
		if (Cruiser.prefab == null) {
			foreach (Cruiser vc in Resources.FindObjectsOfTypeAll<Cruiser>()) {
				if (vc.name == "CompanyCruiser") {
					Cruiser.prefab = vc.gameObject;
					break;
				}
			}
		}
		return Cruiser.prefab;
	}}
	
	public void Preserve() {
		if (!Config.Singleton.SaveMapObjects || !Config.Singleton.SaveCruisers) return;
		
		var vc = this.GetComponent<VehicleController>();
		if (vc.magnetedToShip  ||  vc.carDestroyed && vc.GetComponentsInChildren<MapObject>().Length == 0) {
			this.transform.parent = null;
			return;
		}
		foreach (MapObject mo in this.GetComponentsInChildren<MapObject>()) {
			mo.Preserve();
		}
		SetMoon(MapHandler.Instance.ActiveMoon);
		this.gameObject.SetActive(false);
	}
	
	public void Restore() {
		if (!this.IsServer) return;
		
		GameObject    gameObj = GameObject.Instantiate(Prefab);
		NetworkObject netObj  = gameObj.GetComponent<NetworkObject>();
		
		gameObj.transform.position = this.transform.position;
		gameObj.transform.rotation = this.transform.rotation;
		
		netObj.Spawn();
		RestoreClientRpc(this.NetworkObjectId, netObj.NetworkObjectId);
	}
	
	[ClientRpc]
	public void RestoreClientRpc(ulong oldId, ulong newId) {
		NetworkObject older = NetworkManager.Singleton.SpawnManager.SpawnedObjects[oldId];
		NetworkObject newer = NetworkManager.Singleton.SpawnManager.SpawnedObjects[newId];
		
		// Why does this cause a softlock?
		// Something with reparenting, but why does StopIgnition being disabled matter?
		// newer.GetComponent<Cruiser>().SetMoon(older.GetComponent<Cruiser>().Moon);
		
		foreach (var mo in older.GetComponentsInChildren<MapObject>(true)) {
			mo.transform.parent = newer.transform;
			mo.Restore();
		}
		
		older.GetComponent<Cruiser>().DoneWithOldCruiserServerRpc();
	}
	
	private int numFinished = 0;
	[ServerRpc(RequireOwnership=false)]
	public void DoneWithOldCruiserServerRpc(bool disconnect=false) {
		if (!disconnect) numFinished++;
		// (connectedPlayersAmount does not include host)
		if (numFinished >= StartOfRound.Instance.connectedPlayersAmount+1) {
			this.GetComponent<NetworkObject>().Despawn(true);
		}
	}
	
	public void SetMoon(Moon moon) {
		// SHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUP
		this.GetComponent<NetworkObject>().AutoObjectParentSync = false;
		this.transform.parent = moon.transform;
	}
}

public struct MapObjectCollection {
	
	public List<Scrap>            Scrap;
	public List<Equipment>        Equipment;
	public List<BatteryEquipment> BatteryEquipment;
	public List<GunEquipment>     GunEquipment;
	public List<FueledEquipment>  FueledEquipment;
	
	public MapObjectCollection(Component c, bool includeInactive=true) : this(c.gameObject,includeInactive) {}
	public MapObjectCollection(GameObject root, bool includeInactive=true) : this(
		root.GetComponentsInChildren<MapObject>(includeInactive)
	) {}
	public MapObjectCollection(ICollection<MapObject> mapObjects) {
		this.Scrap            = new(mapObjects.Count);
		this.Equipment        = new(mapObjects.Count);
		this.BatteryEquipment = new(mapObjects.Count);
		this.GunEquipment     = new(mapObjects.Count);
		this.FueledEquipment  = new(mapObjects.Count);
		foreach (MapObject mo in mapObjects) {
			if (mo is Scrap s) {
				Scrap.Add(s);
			} else if (mo is GunEquipment ge) {
				GunEquipment.Add(ge);
			} else if (mo is BatteryEquipment be) {
				BatteryEquipment.Add(be);
			} else if (mo is FueledEquipment fe) {
				FueledEquipment.Add(fe);
			} else if (mo is Equipment e) {
				Equipment.Add(e);
			} else {
				Plugin.LogError($"MapObject {mo} is neither Scrap nor Equipment?");
			}
		}
	}
	
	public void Serialize(
		SerializationContext sc, 
		ISerializer<Scrap> ss, 
		ISerializer<Equipment> es, 
		ISerializer<BatteryEquipment> bes,
		ISerializer<GunEquipment> ges,
		ISerializer<FueledEquipment> fes
	) {
		sc.Add((ushort)this.Scrap.Count);
		foreach (var s in this.Scrap) {
			sc.AddInline(s,ss);
		}
		
		sc.Add((ushort)this.Equipment.Count);
		foreach (var eq in this.Equipment) {
			sc.AddInline(eq,es);
		}
		
		sc.Add((ushort)this.BatteryEquipment.Count);
		foreach (var eq in this.BatteryEquipment) {
			sc.AddInline(eq,bes);
		}
		
		sc.Add((ushort)this.GunEquipment.Count);
		foreach (var eq in this.GunEquipment) {
			sc.AddInline(eq,ges);
		}
		
		sc.Add((ushort)this.FueledEquipment.Count);
		foreach (var eq in this.FueledEquipment) {
			sc.AddInline(eq,fes);
		}
	}
	
	public static void Deserialize(
		DeserializationContext dc,
		ISerializer<Scrap> ss, 
		ISerializer<Equipment> es, 
		ISerializer<BatteryEquipment> bes,
		ISerializer<GunEquipment> ges,
		ISerializer<FueledEquipment> fes
	) {
		foreach (ISerializer<MapObject> s in (ISerializer<MapObject>[])[ss,es,bes,ges,fes]) {
			dc.Consume(sizeof(ushort)).CastInto(out ushort count);
			for (int i=0; i<count; i++) {
				dc.ConsumeInline(s);
			}
		}
	}
}

public abstract class MapObjectSerializer<T> : Serializer<T> where T : MapObject {
	public abstract T GetPrefab(string id);
	
	private MonoBehaviour parent;
	public MonoBehaviour Parent {get => parent;}
	
	public MapObjectSerializer(Moon m) {
		parent = m;
	}
	public MapObjectSerializer(DGameMap m) {
		this.parent = m;
	}
	public MapObjectSerializer(Cruiser c) {
		this.parent = c;
	}
	
	/* Format:
	 * Identifier: string
	 * position: Vector3 
	*/
	public override void Serialize(SerializationContext sc, T tgt) {
		sc.Add(tgt.name.Substring(0,tgt.name.Length - "(Clone)".Length));
		sc.Add(new byte[]{(byte)0});
		
		sc.Add(tgt.transform.position.x);
		sc.Add(tgt.transform.position.y);
		sc.Add(tgt.transform.position.z);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		
		if (rt == null) return null;
		rt.transform.position = new Vector3(x,y,z);
		
		rt.transform.parent = this.parent.transform;
		
		return rt;
	}
	public override T Deserialize(DeserializationContext dc) {
		
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string name);
		dc.Consume(1); // null terminator
		T rt = null;
		
		// network object cannot be instantiated by client
		if (MapHandler.Instance.IsServer || MapHandler.Instance.IsHost) {
			rt = Object.Instantiate(GetPrefab(name));
			rt.gameObject.AddComponent<DummyFlag>();
			rt.GetComponent<NetworkObject>().Spawn();
		}
		
		return Deserialize(rt, dc);
	}
}

public class ScrapSerializer : MapObjectSerializer<Scrap> {
	public override Scrap GetPrefab(string id) => Scrap.GetPrefab(id);
	
	public ScrapSerializer(Moon     p) : base(p) {}
	public ScrapSerializer(DGameMap p) : base(p) {}
	public ScrapSerializer(Cruiser  p) : base(p) {}
	
	/* Format:
	 *     base
	 *     ScrapValue: int
	*/
	public override void Serialize(SerializationContext sc, Scrap scrap) {
		base.Serialize(sc,scrap);
		sc.Add(scrap.Grabbable.scrapValue);
	}
	
	protected override Scrap Deserialize(
		Scrap rt, DeserializationContext dc
	) {
		base.Deserialize(rt,dc);
		
		dc.Consume(4).CastInto(out int scrapValue);
		
		if (rt == null) return null;
		rt.Grabbable.SetScrapValue(scrapValue);
		
		return rt;
	}
}

public class EquipmentSerializer<T> : MapObjectSerializer<T> where T : Equipment{
	public EquipmentSerializer(Moon     p) : base(p) {}
	public EquipmentSerializer(DGameMap p) : base(p) {}
	public EquipmentSerializer(Cruiser  p) : base(p) {}
	
	public override T GetPrefab(string id) => (T)Equipment.GetPrefab(id);
}

public class BatteryEquipmentSerializer<T> : EquipmentSerializer<T> where T : BatteryEquipment {
	
	public BatteryEquipmentSerializer(Moon     p) : base(p) {}
	public BatteryEquipmentSerializer(DGameMap p) : base(p) {}
	public BatteryEquipmentSerializer(Cruiser  p) : base(p) {}
	
	public override void Serialize(SerializationContext sc, T tgt) {
		base.Serialize(sc,tgt);
		sc.Add(tgt.Charge);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		base.Deserialize(rt,dc);
		dc.Consume(sizeof(float)).CastInto(out float charge);
		if (rt != null) rt.Charge = charge;
		return rt;
	}
}

public class GunEquipmentSerializer<T> : EquipmentSerializer<T> where T : GunEquipment {
	
	public GunEquipmentSerializer(Moon     p) : base(p) {}
	public GunEquipmentSerializer(DGameMap p) : base(p) {}
	public GunEquipmentSerializer(Cruiser  p) : base(p) {}
	
	public override void Serialize(SerializationContext sc, T tgt) {
		base.Serialize(sc,tgt);
		sc.Add(tgt.Safety);
		sc.Add(tgt.NumShells);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		base.Deserialize(rt,dc);
		dc.Consume(sizeof(bool)).CastInto(out bool safety);
		dc.Consume(sizeof(int)).CastInto(out int numShells);
		if (rt == null) return null;
		rt.Safety = safety;
		rt.NumShells = numShells;
		return rt;
	}
}

public class MapObjectNetworkSerializer<T> : Serializer<T> where T : MapObject {
	public MonoBehaviour Parent {get; private set;}
	
	public MapObjectNetworkSerializer(Moon     p) {this.Parent = p;}
	public MapObjectNetworkSerializer(DGameMap p) {this.Parent = p;}
	public MapObjectNetworkSerializer(Cruiser  p) {this.Parent = p;}
	
	public override void Serialize(SerializationContext sc, T obj) {
		var netObj = obj.GetComponent<NetworkObject>();
		if (!(netObj?.IsSpawned ?? false)) {
			throw new InvalidOperationException(
				$"Cannot use {this.GetType()} to serialize {obj} that is not spawned. " 
			);
		}
		sc.Add(netObj.NetworkObjectId);
	}
	
	protected override T Deserialize(T s, DeserializationContext dc) {
		// s.gameObject.AddComponent<DummyFlag>();
		if (Parent is DGameMap map) {
			s.FindParent(map: map);
		} else {
			s.transform.parent = this.Parent.transform;
		}
		
		s.Grabbable.startFallingPosition = (
			s.Grabbable.targetFloorPosition = s.transform.localPosition
		);
		
		return s;
	}
	
	public override T Deserialize(DeserializationContext dc) {
		dc.Consume(sizeof(ulong)).CastInto(out ulong netobjid);
		
		return Deserialize(
			NetworkManager.Singleton.SpawnManager.SpawnedObjects[netobjid].GetComponent<T>(), 
			dc
		);
	}
}

public class ScrapNetworkSerializer : MapObjectNetworkSerializer<Scrap> {
	
	public ScrapNetworkSerializer(Moon     p) : base(p) {}
	public ScrapNetworkSerializer(DGameMap p) : base(p) {}
	public ScrapNetworkSerializer(Cruiser  p) : base(p) {}
	
	public override void Serialize(SerializationContext sc, Scrap s) {
		base.Serialize(sc,s);
		sc.Add(s.Grabbable.scrapValue);
	}
	
	protected override Scrap Deserialize(
		Scrap s, DeserializationContext dc
	) {
		base.Deserialize(s,dc);
		
		dc.Consume(sizeof(int)).CastInto(out int scrapValue);
		s.Grabbable.SetScrapValue(scrapValue);
		
		return s;
	}
}

public class BatteryEquipmentNetworkSerializer<T> : MapObjectNetworkSerializer<T> where T : BatteryEquipment {
	public BatteryEquipmentNetworkSerializer(Moon     p) : base(p) {}
	public BatteryEquipmentNetworkSerializer(DGameMap p) : base(p) {}
	public BatteryEquipmentNetworkSerializer(Cruiser  p) : base(p) {}
	
	public override void Serialize(SerializationContext sc, T tgt) {
		base.Serialize(sc,tgt);
		sc.Add(tgt.Charge);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		base.Deserialize(rt,dc);
		dc.Consume(sizeof(float)).CastInto(out float charge);
		rt.Charge = charge;
		return rt;
	}
}
public class GunEquipmentNetworkSerializer<T> : MapObjectNetworkSerializer<T> where T : GunEquipment {
	public GunEquipmentNetworkSerializer(Moon     p) : base(p) {}
	public GunEquipmentNetworkSerializer(DGameMap p) : base(p) {}
	public GunEquipmentNetworkSerializer(Cruiser  p) : base(p) {}
	
	public override void Serialize(SerializationContext sc, T tgt) {
		base.Serialize(sc,tgt);
		sc.Add(tgt.Safety);
		sc.Add(tgt.NumShells);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		base.Deserialize(rt,dc);
		dc.Consume(sizeof(bool)).CastInto(out bool safety);
		dc.Consume(sizeof(int)).CastInto(out int numShells);
		rt.Safety = safety;
		rt.NumShells = numShells;
		return rt;
	}
}

// This will break with other vehicles, either modded or added into the game 
// or at least they will if they use Cruiser
public class CruiserSerializer : Serializer<Cruiser> {
	
	private Moon parent;
	public CruiserSerializer(Moon m) {parent = m;}
	
	/* Format:
	 *   float x,y,z
	 *   Quaternion x,y,z,w
	 *   Scrap[] scrap
	 *   Equipment[] equipment
	 *   BatteryEquipment[] batteryEquipment
	 *   GunEquipment[] gunEquipment
	*/
	public override void Serialize(SerializationContext sc, Cruiser cruiser) {
		sc.Add(cruiser.transform.position.x);
		sc.Add(cruiser.transform.position.y);
		sc.Add(cruiser.transform.position.z);
		
		sc.Add(cruiser.transform.rotation.x);
		sc.Add(cruiser.transform.rotation.y);
		sc.Add(cruiser.transform.rotation.z);
		sc.Add(cruiser.transform.rotation.w);
		
		new MapObjectCollection(cruiser).Serialize(
			sc,
			new ScrapSerializer           /* Scrap */       (cruiser),
			new EquipmentSerializer       <Equipment>       (cruiser),
			new BatteryEquipmentSerializer<BatteryEquipment>(cruiser),
			new GunEquipmentSerializer    <GunEquipment>    (cruiser),
			new BatteryEquipmentSerializer<FueledEquipment> (cruiser)
		);
	}
	
	protected override Cruiser Deserialize(
		Cruiser cruiser, DeserializationContext dc
	) {
		// Cruiser is null for clients! (deliberate, clients cannot control network objects)
		
		dc.Consume(4).CastInto(out float x);
		dc.Consume(4).CastInto(out float y);
		dc.Consume(4).CastInto(out float z);
		Vector3 pos = new Vector3(x,y,z);
		
		dc.Consume(4).CastInto(out x);
		dc.Consume(4).CastInto(out y);
		dc.Consume(4).CastInto(out z);
		dc.Consume(4).CastInto(out float w);
		Quaternion rot = new Quaternion(x,y,z,w);
		
		if (cruiser != null) {
			cruiser.SetMoon(parent);
			cruiser.transform.position = pos;
			cruiser.transform.rotation = rot;
		}
		
		MapObjectCollection.Deserialize(
			dc,
			new ScrapSerializer           /* <Scrap> */     (cruiser),
			new EquipmentSerializer       <Equipment>       (cruiser),
			new BatteryEquipmentSerializer<BatteryEquipment>(cruiser),
			new GunEquipmentSerializer    <GunEquipment>    (cruiser),
			new BatteryEquipmentSerializer<FueledEquipment> (cruiser)
		);
		
		return cruiser;
	}
	
	public override Cruiser Deserialize(DeserializationContext dc) {
		Cruiser rt = null;
		if (NetworkManager.Singleton.IsServer) {
			var g = GameObject.Instantiate(Cruiser.Prefab);
			g.AddComponent<DummyFlag>();
			g.GetComponent<NetworkObject>().Spawn();
			rt = g.GetComponent<Cruiser>();
		}
		return Deserialize(rt,dc);
	}
}

public class CruiserNetworkSerializer : Serializer<Cruiser> {
	private Moon parent;
	public CruiserNetworkSerializer(Moon m) {parent=m;}
	
	public override void Serialize(SerializationContext sc, Cruiser tgt) {
		var netobj = tgt.GetComponent<NetworkObject>();
		if (netobj == null || !netobj.IsSpawned) {
			throw new ArgumentException(
				$"Cannot use {this.GetType()} to serialize {tgt} that is not spawned. "
			);
		}
		sc.Add(netobj.NetworkObjectId);
		
		// cruiser transform isn't synced on spawn?
		sc.Add(tgt.transform.position.x);
		sc.Add(tgt.transform.position.y);
		sc.Add(tgt.transform.position.z);
		
		sc.Add(tgt.transform.rotation.x);
		sc.Add(tgt.transform.rotation.y);
		sc.Add(tgt.transform.rotation.z);
		sc.Add(tgt.transform.rotation.w);
		
		new MapObjectCollection(tgt).Serialize(
			sc,
			new ScrapNetworkSerializer                             (tgt),
			new MapObjectNetworkSerializer       <Equipment>       (tgt),
			new BatteryEquipmentNetworkSerializer<BatteryEquipment>(tgt),
			new GunEquipmentNetworkSerializer    <GunEquipment>    (tgt),
			new BatteryEquipmentNetworkSerializer<FueledEquipment> (tgt)
		);
	}
	
	protected override Cruiser Deserialize(Cruiser tgt, DeserializationContext dc) {
		if (tgt == null) {
			Plugin.LogError($"No cruiser referenced to sync");
			return null;
		}
		
		if (parent == null) {
			Plugin.LogError($"No moon referenced to parent cruiser to during sync");
			return null;
		}
		
		tgt.SetMoon(parent);
		
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		tgt.transform.position = new Vector3(x,y,z);
		
		dc.Consume(sizeof(float)).CastInto(out x);
		dc.Consume(sizeof(float)).CastInto(out y);
		dc.Consume(sizeof(float)).CastInto(out z);
		dc.Consume(sizeof(float)).CastInto(out float w);
		tgt.transform.rotation = new Quaternion(x,y,z,w);
		
		MapObjectCollection.Deserialize(
			dc,
			new ScrapNetworkSerializer           /* <Scrap> */     (tgt),
			new MapObjectNetworkSerializer       <Equipment>       (tgt),
			new BatteryEquipmentNetworkSerializer<BatteryEquipment>(tgt),
			new GunEquipmentNetworkSerializer    <GunEquipment>    (tgt),
			new BatteryEquipmentNetworkSerializer<FueledEquipment> (tgt)
		);
		
		return tgt;
	}
	
	public override Cruiser Deserialize(DeserializationContext dc) {
		dc.Consume(sizeof(ulong)).CastInto(out ulong netobjid);
		NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[netobjid];
		netObj.gameObject.AddComponent<DummyFlag>();
		
		return Deserialize(netObj.GetComponent<Cruiser>(),dc);
	}
}