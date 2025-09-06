using System.Reflection;
using PurlieuEcs.Events;

namespace PurlieuEcs.Core;

/// <summary>
/// Central ECS world managing entities, archetypes, and systems.
/// </summary>
public sealed class World
{
    // Optimized constants for chunk calculations (512 = 2^9)
    private const int ChunkCapacity = 512;
    private const int ChunkCapacityBits = 9; // log2(512)
    private const int ChunkCapacityMask = ChunkCapacity - 1; // 511 for fast modulo
    private EntityRecord[] _entities;
    private int _entityCapacity;
    private readonly Queue<uint> _freeIds;
    private uint _nextEntityId;
    
    internal readonly Dictionary<ArchetypeSignature, Archetype> _signatureToArchetype;
    private readonly Dictionary<ulong, Archetype> _idToArchetype;
    internal readonly List<Archetype> _allArchetypes; // Direct list for allocation-free iteration
    internal readonly ArchetypeIndex _archetypeIndex; // High-performance query index
    private ulong _nextArchetypeId;
    
    private readonly Dictionary<Type, object> _eventChannels;
    
    public World(int initialCapacity = 1024)
    {
        _entityCapacity = initialCapacity;
        _entities = new EntityRecord[_entityCapacity];
        _freeIds = new Queue<uint>();
        _nextEntityId = 1; // 0 is reserved for invalid
        
        // Pre-allocate dictionaries to avoid growth allocations
        _signatureToArchetype = new Dictionary<ArchetypeSignature, Archetype>(capacity: 64);
        _idToArchetype = new Dictionary<ulong, Archetype>(capacity: 64);
        _allArchetypes = new List<Archetype>(capacity: 64);
        _archetypeIndex = new ArchetypeIndex(expectedArchetypes: 64);
        _nextArchetypeId = 1; // 0 is reserved for empty archetype
        
        _eventChannels = new Dictionary<Type, object>(capacity: 32);
        
        // Register common component types to avoid reflection later
        RegisterComponentTypes();
        
        // Create empty archetype
        var emptySignature = new ArchetypeSignature();
        var emptyArchetype = new Archetype(0, emptySignature, Array.Empty<Type>());
        _signatureToArchetype[emptySignature] = emptyArchetype;
        _idToArchetype[0] = emptyArchetype;
        _allArchetypes.Add(emptyArchetype);
        _archetypeIndex.AddArchetype(emptyArchetype);
    }
    
