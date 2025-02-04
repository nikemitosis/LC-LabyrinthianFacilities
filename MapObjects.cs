namespace LabyrinthianFacilities;

using System;
using System.Collections.Generic;

using UnityEngine;
using Unity.Netcode;

using DgConversion;
using Serialization;
using Util;

using Object=UnityEngine.Object;

public class MapObject : MonoBehaviour {
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
		
		bool noparentfound = true;
		foreach (Tile t in map.GetComponentsInChildren<Tile>(
			map.gameObject.activeInHierarchy
		)) {
			if (t.BoundingBox.Contains(this.transform.position)) {
				this.transform.parent = t.transform;
				noparentfound = false; break;
			}
		} if (noparentfound) {
			this.transform.parent = moon.transform;
		}
		this.Grabbable.targetFloorPosition 
			= this.Grabbable.startFallingPosition 
			= this.transform.localPosition;
		
		return this.transform.parent;
	}
	
	public virtual void Preserve() {
		var grabbable = this.Grabbable;
		grabbable.isInShipRoom = StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(
			grabbable.transform.position
		); // fix isInShipRoom for people joining partway through a save
		
		if (
			!grabbable.isInShipRoom
			&& this.transform.parent?.GetComponent<Cruiser>() == null // exclude things on cruiser
		) {
			this.FindParent();
			this.gameObject.SetActive(false);
		}
		
	}
	
	public virtual void Restore() {
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
	
	protected virtual void OnDestroy() {
		if (this.Grabbable.radarIcon != null) GameObject.Destroy(this.Grabbable.radarIcon.gameObject);
	}
	
	public override void Preserve() {
		base.Preserve();
		var grabbable = this.Grabbable;
		if (grabbable.radarIcon != null && grabbable.radarIcon.gameObject != null) {
			grabbable.radarIcon.gameObject.SetActive(false);
		}
	}
	
	public override void Restore() {
		base.Restore();
		var grabbable = this.Grabbable;
		if (
			!grabbable.isInShipRoom 
			&& grabbable.radarIcon != null 
			&& grabbable.radarIcon.gameObject != null
		) {
			grabbable.radarIcon.gameObject.SetActive(true);
		}
	}
}

// The purpose of this is to mark an object that is being spawned by the mod, and should not use the 
// normal initialization
internal class DummyFlag : MonoBehaviour {}

public class Beehive : Scrap {
	
