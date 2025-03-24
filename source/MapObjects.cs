namespace LabyrinthianFacilities;
using DgConversion;
using Patches;
using Serialization;
using Util;

using System;
using System.Collections;
using System.Collections.Generic;

using HarmonyLib;

using TMPro;
using UnityEngine;
using Unity.Netcode;


using Object=UnityEngine.Object;


// maybe revert to using GrabbableObject directly? 
// We could differentiate the different types the same way lethal does, including during serialization
// It would eliminate the need to have all these separate arrays in serialization for all the different 
// kinds of mapObjects. 
// It would also allow for things like a shotgun with a battery. The base game doesn't have that, but a mod 
// sure could?
// I *do* dislike how lethal handles consumable items, though (tetra, spraypaint)
public abstract class MapObject : NetworkBehaviour {
	
	public static T GetPrefab<T>(string name) where T : UnityEngine.Component {
		foreach (T s in Resources.FindObjectsOfTypeAll(typeof(T))) {
			if (s.name == name && !s.gameObject.scene.IsValid()) return s;
		}
		Plugin.LogError($"Unable to find {typeof(T).Name} prefab '{name}'");
		return null;
	}
	
	public virtual Transform FindParent(DGameMap map=null) {
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
		
		return this.transform.parent;
	}
	
	public virtual void Preserve() {}
	public virtual void Restore () {}
	
}

public class GrabbableMapObject : MapObject {
	public GrabbableObject Grabbable {get => this.GetComponent<GrabbableObject>();}
	
	public override Transform FindParent(DGameMap map=null) {
		var rt = base.FindParent(map);
		
		this.Grabbable.targetFloorPosition 
			= this.Grabbable.startFallingPosition 
			= this.transform.localPosition;
		
		return rt;
	}
	
	public override void Preserve() {
		if (!Config.Singleton.SaveGrabbableMapObjects) return;
		
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
	
	public override void Restore() {
		this.Grabbable.startFallingPosition = (
			this.Grabbable.targetFloorPosition = this.transform.localPosition
		);
		this.gameObject.SetActive(true);
	}
}

public class Scrap : GrabbableMapObject {
	
	public static Scrap GetPrefab(string name) => MapObject.GetPrefab<Scrap>(name);
	
	public override void OnDestroy() {
		base.OnDestroy();
		if (this.Grabbable == null) return;
		if (this.Grabbable.radarIcon != null) GameObject.Destroy(this.Grabbable.radarIcon.gameObject);
	}
	
	public override void Preserve() {
		if (!Config.Singleton.SaveGrabbableMapObjects || !Config.Singleton.SaveScrap) return;
		
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

// The purpose of this is to mark an object that is being spawned/instantiated by the mod, 
// and should not use the normal initialization
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
		if (!Config.Singleton.SaveGrabbableMapObjects || !Config.Singleton.SaveHives) return;
		base.Preserve();
		
		if (this.Grabbable.isInShipRoom) {
			this.beeInfo = new BeeInfo(Vector3.zero, -1);
		}
	}
	
}

public class Equipment : GrabbableMapObject {
	public static Equipment GetPrefab(string name) => MapObject.GetPrefab<Equipment>(name);
	
	public override void Preserve() {
		if (!Config.Singleton.SaveGrabbableMapObjects || !Config.Singleton.SaveEquipment) return;
		base.Preserve();
	}
}

public class BatteryEquipment : Equipment {
	public virtual float Charge {
		get => this.Grabbable.insertedBattery.charge;
		set => this.Grabbable.insertedBattery.charge = value;
	}
}
public class GunEquipment : Scrap {
	public virtual bool Safety {
		get => ((ShotgunItem)this.Grabbable).safetyOn;
		set => ((ShotgunItem)this.Grabbable).safetyOn = value;
	}
	public virtual int NumShells {
		get => ((ShotgunItem)this.Grabbable).shellsLoaded;
		set => ((ShotgunItem)this.Grabbable).shellsLoaded = value;
	}
	
