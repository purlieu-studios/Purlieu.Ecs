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
        return new DirectChunkEnumerator(_world._allArchetypes, _withSignature, _withoutSignature);
    }
}

/// <summary>
/// Direct chunk enumerator that iterates without allocations.
/// </summary>
public struct DirectChunkEnumerator
{
    private readonly List<Archetype> _archetypes;
    private readonly ArchetypeSignature _withSignature;
    private readonly ArchetypeSignature _withoutSignature;
    private int _archetypeIndex;
    private int _chunkIndex;
    private List<Chunk>? _currentChunks;
    
    internal DirectChunkEnumerator(List<Archetype> archetypes, ArchetypeSignature withSignature, ArchetypeSignature withoutSignature)
    {
        _archetypes = archetypes;
        _withSignature = withSignature;
        _withoutSignature = withoutSignature;
        _archetypeIndex = 0;
        _chunkIndex = 0;
        _currentChunks = null;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        while (true)
        {
            // Try current archetype's chunks
            if (_currentChunks != null)
            {
                while (_chunkIndex < _currentChunks.Count)
                {
                    var chunk = _currentChunks[_chunkIndex++];
                    if (chunk.Count > 0)
                        return true;
                }
            }
            
            // Move to next archetype
            while (_archetypeIndex < _archetypes.Count)
            {
                var archetype = _archetypes[_archetypeIndex++];
                
                // Check if archetype matches query
                if (archetype.Signature.IsSupersetOf(_withSignature) && 
                    !archetype.Signature.HasIntersection(_withoutSignature))
                {
                    _currentChunks = archetype.GetChunks();
                    _chunkIndex = 0;
                    break;
                }
            }
            
            // No more archetypes
            if (_archetypeIndex >= _archetypes.Count && (_currentChunks == null || _chunkIndex >= _currentChunks.Count))
                return false;
        }
    }
    
    public readonly Chunk Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _currentChunks != null && _chunkIndex > 0 ? _currentChunks[_chunkIndex - 1] : throw new InvalidOperationException("Enumerator not positioned");
    }
}