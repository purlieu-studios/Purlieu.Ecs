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
        
        // Validate SIMD support to prevent boxing in debug builds
        if (_isSimdSupported)
        {
            ValidateSimdSupport();
        }
        
        if (_isSimdSupported)
        {
            // Only align memory for types that benefit from SIMD operations
            int vectorSize = GetEffectiveVectorSize();
            
            // Calculate memory overhead: only align if overhead is reasonable (< 25%)
            var alignedCapacity = (capacity + vectorSize - 1) / vectorSize * vectorSize;
            var overhead = (alignedCapacity - capacity) / (float)capacity;
            
            if (overhead <= 0.25f || capacity >= 64) // Always align for larger chunks
            {
                _components = new T[alignedCapacity];
            }
            else
            {
                // Memory overhead too high for small chunks, fall back to exact size
                _components = new T[capacity];
                _isSimdSupported = false; // Disable SIMD for this storage
            }
        }
        else
        {
            _components = new T[capacity];
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
        return new Span<T>(_components, 0, count);
    }
    
    /// <summary>
    /// Gets whether SIMD operations are supported for this component type.
    /// </summary>
    public bool IsSimdSupported => _isSimdSupported;
    
    /// <summary>
    /// Gets the actual capacity (may be larger due to SIMD alignment).
    /// </summary>
    public int ActualCapacity => _components.Length;
    
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