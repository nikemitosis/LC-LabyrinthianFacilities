namespace LabyrinthianFacilities.Util;

using System;
using System.Collections;
using System.Collections.Generic;

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
		} catch (ArgumentOutOfRangeException ex) {
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