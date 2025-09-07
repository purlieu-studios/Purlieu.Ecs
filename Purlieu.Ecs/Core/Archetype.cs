using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Represents a unique component combination storing entities in chunks.
/// </summary>
internal sealed class Archetype
{
    private readonly ulong _id;
    private readonly ArchetypeSignature _signature;
    private readonly Type[] _componentTypes;
    private readonly Dictionary<Type, int> _componentTypeToIndex;
    private readonly List<Chunk> _chunks;
    private readonly int _chunkCapacity;
    private readonly ArchetypeBloomFilter _bloomFilter;
    
    public ulong Id => _id;
    public ArchetypeSignature Signature => _signature;
    public IReadOnlyList<Type> ComponentTypes => _componentTypes;
    
    public Archetype(ulong id, ArchetypeSignature signature, Type[] componentTypes, int chunkCapacity = 512)
    {
        _id = id;
        _signature = signature;
        
        // Optimize component type ordering for spatial locality and cache performance
        _componentTypes = OptimizeComponentTypeOrder(componentTypes);
        _componentTypeToIndex = new Dictionary<Type, int>(capacity: _componentTypes.Length);
        _chunks = new List<Chunk>(capacity: 4); // Pre-allocate chunk list
        _chunkCapacity = chunkCapacity;
        _bloomFilter = new ArchetypeBloomFilter(_componentTypes.Length);
        
        for (int i = 0; i < _componentTypes.Length; i++)
        {
            _componentTypeToIndex[_componentTypes[i]] = i;
            _bloomFilter.AddComponentType(_componentTypes[i]);
        }
        
        // Don't create chunks for empty archetype (no components)
    }
    
    /// <summary>
    /// Adds an entity to this archetype.
    /// </summary>
    public int AddEntity(Entity entity)
    {
        // Empty archetype (no components) doesn't use chunks
        if (_componentTypes.Length == 0)
        {
            return 0; // Row doesn't matter for empty archetype
        }
        
        // Find chunk with space
        Chunk? targetChunk = null;
        int chunkIndex = 0;
        for (int i = 0; i < _chunks.Count; i++)
        {
            if (_chunks[i].Count < _chunkCapacity)
            {
                targetChunk = _chunks[i];
                chunkIndex = i;
                break;
            }
        }
        
        // Create new chunk if needed
        if (targetChunk == null)
        {
            targetChunk = new Chunk(_componentTypes, _chunkCapacity);
            _chunks.Add(targetChunk);
            chunkIndex = _chunks.Count - 1;
        }
        
        var localRow = targetChunk.AddEntity(entity);
        // Return global row index (chunk index * capacity + local row)
        return chunkIndex * _chunkCapacity + localRow;
    }
    
    /// <summary>
    /// Removes an entity from this archetype.
    /// </summary>
    public Entity RemoveEntity(Entity entity, int row)
    {
        // Empty archetype doesn't have chunks
        if (_componentTypes.Length == 0)
        {
            return Entity.Invalid;
        }
        
        // Find chunk containing this row
        int chunkIndex = row / _chunkCapacity;
        int localRow = row % _chunkCapacity;
        
        if (chunkIndex < _chunks.Count)
        {
            return _chunks[chunkIndex].RemoveEntity(localRow);
        }
        
        return Entity.Invalid;
    }
    
    /// <summary>
    /// Gets all chunks in this archetype.
    /// </summary>
    public List<Chunk> GetChunks()
    {
        return _chunks;
    }
    
    /// <summary>
    /// Quickly checks if this archetype might have a component (O(1) with possible false positives).
    /// </summary>
    public bool MightHaveComponent(Type componentType)
    {
        return _bloomFilter.MightHaveComponent(componentType);
    }
    
    /// <summary>
    /// Checks if this archetype might have all specified components.
    /// </summary>
    public bool MightHaveAllComponents(Type[] componentTypes)
    {
        return _bloomFilter.MightHaveAllComponents(componentTypes);
    }
    
    /// <summary>
    /// Optimizes component type ordering for better spatial locality and cache performance.
    /// Orders components by: frequency of access, size, and data relationships.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Type[] OptimizeComponentTypeOrder(Type[] componentTypes)
    {
        if (componentTypes.Length <= 1)
            return componentTypes; // No optimization needed for single component
            
        // Create a copy to avoid mutating the original array
        var optimized = new Type[componentTypes.Length];
        componentTypes.CopyTo(optimized, 0);
        
        // Sort components using spatial locality heuristics
        Array.Sort(optimized, ComponentLocalityComparer.Instance);
        
        return optimized;
    }
}

/// <summary>
/// Comparer that orders component types for optimal spatial locality.
/// Uses heuristics based on component size, access patterns, and relationships.
/// </summary>
internal sealed class ComponentLocalityComparer : IComparer<Type>
{
    public static readonly ComponentLocalityComparer Instance = new();
    
