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
        
        // Pre-warm WorldQuery to eliminate 16KB cold-start allocation from hot path
        PreWarmWorldQuery();
        
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
        
        // Use selective cache invalidation for new archetype
        _archetypeIndex.InvalidateCacheForNewArchetype(signature);
        
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
    /// Gets query cache performance statistics.
    /// </summary>
    public CacheStatistics GetQueryCacheStatistics()
    {
        return _archetypeIndex.GetCacheStatistics();
    }
    
    /// <summary>
    /// Resets query cache performance statistics.
    /// </summary>
    public void ResetQueryCacheStatistics()
    {
        _archetypeIndex.ResetStatistics();
    }
    
    /// <summary>
    /// Pre-warms WorldQuery static initialization to eliminate 16KB cold-start allocation.
    /// This moves the one-time initialization cost from query hot path to World construction.
    /// </summary>
    private void PreWarmWorldQuery()
    {
        // Create and immediately discard a WorldQuery to trigger static initialization
        _ = new Query.WorldQuery(this);
        
        // Also pre-warm the pools that WorldQuery uses
        var tempList1 = new List<int>(4);
        var tempList2 = new List<int>(2);
        tempList1.Clear();
        tempList2.Clear();
    }
    
    /// <summary>
    /// Registers a component type for optimized operations.
    /// Call this for custom components to avoid reflection.
    /// </summary>
    public void RegisterComponent<T>() where T : unmanaged
    {
        ComponentRegistry.Register<T>();
        ComponentStorageFactory.Register<T>();
        
        // Use selective cache invalidation for newly registered component
        var componentTypes = new Type[] { typeof(T) };
        _archetypeIndex.InvalidateCacheForComponents(componentTypes);
        ComponentDeltaCache.InvalidateCacheForComponents(componentTypes);
        PurlieuEcs.Query.QueryCompiler.InvalidateCacheForComponents(componentTypes);
    }
    
    /// <summary>
    /// Adds a component to an entity.
    /// </summary>
    public void AddComponent<T>(Entity entity, T component) where T : unmanaged
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
    public void RemoveComponent<T>(Entity entity) where T : unmanaged
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
    public ref T GetComponent<T>(Entity entity) where T : unmanaged
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
    public bool HasComponent<T>(Entity entity) where T : unmanaged
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
    private void MoveEntityToArchetype<T>(Entity entity, Archetype fromArchetype, Archetype toArchetype, T newComponent = default) where T : unmanaged
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
                
                // Use delta-based migration for efficient component copying
                var delta = ComponentDeltaCache.GetDelta(fromArchetype, toArchetype);
                
                // Copy shared components using pre-computed indices
                foreach (var componentType in delta.SharedComponentTypes)
                {
                    var (sourceIndex, targetIndex) = delta.SharedComponents[componentType];
                    ComponentRegistry.TryCopy(componentType, oldChunk, oldLocalRow, newChunk, newLocalRow);
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
    public EventChannel<T> Events<T>() where T : unmanaged
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
    /// Marks a component as dirty for the specified entity.
    /// </summary>
    public void MarkComponentDirty<T>(Entity entity) where T : unmanaged
    {
        ref var record = ref GetRecord(entity);
        if (record.ArchetypeId != 0)
        {
            if (_idToArchetype.TryGetValue(record.ArchetypeId, out var archetype))
            {
                var chunkIndex = record.Row >> ChunkCapacityBits; // Efficient division by 512
                var localRow = record.Row & ChunkCapacityMask;   // Efficient modulo 512
                var chunks = archetype.GetChunks();
                if (chunkIndex < chunks.Count)
                {
                    chunks[chunkIndex].MarkDirty<T>(localRow);
                }
            }
        }
    }
    
    /// <summary>
    /// Checks if a component is dirty for the specified entity.
    /// </summary>
    public bool IsComponentDirty<T>(Entity entity) where T : unmanaged
    {
        ref var record = ref GetRecord(entity);
        if (record.ArchetypeId != 0)
        {
            if (_idToArchetype.TryGetValue(record.ArchetypeId, out var archetype))
            {
                var chunkIndex = record.Row >> ChunkCapacityBits;
                var localRow = record.Row & ChunkCapacityMask;
                var chunks = archetype.GetChunks();
                if (chunkIndex < chunks.Count)
                {
                    return chunks[chunkIndex].IsDirty<T>(localRow);
                }
            }
        }
        return false;
    }
    
    /// <summary>
    /// Checks if any component is dirty for the specified entity.
    /// </summary>
    public bool IsEntityDirty(Entity entity)
    {
        ref var record = ref GetRecord(entity);
        if (record.ArchetypeId != 0)
        {
            if (_idToArchetype.TryGetValue(record.ArchetypeId, out var archetype))
            {
                var chunkIndex = record.Row >> ChunkCapacityBits;
                var localRow = record.Row & ChunkCapacityMask;
                var chunks = archetype.GetChunks();
                if (chunkIndex < chunks.Count)
                {
                    return chunks[chunkIndex].IsEntityDirty(localRow);
                }
            }
        }
        return false;
    }
    
    /// <summary>
    /// Clears dirty flags for a specific component type across all entities.
    /// </summary>
    public void ClearDirtyFlags<T>() where T : unmanaged
    {
        foreach (var archetype in _allArchetypes)
        {
            foreach (var chunk in archetype.GetChunks())
            {
                chunk.ClearDirty<T>();
            }
        }
    }
    
    /// <summary>
    /// Clears all dirty flags across all entities and components.
    /// </summary>
    public void ClearAllDirtyFlags()
    {
        foreach (var archetype in _allArchetypes)
        {
            foreach (var chunk in archetype.GetChunks())
            {
                chunk.ClearAllDirty();
            }
        }
    }
    
    /// <summary>
    /// Gets all entities that have dirty components of the specified type.
    /// </summary>
    public IEnumerable<Entity> GetEntitiesWithDirtyComponent<T>() where T : unmanaged
    {
        foreach (var archetype in _allArchetypes)
        {
            var chunks = archetype.GetChunks();
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                foreach (var row in chunk.GetDirtyRows<T>())
                {
                    yield return chunk.GetEntity(row);
                }
            }
        }
    }
    
    /// <summary>
    /// Gets all entities that have any dirty components.
    /// </summary>
    public IEnumerable<Entity> GetDirtyEntities()
    {
        foreach (var archetype in _allArchetypes)
        {
            var chunks = archetype.GetChunks();
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                foreach (var row in chunk.GetDirtyEntityRows())
                {
                    yield return chunk.GetEntity(row);
                }
            }
        }
    }
    
    /// <summary>
    /// Gets the total number of active entities in the world.
    /// </summary>
    public int EntityCount 
    { 
        get 
        {
            int count = 0;
            foreach (var archetype in _allArchetypes)
            {
                count += archetype.EntityCount;
            }
            return count;
        } 
    }
    
    /// <summary>
    /// Gets the total number of archetypes in the world.
    /// </summary>
    public int ArchetypeCount => _allArchetypes.Count;
    
    /// <summary>
    /// Gets all archetypes ordered by their ID for deterministic serialization.
    /// </summary>
    public IReadOnlyList<Archetype> GetArchetypesOrderedById()
    {
        var orderedArchetypes = new List<Archetype>(_allArchetypes);
        orderedArchetypes.Sort((a, b) => a.Id.CompareTo(b.Id));
        return orderedArchetypes;
    }
    
    /// <summary>
    /// Gets all entities in the world in deterministic order.
    /// Entities are returned ordered by archetype ID, then by entity ID.
    /// </summary>
    public IEnumerable<Entity> GetAllEntitiesOrdered()
    {
        var orderedArchetypes = GetArchetypesOrderedById();
        foreach (var archetype in orderedArchetypes)
        {
            var chunks = archetype.GetChunks();
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var entities = new List<Entity>();
                
                // Collect entities from this chunk
                for (int row = 0; row < chunk.Count; row++)
                {
                    entities.Add(chunk.GetEntity(row));
                }
                
                // Sort by entity ID for deterministic order
                entities.Sort((a, b) => a.Id.CompareTo(b.Id));
                
                foreach (var entity in entities)
                {
                    yield return entity;
                }
            }
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