	public override void Preserve() {
		if (!Grabbable.isHeldByEnemy) base.Preserve();
	}
}
public class FueledEquipment : BatteryEquipment {
	public override float Charge {
		get => FuelAccess.Get(this.Grabbable);
		set => FuelAccess.Set(this.Grabbable,value);
	}
}

// types descending from Hazard<T> are *not* to be placed directly on the actual script of the hazard; 
// many of these scripts are on children/grandchildren of the prefab of the hazard, 
// and we want a handle to the entire prefab, not just the behaviour. 
public abstract class HazardBase : MapObject {}
public abstract class Hazard<T> : HazardBase where T : NetworkBehaviour {
	public T HazardScript {get => this.GetComponentInChildren<T>();}
	public TerminalAccessibleObject TerminalAccess {
		get => this.GetComponentInChildren<TerminalAccessibleObject>(true);
	}
	public GameObject MapRadarText {
		get => TerminalAccessibleObjectAccess.MapRadarText(TerminalAccess);
	}
	
	public override void Preserve() {
		if (Config.Singleton.SaveHazards) {
			this.FindParent();
			MapRadarText?.SetActive(false);
		}
	}
	public override void Restore() {
		if (Config.Singleton.SaveHazards) {
			MapRadarText?.SetActive(true);
			try {
				TerminalAccess.setCodeRandomlyFromRoundManager = false;
			} catch (NullReferenceException) {}
		}
	}
}
public class TurretHazard : Hazard<Turret> {
	public override void Preserve() {
		if (Config.Singleton.SaveTurrets) base.Preserve();
	}
}
public class LandmineHazard : Hazard<Landmine> {
	public override void Preserve() {
		if (!HazardScript.hasExploded && Config.Singleton.SaveLandmines) base.Preserve();
		else if (this.transform.parent != null) this.GetComponent<NetworkObject>().Despawn(destroy: true);
	}
	
	public override void Restore() {
		base.Restore();
		LandmineAccess.Restart(this.HazardScript);
	}
}
public class SpikeTrapHazard : Hazard<SpikeRoofTrap> {
	public override void Preserve() {
		if (Config.Singleton.SaveSpikeTraps) base.Preserve();
	}
}

public class Cruiser : NetworkBehaviour {
	
	public Moon Moon {get => this.transform.parent?.GetComponent<Moon>();}
	public VehicleController VehicleController {get => this.GetComponent<VehicleController>();}
	
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
	
	// Represents the *desired* state of the back door, only updated on Preserve 
	// (and RestoreClientRpc where it is transfered to the clone cruiser)
	public bool IsBackDoorOpen = false;
	
	public void Preserve() {
		if (!Config.Singleton.SaveCruisers) return;
		
		IsBackDoorOpen = this.transform.Find(
			"Meshes/BackDoorContainer/BackDoor/OpenTrigger"
		).GetComponent<BoxCollider>().enabled;
		
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
		gameObj.AddComponent<DummyFlag>();
		
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
		
		VehicleController newVc = newer.GetComponent<VehicleController>();
		VehicleController oldVc = older.GetComponent<VehicleController>();
		
		Cruiser newC = newer.GetComponent<Cruiser>();
		Cruiser oldC = older.GetComponent<Cruiser>();
		
		newVc.carHP = oldVc.carHP;
		FuelAccess.Set(newVc, FuelAccess.Get(oldVc));
		if (oldVc.carDestroyed) newC.DestroyCar();
		newVc.SetIgnition(oldVc.ignitionStarted);
		
		newC.IsBackDoorOpen = oldC.IsBackDoorOpen;
		
		// possible bug where host finishes much sooner than clients
		// if clients have >5s delay, they will miss the door openning because they won't have 
		// the door to open yet. Seems exceedingly rare/insignificant though, 
		// since the clients should be able to open the door themselves. 
		// AFTER-THOUGHT: move to the completion clause of DoneWithOldCruiserServerRpc?
		if (IsServer) newC.StartCoroutine(newC.DelayUpdateBackDoor());
		
		foreach (var mo in older.GetComponentsInChildren<MapObject>(true)) {
			mo.transform.parent = newer.transform;
			mo.Restore();
		}
		
		oldC.DoneWithOldCruiserServerRpc();
	}
	
	public IEnumerator DelayUpdateBackDoor() {
		yield return new WaitForSeconds(5f);
		UpdateBackDoor();
	}
	
