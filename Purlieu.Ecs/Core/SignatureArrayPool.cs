using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Thread-local pool for reusing ulong arrays in ArchetypeSignature operations
/// to eliminate allocations during component add/remove operations.
/// </summary>
internal static class SignatureArrayPool
{
    // Thread-local pools for different array sizes
    [ThreadStatic]
    private static List<ulong[]>? _smallPool; // 1-4 elements
    
    [ThreadStatic]
    private static List<ulong[]>? _mediumPool; // 5-16 elements
    
    [ThreadStatic]
    private static List<ulong[]>? _largePool; // 17+ elements
    
    private const int MaxPoolSize = 4; // Limit pool size to prevent unbounded growth
    private const int InitialPoolCapacity = 2; // Start with small capacity, grow as needed
    
    /// <summary>
    /// Rents a ulong array of at least the specified capacity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong[] Rent(int minimumCapacity)
    {
        var pool = GetPoolForSize(minimumCapacity);
        
        if (pool != null && pool.Count > 0)
        {
            var index = pool.Count - 1;
            var array = pool[index];
            pool.RemoveAt(index);
            
            // Clear the array before reuse
            Array.Clear(array, 0, array.Length);
            
            return array;
        }
        
        // Create new array with power-of-2 capacity for better pooling
        var capacity = GetOptimalCapacity(minimumCapacity);
        return new ulong[capacity];
    }
    
    /// <summary>
    /// Returns a ulong array to the pool for reuse.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Return(ulong[] array)
    {
        if (array == null || array.Length == 0)
            return;
        
        var pool = GetPoolForSize(array.Length);
        
        if (pool != null && pool.Count < MaxPoolSize)
        {
            pool.Add(array);
        }
        
        // If pool is full or inappropriate size, let GC handle it
    }
    
    private static List<ulong[]>? GetPoolForSize(int size)
    {
        return size switch
        {
            <= 4 => GetOrCreateSmallPool(),
            <= 16 => GetOrCreateMediumPool(),
            _ => GetOrCreateLargePool()
        };
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<ulong[]> GetOrCreateSmallPool()
    {
        return _smallPool ??= new List<ulong[]>(InitialPoolCapacity);
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<ulong[]> GetOrCreateMediumPool()
    {
        return _mediumPool ??= new List<ulong[]>(InitialPoolCapacity);
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<ulong[]> GetOrCreateLargePool()
    {
        return _largePool ??= new List<ulong[]>(InitialPoolCapacity);
    }
    
    private static int GetOptimalCapacity(int minimumCapacity)
    {
        // Round up to next power of 2 for better pooling efficiency
        if (minimumCapacity <= 1) return 1;
        if (minimumCapacity <= 2) return 2;
        if (minimumCapacity <= 4) return 4;
        if (minimumCapacity <= 8) return 8;
        if (minimumCapacity <= 16) return 16;
        if (minimumCapacity <= 32) return 32;
        
        // For larger sizes, just use the minimum
        return minimumCapacity;
    }
    
    /// <summary>
    /// Copies data from source array to a pooled array of the specified size.
    /// NOTE: Does not return source array to pool since it might still be referenced.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong[] Resize(ulong[] sourceArray, int newSize, int copyLength = -1)
    {
        var newArray = Rent(newSize);
        
        if (copyLength == -1)
            copyLength = Math.Min(sourceArray.Length, newSize);
        
        if (copyLength > 0)
        {
            Array.Copy(sourceArray, newArray, copyLength);
        }
        
        // DON'T return sourceArray to pool - it might still be referenced
        return newArray;
    }
    
    /// <summary>
    /// Creates a copy of the source array using the pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong[] Clone(ulong[] sourceArray)
    {
        var cloneArray = Rent(sourceArray.Length);
        Array.Copy(sourceArray, cloneArray, sourceArray.Length);
        return cloneArray;
    }

#if DEBUG
    // Test accessors for allocation debugging
    public static ulong[] TestRent(int minimumCapacity) => Rent(minimumCapacity);
    public static void TestReturn(ulong[] array) => Return(array);
    public static ulong[] TestResize(ulong[] sourceArray, int newSize, int copyLength = -1) => Resize(sourceArray, newSize, copyLength);
#endif
}