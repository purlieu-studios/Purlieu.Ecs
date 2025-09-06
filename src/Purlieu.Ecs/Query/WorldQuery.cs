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
    /// Iterates over all matching chunks.
    /// </summary>
    public ChunkIterator Chunks()
    {
        return ChunkIteratorPool.Rent(_world.GetMatchingChunks(_withSignature, _withoutSignature));
    }
    
    /// <summary>
    /// Gets the first matching chunk, or null if none found.
    /// </summary>
    public Chunk? FirstChunk()
    {
        return _world.GetMatchingChunks(_withSignature, _withoutSignature).FirstOrDefault();
    }
    
    /// <summary>
    /// Counts entities matching the query.
    /// </summary>
    public int Count()
    {
        return _world.GetMatchingChunks(_withSignature, _withoutSignature)
                    .Sum(chunk => chunk.Count);
    }
}