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
	
	public T Deserialize(DeserializationContext dc, object extraContext=null);
	
	public void Finalize(object obj);
}

// If you see an error thrown from a cast in this class, it means you used the wrong serializer on something!
public abstract class Serializer<T> : ISerializer<T> {
	public void Serialize(SerializationContext sc, object tgt) {Serialize(sc,(T)tgt);}
	
	public abstract void Serialize(SerializationContext sc, T tgt);
	
	// ISerializer.Deserialize
	// Intended to instantiate some kind of prefab or default object, and initialize it with above
	// Not intended to be used by inheritors, just for DeserializationContext
	public abstract T Deserialize(DeserializationContext dc, object extraContext=null);
	
	// The bulk of deserialization should occur here
	protected abstract T Deserialize(T baseObject, DeserializationContext dc, object extraContext=null);
	
	
	// ISerializer.Finalize
	// Called when deserialization is completely finished
	public void Finalize(object obj) {this.Finalize((T)obj);}
	
	public virtual void Finalize(T obj) {}
}

public sealed class SerializationContext {
	
	private class ReferenceInfo {
		public List<int> requests = new();
		public ISerializer<object> serializer;
	}
	
	private List<byte> output;
	private Dictionary<object, int> references;
	private Dictionary<object, ReferenceInfo> queuedReferences;
	
	public IList<byte> Output {get {return output.AsReadOnly();}}
	
	public SerializationContext() {
		this.output = new();
		this.output.Add(0);
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
			#if VERBOSE_SERIALIZE
			Plugin.LogDebug($"A {newtgt ?? "null"} | {refInfo.serializer}");
			#endif
			AddInline(newtgt, refInfo.serializer);
		}
		
		return Output;
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
	
	
	public void AddInline(object tgt, ISerializer<object> ser) {
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"I {tgt ?? "null"} | {ser}");
		#endif
		if (references.ContainsKey(tgt)) {
			throw new InvalidOperationException($"Cannot have two references to the same object ({tgt})");
		}
		references.Add(tgt,output.Count);
		if (queuedReferences.TryGetValue(tgt,out ReferenceInfo refInfo)) {
			foreach (int address in refInfo.requests) {
				this.output[address+0] = (byte)(output.Count >> 00);
				this.output[address+1] = (byte)(output.Count >> 08);
				this.output[address+2] = (byte)(output.Count >> 16);
				this.output[address+3] = (byte)(output.Count >> 24);
			}
			queuedReferences.Remove(tgt);
			
			GetBetterSerializer(refInfo.serializer, ser).Serialize(this, tgt);
		} else {
			ser.Serialize(this, tgt);
		}
		
	}
	
	private readonly byte[] REFERENCE_PLACEHOLDER = [1,2,3,4];
	private readonly byte[] NULL_BYTES = [0,0,0,0];
	public void AddReference(object refTo, ISerializer<object> ser) {
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"R {refTo ?? "null"} | {ser}");
		#endif
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
	public object context = null;
}

// Relies on the assumption that data before an object will identify it
// i.e. we will know what an object should be before we come across it in the bytestream
// Finalizers are called in reverse-order of when an object appears in the file
// i.e. the first thing deserialized will be the last thing that has its finalizer called
public sealed class DeserializationContext {
	private byte[] data;
	
	private Dictionary<int, object> references;
	private List<(object obj, Action<object> action)> finalizers;
	private Dictionary<int, ReferenceInfo> unresolvedReferences;
	private int address;
	
	private ReadOnlySpan<byte> Data {get {return data;}}
	public int Address {
		get {return address;}
		private set {address = value;}
	}
	
	public DeserializationContext(byte[] data) {
		this.data = data;
		
		this.references = new();
		this.finalizers = new();
		this.unresolvedReferences = new();
		this.address = 1;
	}
	
	public object Deserialize(
		ISerializer<object> rootDeserializer, 
		object rootContext=null
	) {
		object rt = ConsumeInline(rootDeserializer,rootContext);
		
		if (address != data.Length) {
			Plugin.LogWarning(
				$"Deserialization did not consume entire bytestream. "
				+$"Is there something we didn't load or are we wasting space on file?"
			);
		}
		
		foreach ((var obj, var action) in this.finalizers) {
			try {
				#if VERBOSE_DESERIALIZE
				Plugin.LogDebug($"Calling finalizer for {obj}");
				#endif
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
		var rt = Data.Slice(address,length).ToArray();
		address += length;
		return rt;
	}
	
	// Consume bytes until the next one would satisfy the given condition
	public byte[] ConsumeUntil(Func<byte, bool> condition) {
		int length = 0;
		while (!condition(data[this.address + length])) {
			length++;
		}
		return this.Consume(length);
	}
	
	// Consumes 4 bytes, and queues resolving of the reference if occurs later in the bytestream, otherwise
	// immediately resolves the reference
	public int ConsumeReference(
		ISerializer<object> deserializer, 
		Action<object> action=null, 
		object context=null
	) {
		this.Consume(4).CastInto(out int addr);
		
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
		
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"Q 0x{addr:X} | {deserializer.GetType()}");
		#endif
		
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
		refInfo.context ??= context;
		
		return addr;
	}
	
	public object ConsumeInline(ISerializer<object> deserializer, object context=null) {
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"L 0x{address:X} | {deserializer.GetType()}");
		#endif
		int addr = address;
		var rt = deserializer.Deserialize(this,context);
		finalizers.Add((rt, deserializer.Finalize));
		AddReference(addr, rt);
		if (unresolvedReferences.TryGetValue(address, out ReferenceInfo refInfo)) {
			ConsumeInline(refInfo.deserializer, refInfo.context);
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
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"A 0x{address:X} | {refr}");
		#endif
		references.Add(address,refr);
		if (!unresolvedReferences.TryGetValue(address,out ReferenceInfo refInfo)) return;
		foreach (Action<object> action in refInfo.actions) {
			action?.Invoke(refr);
		}
		unresolvedReferences.Remove(address);
	}
}