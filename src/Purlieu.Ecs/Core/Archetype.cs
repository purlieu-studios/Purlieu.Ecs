using System.Runtime.InteropServices;

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
    
    public ulong Id => _id;
    public ArchetypeSignature Signature => _signature;
    public IReadOnlyList<Type> ComponentTypes => _componentTypes;
    
    public Archetype(ulong id, ArchetypeSignature signature, Type[] componentTypes, int chunkCapacity = 512)
    {
        _id = id;
        _signature = signature;
        _componentTypes = componentTypes;
        _componentTypeToIndex = new Dictionary<Type, int>(capacity: componentTypes.Length);
        _chunks = new List<Chunk>(capacity: 4); // Pre-allocate chunk list
        _chunkCapacity = chunkCapacity;
        
        for (int i = 0; i < componentTypes.Length; i++)
        {
            _componentTypeToIndex[componentTypes[i]] = i;
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
}