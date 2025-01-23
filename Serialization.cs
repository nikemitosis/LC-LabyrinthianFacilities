namespace LabyrinthianFacilities.Serialization;

using LabyrinthianFacilities.Util;

using System;
using System.Collections.Generic;
using System.IO;

public interface ISerializable {
	public IEnumerable<SerializationToken> Serialize();
}

public interface IDeserializer<out T> {
	// Override this for inheritance!
	public T Deserialize(object baseObject, DeserializationContext dc, object extraContext=null);
	
	// Intended to instantiate some kind of prefab or default object, and initialize it with above
	// Not intended to be used by inheritors, just for DeserializationContext
	public T Deserialize(DeserializationContext dc, object extraContext=null);
	
	// Called when deserialization is completely finished
	// Mandatory implementation because if a parent doesn't implement it than a child cannot implement it
	// yes its stupid
	public void Finalize(object obj);
}

public interface ISerializer<out T> {
	public IEnumerable<SerializationToken> Serialize(object tgt); //this is stupid
}

public interface IDeSerializer<T> : ISerializer<T>, IDeserializer<T> {}

public struct SerializationToken {
	public bool IsReference {get {return bytes == null;}}
	
	public ISerializable referenceTo;
	public ISerializer<object> serializer;
	public byte[] bytes;
	
	public ISerializable isStartOf;
	
	public SerializationToken(
		byte[] bytes, ISerializable isStartOf=null
	) {
		this.bytes = bytes;
		this.referenceTo = null;
		this.isStartOf = isStartOf;
	}
	
	public SerializationToken(
		ISerializable referenceTo, ISerializer<object> serializer=null, ISerializable isStartOf=null
	) {
		this.bytes = null;
		this.referenceTo = referenceTo;
		this.serializer = serializer;
		this.isStartOf = isStartOf;
	}
	
	public static implicit operator byte[](SerializationToken st) => st.bytes;
	public static implicit operator SerializationToken(byte[] b) => new SerializationToken(b);
}

public sealed class Serializer {
	private List<byte> output;
	private Dictionary<ISerializable, int> references;
	private Dictionary<ISerializable, List<int>> queuedReferences;
	
	public IList<byte> Output {get {return output.AsReadOnly();}}
	
	public Serializer() {
		this.output = new();
		this.output.Add(0);
		this.references = new();
		this.queuedReferences = new();
	}
	
	private void AddToken(SerializationToken token) {
		if (token.isStartOf != null) {
			BeginObject(token.isStartOf);
		}
		if (!token.IsReference) {
			if (token.bytes == null) {
				throw new ArgumentException($"Cannot add empty token");
			} else {
				this.output.AddRange(token.bytes);
			}
		} else {
			AddReference(token.referenceTo);
		}
	}
	
	private readonly byte[] REFERENCE_PLACEHOLDER = [1,2,3,4];
	private readonly byte[] NULL_BYTES = [0,0,0,0];
	private void AddReference(ISerializable refr) {
		if (refr == null) {
			this.output.AddRange(NULL_BYTES);
			return;
		}
		
		int pos;
		if (!references.TryGetValue(refr, out pos)) {
			List<int> requests;
			if (!queuedReferences.TryGetValue(refr, out requests)) {
				requests = new();
				queuedReferences.Add(refr, requests);
			}
			requests.Add(this.output.Count);
			this.output.AddRange(REFERENCE_PLACEHOLDER);
		} else {
			this.output.AddRange(
				pos.GetBytes()
			);
		}
	}
	
	// Call this *before* adding the object's contents to output
	private void BeginObject(ISerializable item) {
		references[item] = this.output.Count;
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"B {item} -> 0x{this.output.Count:X} - ...");
		#endif
		
		if (queuedReferences.ContainsKey(item)) {
			foreach (int start in queuedReferences[item]) {
				this.output[start+0] = (byte)(output.Count >> 00);
				this.output[start+1] = (byte)(output.Count >> 08);
				this.output[start+2] = (byte)(output.Count >> 16);
				this.output[start+3] = (byte)(output.Count >> 24);
			}
			queuedReferences.Remove(item);
		}
	}
	
	public void Serialize(ISerializable item) {
		serialize(item);
		while (queuedReferences.Count != 0) {
			ISerializable tgt = null;
			foreach (var entry in queuedReferences) {
				tgt = entry.Key;
				break;
			}
			try {
				serialize(tgt);
			} catch (ArgumentException e) {
				throw e;
			}
		}
	}
	
	private void serialize(ISerializable item) {
		#if VERBOSE_SERIALIZE
		int start = this.output.Count;
		#endif
		foreach (SerializationToken t in item.Serialize()) {
			try {
				this.AddToken(t);
			} catch (ArgumentException e) {
				throw new ArgumentException($"{item} - " + e.Message);
			}
		}
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"S {item} -> 0x{start:X} - 0x{this.output.Count:X}");
		#endif
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

public sealed class NewSerializer {
	
	private class ReferenceInfo {
		public List<int> requests;
		public ISerializer<object> serializer;
		
		public ReferenceInfo() {
			requests = new();
		}
	}
	
	private List<byte> output;
	private Dictionary<object, int> references;
	private Dictionary<object, ReferenceInfo> queuedReferences;
	
	public IList<byte> Output {get {return output.AsReadOnly();}}
	
	public NewSerializer() {
		this.output = new();
		this.output.Add(0);
		this.references = new();
		this.queuedReferences = new();
	}
	
