using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Direct chunk enumerable that doesn't allocate intermediate collections.
/// </summary>
public readonly struct DirectChunkEnumerable
{
    private readonly World _world;
    private readonly ArchetypeSignature _withSignature;
    private readonly ArchetypeSignature _withoutSignature;
    
    internal DirectChunkEnumerable(World world, ArchetypeSignature withSignature, ArchetypeSignature withoutSignature)
    {
        _world = world;
        _withSignature = withSignature;
        _withoutSignature = withoutSignature;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DirectChunkEnumerator GetEnumerator()
    {
        return new DirectChunkEnumerator(_world._archetypeIndex, _withSignature, _withoutSignature);
    }
}

/// <summary>
/// Direct chunk enumerator that uses optimized archetype index for zero allocations.
/// </summary>
public struct DirectChunkEnumerator
{
    private ArchetypeSet.ChunkEnumerator _chunkEnumerator;
    
    internal DirectChunkEnumerator(ArchetypeIndex archetypeIndex, ArchetypeSignature withSignature, ArchetypeSignature withoutSignature)
    {
        var matchingArchetypes = archetypeIndex.GetMatchingArchetypes(withSignature, withoutSignature);
        _chunkEnumerator = matchingArchetypes.GetChunks();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        return _chunkEnumerator.MoveNext();
    }
    
    public readonly Chunk Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _chunkEnumerator.Current;
    }
}