	private bool IsServer {
		get {return NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;}
	}
	
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
		if (this.GetComponentInParent<Moon>() != null) {
			this.bees = SpawnBees();
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
	
	protected virtual RedLocustBees SpawnBees() {
		if (!IsServer) return null;
		
		if (this.beeInfo.IsInvalid) {
			this.beeInfo = new BeeInfo(position: this.transform.position, currentBehaviourStateIndex: 0);
		}
		GameObject g = GameObject.Instantiate(BeesPrefab, this.beeInfo.position, Quaternion.identity);
		
		RedLocustBees bees = g.GetComponent<RedLocustBees>();
		bees.currentBehaviourStateIndex = this.beeInfo.currentBehaviourStateIndex;
		
		bees.hive = this.Grabbable;
		bees.lastKnownHivePosition = this.transform.position;
		RoundManager.Instance.SpawnedEnemies.Add(bees);
		
		g.AddComponent<DummyFlag>();
		g.GetComponent<NetworkObject>().Spawn();
		return bees;
	}
	
	public override void Preserve() {
		base.Preserve();
		
		GrabbableObject grabbable = this.Grabbable;
		if (grabbable.isInShipRoom) {
			this.beeInfo = new BeeInfo(Vector3.zero, -1);
			return;
		}
		
		if (this.bees == null) {
			foreach (RedLocustBees swarm in Object.FindObjectsByType(
				typeof(RedLocustBees), 
				FindObjectsSortMode.None
			)) {
				if (swarm.hive == grabbable) {
					this.bees = swarm;
					break;
				}
			}
			if (this.bees == null) {
				Plugin.LogError($"Could not find bees for hive");
				return;
			}
		}
		
		this.beeInfo = new BeeInfo(
			position: this.bees.transform.position, 
			currentBehaviourStateIndex: this.bees.currentBehaviourStateIndex
		);
		
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
}

// Bypassing Netcode's requirement that NetworkObjects must be parented under other NetworkObjects
// Yes this is bad practice! 
// (I didn't want moons to be server-managed just to take advantage of unity's parenting)
public class Cruiser : NetworkBehaviour {
	
	public Moon Moon {get => this.transform.parent.GetComponent<Moon>();}
	
	private static GameObject prefab = null;
	public GameObject Prefab {get {
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
		var vc = this.GetComponent<VehicleController>();
		if (vc.magnetedToShip  ||  vc.carDestroyed && vc.GetComponentsInChildren<MapObject>().Length == 0) {
			this.transform.parent = null;
			return;
		}
		foreach (MapObject mo in this.GetComponentsInChildren<MapObject>()) {
			mo.gameObject.SetActive(false);
		}
		SetMoon(MapHandler.Instance.ActiveMoon);
		this.gameObject.SetActive(false);
	}
	
	public void Restore() {
		if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer) {
			GameObject    gameObj = GameObject.Instantiate(Prefab);
			NetworkObject netObj  = gameObj.GetComponent<NetworkObject>();
			
			gameObj.transform.position = this.transform.position;
			gameObj.transform.rotation = this.transform.rotation;
			
			netObj.Spawn();
			RestoreClientRpc(this.NetworkObjectId, netObj.NetworkObjectId);
		}
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
			mo.gameObject.SetActive(true);
		}
		
		older.GetComponent<Cruiser>().DoneWithOldCruiserServerRpc();
	}
	
	private int numFinished = 0;
	[ServerRpc(RequireOwnership=false)]
	public void DoneWithOldCruiserServerRpc(bool disconnect=false) {
		if (!disconnect) numFinished++;
		if (numFinished == StartOfRound.Instance.connectedPlayersAmount) {
			this.GetComponent<NetworkObject>().Despawn(true);
		}
	}
	
	public void SetMoon(Moon moon) {
		// SHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUPSHUTUP
		this.GetComponent<NetworkObject>().AutoObjectParentSync = false;
		this.transform.parent = moon.transform;
	}
}

// extraContext is GameMap that this is parented to
public abstract class MapObjectSerializer<T> : Serializer<T> where T : MapObject {
	public abstract T GetPrefab(string id);
	
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
	
	protected override T Deserialize(T rt, DeserializationContext dc, object extraContext=null) {
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		
		if (rt == null) return null;
		rt.transform.position = new Vector3(x,y,z);
		
		if (extraContext is Moon moon) {
			rt.transform.parent = moon.transform;
		} else if (extraContext is DGameMap map) {
			rt.transform.parent = map.transform;
		} else {
			throw new NullReferenceException($"No moon or map provided for MapObject {rt} to parent to.");
		}
		return rt;
	}
	public override T Deserialize(DeserializationContext dc, object extraContext=null) {
		
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string name);
		dc.Consume(1); // null terminator
		T rt = null;
		
		// network object cannot be instantiated by client
		if (MapHandler.Instance.IsServer || MapHandler.Instance.IsHost) {
			rt = Object.Instantiate(GetPrefab(name));
		}
		
		return Deserialize(rt, dc, extraContext);
	}
}

public class ScrapSerializer : MapObjectSerializer<Scrap> {
	public override Scrap GetPrefab(string id) => Scrap.GetPrefab(id);
	
	/* Format:
	 *     base
	 *     ScrapValue: int
	*/
	public override void Serialize(SerializationContext sc, Scrap scrap) {
		base.Serialize(sc,scrap);
		sc.Add(scrap.Grabbable.scrapValue);
	}
	
