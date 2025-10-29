namespace LabyrinthianFacilities.Serialization;

using LabyrinthianFacilities.Util;

using System;
using System.Collections.Generic;
using System.IO;

public interface ISerializer<out T> {
	// using object here instead of T sucks
	// I effectively want Action<Tile> to be assignable to Action<object>
	// but then if you call the action with a tile when it expects a map... (both are objects) :(
	public void Serialize(SerializationContext sc, object tgt); 
	
	public T Deserialize(DeserializationContext dc);
	
	// Called when deserialization is completely finished
	public void Finalize(object obj);
}

internal interface IAutocastSerializer<T> : ISerializer<T> {
	// To be implemented
	public void Serialize(SerializationContext sc, T tgt);
	public void Finalize(T tgt) {}
	
	// Auto-implemented
	void ISerializer<T>.Serialize(SerializationContext sc, object tgt) => Serialize(sc,(T)tgt);
	void ISerializer<T>.Finalize(object tgt) => Finalize((T)tgt);
}

// If you see an error thrown from a cast in this class, it means you used the wrong serializer on something!
public abstract class Serializer<T> : IAutocastSerializer<T> {
	public abstract void Serialize(SerializationContext sc, T tgt);
	
	// ISerializer.Deserialize
	// Intended to instantiate some kind of prefab or default object, and initialize it with above
	// Not intended to be called by inheritors, just for DeserializationContext
	public abstract T Deserialize(DeserializationContext dc);
	
	// The bulk of deserialization should occur here
	protected virtual T Deserialize(T baseObject, DeserializationContext dc) => baseObject;
	
	public virtual void Finalize(T tgt) {}
}

// should not be implemented directly
public interface IItemSerializer<out T> : ISerializer<T> {
	
	// To be implmeneted:
	public ICollectionSerializer GroupSerializer {get; set;}
	
	public void SerializePreamble(SerializationContext sc,object tgt);
	public void SerializeData    (SerializationContext sc,object tgt);
	
	public T DeserializePreamble(DeserializationContext dc);
	public T DeserializeData(object rt, DeserializationContext dc);
	
	// Auto-implemented
	public sealed bool InGroup {get => GroupSerializer?.ItemSerializer == this;}
	
	// can't use sealed
	void ISerializer<T>.Serialize(SerializationContext sc,object tgt) {
		if (!InGroup) SerializePreamble(sc,tgt);
		SerializeData(sc,tgt);
	}
	
	T ISerializer<T>.Deserialize(DeserializationContext dc) {
		T rt = InGroup ? (T)GroupSerializer.GetItemSkeleton() : DeserializePreamble(dc);
		return DeserializeData(rt,dc);
	}
}
// should not be implemented directly
public interface ICollectionSerializer {
	public bool IsValid {get => ItemSerializer?.GroupSerializer == this;}
	public IItemSerializer<object> ItemSerializer {get; set;}
	public object GetItemSkeleton();
}
public interface IAutocastCollectionSerializer<T> : ICollectionSerializer {
	// To be implemented:
	public new IItemSerializer<T> ItemSerializer {get; set;}
	public new T GetItemSkeleton();
	
	
	// Auto-implemented
	IItemSerializer<object> ICollectionSerializer.ItemSerializer {
		get => (IItemSerializer<object>)ItemSerializer;
		set => ItemSerializer = (IItemSerializer<T>)value;
	}
	object ICollectionSerializer.GetItemSkeleton() => GetItemSkeleton();
}

internal interface IAutocastItemSerializer<T> : IItemSerializer<T> {
	// To be implemented:
	public void SerializePreamble(SerializationContext sc, T tgt);
	public void SerializeData    (SerializationContext sc, T tgt);
	public T DeserializeData(T rt,DeserializationContext dc);
	public void Finalize(T tgt) {}
	
	
	// Auto-implemented
	void IItemSerializer<T>.SerializePreamble(SerializationContext sc, object tgt) {
		this.SerializePreamble(sc,(T)tgt);
	}
	void IItemSerializer<T>.SerializeData(SerializationContext sc, object tgt) {
		this.SerializeData(sc,(T)tgt);
	}
	T IItemSerializer<T>.DeserializeData(object rt, DeserializationContext dc) {
		return this.DeserializeData((T)rt,dc);
	}
	void ISerializer<T>.Finalize(object t) => this.Finalize((T)t);
}