	public void UpdateBackDoor() {
		if (
			IsBackDoorOpen == this.transform.Find(
				"Meshes/BackDoorContainer/BackDoor/OpenTrigger"
			).GetComponent<BoxCollider>().enabled
		) return;
		
		this.transform.Find(
			$"Meshes/BackDoorContainer/BackDoor/{(IsBackDoorOpen ? "ClosedTrigger" : "OpenTrigger")}"
		).GetComponent<InteractTrigger>().Interact(
			StartOfRound.Instance.localPlayerController.transform
		);
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
		// Bypassing Netcode's requirement that cruisers must be parented under other NetworkObjects
		// (I didn't want moons to be server-managed just to take advantage of unity's parenting for cruisers)
		this.GetComponent<NetworkObject>().AutoObjectParentSync = false;
		this.transform.parent = moon.transform;
	}
	
	public void DestroyCar() {
		new Traverse(this.VehicleController).Method("DestroyCar").GetValue();
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
		root.GetComponentsInChildren<GrabbableMapObject>(includeInactive)
	) {}
	public MapObjectCollection(ICollection<GrabbableMapObject> mapObjects) {
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
			if (DeserializationContext.Verbose) Plugin.LogDebug($"Found {count} items for {s}");
			for (int i=0; i<count; i++) {
				dc.ConsumeInline(s);
			}
		}
	}
}


// unused for now
public enum EquipmentSerializationFlags {
	Battery=0x1,
	Fuel=0x2
}

public class MapObjectGroupSerializer<T> : CollectionSerializer<T> where T : MapObject {
	
	public T Prefab;
	public MonoBehaviour Parent {get; protected set;}
	
	public MapObjectGroupSerializer(MonoBehaviour p) {
		if (!(
			p is Moon
			|| p is DGameMap
			|| p is Cruiser
		)) throw new ArgumentException($"Parent type {p.GetType()} not a valid MapObject parent");
		Parent = p;
	}
	public MapObjectGroupSerializer(Moon     p) {Parent = p;}
	public MapObjectGroupSerializer(DGameMap p) {Parent = p;}
	public MapObjectGroupSerializer(Cruiser  p) {Parent = p;}
	
	public T GetPrefab(string id) => this.Prefab = MapObject.GetPrefab<T>(id);
	
	protected override void PreserializeStep(ICollection<T> mapObjects) {
		foreach(T item in mapObjects) {
			ChooseSerializer(item);
			break;
		}
	}
	
	protected override void DeserializeSharedPreamble(DeserializationContext dc) {
		dc.ConsumeUntil((byte b) => (b == 0)).CastInto(out string ident);
		dc.Consume(1);
		
		ChooseSerializer(GetPrefab(ident));
		
		if (!IsValid) {
			throw new InvalidOperationException(
				$"CollectionSerializer has no relationship with an ItemSerializer"
			);
		}
	}
	
	public override T GetItemSkeleton() {
		if (!NetworkManager.Singleton.IsServer || Parent == null) {
			return null;
		}
		var go = GameObject.Instantiate(this.Prefab);
		go.GetComponent<NetworkObject>().Spawn();
		return go.GetComponent<T>();
	}
	
	public virtual void ChooseSerializer(MapObject mapObject) {
		Type type = mapObject.GetType();
		
		IItemSerializer<MapObject> itemSerializer;
		if (type == typeof(SpikeTrapHazard)) {
			itemSerializer = new SpikeTrapHazardSerializer <SpikeTrapHazard >(Parent);
		} else if (type == typeof(HazardBase)) {
			itemSerializer = new HazardSerializer          <HazardBase      >(Parent);
		} else if (type == typeof(GunEquipment)) {
			itemSerializer = new GunEquipmentSerializer    <GunEquipment    >(Parent);
		} else if (type == typeof(Scrap)) {
			itemSerializer = new ScrapSerializer           <Scrap           >(Parent);
		} else if (type == typeof(BatteryEquipment)) {
			itemSerializer = new BatteryEquipmentSerializer<BatteryEquipment>(Parent); 
		} else if (type == typeof(Equipment)) {
			itemSerializer = new EquipmentSerializer       <Equipment       >(Parent);
		} else {
			itemSerializer = null; 
			Plugin.LogError($"Could not find a serializer for '{Prefab.GetType()}'");
		}
		this.Init((IItemSerializer<T>)itemSerializer);
	}
}
public class MapObjectGroupSerializer : MapObjectGroupSerializer<MapObject> {
	public MapObjectGroupSerializer(MonoBehaviour m) : base(m) {}
}