	private void AddToken(SerializationToken token) {
		if (token.isStartOf != null) {
			BeginObject(token.isStartOf);
		}
		if (!token.IsReference) {
			if (token.bytes == null) {
				throw new ArgumentException($"Cannot add empty token");
			} else {
				this.output.AddRange(token.bytes);
			}
		} else {
			AddReference(token.referenceTo, token.serializer);
		}
	}
	
	private readonly byte[] REFERENCE_PLACEHOLDER = [1,2,3,4];
	private readonly byte[] NULL_BYTES = [0,0,0,0];
	private void AddReference(object refr, ISerializer<object> serializer=null) {
		if (refr == null) {
			this.output.AddRange(NULL_BYTES);
			return;
		}
		
		if (references.TryGetValue(refr, out int pos)) {
			this.output.AddRange(
				pos.GetBytes()
			);
			return;
		}
		
		if (!queuedReferences.TryGetValue(refr, out ReferenceInfo refInfo)) {
			refInfo = new();
			queuedReferences.Add(refr, refInfo);
		}
		
		if (
			refInfo.serializer == null
			|| serializer.GetType().IsSubclassOf(refInfo.serializer.GetType())
		) {
			refInfo.serializer = serializer;
		} else if (
			!refInfo.serializer.GetType().IsSubclassOf(serializer.GetType())
			&& refInfo.serializer.GetType() != serializer.GetType()
		) {
			throw new ArgumentException(
				$"Serializers {refInfo.serializer.GetType()} and {serializer.GetType()} "
				+$"for object {refr} are not compatible. \n"
				+$"If this is a simple mistake, one should inherit from the other. "
			);
		}
		
		refInfo.requests.Add(this.output.Count);
		this.output.AddRange(REFERENCE_PLACEHOLDER);
	}
	
	// Call this *before* adding the object's contents to output
	private void BeginObject(object item) {
		references[item] = this.output.Count;
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"B {item} -> 0x{this.output.Count:X} - ...");
		#endif
		
		if (queuedReferences.ContainsKey(item)) {
			foreach (int start in queuedReferences[item].requests) {
				this.output[start+0] = (byte)(output.Count >> 00);
				this.output[start+1] = (byte)(output.Count >> 08);
				this.output[start+2] = (byte)(output.Count >> 16);
				this.output[start+3] = (byte)(output.Count >> 24);
			}
			queuedReferences.Remove(item);
		}
	}
	
	public void Serialize(object item, ISerializer<object> serializer) {
		serialize(item,serializer);
		while (queuedReferences.Count != 0) {
			object tgt = null;
			ISerializer<object> ser = null;
			foreach (var entry in queuedReferences) {
				tgt = entry.Key;
				ser = entry.Value.serializer;
				break;
			}
			try {
				serialize(tgt,ser);
			} catch (ArgumentException e) {
				throw e;
			}
		}
	}
	
	private void serialize(object item,ISerializer<object> serializer) {
		#if VERBOSE_SERIALIZE
		int start = this.output.Count;
		#endif
		foreach (SerializationToken t in serializer.Serialize(item)) {
			try {
				this.AddToken(t);
			} catch (ArgumentException e) {
				throw new ArgumentException($"{item} - " + e.Message);
			}
		}
		#if VERBOSE_SERIALIZE
		Plugin.LogDebug($"S {item} -> 0x{start:X} - 0x{this.output.Count:X}");
		#endif
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
	public IDeserializer<ISerializable> deserializer = null;
	public List<Action<ISerializable>> actions = new();
	public object context = null;
}

// Relies on the assumption that data before an object will identify it
// i.e. we will know what an object should be before we come across it in the bytestream
public sealed class DeserializationContext {
	private byte[] data;
	
	private Dictionary<int, ISerializable> references;
	private List<(ISerializable obj, Action<object> action)> finalizers;
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
	
	public ISerializable Deserialize(
		IDeserializer<ISerializable> rootDeserializer, 
		object rootContext=null
	) {
		ISerializable rt = ConsumeInline(rootDeserializer,rootContext);
		
		if (address != data.Length) {
			Plugin.LogWarning(
				$"Deserialization did not consume entire bytestream. "
				+$"Is there something we didn't load or are we wasting space on file?"
			);
		}
		
		foreach ((var obj, var action) in this.finalizers) {
			try {
				action?.Invoke(obj);
			} catch (Exception e) {
				Plugin.LogError($"Deserialization finalizer threw an exception: \n{e.Message}");
			}
		}
		// while (address != data.Length) Consume(1);
		
		this.references = new();
		this.finalizers = new();
		this.unresolvedReferences = new();
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
		IDeserializer<ISerializable> deserializer, 
		Action<ISerializable> action=null, 
		object context=null
	) {
		this.Consume(4).CastInto(out int addr);
		
		// Already resolved
		if (addr == 0) return 0;
		if (references.TryGetValue(addr, out ISerializable obj)) {
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
	
	public ISerializable ConsumeInline(IDeserializer<ISerializable> deserializer, object context=null) {
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
	
	public ISerializable GetReference(int address) {
		return references[address];
	}
	
	public void AddAction(int address, Action<ISerializable> action) {
		if (references.TryGetValue(address, out ISerializable tgt)) {
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
	
	private void AddReference(int address, ISerializable refr) {
		#if VERBOSE_DESERIALIZE
		Plugin.LogDebug($"A 0x{address:X} | {refr}");
		#endif
		references.Add(address,refr);
		if (!unresolvedReferences.TryGetValue(address,out ReferenceInfo refInfo)) return;
		foreach (Action<ISerializable> action in refInfo.actions) {
			action?.Invoke(refr);
		}
		unresolvedReferences.Remove(address);
	}
}