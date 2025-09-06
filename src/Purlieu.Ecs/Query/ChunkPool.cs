using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Pool for reusing chunk collections to avoid allocations.
/// </summary>
internal static class ChunkPool
{
    private static readonly Stack<List<Chunk>> _listPool = new(capacity: 16);
    private static readonly object _poolLock = new object();
    
    /// <summary>
    /// Rents a list from the pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<Chunk> RentList()
    {
        lock (_poolLock)
        {
            if (_listPool.Count > 0)
            {
                var list = _listPool.Pop();
                list.Clear();
                return list;
            }
        }
        
        return new List<Chunk>(capacity: 16);
    }
    
    /// <summary>
    /// Returns a list to the pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnList(List<Chunk> list)
    {
        if (list.Capacity <= 128) // Don't pool very large lists
        {
            list.Clear();
            lock (_poolLock)
            {
                if (_listPool.Count < 16) // Limit pool size
                {
                    _listPool.Push(list);
                }
            }
        }
    }
}

/// <summary>
/// Pooled chunk collection that automatically returns to pool when disposed.
/// </summary>
public readonly struct PooledChunkCollection : IDisposable
{
    private readonly List<Chunk> _chunks;
    
    internal PooledChunkCollection(List<Chunk> chunks)
    {
        _chunks = chunks;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChunkEnumerator GetEnumerator() => new ChunkEnumerator(_chunks);
    
    public int Count => _chunks.Count;
    
    public void Dispose()
    {
        if (_chunks != null)
        {
            ChunkPool.ReturnList(_chunks);
        }
    }
}