namespace PurlieuEcs.Core;

/// <summary>
/// Central ECS world managing entities, archetypes, and systems.
/// </summary>
public sealed class World
{
    private EntityRecord[] _entities;
    private int _entityCapacity;
    private readonly Queue<uint> _freeIds;
    private uint _nextEntityId;
    
    private readonly Dictionary<ArchetypeSignature, Archetype> _signatureToArchetype;
    private readonly Dictionary<ulong, Archetype> _idToArchetype;
    private ulong _nextArchetypeId;
    
    public World(int initialCapacity = 1024)
    {
        _entityCapacity = initialCapacity;
        _entities = new EntityRecord[_entityCapacity];
        _freeIds = new Queue<uint>();
        _nextEntityId = 1; // 0 is reserved for invalid
        
        _signatureToArchetype = new Dictionary<ArchetypeSignature, Archetype>();
        _idToArchetype = new Dictionary<ulong, Archetype>();
        _nextArchetypeId = 1; // 0 is reserved for empty archetype
        
        // Create empty archetype
        var emptySignature = new ArchetypeSignature();
        var emptyArchetype = new Archetype(0, emptySignature, Array.Empty<Type>());
        _signatureToArchetype[emptySignature] = emptyArchetype;
        _idToArchetype[0] = emptyArchetype;
    }
    
    /// <summary>
    /// Creates a new entity with no components.
    /// </summary>
    public Entity CreateEntity()
    {
        uint id;
        uint version = 1;
        
        if (_freeIds.Count > 0)
        {
            id = _freeIds.Dequeue();
            var oldRecord = _entities[(int)id - 1];
            version = oldRecord.Version + 1;
            
            // Add reused entity to empty archetype
            var emptyArchetype = _idToArchetype[0];
            var row = emptyArchetype.AddEntity(new Entity(id, version));
            _entities[(int)id - 1] = new EntityRecord(version, 0, row);
        }
        else
        {
            id = _nextEntityId++;
            
            // Grow array if needed
            if (id > _entityCapacity)
            {
                _entityCapacity *= 2;
                Array.Resize(ref _entities, _entityCapacity);
            }
            
            // Add entity to empty archetype
            var emptyArchetype = _idToArchetype[0];
            var row = emptyArchetype.AddEntity(new Entity(id, version));
            _entities[(int)id - 1] = new EntityRecord(version, 0, row);
        }
        
        return new Entity(id, version);
    }
    
    /// <summary>
    /// Destroys an entity and recycles its ID.
    /// </summary>
    public void DestroyEntity(Entity entity)
    {
        if (!IsAlive(entity))
            return;
        
        ref var record = ref GetRecord(entity);
        
        // Clear from archetype if it has one
        if (record.ArchetypeId != 0 && _idToArchetype.TryGetValue(record.ArchetypeId, out var archetype))
        {
            archetype.RemoveEntity(entity, record.Row);
        }
        
        // Mark as destroyed by incrementing version
        record.Version++;
        record.ArchetypeId = 0;
        record.Row = -1;
        
        _freeIds.Enqueue(entity.Id);
    }
    
    /// <summary>
    /// Checks if an entity is still alive.
    /// </summary>
    public bool IsAlive(Entity entity)
    {
        if (entity.Id == 0 || entity.Id >= _nextEntityId)
            return false;
        
        var record = _entities[(int)entity.Id - 1];
        return record.Version == entity.Version && record.Row != -1;
    }
    
    /// <summary>
    /// Gets the entity record for direct manipulation.
    /// </summary>
    internal ref EntityRecord GetRecord(Entity entity)
    {
        return ref _entities[(int)entity.Id - 1];
    }
    
    /// <summary>
    /// Gets or creates an archetype for the given signature.
    /// </summary>
    public Archetype GetOrCreateArchetype(ArchetypeSignature signature, Type[] componentTypes)
    {
        if (_signatureToArchetype.TryGetValue(signature, out var archetype))
            return archetype;
        
        var id = _nextArchetypeId++;
        archetype = new Archetype(id, signature, componentTypes);
        _signatureToArchetype[signature] = archetype;
        _idToArchetype[id] = archetype;
        
        return archetype;
    }
    
    /// <summary>
    /// Tracks entity metadata.
    /// </summary>
    internal struct EntityRecord
    {
        public uint Version;
        public ulong ArchetypeId;
        public int Row; // Index in archetype chunk
        
        public EntityRecord(uint version, ulong archetypeId, int row)
        {
            Version = version;
            ArchetypeId = archetypeId;
            Row = row;
        }
    }
}