    private ComponentLocalityComparer() { }
    
    public int Compare(Type? x, Type? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        
        // 1. Prioritize frequently co-accessed components
        var xPriority = GetAccessPriority(x);
        var yPriority = GetAccessPriority(y);
        if (xPriority != yPriority)
            return yPriority.CompareTo(xPriority); // Higher priority first
        
        // 2. Group small components together (better cache packing)
        var xSize = GetComponentSize(x);
        var ySize = GetComponentSize(y);
        
        // Group small components (≤16 bytes) before larger ones
        bool xSmall = xSize <= 16;
        bool ySmall = ySize <= 16;
        
        if (xSmall && !ySmall) return -1;  // Small components first
        if (!xSmall && ySmall) return 1;
        
        // 3. Within same size category, order by actual size (smaller first for better packing)
        if (xSmall && ySmall)
            return xSize.CompareTo(ySize);
        
        // 4. For large components, order by alignment requirements and size
        var xAlignment = GetAlignmentRequirement(x);
        var yAlignment = GetAlignmentRequirement(y);
        
        if (xAlignment != yAlignment)
            return yAlignment.CompareTo(xAlignment); // Higher alignment first
        
        // 5. Final tiebreaker: type name for deterministic ordering
        return string.Compare(x.FullName, y.FullName, StringComparison.Ordinal);
    }
    
    /// <summary>
    /// Gets access priority based on common ECS usage patterns.
    /// Higher values = accessed more frequently and should be co-located.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetAccessPriority(Type componentType)
    {
        var typeName = componentType.Name;
        
        // High priority: Transform-like components (Position, Velocity, etc.)
        if (typeName.Contains("Position") || typeName.Contains("Transform") ||
            typeName.Contains("Location") || typeName.Contains("Coord"))
            return 100;
            
        if (typeName.Contains("Velocity") || typeName.Contains("Speed") ||
            typeName.Contains("Move") || typeName.Contains("Motion"))
            return 90;
            
        // Medium priority: Commonly accessed gameplay components  
        if (typeName.Contains("Health") || typeName.Contains("HP") ||
            typeName.Contains("Damage") || typeName.Contains("Stats"))
            return 80;
            
        if (typeName.Contains("Render") || typeName.Contains("Sprite") ||
            typeName.Contains("Mesh") || typeName.Contains("Visual"))
            return 70;
        
        // Lower priority: Less frequently accessed components
        if (typeName.Contains("Config") || typeName.Contains("Settings") ||
            typeName.Contains("Static") || typeName.Contains("Const"))
            return 20;
        
        // Default priority for unknown components
        return 50;
    }
    
    /// <summary>
    /// Estimates component size using runtime information and common patterns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetComponentSize(Type componentType)
    {
        // For unmanaged struct types, we can get an approximate size
        if (componentType.IsValueType && componentType.IsLayoutSequential)
        {
            try
            {
                return Marshal.SizeOf(componentType);
            }
            catch
            {
                // Fallback for types where Marshal.SizeOf doesn't work
            }
        }
        
        // Size estimation based on common component patterns
        var typeName = componentType.Name;
        
        // Very small components (1-4 bytes)
        if (typeName.Contains("Flag") || typeName.Contains("Bool") ||
            typeName.Contains("State") && typeName.Length < 15)
            return 4;
        
        // Small components (4-12 bytes) 
        if (typeName.Contains("ID") || typeName.Contains("Index") ||
            typeName.Contains("Count") || typeName.Contains("Timer"))
            return 8;
            
        // Medium components (12-32 bytes) - typical 3D vectors, quaternions
        if (typeName.Contains("Position") || typeName.Contains("Velocity") ||
            typeName.Contains("Vector") || typeName.Contains("Rotation"))
            return 24; // Assume 3 floats or similar
            
        // Larger components
        if (typeName.Contains("Matrix") || typeName.Contains("Transform"))
            return 64; // 4x4 matrix
            
        if (typeName.Contains("Config") || typeName.Contains("Settings") ||
            typeName.Contains("Data") || typeName.Contains("Info"))
            return 128; // Assume larger data structures
        
        // Default estimate for unknown components
        return 32;
    }
    
    /// <summary>
    /// Gets alignment requirement for optimal memory layout.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetAlignmentRequirement(Type componentType)
    {
        var size = GetComponentSize(componentType);
        
        // Determine alignment based on size and content
        if (size >= 64) return 64;    // Large components: cache-line align
        if (size >= 32) return 32;    // Medium components: 32-byte align  
        if (size >= 16) return 16;    // SIMD-friendly: 16-byte align
        if (size >= 8) return 8;      // 64-bit align
        if (size >= 4) return 4;      // 32-bit align
        
        return 1; // Byte aligned
    }
}