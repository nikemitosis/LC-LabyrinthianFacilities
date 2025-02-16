namespace BoundsExtensions;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

// Axis-aligned rectangle in 3d-space
struct RectFace {
	private int zeroIdx;
	private Bounds _bounds;
	private bool negative=false;
	
	public Bounds bounds {get {return _bounds;}}
	
	public Vector3 perpindicular {
		get {
			return new Vector3(
				zeroIdx==0 ? (negative ? -1:1):0, 
				zeroIdx==1 ? (negative ? -1:1):0, 
				zeroIdx==2 ? (negative ? -1:1):0
			);
		}
	}
	
	public RectFace(Vector3 min, Vector3 max, bool negative=false) {
		Vector3 size = max - min;
		bool forElse = true;
		for (int i=0; i<3; i++) {
			if (size[i] == 0) {
				forElse = false;
				this.zeroIdx = i;
				break;
			}
		} if (forElse) {
			throw new ArgumentException("RectFace must have zero width in at least one dimension");
		}
		
		this._bounds = new Bounds(0.5f*(min+max),max-min);
		this._bounds.FixExtents();
		this.negative = negative;
	}
	
	public static bool IsAcceptableParams(Vector3 min, Vector3 max) {
		Vector3 v = max - min;
		for (int i=0; i<3; i++) {
			if (v[i] == 0) return true;
		}
		return false;
	}
}

static class BoundsExtension {
	public static void FixExtents(this ref Bounds ths) {
		Vector3 ext = ths.extents;
		for (int i=0; i<3; i++) {
			ext[i] = Math.Abs(ext[i]);
		}
		ths.extents = ext;
	}
	
	// Always returns 6 faces... because its a rect prism. Wow. 
	// Faces' perpindicular property all point *outside* the bounding box
	public static RectFace[] GetFaces(this Bounds ths) {
		RectFace[] rt = new RectFace[6];
		Vector3 a = ths.center + ths.extents;
		rt[0] = new RectFace( //+x
			a, ths.center + Vector3.Scale(ths.extents,new Vector3( 1,-1,-1))
		);
		rt[1] = new RectFace( //+y
			a, ths.center + Vector3.Scale(ths.extents,new Vector3(-1, 1,-1))
		);
		rt[2] = new RectFace( //+z
			a, ths.center + Vector3.Scale(ths.extents,new Vector3(-1,-1, 1))
		);
		a = ths.center - ths.extents;
		rt[3] = new RectFace( //-x
			a, ths.center + Vector3.Scale(ths.extents,new Vector3(-1, 1, 1)), true
		);
		rt[4] = new RectFace( //-y
			a, ths.center + Vector3.Scale(ths.extents,new Vector3( 1,-1, 1)), true
		);
		rt[5] = new RectFace( //-z
			a, ths.center + Vector3.Scale(ths.extents,new Vector3( 1, 1,-1)), true
		);
		
		return rt;
	}
	
	public static RectFace ClosestFace(this Bounds ths,Vector3 point) {
		RectFace[] faces = ths.GetFaces();
		
		RectFace closest = faces[0];
		float closest_dist = closest.bounds.SqrDistance(point);
		for (int i=1; i<6; i++) {
			RectFace f = faces[i];
			float dist = f.bounds.SqrDistance(point);
			if (dist < closest_dist) {
				closest = f;
				closest_dist = dist;
				if (dist == 0) return closest;
			}
		}
		return closest;
	}
	
	public static Quaternion AwayRotation(this Bounds ths, Vector3 point) {
		RectFace closest = ths.ClosestFace(point);
		Vector3 dir = closest.perpindicular;
		
		return Quaternion.LookRotation(dir);
	}
	
	public static IEnumerable<Vector3> Vertices(this Bounds ths) {
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3(-1,-1,-1));
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3(-1,-1, 1));
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3(-1, 1,-1));
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3(-1, 1, 1));
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3( 1,-1,-1));
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3( 1,-1, 1));
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3( 1, 1,-1));
		yield return ths.center + Vector3.Scale(ths.extents, new Vector3( 1, 1, 1));
	}
}

// Does not support bounds moving after they have been added!
public class BoundsMap<T> : ICollection<T> where T : class {
	private Vector3 center;
	public Vector3 Center {
		get => center; 
		protected set {center = value; bounds.center = value;}
	}
	
	private float radius;
	public float Radius {
		get => radius; 
		protected set {radius = value; bounds.extents = value*Vector3.one;}
	}
	
	private Bounds bounds;
	public Bounds Bounds {get => bounds;}
	
	private BoundsMap<T>[] Segments;
	
	public int Count {get; private set;}
	private T[] Items;
	
	// this function should not modify the bounds of the item it measures!
	protected Func<T,Bounds> ItemBounds;
	
	public bool HasSplit {get => Segments != null;}
	
	public bool IsReadOnly {get => false;}
	
	public BoundsMap(
		float radius, Func<T,Bounds> itemBounds
	) : this(
		Vector3.zero, radius, itemBounds
	) {}
	
	public BoundsMap(
		Vector3 center, float radius, Func<T,Bounds> itemBounds
	) : this(
		Vector3.zero, radius, itemBounds, 8
	) {}
	
	public BoundsMap(Vector3 center, float radius, Func<T,Bounds> itemBounds, int capacity) {
		this.bounds = new Bounds(center,2*radius*Vector3.one);
		this.center = center;
		this.radius = radius;
		
		this.ItemBounds = itemBounds;
		
		this.Segments = null;
		this.Items = new T[capacity];
	}
	
