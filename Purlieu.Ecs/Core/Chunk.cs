using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Collections;

namespace PurlieuEcs.Core;

/// <summary>
/// Provides cache-line aligned memory allocation for optimal performance.
/// </summary>
internal static class CacheLineAlignedAllocator
{
    // Cache line size is typically 64 bytes on most modern processors
    public const int CacheLineSize = 64;
    
    /// <summary>
    /// Allocates a cache-line aligned array for maximum memory performance.
    /// </summary>
    public static T[] AllocateAligned<T>(int capacity) where T : unmanaged
    {
        // Calculate how many elements fit in a cache line
        var elementSize = Unsafe.SizeOf<T>();
        var elementsPerCacheLine = Math.Max(1, CacheLineSize / elementSize);
        
        // Round capacity up to cache line boundary
        var alignedCapacity = ((capacity + elementsPerCacheLine - 1) / elementsPerCacheLine) * elementsPerCacheLine;
        
        // For small arrays where alignment overhead > 50%, use regular allocation
        if (capacity < 16 && (alignedCapacity - capacity) > capacity / 2)
        {
            return new T[capacity];
        }
        
        return new T[alignedCapacity];
    }
    
    /// <summary>
    /// Gets the cache-line aligned capacity for a given element count.
    /// </summary>
    public static int GetAlignedCapacity<T>(int capacity) where T : unmanaged
    {
        var elementSize = Unsafe.SizeOf<T>();
        var elementsPerCacheLine = Math.Max(1, CacheLineSize / elementSize);
        return ((capacity + elementsPerCacheLine - 1) / elementsPerCacheLine) * elementsPerCacheLine;
    }
    
    /// <summary>
    /// Gets memory alignment information for debugging/profiling.
    /// </summary>
    public static (int ElementSize, int ElementsPerCacheLine, int AlignedCapacity, float Overhead) 
        GetAlignmentInfo<T>(int capacity) where T : unmanaged
    {
        var elementSize = Unsafe.SizeOf<T>();
        var elementsPerCacheLine = Math.Max(1, CacheLineSize / elementSize);
        var alignedCapacity = GetAlignedCapacity<T>(capacity);
        var overhead = alignedCapacity > capacity ? (alignedCapacity - capacity) / (float)capacity : 0f;
        
        return (elementSize, elementsPerCacheLine, alignedCapacity, overhead);
    }
}

/// <summary>
/// Provides hardware memory prefetching hints for optimal cache utilization.
/// </summary>
internal static class MemoryPrefetcher
{
    private const int CacheLineSize = 64;
    
    /// <summary>
    /// Prefetches memory for temporal locality (data will be accessed multiple times).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void PrefetchTemporal<T>(T[] array, int index) where T : unmanaged
    {
        if (Sse.IsSupported && index < array.Length)
        {
            fixed (T* ptr = &array[index])
            {
                // PREFETCHT0 - prefetch to all cache levels
                Sse.Prefetch0(ptr);
            }
        }
    }
    
    /// <summary>
    /// Prefetches memory for non-temporal locality (data accessed once, streaming).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void PrefetchNonTemporal<T>(T[] array, int index) where T : unmanaged
    {
        if (Sse.IsSupported && index < array.Length)
        {
            fixed (T* ptr = &array[index])
            {
                // PREFETCHNTA - prefetch to non-temporal cache structure
                Sse.PrefetchNonTemporal(ptr);
            }
        }
    }
    
    /// <summary>
    /// Prefetches sequential data starting from the given index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void PrefetchSequential<T>(T[] array, int startIndex, int prefetchDistance = 2) where T : unmanaged
    {
        if (!Sse.IsSupported || startIndex >= array.Length) return;
        
        var elementSize = Unsafe.SizeOf<T>();
        var elementsPerCacheLine = Math.Max(1, CacheLineSize / elementSize);
        
        // Prefetch multiple cache lines ahead
        for (int i = 0; i < prefetchDistance; i++)
        {
            int prefetchIndex = startIndex + (i * elementsPerCacheLine);
            if (prefetchIndex < array.Length)
            {
                fixed (T* ptr = &array[prefetchIndex])
                {
                    Sse.Prefetch0(ptr);
                }
            }
        }
    }
    
