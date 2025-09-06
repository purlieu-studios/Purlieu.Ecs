using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Struct enumerator for zero-allocation chunk traversal.
/// </summary>
public struct ChunkEnumerator
{
    private readonly List<Chunk> _chunks;
    private int _index;
    private Chunk? _current;
    
    internal ChunkEnumerator(List<Chunk> chunks)
    {
        _chunks = chunks;
        _index = -1;
        _current = null;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        _index++;
        if (_index < _chunks.Count)
        {
            _current = _chunks[_index];
            return true;
        }
        
        _current = null;
        return false;
    }
    
    public readonly Chunk Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current ?? throw new InvalidOperationException("Enumerator not positioned");
    }
    
    public void Reset()
    {
        _index = -1;
        _current = null;
    }
}

/// <summary>
/// Struct-based chunk collection for foreach support.
/// </summary>
public readonly struct ChunkCollection
{
    private readonly List<Chunk> _chunks;
    
    internal ChunkCollection(IEnumerable<Chunk> chunks)
    {
        _chunks = chunks as List<Chunk> ?? chunks.ToList();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChunkEnumerator GetEnumerator() => new ChunkEnumerator(_chunks);
    
    public int Count => _chunks.Count;
}