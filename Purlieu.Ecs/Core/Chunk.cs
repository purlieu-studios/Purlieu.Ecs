using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

namespace PurlieuEcs.Core;

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
internal sealed class ComponentStorage<T> : IComponentStorage where T : struct
{
    private readonly T[] _components;
    private readonly bool _isSimdSupported;
    
    public Type ComponentType => typeof(T);
    
    public ComponentStorage(int capacity)
    {
        _isSimdSupported = Vector.IsHardwareAccelerated && IsSimdCompatible();
        
        if (_isSimdSupported)
        {
            // Align capacity to SIMD boundaries for optimal vectorization
            var vectorSize = Vector<T>.Count;
            var alignedCapacity = (capacity + vectorSize - 1) / vectorSize * vectorSize;
            _components = new T[alignedCapacity];
        }
        else
        {
            _components = new T[capacity];
        }
    }
    
    /// <summary>
    /// Checks if type T is compatible with SIMD operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSimdCompatible()
    {
        // Vector<T> supports primitive numeric types
        var type = typeof(T);
        return type == typeof(int) || type == typeof(float) || type == typeof(double) ||
               type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
               type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
               type == typeof(sbyte);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan(int count)
    {
        return new Span<T>(_components, 0, count);
    }
    
    /// <summary>
    /// Gets span suitable for SIMD operations with proper alignment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSimdSpan(int count)
    {
        if (!_isSimdSupported)
            return GetSpan(count);
            
        // Return span that's safe for SIMD operations
        var vectorSize = Vector<T>.Count;
        var alignedCount = (count / vectorSize) * vectorSize;
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
            
        var vectorSize = Vector<T>.Count;
        var alignedCount = (count / vectorSize) * vectorSize;
        var remainderCount = count - alignedCount;
        
        if (remainderCount > 0)
            return new Span<T>(_components, alignedCount, remainderCount);
        
        return Span<T>.Empty;
    }
    
    public bool IsSimdSupported => _isSimdSupported;
    
    public void SwapRemove(int from, int to)
    {
        _components[to] = _components[from];
    }
}

/// <summary>
/// Stores entities and components in SoA layout for cache efficiency.
/// </summary>
public sealed class Chunk
{
    private readonly Type[] _componentTypes;
    private readonly IComponentStorage[] _componentStorages;
    private readonly Dictionary<Type, int> _typeToIndex;
    private readonly Entity[] _entities;
    private readonly int _capacity;
    private int _count;
    
    public int Count => _count;
    public int Capacity => _capacity;
    
    public Chunk(Type[] componentTypes, int capacity)
    {
        _componentTypes = componentTypes;
        _capacity = capacity;
        _count = 0;
        
        _entities = new Entity[capacity];
        _componentStorages = new IComponentStorage[componentTypes.Length];
        _typeToIndex = new Dictionary<Type, int>();
        
        // Create typed component storages
        for (int i = 0; i < componentTypes.Length; i++)
        {
            var componentType = componentTypes[i];
            _typeToIndex[componentType] = i;
            
            // Create storage using factory (avoids reflection for registered types)
            _componentStorages[i] = ComponentStorageFactory.Create(componentType, capacity);
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
    public Span<T> GetSpan<T>() where T : struct
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
    /// Gets SIMD-aligned span for vectorized operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSimdSpan<T>() where T : struct
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
    public Span<T> GetRemainderSpan<T>() where T : struct
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
    public bool IsSimdSupported<T>() where T : struct
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
    public bool HasComponent<T>() where T : struct
    {
        return _typeToIndex.ContainsKey(typeof(T));
    }
    
    /// <summary>
    /// Gets a component reference for direct access.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(int row) where T : struct
    {
        var span = GetSpan<T>();
        return ref span[row];
    }
    
    /// <summary>
    /// Processes components using SIMD vectorization for maximum performance.
    /// Automatically handles both vectorized and remainder elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ProcessVectorized<T>(VectorProcessor<T> processor) where T : struct
    {
        if (!IsSimdSupported<T>() || !Vector.IsHardwareAccelerated)
        {
            // Fallback to scalar processing
            var span = GetSpan<T>();
            processor.ProcessScalar(span);
            return;
        }
        
        // Process SIMD-aligned elements
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
    /// Applies a vectorized transform to all components of type T in this chunk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TransformVectorized<T>(Func<Vector<T>, Vector<T>> transform) where T : struct
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
}

/// <summary>
/// Interface for processing components with both SIMD and scalar paths.
/// </summary>
public interface VectorProcessor<T> where T : struct
{
    void ProcessSimd(Span<T> simdSpan);
    void ProcessScalar(Span<T> scalarSpan);
}