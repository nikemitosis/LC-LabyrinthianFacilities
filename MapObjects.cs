namespace LabyrinthianFacilities;

using System;
using System.Collections.Generic;

using UnityEngine;
using Unity.Netcode;

using DgConversion;
using Serialization;
using Util;

using Object=UnityEngine.Object;

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
		base.Preserve();
		var grabbable = this.Grabbable;
		if (grabbable.radarIcon != null && grabbable.radarIcon.gameObject != null) {
			grabbable.radarIcon.gameObject.SetActive(false);
		}
	}
	
	public override void Restore() {
		base.Restore();
		var grabbable = this.Grabbable;
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
			SpawnBeesServerRpc();
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
	protected virtual void SendBeesClientRpc(NetworkObjectReference beeNetObj) {
		
		this.bees = ((NetworkObject)beeNetObj).GetComponent<RedLocustBees>();
		this.bees.hive = this.Grabbable;
		this.bees.lastKnownHivePosition = this.transform.position;
		RoundManager.Instance.SpawnedEnemies.Add(this.bees);
	}
	
	[ServerRpc]
	protected virtual void SpawnBeesServerRpc() {
		if (this.beeInfo.IsInvalid) {
			this.beeInfo = new BeeInfo(position: this.transform.position, currentBehaviourStateIndex: 0);
		}
		GameObject g = GameObject.Instantiate(BeesPrefab, this.beeInfo.position, Quaternion.identity);
		
		this.bees = g.GetComponent<RedLocustBees>();
		this.bees.currentBehaviourStateIndex = this.beeInfo.currentBehaviourStateIndex;
		
		this.bees.hive = this.Grabbable;
		this.bees.lastKnownHivePosition = this.transform.position;
		RoundManager.Instance.SpawnedEnemies.Add(this.bees);
		
		g.AddComponent<DummyFlag>();
		NetworkObject netObj = g.GetComponent<NetworkObject>();
		netObj.Spawn();
		SendBeesClientRpc(netObj);
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

// extraContext is object that this is parented to
public abstract class MapObjectSerializer<T> : Serializer<T> where T : MapObject {
	public abstract T GetPrefab(string id);
	
	private MonoBehaviour parent;
	
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

public class EquipmentSerializer : MapObjectSerializer<Equipment> {
	public EquipmentSerializer(Moon     p) : base(p) {}
	public EquipmentSerializer(DGameMap p) : base(p) {}
	public EquipmentSerializer(Cruiser  p) : base(p) {}
	
	public override Equipment GetPrefab(string id) => Equipment.GetPrefab(id);
	
	public override void Serialize(SerializationContext sc, Equipment eq) => base.Serialize(sc,eq);
}

public class MapObjectNetworkSerializer<T> : Serializer<T> where T : MapObject {
	private MonoBehaviour parent;
	
	public MapObjectNetworkSerializer(Moon     p) {this.parent = p;}
	public MapObjectNetworkSerializer(DGameMap p) {this.parent = p;}
	public MapObjectNetworkSerializer(Cruiser  p) {this.parent = p;}
	
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
		s.gameObject.AddComponent<DummyFlag>();
		if (parent is DGameMap map) {
			s.FindParent(map: map);
		} else {
			s.transform.parent = this.parent.transform;
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

public class EquipmentNetworkSerializer : MapObjectNetworkSerializer<Equipment> {
	public EquipmentNetworkSerializer(Moon     p) : base(p) {}
	public EquipmentNetworkSerializer(DGameMap p) : base(p) {}
	public EquipmentNetworkSerializer(Cruiser  p) : base(p) {}
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
		ScrapSerializer scrapSer = new ScrapSerializer((Moon)null);
		sc.Add((ushort)scrap.Length);
		foreach (Scrap s in scrap) {
			sc.AddInline(s,scrapSer);
		}
		
		Equipment[] equipment = cruiser.GetComponentsInChildren<Equipment>(true);
		EquipmentSerializer eqSer = new((Moon)null);
		sc.Add((ushort)equipment.Length);
		foreach (Equipment eq in equipment) {
			sc.AddInline(eq,eqSer);
		}
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
		
		dc.Consume(2).CastInto(out ushort count);
		var scrapSer = new ScrapSerializer(cruiser);
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(scrapSer);
		}
		
		dc.Consume(2).CastInto(out count);
		var eqSer = new EquipmentSerializer(cruiser);
		for (ushort i=0; i<count; i++) {
			dc.ConsumeInline(eqSer);
		}
		
		return cruiser;
	}
	
	public override Cruiser Deserialize(DeserializationContext dc) {
		Cruiser rt = null;
		if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost) {
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
		
		Scrap[] scrap = tgt.GetComponentsInChildren<Scrap>(true);
		sc.Add((ushort)scrap.Length);
		var scrapSerializer = new ScrapNetworkSerializer((Moon)null);
		foreach (Scrap s in scrap) {
			sc.AddInline(s,scrapSerializer);
		}
		
		Equipment[] equipment = tgt.GetComponentsInChildren<Equipment>(true);
		sc.Add((ushort)equipment.Length);
		var eqSerializer = new EquipmentNetworkSerializer((Moon)null);
		foreach (Equipment eq in equipment) {
			sc.AddInline(eq,eqSerializer);
		}
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
		
		dc.Consume(2).CastInto(out ushort numScrap);
		var scrapSerializer = new ScrapNetworkSerializer(tgt);
		for (ushort i=0; i<numScrap; i++) {
			dc.ConsumeInline(scrapSerializer);
		}
		
		dc.Consume(2).CastInto(out ushort numEquipment);
		var equipmentSerializer = new EquipmentNetworkSerializer(tgt);
		for (ushort i=0; i<numEquipment; i++) {
			dc.ConsumeInline(equipmentSerializer);
		}
		
		return tgt;
	}
	
	public override Cruiser Deserialize(DeserializationContext dc) {
		dc.Consume(sizeof(ulong)).CastInto(out ulong netobjid);
		NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[netobjid];
		netObj.gameObject.AddComponent<DummyFlag>();
		
		return Deserialize(netObj.GetComponent<Cruiser>(),dc);
	}
}