using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Stores entities and components in SoA layout for cache efficiency.
/// </summary>
internal sealed class Chunk
{
    private readonly Type[] _componentTypes;
    private readonly Array[] _componentArrays;
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
        _componentArrays = new Array[componentTypes.Length];
        
        // Create component arrays
        for (int i = 0; i < componentTypes.Length; i++)
        {
            _componentArrays[i] = Array.CreateInstance(componentTypes[i], capacity);
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
            
            // Swap component data
            for (int i = 0; i < _componentArrays.Length; i++)
            {
                var array = _componentArrays[i];
                var elementType = array.GetType().GetElementType()!;
                var size = Marshal.SizeOf(elementType);
                
                // Simple swap - in production would use unsafe code for performance
                var temp = array.GetValue(_count);
                array.SetValue(array.GetValue(row), _count);
                array.SetValue(temp, row);
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
    public Span<T> GetSpan<T>() where T : struct
    {
        var type = typeof(T);
        for (int i = 0; i < _componentTypes.Length; i++)
        {
            if (_componentTypes[i] == type)
            {
                var array = _componentArrays[i] as T[];
                return new Span<T>(array, 0, _count);
            }
        }
        
        return Span<T>.Empty;
    }
    
    /// <summary>
    /// Checks if this chunk has a component type.
    /// </summary>
    public bool HasComponent<T>() where T : struct
    {
        var type = typeof(T);
        for (int i = 0; i < _componentTypes.Length; i++)
        {
            if (_componentTypes[i] == type)
                return true;
        }
        return false;
    }
}