public abstract class MapObjectSerializer<T> : ItemSerializer<T> where T : MapObject {
	public abstract T GetPrefab(string id);
	
	private MonoBehaviour parent;
	public MonoBehaviour Parent {get; private set;}
	
	public new MapObjectGroupSerializer<T> GroupSerializer {
		get => (MapObjectGroupSerializer<T>)base.GroupSerializer;
		set => base.GroupSerializer = value;
	}
	
	public MapObjectSerializer(MonoBehaviour m) {
		if (!(m is Moon || m is DGameMap || m is Cruiser)) {
			throw new InvalidCastException($"{m.GetType()} is not a valid parent type for a MapObject");
		}
		this.parent = m;
	}
	
	/* Format: 
	 * Identifier: string
	 * position: Vector3
	*/
	protected override void SerializePreamble(SerializationContext sc, T tgt) {
		sc.Add(tgt.name.Substring(0,tgt.name.Length - "(Clone)".Length));
		sc.Add(new byte[]{(byte)0});
	}
	protected override void SerializeData(SerializationContext sc, T tgt) {
		sc.Add(tgt.transform.position.x);
		sc.Add(tgt.transform.position.y);
		sc.Add(tgt.transform.position.z);
	}
	
	protected override T DeserializePreamble(DeserializationContext dc) {
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string id);
		dc.Consume(1);
		T rt = null;
		if (NetworkManager.Singleton.IsServer && Parent != null) {
			T prefab = GroupSerializer?.Prefab ?? GetPrefab(id);
			rt = Object.Instantiate(prefab.gameObject).GetComponent<T>();
			rt.GetComponent<NetworkObject>().Spawn();
		}
		return rt;
	}
	protected override T DeserializeData(T rt, DeserializationContext dc) {
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		
		if (rt == null) return rt;
		
		rt.transform.position = new Vector3(x,y,z);
		rt.transform.parent = this.Parent.transform;
		
		return rt;
	}
}

public abstract class GrabbableMapObjectSerializer<T> : MapObjectSerializer<T> where T : GrabbableMapObject {
	
	public GrabbableMapObjectSerializer(MonoBehaviour p) : base(p) {}
	
	/* Format:
	 * yrot: byte (degrees rounded to nearest multiple of two)
	*/
	protected override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		sc.Add((byte)(tgt.transform.rotation.eulerAngles.y / 2));
	}
	
	protected override T DeserializeData(T rt, DeserializationContext dc) {
		base.DeserializeData(rt,dc);
		
		dc.Consume(sizeof(byte)).CastInto(out byte yRotRounded);
		
		if (rt == null) return rt;
		
		rt.gameObject.AddComponent<DummyFlag>();
		
		float yRot = 2f * yRotRounded;
		rt.transform.rotation = Quaternion.Euler(
			rt.Grabbable.itemProperties.restingRotation.x, 
			yRot, 
			rt.Grabbable.itemProperties.restingRotation.z
		);
		
		return rt;
	}
}

public class ScrapSerializer<T> : GrabbableMapObjectSerializer<T> where T : Scrap {
	public override T GetPrefab(string id) => (T)Scrap.GetPrefab(id);
	
	public ScrapSerializer(MonoBehaviour p) : base(p) {}
	
	/* Format:
	 *     base
	 *     ScrapValue: int
	*/
	protected override void SerializeData(SerializationContext sc, T scrap) {
		base.SerializeData(sc,scrap);
		sc.Add(scrap.Grabbable.scrapValue);
	}
	
	protected override T DeserializeData(
		T rt, DeserializationContext dc
	) {
		base.DeserializeData(rt,dc);
		
		dc.Consume(4).CastInto(out int scrapValue);
		
		if (rt == null) return rt;
		rt.Grabbable.SetScrapValue(scrapValue);
		
		return rt;
	}
}

public class GunEquipmentSerializer<T> : ScrapSerializer<T> where T : GunEquipment {
	
