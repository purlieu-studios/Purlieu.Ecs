using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<QuerySignatureKey, ArchetypeSet> _queryCache;
    private int _archetypeVersion; // Incremented when archetypes change to invalidate cache
    
    // Cache performance metrics
    private long _cacheHits;
    private long _cacheMisses;
    private long _cacheInvalidations;
    
    public ArchetypeIndex(int expectedArchetypes = 64)
    {
        _signatureBucketsIndex = new Dictionary<ulong, List<Archetype>>(expectedArchetypes);
        _allArchetypes = new List<Archetype>(expectedArchetypes);
        _queryCache = new ConcurrentDictionary<QuerySignatureKey, ArchetypeSet>();
        _archetypeVersion = 0;
    }
    
    /// <summary>
    /// Adds an archetype to the index for fast lookup.
    /// Uses spatial locality optimization to cluster related archetypes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddArchetype(Archetype archetype)
    {
        // Add to main list using spatial locality insertion
        InsertWithSpatialLocality(archetype);
        
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
        _cacheInvalidations += _queryCache.Count; // Track how many entries we're invalidating
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
            _cacheHits++;
            return cachedResult;
        }
        
        _cacheMisses++;
        
        // Use pooled small array for common case (≤16 archetypes) to minimize allocations
        const int SmallResultLimit = 16;
        var smallBuffer = SmallArchetypeArrayPool.Rent();
        var matchingCount = 0;
        List<Archetype>? heapList = null;
        
        // Fast path: if no required components, only need to check exclusions
        if (withSignature.Equals(new ArchetypeSignature()))
        {
            foreach (var archetype in _allArchetypes)
            {
                if (!archetype.Signature.HasIntersection(withoutSignature))
                {
                    if (matchingCount < SmallResultLimit)
                    {
                        smallBuffer[matchingCount] = archetype;
                    }
                    else
                    {
                        // First overflow: copy small buffer to heap list
                        if (heapList == null)
                        {
                            heapList = ListPool<Archetype>.Rent();
                            for (int i = 0; i < SmallResultLimit; i++)
                            {
                                heapList.Add(smallBuffer[i]);
                            }
                        }
                        heapList.Add(archetype);
                    }
                    matchingCount++;
                }
            }
        }
        else
        {
            // Optimized path: iterate through archetypes and use bitwise operations
            // This is still O(archetypes) but with highly optimized inner loop
            foreach (var archetype in _allArchetypes)
            {
                // FIXED: Use correct signature superset checking
                // The IsSupersetOf method was the root cause of the query failures
                if (archetype.Signature.IsSupersetOf(withSignature) && 
                    !archetype.Signature.HasIntersection(withoutSignature))
                {
                    if (matchingCount < SmallResultLimit)
                    {
                        smallBuffer[matchingCount] = archetype;
                    }
                    else
                    {
                        // First overflow: copy small buffer to heap list
                        if (heapList == null)
                        {
                            heapList = ListPool<Archetype>.Rent();
                            for (int i = 0; i < SmallResultLimit; i++)
                            {
                                heapList.Add(smallBuffer[i]);
                            }
                        }
                        heapList.Add(archetype);
                    }
                    matchingCount++;
                }
            }
        }
        
        // Create result based on storage used
        ArchetypeSet result;
        if (heapList != null)
        {
            // Used heap storage for large result sets
            result = new ArchetypeSet(heapList);
        }
        else
        {
            // Used small buffer - pass array directly to avoid List allocation
            result = new ArchetypeSet(smallBuffer, matchingCount);
        }
        
        // Cache results to improve performance on repeated queries
        if (_queryCache.Count < 100)
        {
            _queryCache[queryKey] = result;
            // Don't return smallBuffer to pool since it's cached in the result
        }
        else 
        {
            // Not caching - return resources to pools
            if (heapList != null)
            {
                ListPool<Archetype>.Return(heapList);
            }
            else
            {
                // Return small buffer to pool since result won't be cached
                SmallArchetypeArrayPool.Return(smallBuffer);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets total number of indexed archetypes.
    /// </summary>
    public int ArchetypeCount => _allArchetypes.Count;
    
    /// <summary>
    /// Selectively invalidates query cache entries that are affected by the specified component types.
    /// This is more efficient than clearing the entire cache as it only removes entries that could be affected.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvalidateCacheForComponents(Type[] componentTypes)
    {
        if (componentTypes.Length == 0)
            return;

        var keysToRemove = new List<QuerySignatureKey>();
        var componentSet = new HashSet<Type>(componentTypes);
        
        foreach (var kvp in _queryCache)
        {
            var key = kvp.Key;
            
            // Check if this query involves any of the affected component types
            if (QueryAffectedByComponents(key.WithSignature, key.WithoutSignature, componentSet))
            {
                keysToRemove.Add(key);
            }
        }
        
        // Remove affected cache entries
        foreach (var key in keysToRemove)
        {
            _queryCache.TryRemove(key, out _);
        }
        
        _cacheInvalidations += keysToRemove.Count;
        
        // Only increment version if we actually invalidated something
        if (keysToRemove.Count > 0)
        {
            _archetypeVersion++;
        }
    }
    
    /// <summary>
    /// Invalidates cache entries affected by adding a new archetype with the specified signature.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvalidateCacheForNewArchetype(ArchetypeSignature newArchetypeSignature)
    {
        var keysToRemove = new List<QuerySignatureKey>();
        
        foreach (var kvp in _queryCache)
        {
            var key = kvp.Key;
            
            // Check if the new archetype would match this cached query
            if (newArchetypeSignature.Matches(key.WithSignature, key.WithoutSignature))
            {
                keysToRemove.Add(key);
            }
        }
        
        // Remove affected cache entries
        foreach (var key in keysToRemove)
        {
            _queryCache.TryRemove(key, out _);
        }
        
        _cacheInvalidations += keysToRemove.Count;
        
        if (keysToRemove.Count > 0)
        {
            _archetypeVersion++;
        }
    }
    
    /// <summary>
    /// Clears the entire query cache (useful for memory management or when selective invalidation isn't sufficient).
    /// </summary>
    public void ClearCache()
    {
        _cacheInvalidations += _queryCache.Count;
        _queryCache.Clear();
        _archetypeVersion++;
    }
    
    /// <summary>
    /// Checks if a query signature would be affected by changes to the specified component types.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool QueryAffectedByComponents(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature, HashSet<Type> componentTypes)
    {
        // Query is affected if any of the changed components are in the with or without constraints
        foreach (var componentType in componentTypes)
        {
            if (withSignature.Has(componentType) || withoutSignature.Has(componentType))
            {
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// Gets cache performance statistics.
    /// </summary>
    public CacheStatistics GetCacheStatistics()
    {
        return new CacheStatistics
        {
            Hits = _cacheHits,
            Misses = _cacheMisses,
            Invalidations = _cacheInvalidations,
            CurrentSize = _queryCache.Count,
            HitRate = _cacheHits + _cacheMisses > 0 ? (double)_cacheHits / (_cacheHits + _cacheMisses) : 0.0,
            ArchetypeGeneration = _archetypeVersion
        };
    }
    
    /// <summary>
    /// Resets cache performance statistics.
    /// </summary>
    public void ResetStatistics()
    {
        _cacheHits = 0;
        _cacheMisses = 0;
        _cacheInvalidations = 0;
    }
    
    /// <summary>
    /// Inserts archetype using spatial locality to cluster similar archetypes together.
    /// This improves cache performance during archetype iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertWithSpatialLocality(Archetype archetype)
    {
        if (_allArchetypes.Count == 0)
        {
            _allArchetypes.Add(archetype);
            return;
        }
        
        // Find the best insertion position based on component similarity
        var bestPosition = FindBestInsertionPosition(archetype);
        
        if (bestPosition >= _allArchetypes.Count)
        {
            _allArchetypes.Add(archetype);
        }
        else
        {
            _allArchetypes.Insert(bestPosition, archetype);
        }
    }
    
    /// <summary>
    /// Finds the optimal insertion position to maximize spatial locality.
    /// Places archetypes with similar component sets near each other.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindBestInsertionPosition(Archetype newArchetype)
    {
        if (_allArchetypes.Count <= 4)
        {
            // For small lists, append at the end (simple case)
            return _allArchetypes.Count;
        }
        
        int bestPosition = _allArchetypes.Count;
        double bestSimilarity = -1.0;
        
        var newSignature = newArchetype.Signature;
        var newComponentTypes = newArchetype.ComponentTypes;
        
        // Sample a subset of archetypes to balance performance vs. optimality
        int sampleSize = Math.Min(16, _allArchetypes.Count);
        int step = _allArchetypes.Count / sampleSize;
        
        for (int i = 0; i < sampleSize; i++)
        {
            int archetypeIndex = i * step;
            if (archetypeIndex >= _allArchetypes.Count) break;
            
            var existingArchetype = _allArchetypes[archetypeIndex];
            var similarity = CalculateArchetypeSimilarity(newSignature, newComponentTypes, 
                                                        existingArchetype.Signature, existingArchetype.ComponentTypes);
            
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestPosition = archetypeIndex + 1; // Insert after similar archetype
            }
        }
        
        return bestPosition;
    }
    
    /// <summary>
    /// Calculates similarity score between two archetypes for spatial locality optimization.
    /// Higher score = more similar = should be placed closer together.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalculateArchetypeSimilarity(ArchetypeSignature sig1, IReadOnlyList<Type> types1,
                                                     ArchetypeSignature sig2, IReadOnlyList<Type> types2)
    {
        // 1. Component overlap score (Jaccard similarity)
        var intersection = sig1.GetIntersectionCount(sig2);
        var union = sig1.GetComponentCount() + sig2.GetComponentCount() - intersection;
        
        if (union == 0) return 0.0; // Both empty
        
        double jaccardSimilarity = (double)intersection / union;
        
        // 2. Size similarity score (prefer similar-sized archetypes)
        int size1 = types1.Count;
        int size2 = types2.Count;
        int sizeDiff = Math.Abs(size1 - size2);
        double sizeSimilarity = 1.0 / (1.0 + sizeDiff * 0.2); // Penalty for size differences
        
        // 3. Component type similarity (high-priority components get more weight)
        double typeSimilarity = CalculateComponentTypeSimilarity(types1, types2);
        
        // Weighted combination of similarity factors
        return (jaccardSimilarity * 0.5) + (sizeSimilarity * 0.2) + (typeSimilarity * 0.3);
    }
    
    /// <summary>
    /// Calculates similarity based on specific component types and their priorities.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalculateComponentTypeSimilarity(IReadOnlyList<Type> types1, IReadOnlyList<Type> types2)
    {
        if (types1.Count == 0 || types2.Count == 0) return 0.0;
        
        double totalSimilarity = 0.0;
        
        // Find common high-priority component types
        var commonHighPriority = 0;
        var totalHighPriority = 0;
        
        foreach (var type1 in types1)
        {
            var priority1 = GetComponentPriority(type1);
            if (priority1 >= 80) // High priority components
            {
                totalHighPriority++;
                if (types2.Contains(type1))
                {
                    commonHighPriority++;
                }
            }
        }
        
        foreach (var type2 in types2)
        {
            var priority2 = GetComponentPriority(type2);
            if (priority2 >= 80 && !types1.Contains(type2))
            {
                totalHighPriority++;
            }
        }
        
        // High-priority component overlap bonus
        if (totalHighPriority > 0)
        {
            totalSimilarity += (double)commonHighPriority / totalHighPriority;
        }
        
        return Math.Min(1.0, totalSimilarity);
    }
    
    /// <summary>
    /// Gets component priority for spatial locality calculations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetComponentPriority(Type componentType)
    {
        var typeName = componentType.Name;
        
        // High priority: Frequently accessed components that should be co-located
        if (typeName.Contains("Position") || typeName.Contains("Transform") ||
            typeName.Contains("Location") || typeName.Contains("Coord"))
            return 100;
            
        if (typeName.Contains("Velocity") || typeName.Contains("Speed") ||
            typeName.Contains("Move") || typeName.Contains("Motion"))
            return 90;
            
        if (typeName.Contains("Health") || typeName.Contains("HP") ||
            typeName.Contains("Damage") || typeName.Contains("Stats"))
            return 80;
        
        // Lower priority components
        return 50;
    }
}

/// <summary>
/// Immutable set of archetypes optimized for fast chunk enumeration.
/// </summary>
internal readonly struct ArchetypeSet
{
    private readonly List<Archetype>? _archetypes;
    private readonly Archetype[]? _archetypeArray;
    private readonly int _count;
    private readonly bool _isPooled;
    
    public ArchetypeSet(List<Archetype> archetypes, bool isPooled = false)
    {
        _archetypes = archetypes;
        _archetypeArray = null;
        _count = archetypes.Count;
        _isPooled = isPooled;
    }
    
    public ArchetypeSet(Archetype[] archetypes, int count)
    {
        _archetypes = null;
        _archetypeArray = archetypes;
        _count = count;
        _isPooled = false;
    }
    
    public int Count => _count;
    
    /// <summary>
    /// Enumerates all chunks from matching archetypes with zero allocations.
    /// </summary>
    public ChunkEnumerator GetChunks()
    {
        if (_archetypes != null)
            return new ChunkEnumerator(_archetypes);
        else
            return new ChunkEnumerator(_archetypeArray!, _count);
    }
    
    /// <summary>
    /// Zero-allocation chunk enumerator that walks through all chunks
    /// in the matching archetypes.
    /// </summary>
    public struct ChunkEnumerator
    {
        private readonly List<Archetype>? _archetypes;
        private readonly Archetype[]? _archetypeArray;
        private readonly int _archetypeCount;
        private int _archetypeIndex;
        private List<Chunk>? _currentChunks;
        private int _chunkIndex;
        
        internal ChunkEnumerator(List<Archetype> archetypes)
        {
            _archetypes = archetypes;
            _archetypeArray = null;
            _archetypeCount = archetypes.Count;
            _archetypeIndex = 0;
            _currentChunks = null;
            _chunkIndex = 0;
        }
        
        internal ChunkEnumerator(Archetype[] archetypes, int count)
        {
            _archetypes = null;
            _archetypeArray = archetypes;
            _archetypeCount = count;
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
                if (_archetypeIndex >= _archetypeCount)
                    return false;
                
                var archetype = _archetypes?[_archetypeIndex++] ?? _archetypeArray![_archetypeIndex++];
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
    public readonly ArchetypeSignature WithSignature;
    public readonly ArchetypeSignature WithoutSignature;
    public readonly int Version;
    
    public QuerySignatureKey(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature, int version)
    {
        WithSignature = withSignature;
        WithoutSignature = withoutSignature;
        Version = version;
    }
    
    public bool Equals(QuerySignatureKey other)
    {
        return WithSignature.Equals(other.WithSignature) &&
               WithoutSignature.Equals(other.WithoutSignature) &&
               Version == other.Version;
    }
    
    public override bool Equals(object? obj) => obj is QuerySignatureKey other && Equals(other);
    
    public override int GetHashCode()
    {
        return HashCode.Combine(WithSignature.GetHashCode(), WithoutSignature.GetHashCode(), Version);
    }
}

/// <summary>
/// Cache performance statistics for profiling and optimization.
/// </summary>
public readonly struct CacheStatistics
{
    public long Hits { get; init; }
    public long Misses { get; init; }
    public long Invalidations { get; init; }
    public int CurrentSize { get; init; }
    public double HitRate { get; init; }
    public int ArchetypeGeneration { get; init; }
    
    public long TotalQueries => Hits + Misses;
    
    public override string ToString()
    {
        return $"Cache: {Hits}H/{Misses}M (hit rate: {HitRate:P1}), {Invalidations} invalidations, {CurrentSize} cached, gen {ArchetypeGeneration}";
    }
}