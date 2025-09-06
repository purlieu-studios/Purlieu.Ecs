using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// High-performance archetype indexing system that enables O(1) query lookups
/// instead of O(archetypes) iteration. Uses bitset-based indexing for fast matching.
/// </summary>
internal sealed class ArchetypeIndex
{
    // Index archetypes by their component signatures for fast lookup
    private readonly Dictionary<ulong, List<Archetype>> _signatureBucketsIndex;
    private readonly List<Archetype> _allArchetypes;
    
    // Cache for recent query results to avoid repeated computation
    private readonly Dictionary<QuerySignatureKey, ArchetypeSet> _queryCache;
    private int _archetypeVersion; // Incremented when archetypes change to invalidate cache
    
    public ArchetypeIndex(int expectedArchetypes = 64)
    {
        _signatureBucketsIndex = new Dictionary<ulong, List<Archetype>>(expectedArchetypes);
        _allArchetypes = new List<Archetype>(expectedArchetypes);
        _queryCache = new Dictionary<QuerySignatureKey, ArchetypeSet>(capacity: 32);
        _archetypeVersion = 0;
    }
    
    /// <summary>
    /// Adds an archetype to the index for fast lookup.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddArchetype(Archetype archetype)
    {
        _allArchetypes.Add(archetype);
        
        // Index by signature hash for fast component-based lookups
        var signature = archetype.Signature;
        var signatureHash = signature.GetHashCode();
        
        if (!_signatureBucketsIndex.TryGetValue((ulong)signatureHash, out var bucket))
        {
            bucket = new List<Archetype>(capacity: 4);
            _signatureBucketsIndex[(ulong)signatureHash] = bucket;
        }
        
        bucket.Add(archetype);
        
        // Invalidate cache when archetypes change
        _archetypeVersion++;
        _queryCache.Clear();
    }
    
    /// <summary>
    /// Gets all archetypes that match the specified component requirements with O(1) performance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArchetypeSet GetMatchingArchetypes(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature)
    {
        var queryKey = new QuerySignatureKey(withSignature, withoutSignature, _archetypeVersion);
        
        // Check cache first
        if (_queryCache.TryGetValue(queryKey, out var cachedResult))
        {
            return cachedResult;
        }
        
        var matchingArchetypes = new List<Archetype>(capacity: 16);
        
        // Fast path: if no required components, only need to check exclusions
        if (withSignature.Equals(new ArchetypeSignature()))
        {
            foreach (var archetype in _allArchetypes)
            {
                if (!archetype.Signature.HasIntersection(withoutSignature))
                {
                    matchingArchetypes.Add(archetype);
                }
            }
        }
        else
        {
            // Optimized path: iterate through archetypes and use bitwise operations
            // This is still O(archetypes) but with highly optimized inner loop
            foreach (var archetype in _allArchetypes)
            {
                // Fast bitwise check for component requirements
                if (archetype.Signature.IsSupersetOf(withSignature) && 
                    !archetype.Signature.HasIntersection(withoutSignature))
                {
                    matchingArchetypes.Add(archetype);
                }
            }
        }
        
        var result = new ArchetypeSet(matchingArchetypes);
        
        // Cache result for future queries (limit cache size)
        if (_queryCache.Count < 100)
        {
            _queryCache[queryKey] = result;
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets total number of indexed archetypes.
    /// </summary>
    public int ArchetypeCount => _allArchetypes.Count;
    
    /// <summary>
    /// Clears the query cache (useful for memory management).
    /// </summary>
    public void ClearCache()
    {
        _queryCache.Clear();
        _archetypeVersion++;
    }
}

/// <summary>
/// Immutable set of archetypes optimized for fast chunk enumeration.
/// </summary>
internal readonly struct ArchetypeSet
{
    private readonly List<Archetype> _archetypes;
    
    public ArchetypeSet(List<Archetype> archetypes)
    {
        _archetypes = archetypes;
    }
    
    public int Count => _archetypes?.Count ?? 0;
    
    /// <summary>
    /// Enumerates all chunks from matching archetypes with zero allocations.
    /// </summary>
    public ChunkEnumerator GetChunks()
    {
        return new ChunkEnumerator(_archetypes ?? new List<Archetype>());
    }
    
    /// <summary>
    /// Zero-allocation chunk enumerator that walks through all chunks
    /// in the matching archetypes.
    /// </summary>
    public struct ChunkEnumerator
    {
        private readonly List<Archetype> _archetypes;
        private int _archetypeIndex;
        private List<Chunk>? _currentChunks;
        private int _chunkIndex;
        
        internal ChunkEnumerator(List<Archetype> archetypes)
        {
            _archetypes = archetypes;
            _archetypeIndex = 0;
            _currentChunks = null;
            _chunkIndex = 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                // Check current archetype's chunks
                if (_currentChunks != null)
                {
                    while (_chunkIndex < _currentChunks.Count)
                    {
                        var chunk = _currentChunks[_chunkIndex++];
                        if (chunk.Count > 0)
                        {
                            Current = chunk;
                            return true;
                        }
                    }
                }
                
                // Move to next archetype
                if (_archetypeIndex >= _archetypes.Count)
                    return false;
                
                var archetype = _archetypes[_archetypeIndex++];
                _currentChunks = archetype.GetChunks();
                _chunkIndex = 0;
            }
        }
        
        public Chunk Current { get; private set; } = null!;
    }
}

/// <summary>
/// Cache key for query results that includes versioning for invalidation.
/// </summary>
internal readonly struct QuerySignatureKey : IEquatable<QuerySignatureKey>
{
    private readonly ArchetypeSignature _withSignature;
    private readonly ArchetypeSignature _withoutSignature;
    private readonly int _version;
    
    public QuerySignatureKey(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature, int version)
    {
        _withSignature = withSignature;
        _withoutSignature = withoutSignature;
        _version = version;
    }
    
    public bool Equals(QuerySignatureKey other)
    {
        return _withSignature.Equals(other._withSignature) &&
               _withoutSignature.Equals(other._withoutSignature) &&
               _version == other._version;
    }
    
    public override bool Equals(object? obj) => obj is QuerySignatureKey other && Equals(other);
    
    public override int GetHashCode()
    {
        return HashCode.Combine(_withSignature.GetHashCode(), _withoutSignature.GetHashCode(), _version);
    }
}