	public GunEquipmentSerializer(MonoBehaviour p) : base(p) {}
	/* Format:
	 * base
	 * bool: safetyOn
	 * int:  numShells
	*/
	protected override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		sc.Add(tgt.Safety);
		sc.Add(tgt.NumShells);
	}
	
	protected override T DeserializeData(T rt, DeserializationContext dc) {
		base.DeserializeData(rt,dc);
		dc.Consume(sizeof(bool)).CastInto(out bool safety   );
		dc.Consume(sizeof(int )).CastInto(out int  numShells);
		if (rt == null) return rt;
		rt.Safety = safety;
		rt.NumShells = numShells;
		
		return rt;
	}
}

public class EquipmentSerializer<T> : GrabbableMapObjectSerializer<T> where T : Equipment{
	public EquipmentSerializer(MonoBehaviour p) : base(p) {}
	
	public override T GetPrefab(string id) => (T)Equipment.GetPrefab(id);
}

public class BatteryEquipmentSerializer<T> : EquipmentSerializer<T> where T : BatteryEquipment {
	
	public BatteryEquipmentSerializer(MonoBehaviour p) : base(p) {}
	
	protected override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		sc.Add(tgt.Charge);
	}
	
	protected override T DeserializeData(T rt, DeserializationContext dc) {
		base.DeserializeData(rt,dc);
		dc.Consume(sizeof(float)).CastInto(out float charge);
		if (rt != null) rt.Charge = charge;
		return rt;
	}
}

public class HazardSerializer<T> : MapObjectSerializer<T>  where T : HazardBase {
	public override T GetPrefab(string name) {
		foreach (T h in Resources.FindObjectsOfTypeAll<T>()) {
			if (h.name == name && !h.gameObject.scene.IsValid()) return h;
		}
		Plugin.LogError($"Unable to find {typeof(T)} prefab '{name}'");
		return null;
	}
	
	public HazardSerializer(MonoBehaviour m) : base(m) {
		if (!(m is DGameMap)) {
			throw new InvalidCastException($"{m.GetType()} is not a valid parent type for Hazards");
		}
	}
	
	/* Format:
	 * base
	 * rotation: Vector3
	 * code: byte[2]
	*/
	protected override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		
		sc.Add(tgt.transform.rotation.eulerAngles.x);
		sc.Add(tgt.transform.rotation.eulerAngles.y);
		sc.Add(tgt.transform.rotation.eulerAngles.z);
		
		sc.Add(tgt.GetComponentInChildren<TerminalAccessibleObject>(true).objectCode.Substring(0,2));
	}
	
	protected override T DeserializeData(T rt, DeserializationContext dc) {
		base.DeserializeData(rt,dc);
		
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		
		dc.Consume(2).CastInto(out string code);
		
		if (rt == null) return rt;
		
		rt.transform.rotation = Quaternion.Euler(x,y,z);
		rt.GetComponentInChildren<TerminalAccessibleObject>(true).objectCode = code;
		
		return rt;
	}
}

public class SpikeTrapHazardSerializer<T> : HazardSerializer<T> where T : SpikeTrapHazard {
	
	public SpikeTrapHazardSerializer(MonoBehaviour m) : base(m) {}
	
	/* Format:
	 * base
	 * slamInterval: int (0 => playerDetection)
	*/
	protected override void SerializeData(SerializationContext sc,T tgt) {
		base.SerializeData(sc,tgt);
		bool playerDetection = !SpikeRoofTrapAccess.slamOnIntervals(tgt.HazardScript);
		float serializedValue = playerDetection ? 0.0f : SpikeRoofTrapAccess.slamInterval(tgt.HazardScript);
		sc.Add(serializedValue);
	}
	protected override T DeserializeData(T rt, DeserializationContext dc) {
		base.DeserializeData(rt,dc);
		
		dc.Consume(sizeof(float)).CastInto(out float slamInterval);
		
		if (rt == null) return rt;
		
		SpikeRoofTrapAccess.slamOnIntervals(rt.HazardScript,slamInterval == 0);
		SpikeRoofTrapAccess.slamInterval   (rt.HazardScript,slamInterval);
		
		return rt;
	}
}