    /// <summary>
    /// Gets the optimal prefetch distance based on element size and access pattern.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetOptimalPrefetchDistance<T>() where T : unmanaged
    {
        var elementSize = Unsafe.SizeOf<T>();
        
        // Larger elements need less prefetch distance
        // Smaller elements benefit from more prefetching
        return elementSize switch
        {
            <= 4 => 4,   // int, float
            <= 8 => 3,   // long, double, Entity
            <= 16 => 2,  // Position, Velocity (12 bytes)
            _ => 1       // Larger components
        };
    }
}

/// <summary>
/// Interface for component storage in chunks.
/// </summary>
internal interface IComponentStorage
{
    void SwapRemove(int from, int to);
    Type ComponentType { get; }
}

/// <summary>
/// Typed component storage for zero-boxing access with SIMD optimization.
/// </summary>
internal sealed class ComponentStorage<T> : IComponentStorage where T : unmanaged
{
    private readonly T[] _components;
    private readonly bool _isSimdSupported;
    
    public Type ComponentType => typeof(T);
    
    public ComponentStorage(int capacity)
    {
        _isSimdSupported = Vector.IsHardwareAccelerated && IsSimdCompatible();
        
        // Validate SIMD support to prevent boxing in debug builds
        if (_isSimdSupported)
        {
            ValidateSimdSupport();
        }
        
        // Always try cache-line alignment for better memory performance
        _components = CacheLineAlignedAllocator.AllocateAligned<T>(capacity);
        
        // If we have cache-line aligned memory and SIMD support, ensure SIMD alignment too
        if (_isSimdSupported && _components.Length >= capacity)
        {
            int vectorSize = GetEffectiveVectorSize();
            
            // Check if we need additional SIMD alignment beyond cache-line alignment
            if (_components.Length % vectorSize != 0)
            {
                // Calculate combined alignment (cache-line + SIMD)
                var cacheLineAligned = CacheLineAlignedAllocator.GetAlignedCapacity<T>(capacity);
                var simdAlignedCapacity = ((cacheLineAligned + vectorSize - 1) / vectorSize) * vectorSize;
                var overhead = (simdAlignedCapacity - capacity) / (float)capacity;
                
                if (overhead <= 0.3f || capacity >= 64) // Allow slightly more overhead for combined alignment
                {
                    _components = new T[simdAlignedCapacity];
                }
                else
                {
                    // Use just cache-line alignment if combined overhead is too high
                    _isSimdSupported = false;
                }
            }
        }
    }
    
    /// <summary>
    /// Gets the effective vector size for SIMD operations.
    /// For primitive types, uses Vector<T>.Count.
    /// For composite types, uses Vector<float>.Count as the base unit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetEffectiveVectorSize()
    {
        var type = typeof(T);
        
        // For primitive types, use direct Vector<T>.Count
        if (type == typeof(int) || type == typeof(float) || type == typeof(double) ||
            type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
            type == typeof(sbyte))
        {
            return Vector<T>.Count;
        }
        
        // For composite types with float fields, use Vector<float>.Count
        // This is the basic vectorization unit for component-wise SIMD
        return Vector<float>.Count;
    }
    
