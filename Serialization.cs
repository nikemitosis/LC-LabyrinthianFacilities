namespace LabyrinthianFacilities.Serialization;

using LabyrinthianFacilities.Util;

using System;
using System.Collections.Generic;
using System.IO;

public interface ISerializable {
	public IEnumerable<SerializationToken> Serialize();
}

public interface IDeserializer<out T> where T : ISerializable {
	// (Intended for inheritors of T)
	public T Deserialize(ISerializable baseObject, DeserializationContext dc, object extraContext=null);
	public T Deserialize(DeserializationContext dc, object extraContext=null);
}

public struct SerializationToken {
	public bool IsReference {get {return bytes == null;}}
	
	public ISerializable referenceTo;
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
		ISerializable referenceTo, ISerializable isStartOf=null
	) {
		this.bytes = null;
		this.referenceTo = referenceTo;
		this.isStartOf = isStartOf;
	}
	
	public static implicit operator byte[](SerializationToken st) => st.bytes;
	public static implicit operator SerializationToken(byte[] b) => new SerializationToken(b);
}

public class Serializer {
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
		foreach (SerializationToken t in item.Serialize()) {
			try {
				this.AddToken(t);
			} catch (ArgumentException e) {
				throw new ArgumentException($"{item} - " + e.Message);
			}
		}
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

public class DeserializationContext {
	private byte[] data;
	private ReadOnlySpan<byte> Data {get {return data;}}
	
	private class QueuedReferenceInfo {
		public IDeserializer<ISerializable> deserializer;
		public List<Action<ISerializable>> responses;
		public object context;
		
		public QueuedReferenceInfo() {
			this.deserializer = null;
			this.responses = new();
			this.context = null;
		}
	}
	
	private Dictionary<int, ISerializable> references;
	private Dictionary<int, QueuedReferenceInfo> unresolvedReferences;
	private int offset;
	
	public int Address {get {return this.offset;}}
	
	public DeserializationContext(byte[] data) {
		this.data = data;
		
		this.references = new();
		this.unresolvedReferences = new();
		this.offset = 1;
	}
	
	public ISerializable Deserialize(IDeserializer<ISerializable> rootDeserializer, object rootContext=null) {
		EnqueueDependency(1, rootDeserializer, (ISerializable x) => {}, rootContext);
		
		while (unresolvedReferences.Count != 0) {
			Plugin.LogFatal($"Unresolved References: {unresolvedReferences.Count}");
			QueuedReferenceInfo refInfo = default;
			foreach (var entry in unresolvedReferences) {
				this.offset = entry.Key;
				refInfo = entry.Value;
				break;
			}
			Plugin.LogFatal($"Deserializing at 0x{this.offset:X}");
			Plugin.LogFatal($"Deserializer: {refInfo.deserializer}");
			
			ResolveDependency(
				this.offset, 
				refInfo.deserializer.Deserialize(this,refInfo.context)
			);
		}
		
		return references[1];
	}
	
	public void EnqueueDependency(
		int address, IDeserializer<ISerializable> deserializer, Action<ISerializable> response, object context=null
	) {
		if (references.TryGetValue(address, out ISerializable obj)) {
			response?.Invoke(obj);
			return;
		}
		
		QueuedReferenceInfo refInfo;
		if (!this.unresolvedReferences.TryGetValue(address,out refInfo)) {
			refInfo = new();
			this.unresolvedReferences.Add(address,refInfo);
		}
		
		ref IDeserializer<ISerializable> currentDeserializer = ref refInfo.deserializer;
		ref object currentContext = ref refInfo.context;
		ref List<Action<ISerializable>> responses = ref refInfo.responses;
		
		if (currentDeserializer == null || currentDeserializer.GetType().IsSubclassOf(deserializer.GetType())) {
			currentDeserializer = deserializer;
		} else if (!deserializer.GetType().IsSubclassOf(currentDeserializer.GetType())) {
			throw new ArgumentException(
				$"Deserializers {currentDeserializer.GetType()} and {deserializer.GetType()} "
				+$"for address {address} are not compatible. \n"
				+$"If this is a simple mistake, one should inherit from the other; "
				+$"One object representing two sibling/unrelated types is not currently supported. "
			);
		}
		currentContext ??= context;
		responses.Add(response);
	}
	
	private void ResolveDependency(int address, ISerializable obj)  {
		foreach (var response in unresolvedReferences[address].responses) {
			response?.Invoke(obj);
		}
		references[address] = obj;
		this.unresolvedReferences.Remove(address);
	}
	
	public byte[] Consume(int length) {
		var rt = Data.Slice(offset,length).ToArray();
		offset += length;
		return rt;
	}
	
	public byte[] ConsumeUntil(Func<byte, bool> condition) {
		int length = 0;
		while (!condition(data[this.offset + length])) {
			length++;
		}
		return this.Consume(length);
	}
}