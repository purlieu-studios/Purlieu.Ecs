using System.Reflection;
using PurlieuEcs.Events;

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
    
    private readonly Dictionary<Type, object> _eventChannels;
    
    public World(int initialCapacity = 1024)
    {
        _entityCapacity = initialCapacity;
        _entities = new EntityRecord[_entityCapacity];
        _freeIds = new Queue<uint>();
        _nextEntityId = 1; // 0 is reserved for invalid
        
        _signatureToArchetype = new Dictionary<ArchetypeSignature, Archetype>();
        _idToArchetype = new Dictionary<ulong, Archetype>();
        _nextArchetypeId = 1; // 0 is reserved for empty archetype
        
        _eventChannels = new Dictionary<Type, object>();
        
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
    internal Archetype GetOrCreateArchetype(ArchetypeSignature signature, Type[] componentTypes)
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
    /// Creates a query builder for finding entities.
    /// </summary>
    public Query.WorldQuery Query()
    {
        return new Query.WorldQuery(this);
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
        var newComponentTypes = currentArchetype.ComponentTypes.Concat(new[] { typeof(T) }).ToArray();
        
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
        var newComponentTypes = currentArchetype.ComponentTypes.Where(t => t != typeof(T)).ToArray();
        
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
        
        // Find the chunk containing this entity
        int chunkIndex = record.Row / 512; // Assuming chunk capacity of 512
        int localRow = record.Row % 512;
        
        var chunks = archetype.GetChunks().ToList();
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
        
        // Store old component data before moving (if needed)
        Dictionary<Type, object> componentData = new Dictionary<Type, object>();
        if (fromArchetype.Id != 0 && oldRow >= 0)
        {
            // Copy component data from old archetype
            int oldChunkIndex = oldRow / 512;
            int oldLocalRow = oldRow % 512;
            var oldChunks = fromArchetype.GetChunks().ToList();
            
            if (oldChunkIndex < oldChunks.Count)
            {
                var oldChunk = oldChunks[oldChunkIndex];
                
                // Store each component that exists in both archetypes
                foreach (var componentType in fromArchetype.ComponentTypes)
                {
                    if (toArchetype.ComponentTypes.Contains(componentType))
                    {
                        // Use reflection to get component data (only at migration time)
                        var getSpanMethod = oldChunk.GetType().GetMethod("GetSpan").MakeGenericMethod(componentType);
                        var span = getSpanMethod.Invoke(oldChunk, null);
                        var spanType = span.GetType();
                        var indexer = spanType.GetProperty("Item");
                        var value = indexer.GetValue(span, new object[] { oldLocalRow });
                        componentData[componentType] = value;
                    }
                }
            }
        }
        
        // Add entity to new archetype
        var newRow = toArchetype.AddEntity(entity);
        
        // Update entity record
        record.ArchetypeId = toArchetype.Id;
        record.Row = newRow;
        
        // Restore component data to new archetype
        if (componentData.Count > 0)
        {
            int newChunkIndex = newRow / 512;
            int newLocalRow = newRow % 512;
            var newChunks = toArchetype.GetChunks().ToList();
            
            if (newChunkIndex < newChunks.Count)
            {
                var newChunk = newChunks[newChunkIndex];
                
                foreach (var kvp in componentData)
                {
                    // Use reflection to set component data (only at migration time)
                    var getSpanMethod = newChunk.GetType().GetMethod("GetSpan").MakeGenericMethod(kvp.Key);
                    var span = getSpanMethod.Invoke(newChunk, null);
                    var spanType = span.GetType();
                    var indexer = spanType.GetProperty("Item");
                    indexer.SetValue(span, kvp.Value, new object[] { newLocalRow });
                }
            }
        }
        
        // Set the new component
        if (!EqualityComparer<T>.Default.Equals(newComponent, default(T)))
        {
            int chunkIndex = newRow / 512;
            int localRow = newRow % 512;
            
            var chunks = toArchetype.GetChunks().ToList();
            if (chunkIndex < chunks.Count)
            {
                var chunk = chunks[chunkIndex];
                if (chunk.HasComponent<T>())
                {
                    chunk.GetComponent<T>(localRow) = newComponent;
                }
            }
        }
        
        // Remove from old archetype
        if (fromArchetype.Id != 0 && oldRow >= 0)
        {
            fromArchetype.RemoveEntity(entity, oldRow);
        }
    }
    
    /// <summary>
    /// Gets all chunks that match the specified component requirements.
    /// TODO: Optimize with archetype registry using bitset indexing for O(1) lookup instead of O(archetypes)
    /// </summary>
    internal IEnumerable<Chunk> GetMatchingChunks(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature)
    {
        foreach (var archetype in _signatureToArchetype.Values)
        {
            // Check if archetype has all required components
            if (!archetype.Signature.IsSupersetOf(withSignature))
                continue;
                
            // Optimized: Use bitwise operations to check forbidden components
            // Check if archetype signature intersects with forbidden signature
            if (archetype.Signature.HasIntersection(withoutSignature))
                continue;
                
            // Return all chunks from this archetype that have entities
            foreach (var chunk in archetype.GetChunks())
            {
                if (chunk.Count > 0)
                    yield return chunk;
            }
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
        // Clear one-frame events
        foreach (var kvp in _eventChannels)
        {
            var eventType = kvp.Key;
            if (eventType.GetCustomAttribute<Components.OneFrameAttribute>() != null)
            {
                var channel = kvp.Value;
                var clearMethod = channel.GetType().GetMethod("Clear");
                clearMethod?.Invoke(channel, null);
            }
        }
        
        // TODO: Clear one-frame components from all chunks
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