	protected override Scrap Deserialize(
		Scrap rt, DeserializationContext dc, object extraContext=null
	) {
		base.Deserialize(rt,dc,extraContext);
		
		dc.Consume(4).CastInto(out int scrapValue);
		
		if (rt == null) return null;
		rt.Grabbable.SetScrapValue(scrapValue);
		
		return rt;
	}
}

public class EquipmentSerializer : MapObjectSerializer<Equipment> {
	public override Equipment GetPrefab(string id) => Equipment.GetPrefab(id);
	
	public override void Serialize(SerializationContext sc, Equipment eq) => base.Serialize(sc,eq);
}

// extraContext is DGameMap or Moon that this is parented to
public class MapObjectNetworkSerializer<T> : Serializer<T> where T : MapObject {
	public override void Serialize(SerializationContext sc, T obj) {
		var netObj = obj.GetComponent<NetworkObject>();
		if (!(netObj?.IsSpawned ?? false)) {
			throw new InvalidOperationException(
				$"Cannot use {this.GetType()} to serialize {obj} that is not spawned. " 
			);
		}
		sc.Add(netObj.NetworkObjectId);
	}
	
	protected override T Deserialize(
		T s, DeserializationContext dc, object extraContext=null
	) {
		if (extraContext is DGameMap map) {
			s.FindParent(map: map);
		} else if (extraContext is Moon moon) {
			s.transform.parent = moon.transform;
		} else if (extraContext is Cruiser cruiser) {
			s.transform.parent = cruiser.transform;
		} else {
			try {s.FindParent();} catch (NullReferenceException) {
				throw new NullReferenceException("No moon/map provided to network-deserialized MapObject");
			}
		}
		
		return s;
	}
	
	public override T Deserialize(DeserializationContext dc, object extraContext=null) {
		dc.Consume(sizeof(ulong)).CastInto(out ulong netobjid);
		return Deserialize(
			NetworkManager.Singleton.SpawnManager.SpawnedObjects[netobjid].GetComponent<T>(),
			dc,
			extraContext
		);
	}
}

public class ScrapNetworkSerializer : MapObjectNetworkSerializer<Scrap> {
	public override void Serialize(SerializationContext sc, Scrap s) {
		base.Serialize(sc,s);
		sc.Add(s.Grabbable.scrapValue);
	}
	
	protected override Scrap Deserialize(
		Scrap s, DeserializationContext dc, object extraContext=null
	) {
		base.Deserialize(s,dc,extraContext);
		
		dc.Consume(sizeof(int)).CastInto(out int scrapValue);
		s.Grabbable.SetScrapValue(scrapValue);
		
		return s;
	}
}

public class EquipmentNetworkSerializer : MapObjectNetworkSerializer<Equipment> {}

// This will break with other vehicles, either modded or added into the game 
// or at least they will if they use Cruiser
public class CruiserSerializer : Serializer<Cruiser> {
	
	private static GameObject prefab = null;
	
	/* Format:
	 *   float x,y,z
	 *   Quaternion x,y,z,w
	 *   Scrap[] scrap
	 *   Equipment[] equipment
	*/
	public override void Serialize(SerializationContext sc, Cruiser cruiser) {
		sc.Add(cruiser.transform.position.x);
		sc.Add(cruiser.transform.position.y);
		sc.Add(cruiser.transform.position.z);
		
		sc.Add(cruiser.transform.rotation.x);
		sc.Add(cruiser.transform.rotation.y);
		sc.Add(cruiser.transform.rotation.z);
		sc.Add(cruiser.transform.rotation.w);
		
		Scrap[] scrap = cruiser.GetComponentsInChildren<Scrap>(true);
		ScrapSerializer scrapSer = new ScrapSerializer();
		sc.Add((ushort)scrap.Length);
		foreach (Scrap s in scrap) {
			sc.AddInline(s,scrapSer);
		}
		
		Equipment[] equipment = cruiser.GetComponentsInChildren<Equipment>(true);
		EquipmentSerializer eqSer = new();
		sc.Add((ushort)scrap.Length);
		foreach (Equipment eq in equipment) {
			sc.AddInline(eq,eqSer);
		}
	}
	
