using System.Collections;
using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Thread-local pool for reusing chunk collections to avoid allocations.
/// </summary>
internal static class ChunkPool
{
    [ThreadStatic]
    private static List<Chunk>? _threadLocalList;
    
    [ThreadStatic]
    private static long _lastAccessTicks;
    
    /// <summary>
    /// Rents a list from the thread-local pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<Chunk> RentList()
    {
        _lastAccessTicks = Environment.TickCount64;
        
        var list = _threadLocalList;
        if (list != null)
        {
            _threadLocalList = null; // Remove from pool
            list.Clear();
            return list;
        }
        
        return new List<Chunk>(capacity: 32); // Pre-size to avoid growth
    }
    
    /// <summary>
    /// Returns a list to the thread-local pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnList(List<Chunk> list)
    {
        if (list.Capacity <= 128 && _threadLocalList == null) // Only pool one list per thread
        {
            list.Clear();
            _threadLocalList = list;
            _lastAccessTicks = Environment.TickCount64;
        }
    }
    
    /// <summary>
    /// Clears unused pools that haven't been accessed recently.
    /// </summary>
    public static void ClearUnusedPools()
    {
        // Clear if not accessed in last 5 minutes
        const long UnusedThresholdMs = 5 * 60 * 1000;
        
        if (_threadLocalList != null)
        {
            var elapsedMs = Environment.TickCount64 - _lastAccessTicks;
            if (elapsedMs > UnusedThresholdMs)
            {
                _threadLocalList = null;
            }
        }
    }
}

/// <summary>
/// Pooled chunk collection that automatically returns to pool when disposed.
/// </summary>
public readonly struct PooledChunkCollection : IDisposable, IEnumerable<Chunk>
{
    private readonly List<Chunk> _chunks;
    
    internal PooledChunkCollection(List<Chunk> chunks)
    {
        _chunks = chunks;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChunkEnumerator GetEnumerator() => new ChunkEnumerator(_chunks);
    
    public int Count => _chunks.Count;
    
    IEnumerator<Chunk> IEnumerable<Chunk>.GetEnumerator() => _chunks?.GetEnumerator() ?? Enumerable.Empty<Chunk>().GetEnumerator();
    
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Chunk>)this).GetEnumerator();
    
    public void Dispose()
    {
        if (_chunks != null)
        {
            ChunkPool.ReturnList(_chunks);
        }
    }
}