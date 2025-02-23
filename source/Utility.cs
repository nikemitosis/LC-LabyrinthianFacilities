namespace LabyrinthianFacilities.Util;

using System;
using System.Collections;
using System.Collections.Generic;

public static class SerializationHelper {
	// Define our own GetBytes methods to avoid problems of endianness
	// + convenience of doing x.GetBytes() instead of BitConverter.GetBytes(x)
	
	public static byte[] GetBytes(this bool x) {
		return new byte[]{(byte)(x ? 1 : 0)};
	}
	
	// Little Endian
	public static byte[] GetBytes(this ushort x) {
		return new byte[]{
			(byte)(x >> 0),
			(byte)(x >> 8)
		};
	}
	
	// Little Endian
	public static byte[] GetBytes(this int x) {
		return new byte[]{
			(byte)(x >> 00),
			(byte)(x >> 08),
			(byte)(x >> 16),
			(byte)(x >> 24)
		};
	}
	
	public static byte[] GetBytes(this ulong x) {
		return new byte[]{
			(byte)(x >> 00),
			(byte)(x >> 08),
			(byte)(x >> 16),
			(byte)(x >> 24),
			(byte)(x >> 32),
			(byte)(x >> 40),
			(byte)(x >> 48),
			(byte)(x >> 56)
		};
	}
	
	public static byte[] GetBytes(this float x) {
		return BitConverter.GetBytes(x);
	}
	
	// UTF-8
	// Does not null-terminate!
	public static byte[] GetBytes(this string str) {
		if (str == null) return new byte[0];
		
		var rt = new byte[str.Length];
		for (int i=0; i<str.Length; i++) {
			if (str[i] > 0xFF) {
				throw new ArgumentOutOfRangeException(
					$"Cannot coerce value {(int)(str[i])} to UTF-8"
				);
			}
			rt[i] = (byte)(str[i]);
		}
		return rt;
	}
	
	public static void CastInto(this byte[] bytes, out string str) {
		str = System.Text.Encoding.UTF8.GetString(bytes);
	}
	public static void CastInto(this byte[] bytes, out bool o) {
		o = bytes[0] != 0;
	}
	public static void CastInto(this byte[] bytes, out ushort o) {
		o = (ushort)(bytes[1] << 8 | bytes[0]);
	}
	public static void CastInto(this byte[] bytes, out int o) {
		o = (
			  (int)bytes[3] << 24
			| (int)bytes[2] << 16
			| (int)bytes[1] << 08
			| (int)bytes[0] << 00
		);
	}
	public static void CastInto(this byte[] bytes, out ulong o) {
		o = (
			  (ulong)bytes[7] << 56
			| (ulong)bytes[6] << 48
			| (ulong)bytes[5] << 40
			| (ulong)bytes[4] << 32
			| (ulong)bytes[3] << 24
			| (ulong)bytes[2] << 16
			| (ulong)bytes[1] << 08
			| (ulong)bytes[0] << 00
		);
	}
	public static void CastInto(this byte[] bytes, out float o) {
		// Sometimes, bitconverter is nice after all
		o = BitConverter.ToSingle(bytes,0);
	}
}

public class WeightedList<T> : IEnumerable<T>, ICollection<T> {
	
	public sealed class ItemEnumerator : IEnumerator<T> {
		
		private int idx = -1;
		private WeightedList<T> list;
		
		public T Current {get {return list.items[idx];}}
		object IEnumerator.Current {get {return Current;}}
		
		public ItemEnumerator(WeightedList<T> list) {
			this.list = list;
			idx = -1;
		}
		
		public void Dispose() {return;}
		
		public bool MoveNext() {
			return ++idx < list.Count;
		}
		public void Reset() {
			idx = -1;
		}
	}
	
	public struct Entry {
		public T item;
		public float weight;
		
		public Entry(T item, float weight) {
			this.item = item;
			this.weight = weight;
		}
		
		public void Deconstruct(out T item, out float weight) {
			item = this.item;
			weight = this.weight;
		}
	}
	
	public float SummedWeight {get {return summedWeight;}}
	
	protected List<T> items;
	protected List<float> weights;
	protected float summedWeight;
	
	public int Count {get => items.Count;}
	public virtual bool IsReadOnly {get => false;}
	