	// extraContext is Moon
	protected override Cruiser Deserialize(
		Cruiser cruiser, DeserializationContext dc, object extraContext=null
	) {
		// Cruiser is null for clients! (deliberate, clients cannot control network objects)
		
		var moon = (Moon)extraContext;
		
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
			cruiser.transform.parent = moon.transform;
			cruiser.transform.position = pos;
			cruiser.transform.rotation = rot;
		}
		
		dc.Consume(2).CastInto(out ushort count);
		var scrapSer = new ScrapSerializer();
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(scrapSer,cruiser);
		}
		
		dc.Consume(2).CastInto(out count);
		var eqSer = new EquipmentSerializer();
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(eqSer,cruiser);
		}
		
		return cruiser;
	}
	
	// extraContext is Moon
	public override Cruiser Deserialize(DeserializationContext dc, object extraContext=null) {
		Cruiser rt = null;
		if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost) {
			if (prefab == null) {
				foreach (Cruiser vc in Resources.FindObjectsOfTypeAll<Cruiser>()) {
					if (vc.name == "CompanyCruiser") {
						prefab = vc.gameObject;
						break;
					}
				}
			}
			var g = GameObject.Instantiate(prefab);
			g.AddComponent<DummyFlag>();
			g.GetComponent<NetworkObject>().Spawn();
			rt = g.GetComponent<Cruiser>();
		}
		return Deserialize(rt,dc,extraContext);
	}
}

public class CruiserNetworkSerializer : Serializer<Cruiser> {
	public override void Serialize(SerializationContext sc, Cruiser tgt) {
		var netobj = tgt.GetComponent<NetworkObject>();
		if (netobj == null || !netobj.IsSpawned) {
			throw new ArgumentException(
				$"Cannot use {this.GetType()} to serialize {tgt} that is not spawned. "
			);
		}
		sc.Add(netobj.NetworkObjectId);
		
		Scrap[] scrap = tgt.GetComponentsInChildren<Scrap>();
		sc.Add((ushort)scrap.Length);
		var scrapSerializer = new ScrapNetworkSerializer();
		foreach (Scrap s in scrap) {
			sc.AddInline(s,scrapSerializer);
		}
		
		Equipment[] equipment = tgt.GetComponentsInChildren<Equipment>();
		sc.Add((ushort)equipment.Length);
		var eqSerializer = new EquipmentNetworkSerializer();
		foreach (Equipment eq in equipment) {
			sc.AddInline(eq,eqSerializer);
		}
	}
	
	protected override Cruiser Deserialize(
		Cruiser tgt, DeserializationContext dc, object extraContext=null
	) {
		Moon moon = (Moon)extraContext;
		
		tgt.transform.parent = moon.transform;
		
		dc.Consume(2).CastInto(out ushort numScrap);
		var scrapSerializer = new ScrapNetworkSerializer();
		for (ushort i=0; i<numScrap; i++) {
			dc.ConsumeInline(scrapSerializer,tgt);
		}
		
		dc.Consume(2).CastInto(out ushort numEquipment);
		var equipmentSerializer = new EquipmentNetworkSerializer();
		for (ushort i=0; i<numEquipment; i++) {
			dc.ConsumeInline(equipmentSerializer,tgt);
		}
		
		return tgt;
	}
	
	public override Cruiser Deserialize(DeserializationContext dc, object extraContext=null) {
		dc.Consume(sizeof(ulong)).CastInto(out ulong netobjid);
		return Deserialize(
			NetworkManager.Singleton.SpawnManager.SpawnedObjects[netobjid].GetComponent<Cruiser>(),
			dc,
			extraContext
		);
	}
}