	// You shouldnt use 'T?' because T is a reference type, so we'll just change that to T for you
	// Oh no! You can't use T there, what are you doing? 
	// T might be a value type, you can't return null for that!
	// smfh, I guess structs aren't welcome here
	public T GetFirstIntersection(Bounds bounds) {
		if (!this.Bounds.Intersects(bounds)) return null;
		
		if (!HasSplit) {
			for (int i=0; i<Count; i++) {
				T item = Items[i];
				if (ItemBounds(item).Intersects(bounds)) return item;
			}
		} else {
			foreach (BoundsMap<T> segment in Segments) {
				T rt = segment.GetFirstIntersection(bounds);
				if (rt != null) return rt;
			}
		}
		return null;
	}
	
	public T GetFirstIntersection(T item) => GetFirstIntersection(ItemBounds(item));
	
	public bool Intersects(T item) => GetFirstIntersection(ItemBounds(item)) != null;
	public bool Intersects(Bounds bounds) => GetFirstIntersection(bounds) != null;
	
	public void Add(T item) {
		Bounds itemBounds = ItemBounds(item);
		if (!this.Bounds.Intersects(itemBounds)) {
			throw new ArgumentException(
				$"Item {item} out of range of this BoundsMap which has bounds {this.Bounds}"
			);
		}
		if (HasSplit) {
			Add(item, itemBounds);
			Count++;
			return;
		}
		Items[Count++] = item;
		if (Count == Items.Length) Split();
	}
	
	private void Add(T item, Bounds bounds) {
		if (!HasSplit) {
			Add(item);
			return;
		}
		foreach (BoundsMap<T> segment in Segments) {
			if (segment.Bounds.Intersects(bounds)) {
				segment.Add(item,bounds);
			}
		}
	}
	
	public bool Remove(T item) {
		if (!HasSplit) {
			for (int i=0; i<Count; i++) {
				if (Items[i]?.Equals(item) ?? item?.Equals(Items[i]) ?? true) {
					Items[i] = Items[--Count];
					return true;
				}
			}
			return false;
		} else {
			bool rt = false;
			foreach (var segment in Segments) {
				rt = rt || segment.Remove(item);
			}
			if (rt) Count--;
			return rt;
		}
	}
	
	public void Clear() {
		Segments = null;
		Count = 0;
	}
	
	public bool Contains(T item) {
		if (!HasSplit) {
			for (int i=0; i<Count; i++) {
				if (Items[i].Equals(item)) {
					return true;
				}
			}
			return false;
		} else {
			foreach (var segment in Segments) {
				if (segment.Contains(item)) return true;
			}
			return false;
		}
	}
	
	
	private void Split() {
		Segments = new BoundsMap<T>[8];
		float subradius = Radius/2.0f;
		var foo = (int a,int b, int c) => Center + subradius*new Vector3(a,b,c);
		Segments[0] = new BoundsMap<T>(foo(-1,-1,-1),subradius,ItemBounds,Items.Length);
		Segments[1] = new BoundsMap<T>(foo(-1,-1, 1),subradius,ItemBounds,Items.Length);
		Segments[2] = new BoundsMap<T>(foo(-1, 1,-1),subradius,ItemBounds,Items.Length);
		Segments[3] = new BoundsMap<T>(foo(-1, 1, 1),subradius,ItemBounds,Items.Length);
		Segments[4] = new BoundsMap<T>(foo( 1,-1,-1),subradius,ItemBounds,Items.Length);
		Segments[5] = new BoundsMap<T>(foo( 1,-1, 1),subradius,ItemBounds,Items.Length);
		Segments[6] = new BoundsMap<T>(foo( 1, 1,-1),subradius,ItemBounds,Items.Length);
		Segments[7] = new BoundsMap<T>(foo( 1, 1, 1),subradius,ItemBounds,Items.Length);
		
		foreach (T item in Items) {
			Add(item);
		}
	}
	
	public void CopyTo(T[] arr, int startIdx) {
		throw new NotImplementedException("Nope.");
	}
	
	private class BoundsMapEnumerator : IEnumerator<T> {
		private BoundsMap<T> boundsMap;
		private BoundsMapEnumerator subEnumerator;
		private int index;
		
		object IEnumerator.Current {get => Current;}
		public T Current {get; private set;}
		
		public BoundsMapEnumerator(BoundsMap<T> boundsMap) {
			this.boundsMap = boundsMap;
			this.Reset();
		}
		
		public void Dispose() {}
		
		public bool MoveNext() {
			if (boundsMap.HasSplit) {
				while (!subEnumerator.MoveNext()) {
					if (++index == boundsMap.Segments.Length) return false;
					subEnumerator = (BoundsMapEnumerator)boundsMap.Segments[index].GetEnumerator();
				}
				Current = subEnumerator.Current;
			} else {
				if (index == boundsMap.Count) return false;
				Current = boundsMap.Items[index++];
			}
			return true;
		}
		
		public void Reset() {
			if (boundsMap.HasSplit) {
				this.subEnumerator = (BoundsMapEnumerator)boundsMap.Segments[0].GetEnumerator();
			}
			this.index = 0;
		}
	}
	
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public IEnumerator<T> GetEnumerator() {
		return new BoundsMapEnumerator(this);
	}
}