public abstract class ItemSerializer<T> : IAutocastItemSerializer<T> {
	
	// IItemSerializer implementation
	public ICollectionSerializer GroupSerializer {get; set;}
	
	// IAutocastItemSerializer "implementation"
	public abstract void SerializePreamble(SerializationContext sc,T tgt);
	public abstract void SerializeData    (SerializationContext sc,T tgt);
	
	public abstract T DeserializePreamble(     DeserializationContext dc);
	public abstract T DeserializeData    (T rt,DeserializationContext dc); 
	// ^ return value is not going ref-equals rt for value types
}

// Intended to be used if you have several elements with the same preamble
public abstract class CollectionSerializer<T> : ItemSerializer<ICollection<T>>, IAutocastCollectionSerializer<T> {
	public IItemSerializer<T> ItemSerializer {get; set;} = null;
	
	public virtual int Count {get; protected set;}
	
	public virtual void Init(IItemSerializer<T> ser) {
		if (ser != null) ser.GroupSerializer = this;
		ItemSerializer = ser;
	}
	
	protected virtual void PreserializeStep(SerializationContext sc, ICollection<T> tgt) {}
	public sealed override void SerializePreamble(SerializationContext sc, ICollection<T> tgt) {
		PreserializeStep(sc,tgt);
		
		foreach (T item in tgt) {
			// dont use SerializeInline because we don't want to register this as a referable object yet
			ItemSerializer.SerializePreamble(sc,item); 
			break;
		}
		sc.Add(tgt.Count);
	}
	
	public sealed override void SerializeData(SerializationContext sc, ICollection<T> tgt) {
		foreach (T item in tgt) {
			sc.AddInline(item,(IItemSerializer<object>)ItemSerializer);
		}
	}
	
	// not sealed in case inheritors want to return a dictionary, for example, instead of a List
	// or if they want to use a different method to derive Count
	public override ICollection<T> DeserializePreamble(DeserializationContext dc) {
		DeserializeSharedPreamble(dc);
		dc.Consume(sizeof(int)).CastInto(out int count);
		this.Count = count;
		return new List<T>(count);
	}
	// not sealed in case inheritors do not know Count from DeserializePreamble 
	// (e.g. something similar to null-terminated string)
	public override ICollection<T> DeserializeData(ICollection<T> rt, DeserializationContext dc) {
		for (int i=0; i<Count; i++) {
			rt.Add((T)dc.ConsumeInline((IItemSerializer<object>)ItemSerializer));
		}
		return rt;
	}
	
	protected abstract void DeserializeSharedPreamble(DeserializationContext dc);
	
	// Create a skeleton item for ItemSerializer.DeserializeData to fill
	public abstract T GetItemSkeleton();
	object ICollectionSerializer.GetItemSkeleton() => GetItemSkeleton();
}

public sealed class SerializationContext {
	
	public static bool Verbose = false;
	
	private class ReferenceInfo {
		public List<int> requests = new();
		public ISerializer<object> serializer;
	}
	
	private List<byte> output;
	private Dictionary<object, int> references;
	private Dictionary<object, ReferenceInfo> queuedReferences;
	
	public IList<byte> Output {get {return output.AsReadOnly();}}
	public int Address {get => output.Count+1;}
	
	public SerializationContext() {
		this.output = new();
		this.references = new();
		this.queuedReferences = new();
	}
	