    /// <summary>
    /// Checks if type T is compatible with SIMD operations.
    /// Prevents boxing by validating Vector<T> support at compile-time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSimdCompatible()
    {
        var type = typeof(T);
        
        // Direct primitive types supported by Vector<T>
        if (type == typeof(int) || type == typeof(float) || type == typeof(double) ||
            type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
            type == typeof(sbyte))
        {
            return true;
        }
        
        // Composite types: check if they contain only SIMD-compatible floats
        // This enables SIMD for Position, Velocity, Force (3x float structs)
        if (type.IsValueType && !type.IsEnum)
        {
            var fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fields.Length > 0)
            {
                // All fields must be float for SIMD compatibility
                return fields.All(f => f.FieldType == typeof(float));
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Validates SIMD compatibility at runtime to prevent boxing.
    /// Only validates primitive types that should support Vector<T> directly.
    /// Composite types use component-wise SIMD processing instead.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateSimdSupport()
    {
        #if DEBUG
        var type = typeof(T);
        
        // Only validate primitive types for direct Vector<T> support
        bool isPrimitive = type == typeof(int) || type == typeof(float) || type == typeof(double) ||
                          type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                          type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
                          type == typeof(sbyte);
        
        if (isPrimitive && !Vector<T>.IsSupported)
        {
            throw new InvalidOperationException($"Primitive type {typeof(T).Name} is not supported by Vector<T>. This would cause boxing in SIMD operations.");
        }
        // Composite types like Position don't use Vector<T> directly, so no validation needed
        #endif
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan(int count)
    {
        // Prefetch data for better cache performance during sequential access
        if (count > 0)
        {
            MemoryPrefetcher.PrefetchSequential(_components, 0, MemoryPrefetcher.GetOptimalPrefetchDistance<T>());
        }
        
        return new Span<T>(_components, 0, count);
    }
    
    /// <summary>
    /// Gets a typed memory for allocation-free enumerators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<T> GetMemory(int count)
    {
        return new ReadOnlyMemory<T>(_components, 0, count);
    }
    
    /// <summary>
    /// Gets whether SIMD operations are supported for this component type.
    /// </summary>
    public bool IsSimdSupported => _isSimdSupported;
    
    /// <summary>
    /// Gets the actual capacity (may be larger due to cache-line and SIMD alignment).
    /// </summary>
    public int ActualCapacity => _components.Length;
    
    /// <summary>
    /// Gets cache alignment information for debugging and profiling.
    /// </summary>
    public (int ElementSize, int ElementsPerCacheLine, int AlignedCapacity, float Overhead) GetAlignmentInfo(int requestedCapacity)
    {
        return CacheLineAlignedAllocator.GetAlignmentInfo<T>(requestedCapacity);
    }
    
    /// <summary>
    /// Gets the internal components array for prefetching operations.
    /// </summary>
    internal T[] ComponentsArray => _components;
    
    /// <summary>
    /// Gets span suitable for SIMD operations with proper alignment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSimdSpan(int count)
    {
        if (!_isSimdSupported)
            return GetSpan(count);
            
        // Return span that's safe for SIMD operations
        var vectorSize = GetEffectiveVectorSize();
        var alignedCount = (count / vectorSize) * vectorSize;
        
        // Prefetch for SIMD operations (streaming access pattern)
        if (alignedCount > 0)
        {
            MemoryPrefetcher.PrefetchSequential(_components, 0, MemoryPrefetcher.GetOptimalPrefetchDistance<T>());
        }
        
        return new Span<T>(_components, 0, alignedCount);
    }
    
    /// <summary>
    /// Gets remainder span for non-SIMD processing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetRemainderSpan(int count)
    {
        if (!_isSimdSupported)
            return Span<T>.Empty;
            
        var vectorSize = GetEffectiveVectorSize();
        var alignedCount = (count / vectorSize) * vectorSize;
        var remainderCount = count - alignedCount;
        
        if (remainderCount > 0)
            return new Span<T>(_components, alignedCount, remainderCount);
        
        return Span<T>.Empty;
    }
    
    public void SwapRemove(int from, int to)
    {
        _components[to] = _components[from];
    }
}

/// <summary>
/// Stores entities and components in SoA layout for cache efficiency.
/// Enhanced with dirty component tracking for selective processing.
/// </summary>
public sealed class Chunk
{
    private readonly Type[] _componentTypes;
    private readonly IComponentStorage[] _componentStorages;
    private readonly Dictionary<Type, int> _typeToIndex;
    private readonly Entity[] _entities;
    private readonly int _capacity;
    private int _count;
    
    // Dirty tracking system using bitsets for efficient change detection
    private readonly BitArray[] _dirtyBits;          // Per-component dirty bitsets
    private readonly BitArray _entityDirtyBits;      // Per-entity dirty flags
    private int _dirtyVersion;                       // Incremented on any change
    
    public int Count => _count;
    public int Capacity => _capacity;
    
    public Chunk(Type[] componentTypes, int capacity)
    {
        _componentTypes = componentTypes;
        _capacity = capacity;
        _count = 0;
        _dirtyVersion = 0;
        
        // Use cache-line aligned allocation for entities array
        _entities = CacheLineAlignedAllocator.AllocateAligned<Entity>(capacity);
        _componentStorages = new IComponentStorage[componentTypes.Length];
        _typeToIndex = new Dictionary<Type, int>();
        
        // Initialize dirty tracking system
        _dirtyBits = new BitArray[componentTypes.Length];
        _entityDirtyBits = new BitArray(capacity);
        
        // Create typed component storages and initialize dirty tracking
        for (int i = 0; i < componentTypes.Length; i++)
        {
            var componentType = componentTypes[i];
            _typeToIndex[componentType] = i;
            
            // Create storage using factory (avoids reflection for registered types)
            _componentStorages[i] = ComponentStorageFactory.Create(componentType, capacity);
            
            // Initialize dirty bitset for this component type
            _dirtyBits[i] = new BitArray(capacity);
        }
    }
    
    /// <summary>
    /// Adds an entity to this chunk.
    /// </summary>
    public int AddEntity(Entity entity)
    {
        if (_count >= _capacity)
            throw new InvalidOperationException("Chunk is full");
        
        int row = _count;
        _entities[row] = entity;
        _count++;
        
        return row;
    }
    
    /// <summary>
    /// Removes an entity by swapping with last.
    /// </summary>
    public Entity RemoveEntity(int row)
    {
        if (row < 0 || row >= _count)
            throw new ArgumentOutOfRangeException(nameof(row));
        
        _count--;
        
        // Swap with last if not already last
        if (row < _count)
        {
            _entities[row] = _entities[_count];
            
            // Swap component data using typed storages
            for (int i = 0; i < _componentStorages.Length; i++)
            {
                _componentStorages[i].SwapRemove(_count, row);
            }
            
            return _entities[row]; // Return entity that was moved
        }
        
        return Entity.Invalid;
    }
    
    /// <summary>
    /// Gets the entity at the specified row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity GetEntity(int row)
    {
        return _entities[row];
    }
    
    /// <summary>
    /// Gets a typed span for component access.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan<T>() where T : unmanaged
    {
        var type = typeof(T);
        if (_typeToIndex.TryGetValue(type, out var index))
        {
            var storage = _componentStorages[index] as ComponentStorage<T>;
            return storage != null ? storage.GetSpan(_count) : Span<T>.Empty;
        }
        
        return Span<T>.Empty;
    }
    
    /// <summary>
    /// Gets a typed memory for component access to avoid allocations in enumerators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<T> GetMemory<T>() where T : unmanaged
    {
        var type = typeof(T);
        if (_typeToIndex.TryGetValue(type, out var index))
        {
            var storage = _componentStorages[index] as ComponentStorage<T>;
            return storage != null ? storage.GetMemory(_count) : ReadOnlyMemory<T>.Empty;
        }
        
        return ReadOnlyMemory<T>.Empty;
    }
    
    /// <summary>
    /// Gets SIMD-aligned span for vectorized operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSimdSpan<T>() where T : unmanaged
    {
        var type = typeof(T);
        if (_typeToIndex.TryGetValue(type, out var index))
        {
            var storage = _componentStorages[index] as ComponentStorage<T>;
            return storage != null ? storage.GetSimdSpan(_count) : Span<T>.Empty;
        }
        
        return Span<T>.Empty;
    }
    
    /// <summary>
    /// Gets remainder span for scalar processing after SIMD operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetRemainderSpan<T>() where T : unmanaged
    {
        var type = typeof(T);
        if (_typeToIndex.TryGetValue(type, out var index))
        {
            var storage = _componentStorages[index] as ComponentStorage<T>;
            return storage != null ? storage.GetRemainderSpan(_count) : Span<T>.Empty;
        }
        
        return Span<T>.Empty;
    }
    
    /// <summary>
    /// Checks if SIMD operations are supported for the given component type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSimdSupported<T>() where T : unmanaged
    {
        var type = typeof(T);
        if (_typeToIndex.TryGetValue(type, out var index))
        {
            var storage = _componentStorages[index] as ComponentStorage<T>;
            return storage?.IsSimdSupported ?? false;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if this chunk has a component type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>() where T : unmanaged
    {
        return _typeToIndex.ContainsKey(typeof(T));
    }
    
    /// <summary>
    /// Gets a component reference for direct access.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(int row) where T : unmanaged
    {
        var span = GetSpan<T>();
        return ref span[row];
    }
    
    /// <summary>
    /// Gets a component reference for modification with automatic dirty tracking.
    /// Use this when you intend to modify the component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponentForWrite<T>(int row) where T : unmanaged
    {
        MarkDirty<T>(row);
        var span = GetSpan<T>();
        return ref span[row];
    }
    
    /// <summary>
    /// Gets a readonly component reference without dirty tracking.
    /// Use this for read-only access to avoid unnecessary dirty flags.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly T GetComponentReadOnly<T>(int row) where T : unmanaged
    {
        var span = GetSpan<T>();
        return ref span[row];
    }
    
    /// <summary>
    /// Processes components using SIMD vectorization for maximum performance.
    /// Automatically handles both vectorized and remainder elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ProcessVectorized<T>(VectorProcessor<T> processor) where T : unmanaged
    {
        if (!IsSimdSupported<T>() || !Vector.IsHardwareAccelerated)
        {
            // Fallback to scalar processing with prefetching
            var span = GetSpan<T>();
            if (span.Length > 0)
            {
                // Prefetch for scalar processing
                var storage = GetComponentStorage<T>();
                if (storage != null)
                {
                    MemoryPrefetcher.PrefetchSequential(storage.ComponentsArray, 0, MemoryPrefetcher.GetOptimalPrefetchDistance<T>());
                }
            }
            processor.ProcessScalar(span);
            return;
        }
        
        // Process SIMD-aligned elements (prefetching already done in GetSimdSpan)
        var simdSpan = GetSimdSpan<T>();
        if (simdSpan.Length > 0)
        {
            processor.ProcessSimd(simdSpan);
        }
        
        // Process remainder elements with scalar operations
        var remainderSpan = GetRemainderSpan<T>();
        if (remainderSpan.Length > 0)
        {
            processor.ProcessScalar(remainderSpan);
        }
    }
    
    /// <summary>
    /// Gets the component storage for internal prefetching operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ComponentStorage<T>? GetComponentStorage<T>() where T : unmanaged
    {
        var type = typeof(T);
        if (_typeToIndex.TryGetValue(type, out var index))
        {
            return _componentStorages[index] as ComponentStorage<T>;
        }
        return null;
    }
    
    /// <summary>
    /// Applies a vectorized transform to all components of type T in this chunk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TransformVectorized<T>(Func<Vector<T>, Vector<T>> transform) where T : unmanaged
    {
        if (!IsSimdSupported<T>() || !Vector.IsHardwareAccelerated)
            return;
        
        var simdSpan = GetSimdSpan<T>();
        var vectorSize = Vector<T>.Count;
        
        for (int i = 0; i < simdSpan.Length; i += vectorSize)
        {
            var vector = new Vector<T>(simdSpan.Slice(i, vectorSize));
            var result = transform(vector);
            result.CopyTo(simdSpan.Slice(i, vectorSize));
        }
    }
    
    /// <summary>
    /// Marks a component as dirty for the specified entity row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkDirty<T>(int row) where T : unmanaged
    {
        var componentIndex = GetComponentIndex<T>();
        if (componentIndex >= 0 && row < _count)
        {
            _dirtyBits[componentIndex][row] = true;
            _entityDirtyBits[row] = true;
            _dirtyVersion++;
        }
    }
    
    /// <summary>
    /// Marks a component as dirty by type for the specified entity row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkDirty(Type componentType, int row)
    {
        if (_typeToIndex.TryGetValue(componentType, out var componentIndex) && row < _count)
        {
            _dirtyBits[componentIndex][row] = true;
            _entityDirtyBits[row] = true;
            _dirtyVersion++;
        }
    }
    
    /// <summary>
    /// Checks if a component is dirty for the specified entity row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDirty<T>(int row) where T : unmanaged
    {
        var componentIndex = GetComponentIndex<T>();
        return componentIndex >= 0 && row < _count && _dirtyBits[componentIndex][row];
    }
    
    /// <summary>
    /// Checks if a component is dirty by type for the specified entity row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDirty(Type componentType, int row)
    {
        return _typeToIndex.TryGetValue(componentType, out var componentIndex) && 
               row < _count && _dirtyBits[componentIndex][row];
    }
    
    /// <summary>
    /// Checks if any component is dirty for the specified entity row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEntityDirty(int row)
    {
        return row < _count && _entityDirtyBits[row];
    }
    
    /// <summary>
    /// Clears dirty flags for a specific component type across all entities.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearDirty<T>() where T : unmanaged
    {
        var componentIndex = GetComponentIndex<T>();
        if (componentIndex >= 0)
        {
            _dirtyBits[componentIndex].SetAll(false);
        }
    }
    
    /// <summary>
    /// Clears dirty flags for a specific component type across all entities.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearDirty(Type componentType)
    {
        if (_typeToIndex.TryGetValue(componentType, out var componentIndex))
        {
            _dirtyBits[componentIndex].SetAll(false);
        }
    }
    
    /// <summary>
    /// Clears all dirty flags for the specified entity row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearEntityDirty(int row)
    {
        if (row < _count)
        {
            _entityDirtyBits[row] = false;
            for (int i = 0; i < _dirtyBits.Length; i++)
            {
                _dirtyBits[i][row] = false;
            }
        }
    }
    
    /// <summary>
    /// Clears all dirty flags in the chunk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearAllDirty()
    {
        _entityDirtyBits.SetAll(false);
        for (int i = 0; i < _dirtyBits.Length; i++)
        {
            _dirtyBits[i].SetAll(false);
        }
        _dirtyVersion++;
    }
    
    /// <summary>
    /// Gets entities that have dirty components of the specified type.
    /// Returns an enumerable of entity rows with dirty components.
    /// </summary>
    public IEnumerable<int> GetDirtyRows<T>() where T : unmanaged
    {
        var componentIndex = GetComponentIndex<T>();
        if (componentIndex >= 0)
        {
            for (int row = 0; row < _count; row++)
            {
                if (_dirtyBits[componentIndex][row])
                {
                    yield return row;
                }
            }
        }
    }
    
    /// <summary>
    /// Gets entities that have any dirty components.
    /// Returns an enumerable of entity rows with any dirty component.
    /// </summary>
    public IEnumerable<int> GetDirtyEntityRows()
    {
        for (int row = 0; row < _count; row++)
        {
            if (_entityDirtyBits[row])
            {
                yield return row;
            }
        }
    }
    
    /// <summary>
    /// Gets the current dirty version - incremented whenever any component is marked dirty.
    /// Used for cache invalidation and change detection.
    /// </summary>
    public int DirtyVersion => _dirtyVersion;
    
    /// <summary>
    /// Gets the component index for the specified type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetComponentIndex<T>() where T : unmanaged
    {
        return _typeToIndex.TryGetValue(typeof(T), out var index) ? index : -1;
    }
}

/// <summary>
/// Interface for processing components with both SIMD and scalar paths.
/// </summary>
public interface VectorProcessor<T> where T : unmanaged
{
    void ProcessSimd(Span<T> simdSpan);
    void ProcessScalar(Span<T> scalarSpan);
}