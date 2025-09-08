using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Fluent API for querying entities by component requirements.
/// </summary>
public sealed class WorldQuery
{
    private readonly World _world;
    private readonly List<int> _withTypeIds;
    private readonly List<int> _withoutTypeIds;
    
    // Cached signatures built on first access
    private ArchetypeSignature? _cachedWithSignature;
    private ArchetypeSignature? _cachedWithoutSignature;
    
    internal WorldQuery(World world)
    {
        _world = world;
        _withTypeIds = new List<int>(4); // Common case: 1-4 components
        _withoutTypeIds = new List<int>(2); // Less common
    }
    
    /// <summary>
    /// Requires entities to have the specified component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldQuery With<T>() where T : struct
    {
        _withTypeIds.Add(ComponentTypeId.Get<T>());
        _cachedWithSignature = null; // Invalidate cache
        return this;
    }
    
    /// <summary>
    /// Requires entities to NOT have the specified component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldQuery Without<T>() where T : struct
    {
        _withoutTypeIds.Add(ComponentTypeId.Get<T>());
        _cachedWithoutSignature = null; // Invalidate cache
        return this;
    }
    
    /// <summary>
    /// Gets the "with" signature, building it on-demand.
    /// </summary>
    private ArchetypeSignature GetWithSignature()
    {
        if (_cachedWithSignature == null)
        {
            _cachedWithSignature = BuildSignature(_withTypeIds);
        }
        return _cachedWithSignature.Value;
    }
    
    /// <summary>
    /// Gets the "without" signature, building it on-demand.
    /// </summary>
    private ArchetypeSignature GetWithoutSignature()
    {
        if (_cachedWithoutSignature == null)
        {
            _cachedWithoutSignature = BuildSignature(_withoutTypeIds);
        }
        return _cachedWithoutSignature.Value;
    }
    
    /// <summary>
    /// Builds a signature from a list of type IDs efficiently.
    /// </summary>
    private static ArchetypeSignature BuildSignature(List<int> typeIds)
    {
        if (typeIds.Count == 0)
            return new ArchetypeSignature();
        
        // Find the maximum element index needed
        int maxElementIndex = 0;
        foreach (var typeId in typeIds)
        {
            maxElementIndex = Math.Max(maxElementIndex, typeId / 64);
        }
        
        // Create array of optimal size
        var bits = new ulong[maxElementIndex + 1];
        
        // Set all required bits
        foreach (var typeId in typeIds)
        {
            var elementIndex = typeId / 64;
            var bitIndex = typeId % 64;
            bits[elementIndex] |= 1UL << bitIndex;
        }
        
        return new ArchetypeSignature(bits);
    }
    
    /// <summary>
    /// Iterates over all matching chunks using pooled collections.
    /// </summary>
    public PooledChunkCollection Chunks()
    {
        var chunks = ChunkPool.RentList();
        _world.GetMatchingChunks(GetWithSignature(), GetWithoutSignature(), chunks);
        return new PooledChunkCollection(chunks);
    }
    
    /// <summary>
    /// Iterates over all matching chunks with zero intermediate allocation.
    /// </summary>
    public DirectChunkEnumerable ChunksStack()
    {
        return new DirectChunkEnumerable(_world, GetWithSignature(), GetWithoutSignature());
    }
    
    /// <summary>
    /// Gets the first matching chunk, or null if none found.
    /// </summary>
    public Chunk? FirstChunk()
    {
        using var chunks = Chunks();
        var enumerator = chunks.GetEnumerator();
        return enumerator.MoveNext() ? enumerator.Current : null;
    }
    
    /// <summary>
    /// Counts entities matching the query.
    /// </summary>
    public int Count()
    {
        _world.ThrowIfDisposed();
        using var chunks = Chunks();
        int count = 0;
        foreach (var chunk in chunks)
        {
            count += chunk.Count;
        }
        return count;
    }
}