	public IList<byte> Serialize(object tgt, ISerializer<object> ser) {
		AddInline(tgt, ser);
		while (queuedReferences.Count != 0) {
			object newtgt = null;
			ReferenceInfo refInfo = null;
			foreach (var entry in queuedReferences) {
				newtgt = entry.Key;
				refInfo = entry.Value;
				break;
			}
			if (Verbose) Plugin.LogDebug($"A {newtgt ?? "null"} | {refInfo.serializer}");
			AddInline(newtgt, refInfo.serializer);
		}
		
		return Output;
	}
	
	public void Add(byte i) {
		output.Add(i);
	}
	public void Add(IEnumerable<byte> bytes) {
		output.AddRange(bytes);
	}
	public void Add(bool   i) {Add(i.GetBytes());}
	public void Add(ushort i) {Add(i.GetBytes());}
	public void Add(int    i) {Add(i.GetBytes());}
	public void Add(ulong  i) {Add(i.GetBytes());}
	public void Add(float  i) {Add(i.GetBytes());}
	public void Add(string i) {Add(i.GetBytes());}
	
	public ulong AddBools<T>(IEnumerable<T> items, Func<T,bool> transformer) {
		byte packer = 0;
		byte packerProgress = 0;
		ulong total = 0;
		foreach (T t in items) {
			//  first bool is in first byte, LSB
			// eighth bool is in first byte, MSB
			if (transformer(t)) packer |= (byte)(1 << packerProgress);
			packerProgress++;
			total++;
			
			if (packerProgress == 8) {
				this.Add(packer);
				packer = 0;
				packerProgress = 0;
			}
		}
		if (packerProgress != 0) {
			this.Add(packer);
		}
		
		return total;
	}
	
	public void AddInline(object tgt, ISerializer<object> ser) {
		if (Verbose) Plugin.LogDebug($"I 0x{Address:X} | {tgt ?? "null"} | {ser}");
		if (references.ContainsKey(tgt)) {
			throw new InvalidOperationException($"Cannot have two references to the same object ({tgt})");
		}
		references.Add(tgt,Address);
		if (queuedReferences.TryGetValue(tgt,out ReferenceInfo refInfo)) {
			foreach (int address in refInfo.requests) {
				this.output[address+0] = (byte)(this.Address >> 00);
				this.output[address+1] = (byte)(this.Address >> 08);
				this.output[address+2] = (byte)(this.Address >> 16);
				this.output[address+3] = (byte)(this.Address >> 24);
			}
			queuedReferences.Remove(tgt);
			
			ser = GetBetterSerializer(refInfo.serializer, ser);
		}
		if (ser == null) throw new ArgumentNullException($"No serializer provided for {tgt}");
		ser.Serialize(this, tgt);
		
	}
	
	private readonly byte[] REFERENCE_PLACEHOLDER = [1,2,3,4];
	private readonly byte[] NULL_BYTES = [0,0,0,0];
	public void AddReference(object refTo, ISerializer<object> ser) {
		if (Verbose) Plugin.LogDebug($"R {refTo ?? "null"} | {ser}");
		if (refTo == null) {
			this.Add(NULL_BYTES);
		} else if (references.TryGetValue(refTo, out int addr)) {
			this.Add(addr.GetBytes());
		} else {
			this.QueueReference(refTo,ser);
			this.Add(REFERENCE_PLACEHOLDER);
		}
	}
	
	private ISerializer<object> GetBetterSerializer(ISerializer<object> a, ISerializer<object> b) {
		if (a == null) return b;
		if (b == null) return a;
		
		Type A = a.GetType();
		Type B = b.GetType();
		if (A == B) return a;
		if (A.IsSubclassOf(B)) return a;
		if (B.IsSubclassOf(A)) return b;
		
		throw new ArgumentException(
			$"Serializers {a} and {b} are not compatible. \n"
			+$"If this is a simple mistake, one should inherit from the other. "
		);
	}
	
	private void QueueReference(object refTo, ISerializer<object> ser) {
		if (!queuedReferences.TryGetValue(refTo, out ReferenceInfo refInfo)) {
			refInfo = new();
			queuedReferences.Add(refTo, refInfo);
		}
		
		refInfo.serializer = GetBetterSerializer(refInfo.serializer,ser);
		
		refInfo.requests.Add(output.Count);
	}
	