public abstract class MapObjectNetworkSerializer<T> : Serializer<T> where T : MapObject {
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
		
		sc.Add(obj.transform.position.x);
		sc.Add(obj.transform.position.y);
		sc.Add(obj.transform.position.z);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		
		rt.transform.position = new Vector3(x,y,z);
		
		if (Parent is DGameMap map) {
			rt.FindParent(map: map);
		} else {
			rt.transform.parent = this.Parent.transform;
		}
		return rt;
	}
	
	public override T Deserialize(DeserializationContext dc) {
		dc.Consume(sizeof(ulong)).CastInto(out ulong netobjid);
		
		return Deserialize(
			NetworkManager.Singleton.SpawnManager.SpawnedObjects[netobjid].GetComponent<T>(), 
			dc
		);
	}
}

public class GrabbableMapObjectNetworkSerializer<T> : MapObjectNetworkSerializer<T> where T : GrabbableMapObject {
	public GrabbableMapObjectNetworkSerializer(Moon     p) : base(p) {}
	public GrabbableMapObjectNetworkSerializer(DGameMap p) : base(p) {}
	public GrabbableMapObjectNetworkSerializer(Cruiser  p) : base(p) {}
	
	protected override T Deserialize(T s, DeserializationContext dc) {
		base.Deserialize(s,dc);
		
		s.Grabbable.startFallingPosition = (
			s.Grabbable.targetFloorPosition = s.transform.localPosition
		);
		
		return s;
	}
}

public class ScrapNetworkSerializer<T> : GrabbableMapObjectNetworkSerializer<T> where T : Scrap {
	
	public ScrapNetworkSerializer(Moon     p) : base(p) {}
	public ScrapNetworkSerializer(DGameMap p) : base(p) {}
	public ScrapNetworkSerializer(Cruiser  p) : base(p) {}
	
	public override void Serialize(SerializationContext sc, T s) {
		base.Serialize(sc,s);
		sc.Add(s.Grabbable.scrapValue);
	}
	
	protected override T Deserialize(
		T s, DeserializationContext dc
	) {
		base.Deserialize(s,dc);
		
		dc.Consume(sizeof(int)).CastInto(out int scrapValue);
		s.Grabbable.SetScrapValue(scrapValue);
		
		return s;
	}
}

