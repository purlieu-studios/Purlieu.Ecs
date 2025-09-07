using System.Collections.Concurrent;

namespace PurlieuEcs.Core;

/// <summary>
/// Thread-safe pool for reusing List instances to eliminate allocations.
/// </summary>
internal static class ListPool<T>
{
    private static readonly ConcurrentQueue<List<T>> _pool = new();
    private static int _poolCount = 0;
    private static long _lastAccessTicks = Environment.TickCount64;
    private const int MaxPoolSize = 32;
    
    /// <summary>
    /// Rents a List from the pool or creates a new one.
    /// </summary>
    public static List<T> Rent()
    {
        _lastAccessTicks = Environment.TickCount64;
        
        if (_pool.TryDequeue(out var list))
        {
            Interlocked.Decrement(ref _poolCount);
            return list;
        }
        
        return new List<T>(capacity: 16);
    }
    
    /// <summary>
    /// Returns a List to the pool for reuse.
    /// </summary>
    public static void Return(List<T> list)
    {
        if (list == null)
            return;
            
        _lastAccessTicks = Environment.TickCount64;
        list.Clear();
        
        // Only keep a limited number in the pool to prevent unbounded growth
        if (_poolCount < MaxPoolSize)
        {
            _pool.Enqueue(list);
            Interlocked.Increment(ref _poolCount);
        }
    }
    
    /// <summary>
    /// Clears unused pools that haven't been accessed recently.
    /// </summary>
    public static void ClearIfUnused(TimeSpan threshold)
    {
        var thresholdMs = (long)threshold.TotalMilliseconds;
        var elapsedMs = Environment.TickCount64 - _lastAccessTicks;
        
        if (elapsedMs > thresholdMs)
        {
            // Clear all pooled lists
            while (_pool.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _poolCount);
            }
        }
    }
}