	public void Clear() {
		this.output.Clear();
		this.output.Add(0);
		this.references.Clear();
		this.queuedReferences.Clear();
	}
	
	public void SaveToFile(FileStream fs) {
		foreach (byte b in Output) {
			fs.WriteByte(b);
		}
	}
}

internal class ReferenceInfo {
	public ISerializer<object> deserializer = null;
	public List<Action<object>> actions = new();
}

// Relies on the assumption that data before an object will identify it
// i.e. we will know what an object should be before we come across it in the bytestream
// Finalizers are called in the same order of when an object appears in the file
// i.e. the first thing deserialized will be the last thing that has its finalizer called
public sealed class DeserializationContext {
	public static bool Verbose = false;
	
	private byte[] data;
	
	private Dictionary<int, object> references;
	private List<(object obj, Action<object> action)> finalizers;
	private Dictionary<int, ReferenceInfo> unresolvedReferences;
	private int address;
	
	public ReadOnlySpan<byte> Data {get => data;}
	public int Address {
		get => address;
		private set => address = value;
	}
	
	public DeserializationContext(ReadOnlySpan<byte> data) {
		this.data = new byte[data.Length+1];
		this.data[0] = 0;
		data.CopyTo(((Span<byte>)this.data).Slice(1));
		
		this.references = new();
		this.finalizers = new();
		this.unresolvedReferences = new();
		this.address = 1;
	}
	
	public object Deserialize(ISerializer<object> rootDeserializer) {
		if (Verbose) Plugin.LogDebug($"Deserializing with {Data.Length-1} bytes");
		object rt = ConsumeInline(rootDeserializer);
		
		if (address != data.Length) {
			Plugin.LogWarning(
				$"Deserialization did not consume entire bytestream. "
				+$"Is there something we didn't load or are we wasting space on file?"
			);
		}
		
		foreach ((var obj, var action) in this.finalizers) {
			try {
				if (Verbose) Plugin.LogDebug($"Calling finalizer for {obj}");
				action.Invoke(obj);
			} catch (Exception e) {
				Plugin.LogError($"Deserialization finalizer threw an exception: \n{e}");
				throw;
			}
		}
		
		this.references.Clear();
		this.finalizers.Clear();
		this.unresolvedReferences.Clear();
		this.address = 1;
		return rt;
	}
	
	// Consume <length> bytes
	public byte[] Consume(int length) {
		byte[] rt;
		try {
			rt = Data.Slice(address,length).ToArray();
		} catch (ArgumentOutOfRangeException) {
			Plugin.LogError(
				$"Cannot consume beyond the bytestream;\n"
				+$"Address + request > bytestream.Length ({Address:X} + {length:X} > {Data.Length:X})"
			);
			throw;
		}
		address += length;
		return rt;
	}
	
	// Consume bytes until the next one would satisfy the given condition
	public byte[] ConsumeUntil(Func<byte, bool> condition) {
		int length = 0;
		try {
			while (!condition(data[this.address + length])) {
				length++;
			}
		} catch (ArgumentOutOfRangeException) {
			Plugin.LogError(
				$"Cannot consume beyond the bytestream;\n"
				+$"Address + request > bytestream.Length ({Address:X} + {length:X} > {Data.Length:X})"
			);
			throw;
		}
		return this.Consume(length);
	}
	
	public IEnumerable<bool> ConsumeBools(ulong count) {
		byte curByte = 0;
		for (ulong i=0; i<count; i++) {
			if (i % 8 == 0) {
				try {
					curByte = this.Consume(1)[0];
				} catch (ArgumentOutOfRangeException) {
					throw;
				}
			}
			yield return (curByte & 0x01) != 0;
			curByte >>= 1;
		}
	}
	
