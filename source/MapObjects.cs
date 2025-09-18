namespace LabyrinthianFacilities;
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
	
    public virtual bool ShouldPreserve {get => true;}
    
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
    
    public abstract override bool ShouldPreserve {get => Config.Singleton.SaveGrabbableMapObjects;}
	
	public override Transform FindParent(DGameMap map=null) {
		var rt = base.FindParent(map);
		
		this.Grabbable.targetFloorPosition 
			= this.Grabbable.startFallingPosition 
			= this.transform.localPosition;
		
		return rt;
	}
	
	public override void Preserve() {
		if (!this.ShouldPreserve()) return;
		
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
    public override bool ShouldPreserve {get => base.ShouldPreserve && Config.Singleton.SaveScrap;}
	
	public static Scrap GetPrefab(string name) => MapObject.GetPrefab<Scrap>(name);
	
	public override void OnDestroy() {
		base.OnDestroy();
		if (this.Grabbable == null) return;
		if (this.Grabbable.radarIcon != null) GameObject.Destroy(this.Grabbable.radarIcon.gameObject);
	}
	
	public override void Preserve() {
		if (!this.ShouldPreserve) return;
		
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

public abstract class ParentedScrap<T> : Scrap where T : EnemyAI {
    
    private abstract string parentPrefabIdentifier {get;}
    
    protected struct Parent {
        public Vector3 position = Vector3.zero;
        public int currentBehaviourStateIndex = -1;
        public ParentType reference = null;
        public List<ParentedScrap> scrapGroup = new();
        
        public bool IsInvalid {get => currentBehaviourStateIndex < 0;}
        
        private static GameObject prefab;
        public GameObject ParentPrefab {get {
            if (prefab == null) {
                prefab = MapObject.GetPrefab<ParentType>(parentPrefabIdentifier);
            }
            return prefab;
        }}
        
        public Parent() : Parent(null) {}
        public Parent(Vector3 position, int currentBehaviourStateIndex) {
            this.position = position;
            this.currentBehaviourStateIndex = currentBehaviourStateIndex;
            this.reference = null;
        }
        public Parent(ParentType parent) {
            if (parent == null) {
                this.position = Vector3.zero;
                this.currentBehaviourStateIndex = -1;
                this.reference = null;
            } else {
                this.position = parent.transform.position;
                this.currentBehaviourStateIndex = parent.currentBehaviourStateIndex;
                this.reference = parent;
            }
        }
        
        public void Invalidate() {
            this.position = Vector3.zero;
            this.currentBehaviourStateIndex = -1;
        }
        
        public void Save(ParentType parent) {
            this.reference = parent;
            if (parent == null) {
                this.Invalidate();
            } else {
                this.position = parent.transform.position;
                this.currentBehaviourStateIndex = parent.currentBehaviourStateIndex;
            }
        }
        
        public void Spawn() {
            if (
                !NetworkManager.Singleton.IsServer
                || this.IsInvalid // parent assumed dead
            ) return;
            
            GameObject g = GameObject.Instantiate(ParentPrefab,this.position,Quaternion.identity);
            this.reference = g.GetComponent<ParentType>();
            g.AddComponent<DummyFlag>();
            g.GetComponent<NetworkObject>().Spawn();
        }
        
        public void Despawn() {
            
            if (!NetworkManager.Singleton.IsServer || this.reference == null) return;
            var netObj = this.reference.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned) return;
            this.reference.GetComponent<NetworkObject>().Despawn();
			this.reference = null;
        }
    }
    
    public virtual bool ShouldSpawnParent {
        get => (
            this.IsServer 
            && parent.reference == null 
            && this.GetComponentInParent<Moon>() != null
        );
    }
    public virtual bool ShouldDespawnParent {
        get => (
            this.IsServer
			&& this.parent.reference != null 
			&& this.parent.reference.IsSpawned
        );
    }
    protected Parent parent = new Parent();
    
    protected void OnEnable() {
        if (ShouldSpawnParent) this.SpawnParent();
    }
    
    protected void OnDisable() {
		if (ShouldDespawnParent) {
            this.parent.Despawn();
		}
	}
    
    [ClientRpc]
	protected void SendParentClientRpc(NetworkObjectReference parentNetObj, int behaviourStateIndex) {
		if (IsServer) return;
		this.parent = new Parent( ((NetworkObject)parentNetObj).GetComponent<ParentType>() );
        this.parent.reference.currentBehaviourStateIndex = behaviourStateIndex;
        this.ParentClientInit();
	}
    
    protected abstract void ParentClientInit();
    
    protected void SpawnParent() {
		if (!IsServer) return;
		
        this.parent.Spawn();
        if (this.parent.reference == null) return;
        
		this.ClientInit();
		this.parent.reference.gameObject.AddComponent<DummyFlag>();
		
        this.SendParent();
	}
    
    protected virtual void SendParent() {
        this.SendParentClientRpc(
            this.parent.reference.gameObject.GetComponent<NetworkObject>(),
            this.parent.reference.currentBehaviourStateIndex
        );
    }
    
    public virtual void SaveParent(ParentType p) {
        this.parent.Save(p);
    }
    
    public override void Preserve() {
		if (!ShouldPreserve) return;
		base.Preserve();
		
		if (this.Grabbable.isInShipRoom) {
            this.parent.scrapGroup.Remove(this);
			this.parent.Invalidate();
		}
	}
}

public class Beehive : ParentedScrap<RedLocustBees> {
	
    public override bool ShouldPreserve {get => base.ShouldPreserve && Config.Singleton.SaveBees;}
    
    protected override void ClientInit() {
        this.parent.reference.hive = this.Grabbable;
        this.parent.reference.lastKnownHivePosition = this.transform.position;
        this.parent.reference.gameObject.AddComponent<DummyFlag>();
    }
    
}

public class BirdEgg : ParentedScrap<GiantKiwiAI> {
    
    protected List<BirdEgg> eggGroup {
        get => this.parent.scrapGroup;
        set => this.parent.scrapGroup = value;
    }
    
    public override bool ShouldPreserve {
        get => base.ShouldPreserve /* && Config.Singleton.SaveSapsuckerEggs */;
    }
    
    public override bool ShouldSpawnParent   {get => base.ShouldSpawnParent   && this == eggGroup[0];}
    public override bool ShouldDespawnParent {get => base.ShouldDespawnParent && this == eggGroup[0];}
    
    public static List<BirdEgg> CreateEggGroup(GiantKiwiAI bird) {
        var rt = new List<BirdEgg>(bird.eggs.Count);
        foreach (KiwiBabyItem eggItem in bird.eggs) {
            var egg = eggItem.GetComponent<BirdEgg>()
            rt.Add(egg);
            egg.SaveParent(bird);
        }
        this.parent.scrapGroup = rt;
        return rt;
    }
    
    public override void SaveParent(GiantKiwiAI parent) {
        base.SaveParent(parent);
        this.parent.scrapGroup = parent.eggs;
    }
    
    protected override void SendParent() {
        base.SendParent();
        
        SendGiantKiwiClientRpc(
            this.parent.reference.GetComponent<NetworkObject>()
        );
    }
    
    protected override void ClientInit() {
        this.parent.reference.eggs.Add(this.Grabbable);
        this.parent.reference.hasSpawnedEggs = true;
    }
    
    [ClientRpc]
    private void SendGiantKiwiClientRpc(
        NetworkObjectReference netObjRef, NetworkObjectReference[] eggs
    ) {
        NetworkObject netObj = (NetworkObject)netObjRef;
        GiantKiwiAI giantKiwi = netObj.GetComponent<GiantKiwiAI>();
        
        giantKiwi.eggs = new List<KiwiBabyItem>(eggs.Length);
    }
}

public class Equipment : GrabbableMapObject {
	public static Equipment GetPrefab(string name) => MapObject.GetPrefab<Equipment>(name);
	public override bool ShouldPreserve {get => base.ShouldPreserve && Config.Singleton.SaveEquipment;}
    
	public override void Preserve() {
		if (!ShouldPreserve) return;
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
		if (ShouldPreserve && !Grabbable.isHeldByEnemy) base.Preserve();
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
    public override bool ShouldPreserve {get => Config.Singleton.SaveHazards;}
	public TerminalAccessibleObject TerminalAccess {
		get => this.GetComponentInChildren<TerminalAccessibleObject>(true);
	}
	public GameObject MapRadarText {
		get => TerminalAccessibleObjectAccess.MapRadarText(TerminalAccess);
	}
	
	public override void Preserve() {
		if (ShouldPreserve) {
			this.FindParent();
			MapRadarText?.SetActive(false);
		}
	}
	public override void Restore() {
		if (ShouldPreserve) {
			MapRadarText?.SetActive(true);
			try {
				TerminalAccess.setCodeRandomlyFromRoundManager = false;
			} catch (NullReferenceException) {}
		}
	}
}
public class TurretHazard : Hazard<Turret> {
    public override bool ShouldPreserve {get => base.ShouldPreserve && Config.Singleton.SaveTurrets;}
    
	public override void Preserve() {
		if (ShouldPreserve) base.Preserve();
	}
}
public class LandmineHazard : Hazard<Landmine> {
	public override bool ShouldPreserve {
        get => base.ShouldPreserve && Config.Singleton.SaveLandmines && !HazardScript.hasExploded;
    }
    
    public override void Preserve() {
		if (ShouldPreserve) base.Preserve();
		else if (this.transform.parent != null) { // this landmine has been saved and must be explicitly destroyed
            this.GetComponent<NetworkObject>().Despawn(destroy: true);
        }
	}
	
	public override void Restore() {
		base.Restore();
		LandmineAccess.Restart(this.HazardScript);
	}
}
public class SpikeTrapHazard : Hazard<SpikeRoofTrap> {
    public override bool ShouldPreserve {get => base.ShouldPreserve && Config.Single.SaveSpikeTraps;}
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
		
		netObj.Spawn();
		RestoreClientRpc(this.NetworkObjectId, netObj.NetworkObjectId);
	}
	
	[ClientRpc]
	public void RestoreClientRpc(ulong oldId, ulong newId) {
		NetworkObject older = NetworkManager.Singleton.SpawnManager.SpawnedObjects[oldId];
		NetworkObject newer = NetworkManager.Singleton.SpawnManager.SpawnedObjects[newId];
		
		newer.gameObject.AddComponent<DummyFlag>();
		
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
public interface IMapObjectGroupSerializer : ICollectionSerializer {
	public MapObject Prefab {get;}
	public MonoBehaviour Parent {get;}
}
public class MapObjectGroupSerializer<T> : CollectionSerializer<T>, IMapObjectGroupSerializer where T : MapObject {
	
	MapObject IMapObjectGroupSerializer.Prefab {get => Prefab;}
	public T Prefab;
	public MonoBehaviour Parent {get; protected set;}
	
	public MapObjectGroupSerializer(MonoBehaviour p) {
		if (!(
			p == null 
			|| p is Moon
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
			if (item == null) {
				Plugin.LogError("Null mapObject {item} in collection");
				continue;
			}
			ChooseSerializer(item);
			break;
		}
	}
	
	protected override void DeserializeSharedPreamble(DeserializationContext dc) {
		dc.ConsumeUntil((byte b) => (b == 0)).CastInto(out string ident);
		dc.Consume(1);
		
		ChooseSerializer(GetPrefab(ident));
		
		if (!(this as ICollectionSerializer).IsValid) {
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
		if (mapObject == null) {
			Plugin.LogError($"MapObjectGroupSerializer: Cannot choose a serializer based off of 'null'");
			this.ItemSerializer = null;
			return;
		}
		Type type = mapObject.GetType();
		
		IItemSerializer<MapObject> itemSerializer;
		if (typeof(GrabbableMapObject).IsAssignableFrom(type)) {
			if (typeof(Scrap).IsAssignableFrom(type)) {
				if (typeof(GunEquipment).IsAssignableFrom(type)) {
					itemSerializer = new GunEquipmentSerializer    <GunEquipment    >(Parent);
				} else {
					itemSerializer = new ScrapSerializer           <Scrap           >(Parent);
				}
			} else if (typeof(Equipment).IsAssignableFrom(type)) {
				if (typeof(BatteryEquipment).IsAssignableFrom(type)) {
					itemSerializer = new BatteryEquipmentSerializer<BatteryEquipment>(Parent); 
				} else {
					itemSerializer = new EquipmentSerializer       <Equipment       >(Parent);
				}
			} else {
				itemSerializer = null;
			}
		} else if (typeof(HazardBase).IsAssignableFrom(type)) {
			if (typeof(SpikeTrapHazard).IsAssignableFrom(type)) {
				itemSerializer = new SpikeTrapHazardSerializer <SpikeTrapHazard >(Parent);
			} else {
				itemSerializer = new HazardSerializer          <HazardBase      >(Parent);
			}
		} else {
			itemSerializer = null;
		}
		
		if (itemSerializer == null) {
			Plugin.LogError($"Could not find a serializer for '{type}'");
		} else {
			this.Init((IItemSerializer<T>)itemSerializer);
		}
	}
}

public abstract class MapObjectSerializer<T> : ItemSerializer<T> where T : MapObject {
	public abstract T GetPrefab(string id);
	
	public MonoBehaviour Parent {get; private set;}
	
	public new IMapObjectGroupSerializer GroupSerializer {
		get => (IMapObjectGroupSerializer)base.GroupSerializer;
		set => base.GroupSerializer = (IMapObjectGroupSerializer)value;
	}
	
	public MapObjectSerializer(MonoBehaviour m) {
		if (!(m == null || m is Moon || m is DGameMap || m is Cruiser)) {
			throw new InvalidCastException($"{m.GetType()} is not a valid parent type for a MapObject");
		}
		this.Parent = m;
	}
	
	/* Format: 
	 * Identifier: string
	 * position: Vector3
	*/
	public override void SerializePreamble(SerializationContext sc, T tgt) {
		sc.Add(tgt.name.Substring(0,tgt.name.Length - "(Clone)".Length));
		sc.Add(new byte[]{(byte)0});
	}
	public override void SerializeData(SerializationContext sc, T tgt) {
		sc.Add(tgt.transform.position.x);
		sc.Add(tgt.transform.position.y);
		sc.Add(tgt.transform.position.z);
	}
	
	public override T DeserializePreamble(DeserializationContext dc) {
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string id);
		dc.Consume(1);
		T rt = null;
		if (NetworkManager.Singleton.IsServer && Parent != null) {
			T prefab = (T)(GroupSerializer?.Prefab ?? GetPrefab(id));
			rt = Object.Instantiate(prefab.gameObject).GetComponent<T>();
			rt.GetComponent<NetworkObject>().Spawn();
		}
		return rt;
	}
	public override T DeserializeData(T rt, DeserializationContext dc) {
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		
		if (rt == null) return rt;
		if (this.Parent == null) Plugin.LogError($"Null Parent for MapObjectSerializer");
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
	public override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		sc.Add((byte)(tgt.transform.rotation.eulerAngles.y / 2));
	}
	
	public override T DeserializeData(T rt, DeserializationContext dc) {
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
	public override void SerializeData(SerializationContext sc, T scrap) {
		base.SerializeData(sc,scrap);
		sc.Add(scrap.Grabbable.scrapValue);
	}
	
	public override T DeserializeData(
		T rt, DeserializationContext dc
	) {
		base.DeserializeData(rt,dc);
		
		dc.Consume(4).CastInto(out int scrapValue);
		
		if (rt == null) return rt;
		rt.Grabbable.SetScrapValue(scrapValue);
		
		return rt;
	}
}

public class BirdEggSerializer<T> : ScrapSerializer<T> where T : BirdEgg {
    /* Format:
     * base
     * BirdEgg[] siblings
    */
    public override void SerializeData(SerializationContext sc, T egg) {
        base.SerializeData(sc);
        
        if (egg != egg.eggGroup[0]) {
            sc.Add((ushort)(-1));
            return;
        } else {
            ushort numSiblings = (ushort)egg.eggGroup.Count;
            if (numSiblings != egg.eggGroup.Count || numSiblings == (ushort)(-1)) {
                Plugin.LogError("Too many eggs in one family... more than 65,534... wtf?");
            }
            sc.Add(numSiblings);
            foreach (BirdEgg sibling in egg.eggGroup) {
                sc.AddReference(sibling,this);
            }
        }
    }
    
    public override void DeserializeData(T rt, DeserializationContext dc) {
        base.DeserializeData(rt, dc);
        
        dc.Consume(sizeof(ushort)).CastInto(out ushort numSiblings);
        if (numSiblings == (ushort)(-1)) return;
        
        rt.eggGroup = new List<BirdEgg>(numSiblings);
        rt.eggGroup.Add(rt);
        for (ushort i=0; i<numSiblings; i++) {
            dc.ConsumeReference(
                this,
                (object sibling) => {
                    BirdEgg s = (BirdEgg)sibling;
                    s.eggGroup = rt.eggGroup;
                    rt.eggGroup.Add(s);
                }
            );
        }
    }
}

public class GunEquipmentSerializer<T> : ScrapSerializer<T> where T : GunEquipment {
	
	public GunEquipmentSerializer(MonoBehaviour p) : base(p) {}
	/* Format:
	 * base
	 * bool: safetyOn
	 * int:  numShells
	*/
	public override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		sc.Add(tgt.Safety);
		sc.Add(tgt.NumShells);
	}
	
	public override T DeserializeData(T rt, DeserializationContext dc) {
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
	
	public override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		sc.Add(tgt.Charge);
	}
	
	public override T DeserializeData(T rt, DeserializationContext dc) {
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
	public override void SerializeData(SerializationContext sc, T tgt) {
		base.SerializeData(sc,tgt);
		
		sc.Add(tgt.transform.rotation.eulerAngles.x);
		sc.Add(tgt.transform.rotation.eulerAngles.y);
		sc.Add(tgt.transform.rotation.eulerAngles.z);
		
		sc.Add(tgt.GetComponentInChildren<TerminalAccessibleObject>(true).objectCode.Substring(0,2));
	}
	
	public override T DeserializeData(T rt, DeserializationContext dc) {
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
	public override void SerializeData(SerializationContext sc,T tgt) {
		base.SerializeData(sc,tgt);
		bool playerDetection = !SpikeRoofTrapAccess.slamOnIntervals(tgt.HazardScript);
		float serializedValue = playerDetection ? 0.0f : SpikeRoofTrapAccess.slamInterval(tgt.HazardScript);
		sc.Add(serializedValue);
	}
	public override T DeserializeData(T rt, DeserializationContext dc) {
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