    /// <summary>
    /// Registers known component types with the ComponentRegistry.
    /// </summary>
    private void RegisterComponentTypes()
    {
        // Note: Game-specific components should be registered in the Logic layer
        // This method is available for ECS framework-specific registrations
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
            var swappedEntity = archetype.RemoveEntity(entity, record.Row);
            
            // If an entity was swapped to fill the removed entity's slot, update its record
            if (swappedEntity != Entity.Invalid && swappedEntity != entity)
            {
                ref var swappedRecord = ref GetRecord(swappedEntity);
                swappedRecord.Row = record.Row; // The swapped entity now has the destroyed entity's row index
            }
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
    internal Archetype GetOrCreateArchetype(ArchetypeSignature signature, Type[] componentTypes)
    {
        if (_signatureToArchetype.TryGetValue(signature, out var archetype))
            return archetype;
        
        var id = _nextArchetypeId++;
        archetype = new Archetype(id, signature, componentTypes);
        _signatureToArchetype[signature] = archetype;
        _idToArchetype[id] = archetype;
        _allArchetypes.Add(archetype);
        _archetypeIndex.AddArchetype(archetype);
        
        return archetype;
    }
    
    /// <summary>
    /// Creates a query builder for finding entities.
    /// </summary>
    public Query.WorldQuery Query()
    {
        return new Query.WorldQuery(this);
    }
    
    /// <summary>
    /// Registers a component type for optimized operations.
    /// Call this for custom components to avoid reflection.
    /// </summary>
    public void RegisterComponent<T>() where T : struct
    {
        ComponentRegistry.Register<T>();
        ComponentStorageFactory.Register<T>();
    }
    
    /// <summary>
    /// Adds a component to an entity.
    /// </summary>
    public void AddComponent<T>(Entity entity, T component) where T : struct
    {
        if (!IsAlive(entity))
            return;
            
        ref var record = ref GetRecord(entity);
        var currentArchetype = _idToArchetype[record.ArchetypeId];
        
        // Check if entity already has the component
        if (currentArchetype.Signature.Has<T>())
            return;
        
        var newSignature = currentArchetype.Signature.Add<T>();
        
        // Create new component types array without LINQ allocations
        var oldTypes = currentArchetype.ComponentTypes;
        var newComponentTypes = new Type[oldTypes.Count + 1];
        for (int i = 0; i < oldTypes.Count; i++)
        {
            newComponentTypes[i] = oldTypes[i];
        }
        newComponentTypes[oldTypes.Count] = typeof(T);
        
        var newArchetype = GetOrCreateArchetype(newSignature, newComponentTypes);
        
        // Move entity to new archetype
        MoveEntityToArchetype(entity, currentArchetype, newArchetype, component);
    }
    
    /// <summary>
    /// Removes a component from an entity.
    /// </summary>
    public void RemoveComponent<T>(Entity entity) where T : struct
    {
        if (!IsAlive(entity))
            return;
            
        ref var record = ref GetRecord(entity);
        var currentArchetype = _idToArchetype[record.ArchetypeId];
        
        if (!currentArchetype.Signature.Has<T>())
            return; // Entity doesn't have this component
            
        var newSignature = currentArchetype.Signature.Remove<T>();
        
        // Create new component types array without LINQ allocations
        var oldTypes = currentArchetype.ComponentTypes;
        var targetType = typeof(T);
        var newComponentTypes = new Type[oldTypes.Count - 1];
        int writeIndex = 0;
        
        for (int i = 0; i < oldTypes.Count; i++)
        {
            if (oldTypes[i] != targetType)
            {
                newComponentTypes[writeIndex++] = oldTypes[i];
            }
        }
        
        var newArchetype = GetOrCreateArchetype(newSignature, newComponentTypes);
        
        // Move entity to new archetype
        MoveEntityToArchetype<T>(entity, currentArchetype, newArchetype);
    }
    
    /// <summary>
    /// Gets a component from an entity.
    /// </summary>
    public ref T GetComponent<T>(Entity entity) where T : struct
    {
        if (!IsAlive(entity))
            throw new ArgumentException("Entity is not alive", nameof(entity));
            
        var record = GetRecord(entity);
        var archetype = _idToArchetype[record.ArchetypeId];
        
        if (!archetype.Signature.Has<T>())
            throw new ArgumentException($"Entity does not have component {typeof(T).Name}", nameof(entity));
        
        // Find the chunk containing this entity using fast bit operations
        int chunkIndex = record.Row >> ChunkCapacityBits; // Fast division by 512
        int localRow = record.Row & ChunkCapacityMask; // Fast modulo 512
        
        var chunks = archetype.GetChunks();
        if (chunkIndex < chunks.Count)
        {
            var chunk = chunks[chunkIndex];
            if (chunk.HasComponent<T>())
            {
                return ref chunk.GetComponent<T>(localRow);
            }
        }
        
        throw new ArgumentException($"Entity does not have component {typeof(T).Name}", nameof(entity));
    }
    
    /// <summary>
    /// Checks if an entity has a specific component.
    /// </summary>
    public bool HasComponent<T>(Entity entity) where T : struct
    {
        if (!IsAlive(entity))
            return false;
            
        var record = GetRecord(entity);
        var archetype = _idToArchetype[record.ArchetypeId];
        return archetype.Signature.Has<T>();
    }
    
    /// <summary>
    /// Moves an entity between archetypes when adding a component.
    /// </summary>
    private void MoveEntityToArchetype<T>(Entity entity, Archetype fromArchetype, Archetype toArchetype, T newComponent = default) where T : struct
    {
        ref var record = ref GetRecord(entity);
        var oldRow = record.Row;
        
        // Add entity to new archetype first
        var newRow = toArchetype.AddEntity(entity);
        
        // Copy component data from old to new archetype
        if (fromArchetype.Id != 0 && oldRow >= 0)
        {
            int oldChunkIndex = oldRow >> ChunkCapacityBits;
            int oldLocalRow = oldRow & ChunkCapacityMask;
            int newChunkIndex = newRow >> ChunkCapacityBits;
            int newLocalRow = newRow & ChunkCapacityMask;
            
            var oldChunks = fromArchetype.GetChunks();
            var newChunks = toArchetype.GetChunks();
            
            if (oldChunkIndex < oldChunks.Count && newChunkIndex < newChunks.Count)
            {
                var oldChunk = oldChunks[oldChunkIndex];
                var newChunk = newChunks[newChunkIndex];
                
                // Copy each component that exists in both archetypes
                // Avoid LINQ Contains() call for better performance
                var toComponentTypes = toArchetype.ComponentTypes;
                
                foreach (var componentType in fromArchetype.ComponentTypes)
                {
                    // Manual search to avoid potential LINQ allocations
                    bool found = false;
                    for (int i = 0; i < toComponentTypes.Count; i++)
                    {
                        if (toComponentTypes[i] == componentType)
                        {
                            found = true;
                            break;
                        }
                    }
                    
                    if (found)
                    {
                        ComponentRegistry.TryCopy(componentType, oldChunk, oldLocalRow, newChunk, newLocalRow);
                    }
                }
            }
        }
        
        // Update entity record
        record.ArchetypeId = toArchetype.Id;
        record.Row = newRow;
        
        // Set the new component if provided
        if (!EqualityComparer<T>.Default.Equals(newComponent, default(T)))
        {
            int chunkIndex = newRow >> ChunkCapacityBits;
            int localRow = newRow & ChunkCapacityMask;
            
            var chunks = toArchetype.GetChunks();
            if (chunkIndex < chunks.Count)
            {
                var chunk = chunks[chunkIndex];
                if (chunk.HasComponent<T>())
                {
                    chunk.GetComponent<T>(localRow) = newComponent;
                }
            }
        }
        
        // Remove from old archetype and handle entity swapping
        if (fromArchetype.Id != 0 && oldRow >= 0)
        {
            var swappedEntity = fromArchetype.RemoveEntity(entity, oldRow);
            
            // If an entity was swapped to fill the removed entity's slot, update its record
            if (swappedEntity != Entity.Invalid && swappedEntity != entity)
            {
                ref var swappedRecord = ref GetRecord(swappedEntity);
                swappedRecord.Row = oldRow; // The swapped entity now has the old row index
            }
        }
    }
    
    /// <summary>
    /// Gets all chunks that match the specified component requirements using O(1) archetype index.
    /// </summary>
    internal void GetMatchingChunks(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature, List<Chunk> results)
    {
        results.Clear();
        
        var matchingArchetypes = _archetypeIndex.GetMatchingArchetypes(withSignature, withoutSignature);
        var enumerator = matchingArchetypes.GetChunks();
        
        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }
    }
    
