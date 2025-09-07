using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace PurlieuEcs.Core;

/// <summary>
/// High-performance bloom filter for O(1) component existence checks.
/// Reduces query iteration overhead by pre-filtering archetypes.
/// </summary>
internal sealed class BloomFilter
{
    private readonly ulong[] _bits;
    private readonly int _hashCount;
    private readonly int _size;
    private readonly int _sizeMask;
    
    // Pre-computed hash seeds for deterministic hashing
    private readonly uint[] _hashSeeds;
    
    public BloomFilter(int expectedItems = 1024, double falsePositiveRate = 0.01)
    {
        // Calculate optimal bloom filter parameters
        // m = -n * ln(p) / (ln(2)^2)  where m = bits, n = items, p = false positive rate
        _size = CalculateOptimalSize(expectedItems, falsePositiveRate);
        _sizeMask = _size - 1; // For fast modulo via bitwise AND (requires power of 2)
        
        // k = (m/n) * ln(2)  where k = number of hash functions
        _hashCount = CalculateOptimalHashCount(_size, expectedItems);
        
        // Allocate bit array (64 bits per ulong)
        int arraySize = (_size + 63) / 64;
        _bits = new ulong[arraySize];
        
        // Pre-compute hash seeds for consistent hashing
        _hashSeeds = new uint[_hashCount];
        var rng = RandomNumberGenerator.Create();
        var seedBytes = new byte[4];
        
        for (int i = 0; i < _hashCount; i++)
        {
            rng.GetBytes(seedBytes);
            _hashSeeds[i] = BitConverter.ToUInt32(seedBytes, 0);
        }
    }
    
    /// <summary>
    /// Adds a component type ID to the filter.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int componentTypeId)
    {
        var hash1 = MurmurHash3(componentTypeId, _hashSeeds[0]);
        var hash2 = _hashCount > 1 ? MurmurHash3(componentTypeId, _hashSeeds[1]) : 0;
        
        // Use double hashing to generate k hash values efficiently
        for (int i = 0; i < _hashCount; i++)
        {
            var hash = (int)((hash1 + i * hash2) & _sizeMask);
            SetBit(hash);
        }
    }
    
    /// <summary>
    /// Adds multiple component type IDs in bulk for better cache locality.
    /// </summary>
    public void AddBulk(ReadOnlySpan<int> componentTypeIds)
    {
        foreach (var typeId in componentTypeIds)
        {
            Add(typeId);
        }
    }
    
    /// <summary>
    /// Tests if a component type might exist (with false positive possibility).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MightContain(int componentTypeId)
    {
        var hash1 = MurmurHash3(componentTypeId, _hashSeeds[0]);
        var hash2 = _hashCount > 1 ? MurmurHash3(componentTypeId, _hashSeeds[1]) : 0;
        
        for (int i = 0; i < _hashCount; i++)
        {
            var hash = (int)((hash1 + i * hash2) & _sizeMask);
            if (!IsBitSet(hash))
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Tests if all component types in a signature might exist.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MightContainAll(ReadOnlySpan<int> componentTypeIds)
    {
        foreach (var typeId in componentTypeIds)
        {
            if (!MightContain(typeId))
                return false;
        }
        return true;
    }
    
    /// <summary>
    /// Clears the bloom filter for reuse.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_bits);
    }
    
    /// <summary>
    /// Merges another bloom filter into this one (union operation).
    /// </summary>
    public void MergeWith(BloomFilter other)
    {
        if (other._bits.Length != _bits.Length)
            throw new ArgumentException("Bloom filters must have same size to merge");
        
        for (int i = 0; i < _bits.Length; i++)
        {
            _bits[i] |= other._bits[i];
        }
    }
    
    /// <summary>
    /// Gets the current false positive probability based on items added.
    /// </summary>
    public double GetFalsePositiveProbability(int itemsAdded)
    {
        // (1 - e^(-k*n/m))^k
        double ratio = (double)(_hashCount * itemsAdded) / _size;
        return Math.Pow(1.0 - Math.Exp(-ratio), _hashCount);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBit(int position)
    {
        int arrayIndex = position >> 6; // Divide by 64
        int bitPosition = position & 63; // Modulo 64
        _bits[arrayIndex] |= 1UL << bitPosition;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsBitSet(int position)
    {
        int arrayIndex = position >> 6;
        int bitPosition = position & 63;
        return (_bits[arrayIndex] & (1UL << bitPosition)) != 0;
    }
    
    /// <summary>
    /// MurmurHash3 32-bit implementation for fast, high-quality hashing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MurmurHash3(int key, uint seed)
    {
        const uint c1 = 0xcc9e2d51;
        const uint c2 = 0x1b873593;
        const int r1 = 15;
        const int r2 = 13;
        const uint m = 5;
        const uint n = 0xe6546b64;
        
        uint hash = seed;
        uint k = (uint)key;
        
        k *= c1;
        k = RotateLeft(k, r1);
        k *= c2;
        
        hash ^= k;
        hash = RotateLeft(hash, r2);
        hash = hash * m + n;
        
        // Finalization
        hash ^= 4; // Length of input (4 bytes)
        hash ^= hash >> 16;
        hash *= 0x85ebca6b;
        hash ^= hash >> 13;
        hash *= 0xc2b2ae35;
        hash ^= hash >> 16;
        
        return hash;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateLeft(uint value, int shift)
    {
        return (value << shift) | (value >> (32 - shift));
    }
    
    private static int CalculateOptimalSize(int expectedItems, double falsePositiveRate)
    {
        // m = -n * ln(p) / (ln(2)^2)
        double size = -expectedItems * Math.Log(falsePositiveRate) / (Math.Log(2) * Math.Log(2));
        
        // Round up to next power of 2 for fast modulo operations
        int powerOfTwo = 1;
        while (powerOfTwo < size)
            powerOfTwo <<= 1;
        
        return powerOfTwo;
    }
    
    private static int CalculateOptimalHashCount(int size, int expectedItems)
    {
        // k = (m/n) * ln(2)
        double hashCount = (double)size / expectedItems * Math.Log(2);
        return Math.Max(1, (int)Math.Round(hashCount));
    }
}

/// <summary>
/// Archetype-specific bloom filter that tracks component existence.
/// </summary>
internal sealed class ArchetypeBloomFilter
{
    private readonly BloomFilter _filter;
    private readonly HashSet<int> _componentTypeIds; // For exact verification
    
    public ArchetypeBloomFilter(int expectedComponents = 32)
    {
        _filter = new BloomFilter(expectedComponents, 0.001); // Very low false positive rate
        _componentTypeIds = new HashSet<int>(expectedComponents);
    }
    
    public void AddComponentType(Type componentType)
    {
        int typeId = componentType.GetHashCode();
        _filter.Add(typeId);
        _componentTypeIds.Add(typeId);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MightHaveComponent(Type componentType)
    {
        return _filter.MightContain(componentType.GetHashCode());
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DefinitelyHasComponent(Type componentType)
    {
        int typeId = componentType.GetHashCode();
        return _filter.MightContain(typeId) && _componentTypeIds.Contains(typeId);
    }
    
    public bool MightHaveAllComponents(Type[] componentTypes)
    {
        foreach (var type in componentTypes)
        {
            if (!MightHaveComponent(type))
                return false;
        }
        return true;
    }
}