namespace LabyrinthianFacilities;

using System.Collections.Generic;

using UnityEngine;

using Serialization;
using Util;

public class MapObject : MonoBehaviour, ISerializable {
	public GrabbableObject Grabbable {get {
		return this.GetComponent<GrabbableObject>();
	}}
	
	public void FindParent(GameMap map=null) {
		if (this.Grabbable.isInShipRoom) return;
		map ??= MapHandler.Instance.ActiveMap;
		
		bool noparentfound = true;
		foreach (Tile t in map.GetComponentsInChildren<Tile>(
			includeInactive: !map.gameObject.activeInHierarchy
		)) {
			if (t.BoundingBox.Contains(this.transform.position)) {
				this.transform.parent = t.transform;
				noparentfound = false; break;
			}
		} if (noparentfound) {
			this.transform.parent = map.transform;
		}
		this.Grabbable.targetFloorPosition 
			= this.Grabbable.startFallingPosition 
			= this.transform.localPosition;
	}
	
	public virtual void Preserve() {
		var grabbable = this.Grabbable;
		grabbable.isInShipRoom = StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(
			grabbable.transform.position
		); // fix isInShipRoom for people joining partway through a save
		
		this.FindParent();
		
		if (!grabbable.isInShipRoom) this.gameObject.SetActive(false);
	}
	
	public virtual void Restore() {
		this.gameObject.SetActive(true);
		var grabbable = this.Grabbable;
	}
	
	/* Format:
	 * Identifier: string
	 * localPosition: Vector3 
	 *   (relative position to *GameMap*, not to parent)
	*/
	public virtual IEnumerable<SerializationToken> Serialize() {
		yield return new SerializationToken(
			this.name.Substring(0,this.name.Length - "(Clone)".Length).GetBytes(),
			isStartOf: this
		);
		yield return new byte[]{(byte)0};
		
		yield return this.transform.position.x.GetBytes();
		yield return (this.transform.position.y + 200f).GetBytes();
		yield return this.transform.position.z.GetBytes();
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
	
	/* Format:
	 *     base
	 *     ScrapValue: int
	*/
	public override IEnumerable<SerializationToken> Serialize() {
		foreach (var t in base.Serialize()) {
			yield return t;
		}
		yield return this.Grabbable.scrapValue.GetBytes();
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
public abstract class MapObjectDeserializer<T> : IDeserializer<T> where T : MapObject {
	public abstract T GetPrefab(string id);
	
	public virtual T Deserialize(ISerializable baseObj, DeserializationContext dc, object extraContext=null) {
		T rt = (T)baseObj;
		GameMap parentMap = (GameMap)extraContext;
		
		dc.Consume(sizeof(float)).CastInto(out float x);
		dc.Consume(sizeof(float)).CastInto(out float y);
		dc.Consume(sizeof(float)).CastInto(out float z);
		rt.transform.localPosition = new Vector3(x,y,z);
		
		rt.FindParent(parentMap);
		return rt;
	}
	public T Deserialize(DeserializationContext dc, object extraContext=null) {
		dc.ConsumeUntil((byte b) => b == 0).CastInto(out string name);
		dc.Consume(1); // null terminator
		T rt = Object.Instantiate(GetPrefab(name));
		return Deserialize(rt, dc,extraContext);
	}
}

public class ScrapDeserializer : MapObjectDeserializer<Scrap> {
	public override Scrap GetPrefab(string id) => Scrap.GetPrefab(id);
	
	public override Scrap Deserialize(
		ISerializable baseObj, DeserializationContext dc, object extraContext=null
	) {
		Scrap rt = base.Deserialize(baseObj,dc,extraContext);
		
		dc.Consume(4).CastInto(out int scrapValue);
		rt.Grabbable.SetScrapValue(scrapValue);
		
		return rt;
	}
}

public class EquipmentDeserializer : MapObjectDeserializer<Equipment> {
	public override Equipment GetPrefab(string id) => Equipment.GetPrefab(id);
}