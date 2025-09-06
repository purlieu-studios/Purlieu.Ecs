using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Fluent API for querying entities by component requirements.
/// </summary>
public sealed class WorldQuery
{
    private readonly World _world;
    private ArchetypeSignature _withSignature;
    private ArchetypeSignature _withoutSignature;
    
    internal WorldQuery(World world)
    {
        _world = world;
        _withSignature = new ArchetypeSignature();
        _withoutSignature = new ArchetypeSignature();
    }
    
    /// <summary>
    /// Requires entities to have the specified component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldQuery With<T>() where T : struct
    {
        _withSignature = _withSignature.Add<T>();
        return this;
    }
    
    /// <summary>
    /// Requires entities to NOT have the specified component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldQuery Without<T>() where T : struct
    {
        _withoutSignature = _withoutSignature.Add<T>();
        return this;
    }
    
    /// <summary>
    /// Iterates over all matching chunks using pooled collections.
    /// </summary>
    public PooledChunkCollection Chunks()
    {
        var chunks = ChunkPool.RentList();
        _world.GetMatchingChunks(_withSignature, _withoutSignature, chunks);
        return new PooledChunkCollection(chunks);
    }
    
    /// <summary>
    /// Iterates over all matching chunks with zero intermediate allocation.
    /// </summary>
    public DirectChunkEnumerable ChunksStack()
    {
        return new DirectChunkEnumerable(_world, _withSignature, _withoutSignature);
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
        using var chunks = Chunks();
        int count = 0;
        foreach (var chunk in chunks)
        {
            count += chunk.Count;
        }
        return count;
    }
}