	public virtual IEnumerable<Entry> Entries {get {
		for (int i=0; i<items.Count; i++) {
			yield return new Entry(items[i], weights[i]);
		}
	}}
	
	public WeightedList() {
		items = new();
		weights = new();
		summedWeight = 0.0f;
	}
	public WeightedList(WeightedList<T> copyFrom) {
		items = new(copyFrom.Count);
		weights = new(copyFrom.Count);
		summedWeight = copyFrom.summedWeight;
		foreach ((T item,float weight) in copyFrom.Entries) {
			items.Add(item);
			weights.Add(weight);
		}
	}
	
	public virtual bool Validate() {
		if (items.Count != weights.Count || items.Count != Count) {
			return false;
		}
		if (items.Count != 0 && summedWeight == 0.0f) {
			return false;
		}
		return true;
	}
	
	public void Add(T item) {Add(item,1.0f);}
	public virtual bool Add(T item, float weight) {
		if (weight <= 0.0f) return false;
		this.items.Add(item);
		this.weights.Add(weight);
		summedWeight += weight;
		return true;
	}
	
	public virtual void Clear() {
		items.Clear();
		weights.Clear();
		summedWeight = 0.0f;
	}
	
	public virtual bool Contains(T item) {
		return this.items.Contains(item);
	}
	
	public virtual void CopyTo(T[] arr, int startIdx) {
		for (int idx=startIdx; idx<Count; idx++) {
			arr[idx] = items[idx];
		}
	}
	
	IEnumerator IEnumerable.GetEnumerator() {return GetEnumerator();}
	public virtual IEnumerator<T> GetEnumerator() {
		return new ItemEnumerator(this);
	}
	
	public bool Remove(T item) {
		float w;
		return Remove(item,out w);
	}
	public virtual bool Remove(T item, out float weight) {
		try {
			int idx = items.IndexOf(item);
			items.RemoveAt(idx);
			weight = weights[idx];
			summedWeight -= weight;
			weights.RemoveAt(idx);
			return true;
		} catch (ArgumentOutOfRangeException) {
			weight = default(float);
			return false;
		}
	}
	
	public Entry GetByIndex(int index) {
		try {
			return new Entry(items[index], weights[index]);
		} catch (ArgumentOutOfRangeException) {
			throw;
		}
	}
	
	public virtual void SetWeightByIndex(int index, float weight) {
		if (index >= Count) throw new ArgumentOutOfRangeException($"index >= Count ({index} >= {Count})");
		if (weight <= 0.0f) throw new ArgumentException($"Cannot use weight <= 0.0 (was given {weight})");
		
		summedWeight += weight - weights[index];
		this.weights[index] = weight;
	}
	public virtual void SetItemByIndex(int index, T item) {
		if (index >= Count) throw new ArgumentOutOfRangeException($"index >= Count ({index} >= {Count})");
		
		this.items[index] = item;
	}
	
	public int InternalIndex(float index) {
		if (index == summedWeight) return Count-1;
		
		for (int idx=0; idx<Count; idx++) {
			index -= weights[idx];
			if (index < 0) return idx;
		}
		return -1;
	}
	
	public virtual T this[float index] { get {
		if (index < 0.0f || index > summedWeight || summedWeight == 0) {
			throw new ArgumentOutOfRangeException(
				$"Index out of range ({index}, list size is {this.summedWeight})"
			);
		}
		
		int idx = InternalIndex(index);
		if (idx != -1) return items[idx];
		
		throw new ArgumentOutOfRangeException(
			$"Index out of range ({index}, list size is {this.summedWeight})"
		);
	}}
	
	public virtual float this[T item] {get {
		int idx = items.IndexOf(item);
		if (idx == -1) {
			throw new ArgumentOutOfRangeException($"Item {item} not in list");
		}
		return weights[idx];
	} set {
		int idx = items.IndexOf(item);
		if (idx == -1) {
			throw new ArgumentOutOfRangeException($"Item {item} not in list");
		}
		SetWeightByIndex(idx,value);
	}}
}

public interface IChoice<TItem,TIndex> : ICollection<TItem> {
	public int OpenCount {get;}
	public int ClosedCount {get;}
	
	public TIndex OpenWidth {get;}
	public TIndex ClosedWidth {get;}
	
	public TItem Yield(TIndex idx);
	public void Reset();
}

