using System.Collections;
using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Bitset representing a unique combination of component types.
/// </summary>
public readonly struct ArchetypeSignature : IEquatable<ArchetypeSignature>
{
    private const int BitsPerElement = 64;
    private readonly ulong[] _bits;
    private readonly int _hashCode;
    
    public ArchetypeSignature()
    {
        _bits = Array.Empty<ulong>();
        _hashCode = 0;
    }
    
    private ArchetypeSignature(ulong[] bits)
    {
        _bits = bits;
        _hashCode = ComputeHashCode(bits);
    }
    
    /// <summary>
    /// Creates a signature with the specified component type.
    /// </summary>
    public static ArchetypeSignature With<T>() where T : struct
    {
        var typeId = ComponentTypeId.Get<T>();
        return With(typeId);
    }
    
    /// <summary>
    /// Creates a signature with the specified component type ID.
    /// </summary>
    public static ArchetypeSignature With(int typeId)
    {
        var elementIndex = typeId / BitsPerElement;
        var bitIndex = typeId % BitsPerElement;
        
        var bits = new ulong[elementIndex + 1];
        bits[elementIndex] = 1UL << bitIndex;
        
        return new ArchetypeSignature(bits);
    }
    
    /// <summary>
    /// Adds a component type to this signature.
    /// </summary>
    public ArchetypeSignature Add<T>() where T : struct
    {
        var typeId = ComponentTypeId.Get<T>();
        return Add(typeId);
    }
    
    /// <summary>
    /// Adds a component type ID to this signature.
    /// </summary>
    public ArchetypeSignature Add(int typeId)
    {
        var elementIndex = typeId / BitsPerElement;
        var bitIndex = typeId % BitsPerElement;
        
        var newLength = Math.Max(_bits.Length, elementIndex + 1);
        var newBits = new ulong[newLength];
        
        if (_bits.Length > 0)
            Array.Copy(_bits, newBits, _bits.Length);
        
        newBits[elementIndex] |= 1UL << bitIndex;
        
        return new ArchetypeSignature(newBits);
    }
    
    /// <summary>
    /// Removes a component type from this signature.
    /// </summary>
    public ArchetypeSignature Remove<T>() where T : struct
    {
        var typeId = ComponentTypeId.Get<T>();
        return Remove(typeId);
    }
    
    /// <summary>
    /// Removes a component type ID from this signature.
    /// </summary>
    public ArchetypeSignature Remove(int typeId)
    {
        var elementIndex = typeId / BitsPerElement;
        var bitIndex = typeId % BitsPerElement;
        
        if (elementIndex >= _bits.Length)
            return this;
        
        var newBits = new ulong[_bits.Length];
        Array.Copy(_bits, newBits, _bits.Length);
        
        newBits[elementIndex] &= ~(1UL << bitIndex);
        
        return new ArchetypeSignature(newBits);
    }
    
    /// <summary>
    /// Checks if this signature contains the specified component type.
    /// </summary>
    public bool Has<T>() where T : struct
    {
        var typeId = ComponentTypeId.Get<T>();
        return Has(typeId);
    }
    
    /// <summary>
    /// Checks if this signature contains the specified component type ID.
    /// </summary>
    public bool Has(int typeId)
    {
        var elementIndex = typeId / BitsPerElement;
        var bitIndex = typeId % BitsPerElement;
        
        if (elementIndex >= _bits.Length)
            return false;
        
        return (_bits[elementIndex] & (1UL << bitIndex)) != 0;
    }
    
    /// <summary>
    /// Checks if this signature is a superset of another.
    /// </summary>
    public bool IsSupersetOf(ArchetypeSignature other)
    {
        if (other._bits.Length > _bits.Length)
            return false;
        
        for (int i = 0; i < other._bits.Length; i++)
        {
            if ((other._bits[i] & ~_bits[i]) != 0)
                return false;
        }
        
        return true;
    }
    
    public bool Equals(ArchetypeSignature other)
    {
        if (_hashCode != other._hashCode)
            return false;
        
        if (_bits.Length != other._bits.Length)
            return false;
        
        for (int i = 0; i < _bits.Length; i++)
        {
            if (_bits[i] != other._bits[i])
                return false;
        }
        
        return true;
    }
    
    public override bool Equals(object? obj)
    {
        return obj is ArchetypeSignature other && Equals(other);
    }
    
    public override int GetHashCode() => _hashCode;
    
    private static int ComputeHashCode(ulong[] bits)
    {
        unchecked
        {
            int hash = 17;
            foreach (var element in bits)
            {
                hash = hash * 31 + element.GetHashCode();
            }
            return hash;
        }
    }
    
    public override string ToString()
    {
        if (_bits.Length == 0)
            return "ArchetypeSignature<Empty>";
        
        var componentIds = new List<int>();
        for (int i = 0; i < _bits.Length * BitsPerElement; i++)
        {
            if (Has(i))
                componentIds.Add(i);
        }
        
        return $"ArchetypeSignature<{string.Join(",", componentIds)}>";
    }
}

/// <summary>
/// Manages component type IDs for the ECS.
/// </summary>
internal static class ComponentTypeId
{
    private static int _nextId;
    private static readonly Dictionary<Type, int> _typeToId = new();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Get<T>() where T : struct
    {
        return Cache<T>.Id;
    }
    
    public static int GetOrCreate(Type type)
    {
        if (_typeToId.TryGetValue(type, out var id))
            return id;
        
        id = _nextId++;
        _typeToId[type] = id;
        return id;
    }
    
    private static class Cache<T> where T : struct
    {
        public static readonly int Id = GetOrCreate(typeof(T));
    }
}