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
	
	public Transform FindParent(DGameMap map=null, Moon moon=null, bool? includeInactive=null) {
		moon ??= map?.Moon ?? MapHandler.Instance.ActiveMoon;
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
			includeInactive ?? map.gameObject.activeInHierarchy
		)) {
			if (t.BoundingBox.Contains(this.transform.position)) {
				this.transform.parent = t.transform;
				noparentfound = false; break;
			}
		} if (noparentfound) {
			Plugin.LogFatal($"No tile parent found for {this.name} @ {this.transform.position}");
			Plugin.LogFatal($"(map @ {map.transform.position})");
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
			&& this.transform.parent?.GetComponent<VehicleController>() == null // exclude things on cruiser
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
		GameObject.Destroy(this.Grabbable.radarIcon?.gameObject);
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

public class Beehive : Scrap {
	
	private bool IsServer {
		get {return NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;}
	}
	
	// The sole purpose of this is to mark a bee swarm that is intended to be paired with an 
	// already-existing hive. 
	public class DummyFlag : MonoBehaviour {}
	
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
		if (this.GetComponentInParent<GameMap>() != null) {
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
			s.FindParent(moon: moon);
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