public class ChoiceList<T> : IChoice<T,int> {
	
	public int Count {get => contents.Length;}
	
	public int OpenCount   {get; private set;}
	public int ClosedCount {get; private set;}
	public int OpenWidth   {get => OpenCount;}
	public int ClosedWidth {get => ClosedCount;}
	
	public bool IsReadOnly {get => true;}
	
	protected T[] contents;
	
	
	public ChoiceList(ICollection<T> source) {
		ClosedCount = 0;
		
		contents = new T[source.Count];
		foreach (T item in source) {
			contents[OpenCount++] = item;
		}
		
	}
	
	public T Yield(int idx) {
		if (idx >= OpenCount) throw new ArgumentOutOfRangeException();
		
		T rt = contents[idx];
		
		OpenCount--;
		ClosedCount++;
		
		contents[idx] = contents[^ClosedCount];
		contents[^ClosedCount] = rt;
		
		return rt;
	}
	
	public void Reset() {
		OpenCount = Count;
		ClosedCount = 0;
	}
	
	public bool Contains(T item) => ((IList<T>)contents).Contains(item);
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)contents).GetEnumerator();
	
	public void Add(T item) => throw new NotImplementedException();
	public void Clear() => throw new NotImplementedException();
	public void CopyTo(T[] arr, int startIdx) => throw new NotImplementedException();
	public bool Remove(T item) => throw new NotImplementedException();
}

public class WeightedChoiceList<T> : WeightedList<T>, IChoice<T,float> {
	public int OpenCount {get; protected set;}
	public int ClosedCount {get; protected set;}
	
	public float OpenWidth {get; protected set;}
	public float ClosedWidth {get; protected set;}
	
	public WeightedChoiceList() : base() {
		OpenCount = 0;
		ClosedCount = 0;
		OpenWidth = 0.0f;
		ClosedWidth = 0.0f;
	}
	public WeightedChoiceList(WeightedList<T> w) : base(w) {
		OpenCount = Count;
		ClosedCount = 0;
		OpenWidth = SummedWeight;
		ClosedWidth = 0.0f;
	}
	
	public override void Clear() {
		base.Clear();
		this.OpenCount = this.ClosedCount = 0;
		this.OpenWidth = this.ClosedWidth = 0;
	}
	
	public override bool Validate() {
		return base.Validate() && OpenCount+ClosedCount == Count;
	}
	
	public override bool Add(T item, float weight) {
		if (!base.Add(item,weight)) return false;
		OpenCount++;
		OpenWidth += weight;
		return true;
	}
	
	public override bool Remove(T item, out float weight) {
		int idx = this.items.IndexOf(item);
		bool rt = base.Remove(item,out weight);
		
		if (!rt) return false;
		
		if (idx >= OpenCount) {
			ClosedCount--;
			ClosedWidth -= weight;
		} else {
			OpenCount--;
			OpenWidth -= weight;
		}
		
		return true;
	}
	
	public override void SetWeightByIndex(int index, float weight) {
		float oldWeight;
		try {
			oldWeight = this.weights[index];
			base.SetWeightByIndex(index,weight);
		} catch (ArgumentException) {
			throw;
		}
		
		if (index < OpenCount) {
			OpenWidth += weight - oldWeight;
		} else {
			ClosedWidth += weight - oldWeight;
		}
	}
	
	public T Yield(float index) {
		int idx;
		if (index == OpenWidth) {
			idx = OpenCount-1;
		} else {
			idx = InternalIndex(index);
		}
		
		if (idx == -1 || idx >= OpenCount) {
			throw new ArgumentOutOfRangeException($"index out of range (gave {index}, length {OpenWidth})");
		}
		T rt = this.items[idx];
		float w = this.weights[idx];
		
		OpenCount--;
		ClosedCount++;
		OpenWidth -= w;
		ClosedWidth += w;
		this.items  [idx] = this.items  [OpenCount];
		this.weights[idx] = this.weights[OpenCount];
		
		this.items  [OpenCount] = rt;
		this.weights[OpenCount] = w;
		
		return rt;
	}
	
	public void Reset() {
		OpenCount = Count;
		ClosedCount = 0;
		
		OpenWidth = SummedWeight;
		ClosedWidth = 0.0f;
	}
}