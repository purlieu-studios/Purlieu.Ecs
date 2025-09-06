using System.Collections;
using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Pooled iterator for zero-allocation chunk traversal.
/// </summary>
public struct ChunkIterator : IEnumerable<Chunk>, IEnumerator<Chunk>
{
    private readonly IEnumerator<Chunk> _source;
    private Chunk? _current;
    
    internal ChunkIterator(IEnumerable<Chunk> chunks)
    {
        _source = chunks.GetEnumerator();
        _current = null;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (_source.MoveNext())
        {
            _current = _source.Current;
            return true;
        }
        
        _current = null;
        return false;
    }
    
    public void Reset()
    {
        _source.Reset();
        _current = null;
    }
    
    public Chunk Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current ?? throw new InvalidOperationException("Iterator not positioned");
    }
    
    object IEnumerator.Current => Current;
    
    public void Dispose()
    {
        _source?.Dispose();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChunkIterator GetEnumerator() => this;
    
    IEnumerator<Chunk> IEnumerable<Chunk>.GetEnumerator() => this;
    
    IEnumerator IEnumerable.GetEnumerator() => this;
}

/// <summary>
/// Iterator pool to avoid allocations.
/// </summary>
internal static class ChunkIteratorPool
{
    private static readonly Stack<ChunkIterator> _pool = new(capacity: 32);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChunkIterator Rent(IEnumerable<Chunk> chunks)
    {
        return new ChunkIterator(chunks);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(ChunkIterator iterator)
    {
        iterator.Dispose();
    }
}