public class BatteryEquipmentNetworkSerializer<T> 
	: GrabbableMapObjectNetworkSerializer<T> 
	where T : BatteryEquipment 
{
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
public class GunEquipmentNetworkSerializer<T> : GrabbableMapObjectNetworkSerializer<T> where T : GunEquipment {
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

public class HazardNetworkSerializer<T> : MapObjectNetworkSerializer<T>  where T : HazardBase {
	
	public HazardNetworkSerializer(DGameMap m) : base(m) {}
	
	/* Format:
	 * base
	 * rotation: Vector3
	*/
	public override void Serialize(SerializationContext sc, T tgt) {
		base.Serialize(sc,tgt);
		
		sc.Add(tgt.transform.rotation.eulerAngles.x);
		sc.Add(tgt.transform.rotation.eulerAngles.y);
		sc.Add(tgt.transform.rotation.eulerAngles.z);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		base.Deserialize(rt,dc);
		
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		
		rt.transform.rotation = Quaternion.Euler(x,y,z);
		
		return rt;
	}
}

public class SpikeTrapHazardNetworkSerializer<T> : HazardNetworkSerializer<T> where T : SpikeTrapHazard {
	
	public SpikeTrapHazardNetworkSerializer(DGameMap m) : base(m) {}
	
	/* Format:
	 * base
	 * slamOnIntervals: bool
	*/
	public override void Serialize(SerializationContext sc,T tgt) {
		base.Serialize(sc,tgt);
		sc.Add(SpikeRoofTrapAccess.slamOnIntervals(tgt.HazardScript));
	}
	protected override T Deserialize(T rt, DeserializationContext dc) {
		base.Deserialize(rt,dc);
		
		dc.Consume(sizeof(bool)).CastInto(out bool slamOnIntervals);
		
		SpikeRoofTrapAccess.slamOnIntervals(rt.HazardScript,slamOnIntervals);
		
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
	 *   int hp
	 *   int numBoosts
	 *   bool[] carIsRunning, IsBackDoorOpen
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
		
		sc.Add(cruiser.VehicleController.carHP);
		sc.Add(FuelAccess.Get(cruiser.VehicleController));
		sc.AddBools<bool>(
			[cruiser.VehicleController.ignitionStarted, cruiser.IsBackDoorOpen],
			(t) => t
		);
		
		new MapObjectCollection(cruiser).Serialize(
			sc,
			new ScrapSerializer           <Scrap>           (cruiser),
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
		
		dc.Consume(sizeof(int )).CastInto(out int hp);
		dc.Consume(sizeof(int )).CastInto(out int boosts);
		bool running=false, isBackOpen=false;
		int i=0;
		foreach (bool b in dc.ConsumeBools(2)) {
			switch (i) {
				case 0:
					running = b;
				break; case 1:
					isBackOpen = b;
				break;
			}
			i++;
		}
		
		if (cruiser != null) {
			cruiser.SetMoon(parent);
			cruiser.transform.position = pos;
			cruiser.transform.rotation = rot;
			
			cruiser.VehicleController.carHP = hp;
			cruiser.VehicleController.carDestroyed = (hp == 0);
			FuelAccess.Set(cruiser.VehicleController, boosts);
			cruiser.VehicleController.SetIgnition(running);
			cruiser.IsBackDoorOpen = isBackOpen;
		}
		
		MapObjectCollection.Deserialize(
			dc,
			new ScrapSerializer           <Scrap>           (cruiser),
			new EquipmentSerializer       <Equipment>       (cruiser),
			new BatteryEquipmentSerializer<BatteryEquipment>(cruiser),
			new GunEquipmentSerializer    <GunEquipment>    (cruiser),
			new BatteryEquipmentSerializer<FueledEquipment> (cruiser)
		);
		
		return cruiser;
	}
	
	public override Cruiser Deserialize(DeserializationContext dc) {
		Cruiser rt = null;
		if (NetworkManager.Singleton.IsServer && parent != null) {
			var g = GameObject.Instantiate(Cruiser.Prefab);
			g.AddComponent<DummyFlag>();
			g.GetComponent<NetworkObject>().Spawn();
			rt = g.GetComponent<Cruiser>();
			rt.gameObject.SetActive(false);
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
		
		sc.Add(tgt.VehicleController.carHP);
		sc.Add(FuelAccess.Get(tgt.VehicleController));
		sc.AddBools<bool>(
			[tgt.VehicleController.ignitionStarted, tgt.IsBackDoorOpen],
			(t) => t
		);
		
		new MapObjectCollection(tgt).Serialize(
			sc,
			new ScrapNetworkSerializer             <Scrap>           (tgt),
			new GrabbableMapObjectNetworkSerializer<Equipment>       (tgt),
			new BatteryEquipmentNetworkSerializer  <BatteryEquipment>(tgt),
			new GunEquipmentNetworkSerializer      <GunEquipment>    (tgt),
			new BatteryEquipmentNetworkSerializer  <FueledEquipment> (tgt)
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
		
		dc.Consume(sizeof(int)).CastInto(out int hp);
		tgt.VehicleController.carHP = hp;
		tgt.VehicleController.carDestroyed = (hp == 0);
		
		dc.Consume(sizeof(int)).CastInto(out int boosts);
		FuelAccess.Set(tgt.VehicleController, boosts);
		
		bool running = false, isBackOpen = false;
		int i=0;
		foreach (bool b in dc.ConsumeBools(2)) {
			switch (i) {
			case 0:
				running = b;
			break; case 1:
				isBackOpen = false;
			break;
			}
			i++;
		}
		tgt.VehicleController.SetIgnition(running);
		tgt.IsBackDoorOpen = isBackOpen;
		
		MapObjectCollection.Deserialize(
			dc,
			new ScrapNetworkSerializer             <Scrap>           (tgt),
			new GrabbableMapObjectNetworkSerializer<Equipment>       (tgt),
			new BatteryEquipmentNetworkSerializer  <BatteryEquipment>(tgt),
			new GunEquipmentNetworkSerializer      <GunEquipment>    (tgt),
			new BatteryEquipmentNetworkSerializer  <FueledEquipment> (tgt)
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