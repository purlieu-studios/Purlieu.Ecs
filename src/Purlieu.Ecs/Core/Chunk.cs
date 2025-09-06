using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
/// Typed component storage for zero-boxing access.
/// </summary>
internal sealed class ComponentStorage<T> : IComponentStorage where T : struct
{
    private readonly T[] _components;
    
    public Type ComponentType => typeof(T);
    
    public ComponentStorage(int capacity)
    {
        _components = new T[capacity];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan(int count)
    {
        return new Span<T>(_components, 0, count);
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
}