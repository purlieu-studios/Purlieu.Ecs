using System.Collections.Concurrent;

namespace PurlieuEcs.Core;

/// <summary>
/// Thread-safe pool for reusing small Archetype arrays to eliminate allocations.
/// </summary>
internal static class SmallArchetypeArrayPool
{
    private static readonly ConcurrentQueue<Archetype[]> _pool = new();
    private static int _poolCount = 0;
    private const int MaxPoolSize = 8;
    private const int ArraySize = 16;
    
    /// <summary>
    /// Rents a small Archetype array from the pool or creates a new one.
    /// </summary>
    public static Archetype[] Rent()
    {
        if (_pool.TryDequeue(out var array))
        {
            Interlocked.Decrement(ref _poolCount);
            // Clear the array before reuse
            Array.Clear(array);
            return array;
        }
        
        return new Archetype[ArraySize];
    }
    
    /// <summary>
    /// Returns a small Archetype array to the pool for reuse.
    /// </summary>
    public static void Return(Archetype[] array)
    {
        if (array == null || array.Length != ArraySize)
            return;
            
        // Only keep a limited number in the pool to prevent unbounded growth
        if (_poolCount < MaxPoolSize)
        {
            _pool.Enqueue(array);
            Interlocked.Increment(ref _poolCount);
        }
    }
}