    /// <summary>
    /// Gets matching chunks as an enumerable using O(1) archetype index.
    /// </summary>
    internal IEnumerable<Chunk> GetMatchingChunks(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature)
    {
        var matchingArchetypes = _archetypeIndex.GetMatchingArchetypes(withSignature, withoutSignature);
        var enumerator = matchingArchetypes.GetChunks();
        
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current;
        }
    }
    
    /// <summary>
    /// Gets or creates an event channel for the specified event type.
    /// </summary>
    public EventChannel<T> Events<T>() where T : struct
    {
        var type = typeof(T);
        if (!_eventChannels.TryGetValue(type, out var channel))
        {
            channel = new EventChannel<T>();
            _eventChannels[type] = channel;
        }
        
        return (EventChannel<T>)channel;
    }
    
    /// <summary>
    /// Clears all one-frame events and components.
    /// Call this at the end of each frame.
    /// </summary>
    public void ClearOneFrameData()
    {
        // Clear one-frame events using cached attribute checks
        foreach (var kvp in _eventChannels)
        {
            var eventType = kvp.Key;
            if (ComponentRegistry.IsOneFrame(eventType))
            {
                // Use dynamic dispatch since we have type-erased channels
                if (kvp.Value is IEventChannel channel)
                {
                    channel.Clear();
                }
            }
        }
        
        // Clear one-frame components from all chunks
        var oneFrameTypes = ComponentRegistry.GetOneFrameComponents();
        foreach (var componentType in oneFrameTypes)
        {
            // TODO: Implement component clearing from chunks
            // This would require tracking which chunks contain one-frame components
        }
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