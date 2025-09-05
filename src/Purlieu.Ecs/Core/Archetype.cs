using System.Runtime.InteropServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Represents a unique component combination storing entities in chunks.
/// </summary>
public sealed class Archetype
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
        _componentTypeToIndex = new Dictionary<Type, int>();
        _chunks = new List<Chunk>();
        _chunkCapacity = chunkCapacity;
        
        for (int i = 0; i < componentTypes.Length; i++)
        {
            _componentTypeToIndex[componentTypes[i]] = i;
        }
        
        // Create first chunk
        if (_componentTypes.Length > 0)
        {
            _chunks.Add(new Chunk(_componentTypes, _chunkCapacity));
        }
    }
    
    /// <summary>
    /// Adds an entity to this archetype.
    /// </summary>
    public int AddEntity(Entity entity)
    {
        // Find chunk with space
        Chunk? targetChunk = null;
        foreach (var chunk in _chunks)
        {
            if (chunk.Count < _chunkCapacity)
            {
                targetChunk = chunk;
                break;
            }
        }
        
        // Create new chunk if needed
        if (targetChunk == null)
        {
            targetChunk = new Chunk(_componentTypes, _chunkCapacity);
            _chunks.Add(targetChunk);
        }
        
        return targetChunk.AddEntity(entity);
    }
    
    /// <summary>
    /// Removes an entity from this archetype.
    /// </summary>
    public void RemoveEntity(Entity entity, int row)
    {
        // Find chunk containing this row
        int chunkIndex = row / _chunkCapacity;
        int localRow = row % _chunkCapacity;
        
        if (chunkIndex < _chunks.Count)
        {
            _chunks[chunkIndex].RemoveEntity(localRow);
        }
    }
}