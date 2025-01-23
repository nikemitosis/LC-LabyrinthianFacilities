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

public class WeightedList<T> : IEnumerable<T> {
	
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
	
	public float SummedWeight {get {return summedWeight;}}
	
	private List<T> items;
	private List<float> weights;
	private float summedWeight;
	
	public WeightedList() {
		items = new();
		weights = new();
		summedWeight = 0.0f;
	}
	
	public int Count {get {return items.Count;}}
	
	public void Add(T item, float weight=1.0f) {
		this.items.Add(item);
		this.weights.Add(weight);
		summedWeight += weight;
	}
	
	public void Clear() {
		items.Clear();
		weights.Clear();
	}
	
	public bool Contains(T item) {
		return this.items.Contains(item);
	}
	
	public void CopyTo(T[] arr, int startIdx) {
		for (int idx=startIdx; idx<Count; idx++) {
			arr[idx] = items[idx];
		}
	}
	
	IEnumerator IEnumerable.GetEnumerator() {return GetEnumerator();}
	public IEnumerator<T> GetEnumerator() {
		return new ItemEnumerator(this);
	}
	
	public bool Remove(T item) {
		float w;
		return Remove(item,out w);
	}
	public bool Remove(T item, out float weight) {
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
	
	public T this[float index] { get {
		if (index < 0.0f || index > summedWeight) {
			throw new ArgumentOutOfRangeException(
				$"Index out of range ({index}, list size is {this.summedWeight})"
			);
		}
		
		for (int idx=0; idx<Count; idx++) {
			index -= weights[idx];
			if (index <= 0) return items[idx];
		}
		
		throw new ArgumentOutOfRangeException(
			$"Index out of range ({index}, list size is {this.summedWeight})"
		);
	}}
	
	public float this[T item] {get {
		return weights[items.IndexOf(item)];
	}}
}