	// Consumes 4 bytes, and queues resolving of the reference if occurs later in the bytestream, otherwise
	// immediately resolves the reference
	public int ConsumeReference(
		ISerializer<object> deserializer, 
		Action<object> action=null
	) {
		int addr;
		try {
			this.Consume(4).CastInto(out addr);
		} catch (ArgumentOutOfRangeException) {
			throw;
		}
		
		// Already resolved
		if (addr == 0) return 0;
		if (references.TryGetValue(addr, out object obj)) {
			action?.Invoke(obj);
			return addr;
		}
		
		if (addr < address) {
			throw new InvalidOperationException(
				$"How did we get a reference request to an object we haven't seen "
				+$"which is supposed to be at a place earlier in the bytestream? "
				+$"Did you consume the beginning of a referenced object?\n"
				+$"(Object address: 0x{addr:X} | Current address: 0x{address:X} | "
				+$"Deserializer: {deserializer.GetType()})"
			);
		}
		if (Verbose) Plugin.LogDebug($"Q 0x{addr:X} | {deserializer.GetType()}");
		if (addr >= Data.Length) {
			Plugin.LogError(
				$"Queued reference lays beyond the length of the bytestream ({addr} >= {Data.Length})"
			);
		throw new ArgumentOutOfRangeException($"addr: {addr} >= {Data.Length}");
		}
		
		if (!unresolvedReferences.TryGetValue(addr,out ReferenceInfo refInfo)) {
			refInfo = new();
			unresolvedReferences.Add(addr,refInfo);
		}
		
		if (
			refInfo.deserializer == null 
			|| deserializer.GetType().IsSubclassOf(refInfo.deserializer.GetType())
		) {
			refInfo.deserializer = deserializer;
		} else if (
			deserializer.GetType() != refInfo.deserializer.GetType() 
			&& !refInfo.deserializer.GetType().IsSubclassOf(deserializer.GetType())
		) {
			throw new ArgumentException(
				$"Deserializers {refInfo.deserializer.GetType()} and {deserializer.GetType()} "
				+$"for address {address} are not compatible. \n"
				+$"If this is a simple mistake, one should inherit from the other; "
				+$"One object representing two sibling/unrelated types is not currently supported. "
			);
		}
		
		if (action != null) refInfo.actions.Add(action);
		
		return addr;
	}
	
	public object ConsumeInline(ISerializer<object> deserializer) {
		if (deserializer == null) {
			throw new ArgumentNullException(
				$"No deserializer provided for object at address 0x{address:X}"
			);
		}
		
		if (Verbose) Plugin.LogDebug($"L 0x{address:X} | {deserializer.GetType()}");
		int addr = address;
		int finalizerIdx = finalizers.Count;
		finalizers.Add(default);
		object rt;
		try {
			rt = deserializer.Deserialize(this);
		} catch (Exception e) {
			Plugin.LogError($"Deserializer {deserializer} threw an exception at 0x{Address:X}: {e}");
			throw;
		}
		finalizers[finalizerIdx] = (rt, deserializer.Finalize);
		AddReference(addr, rt);
		if (unresolvedReferences.TryGetValue(address, out ReferenceInfo refInfo)) {
			ConsumeInline(refInfo.deserializer);
		}
		return rt;
	}
	
	public object GetReference(int address) {
		return references[address];
	}
	
	public void AddAction(int address, Action<object> action) {
		if (references.TryGetValue(address, out object tgt)) {
			action(tgt);
			return;
		}
		if (!unresolvedReferences.TryGetValue(address, out ReferenceInfo refInfo)) {
			throw new KeyNotFoundException(
				$"Address 0x{address:X} has no reference associated with it. "
				+$"Use ConsumeReference instead of Consume to register a reference. "
			);
		}
		refInfo.actions.Add(action);
	}
	
	private void AddReference(int address, object refr) {
		if (Verbose) Plugin.LogDebug($"A 0x{address:X} | {refr}");
		references.Add(address,refr);
		if (!unresolvedReferences.TryGetValue(address,out ReferenceInfo refInfo)) return;
		foreach (Action<object> action in refInfo.actions) {
			action?.Invoke(refr);
		}
		unresolvedReferences.Remove(address);
	}
}