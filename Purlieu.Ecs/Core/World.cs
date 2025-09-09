using System.Reflection;
using System.Runtime.CompilerServices;
using PurlieuEcs.Events;
using PurlieuEcs.Logging;
using PurlieuEcs.Validation;
using PurlieuEcs.Monitoring;
using PurlieuEcs.Query;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace PurlieuEcs.Core;

/// <summary>
/// Central ECS world managing entities, archetypes, and systems.
/// </summary>
public sealed class World : IDisposable
{
    // Optimized constants for chunk calculations (512 = 2^9)
    private const int ChunkCapacity = 512;
    private const int ChunkCapacityBits = 9; // log2(512)
    private const int ChunkCapacityMask = ChunkCapacity - 1; // 511 for fast modulo
    private EntityRecord[] _entities;
    private int _entityCapacity;
    private readonly ConcurrentQueue<uint> _freeIds;
    private long _nextEntityId; // Changed to long for Interlocked operations
    
    internal readonly Dictionary<ArchetypeSignature, Archetype> _signatureToArchetype;
    private readonly Dictionary<ulong, Archetype> _idToArchetype;
    internal readonly List<Archetype> _allArchetypes; // Direct list for allocation-free iteration
    internal readonly ArchetypeIndex _archetypeIndex; // High-performance query index
    private ulong _nextArchetypeId;
    
    private readonly Dictionary<Type, object> _eventChannels;
    private readonly MemoryManager _memoryManager;
    private readonly IEcsLogger _logger;
    private readonly IEcsValidator _validator;
    private readonly IEcsHealthMonitor _healthMonitor;
    private readonly SystemScheduler _systemScheduler;
    
    // Thread safety infrastructure
    internal readonly ReaderWriterLockSlim _queryMutationLock = new(LockRecursionPolicy.NoRecursion);
    private readonly ConcurrentDictionary<ulong, object> _archetypeLocks = new();
    private bool _disposed;
    
    /// <summary>
    /// Gets the logger instance for this world
    /// </summary>
    public IEcsLogger Logger => _logger;
    
    /// <summary>
    /// Gets the validator instance for this world
    /// </summary>
    public IEcsValidator Validator => _validator;
    
    /// <summary>
    /// Gets the health monitor instance for this world
    /// </summary>
    public IEcsHealthMonitor HealthMonitor => _healthMonitor;
    
    /// <summary>
    /// Gets the system scheduler for this world
    /// </summary>
    public SystemScheduler SystemScheduler => _systemScheduler;
    
    public World(int initialCapacity = 1024, IEcsLogger? logger = null, IEcsValidator? validator = null, IEcsHealthMonitor? healthMonitor = null)
    {
        _entityCapacity = initialCapacity;
        _entities = new EntityRecord[_entityCapacity];
        _freeIds = new ConcurrentQueue<uint>();
        _nextEntityId = 1; // 0 is reserved for invalid
        
        // Pre-allocate dictionaries to avoid growth allocations
        _signatureToArchetype = new Dictionary<ArchetypeSignature, Archetype>(capacity: 64);
        _idToArchetype = new Dictionary<ulong, Archetype>(capacity: 64);
        _allArchetypes = new List<Archetype>(capacity: 64);
        _archetypeIndex = new ArchetypeIndex(expectedArchetypes: 64);
        _nextArchetypeId = 1; // 0 is reserved for empty archetype
        
        _eventChannels = new Dictionary<Type, object>(capacity: 32);
        
        // Initialize logger (use null logger if none provided)
        _logger = logger ?? NullEcsLogger.Instance;
        
        // Initialize validator (use null validator in release, debug validator in debug)
        _validator = validator ?? 
#if DEBUG
            new EcsValidator();
#else
            NullEcsValidator.Instance;
#endif
        
        // Initialize health monitor (use null monitor in production unless specified)
        _healthMonitor = healthMonitor ?? 
#if DEBUG
            new EcsHealthMonitor(_logger);
#else
            NullEcsHealthMonitor.Instance;
#endif
        
        // Initialize system scheduler with logging and monitoring
        _systemScheduler = new SystemScheduler(_logger, _healthMonitor);
        
        // Initialize memory manager for automatic cleanup
        _memoryManager = new MemoryManager(this);
        
        // Register common component types to avoid reflection later
        RegisterComponentTypes();
        
        // Pre-warm WorldQuery to eliminate 16KB cold-start allocation from hot path
        PreWarmWorldQuery();
        
        // Pre-warm SIMD operations to eliminate cold-start allocations from query paths
        WarmUpSimdOperations();
        
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
        ThrowIfDisposed();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Use write lock for entity mutation
            _queryMutationLock.EnterWriteLock();
            try
            {
                uint id;
                uint version = 1;
                
                // REGRESSION FIX: Never reuse entity IDs within a session to prevent stale references
                // Historical bug: Entity IDs were being reused causing stale references
                // The regression test EntityId_NeverReused_WithinSession ensures this doesn't happen
                if (false) // Disable ID reuse for now
                {
                    // This code path is intentionally disabled
                }
                else
                {
                    var nextId = Interlocked.Increment(ref _nextEntityId);
                    id = (uint)nextId;
                    
                    // Validate we haven't exceeded maximum entity limit
                    if (nextId > uint.MaxValue)
                    {
                        throw new InvalidOperationException("Maximum entity limit reached (uint.MaxValue)");
                    }
                    
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
                
                var entity = new Entity(id, version);
                
                // Log entity creation
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.EntityCreate, entity.Id);
                }
                
                return entity;
            }
            finally
            {
                _queryMutationLock.ExitWriteLock();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EcsOperation.EntityCreate, 0, CorrelationContext.Current);
            throw new EcsException("Failed to create entity", ex);
        }
        finally
        {
            stopwatch.Stop();
            _healthMonitor.RecordEntityOperation(EcsOperation.EntityCreate, stopwatch.ElapsedTicks);
        }
    }
    
    /// <summary>
    /// Destroys an entity and recycles its ID.
    /// </summary>
    public void DestroyEntity(Entity entity)
    {
        try
        {
            // Use write lock for entity mutation
            _queryMutationLock.EnterWriteLock();
            try
            {
                if (!IsAlive(entity))
                    return;
                
                // Log entity destruction
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.EntityDestroy, entity.Id);
                }
                
                ref var record = ref GetRecord(entity);
                
                // Clear from archetype if it has one
                if (record.ArchetypeId != 0 && _idToArchetype.TryGetValue(record.ArchetypeId, out var archetype))
                {
                    // Use archetype lock for thread-safe entity removal
                    var archetypeLock = GetArchetypeLock(archetype.Id);
                    lock (archetypeLock)
                    {
                        var swappedEntity = archetype.RemoveEntity(entity, record.Row);
                        
                        // If an entity was swapped to fill the removed entity's slot, update its record
                        if (swappedEntity != Entity.Invalid && swappedEntity != entity)
                        {
                            ref var swappedRecord = ref GetRecord(swappedEntity);
                            swappedRecord.Row = record.Row; // The swapped entity now has the destroyed entity's row index
                        }
                    }
                }
                
                // Mark as destroyed by incrementing version
                record.Version++;
                record.ArchetypeId = 0;
                record.Row = -1;
                
                // REGRESSION FIX: Don't enqueue IDs for reuse to prevent stale references
                // _freeIds.Enqueue(entity.Id);
            }
            finally
            {
                _queryMutationLock.ExitWriteLock();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EcsOperation.EntityDestroy, entity.Id, CorrelationContext.Current);
            throw new EntityNotFoundException(entity.Id, "Failed to destroy entity", ex);
        }
    }
    
    /// <summary>
    /// Checks if an entity is still alive.
    /// </summary>
    public bool IsAlive(Entity entity)
    {
        if (entity.Id == 0 || entity.Id > _nextEntityId)
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
        
        // Log archetype creation and record memory event
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var signatureString = string.Join(",", componentTypes.Select(t => t.Name));
            _logger.LogArchetypeOperation(LogLevel.Information, "Created", signatureString, 0);
        }
        
        _healthMonitor.RecordMemoryEvent(MemoryEventType.ArchetypeCreated, 1024); // Estimated archetype overhead
        
        // Use selective cache invalidation for new archetype
        _archetypeIndex.InvalidateCacheForNewArchetype(signature);
        
        return archetype;
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
    /// Pre-warms SIMD operations to eliminate cold-start allocations from query processing.
    /// </summary>
    private static void WarmUpSimdOperations()
    {
        // Static warmup only happens once per application domain
        SimdWarmup.EnsureWarmedUp();
    }
    
    /// <summary>
    /// Static class to handle one-time SIMD warmup across all World instances.
    /// </summary>
    private static class SimdWarmup
    {
        private static bool _warmedUp = false;
        private static readonly object _lock = new object();
        
        public static void EnsureWarmedUp()
        {
            if (_warmedUp || !System.Numerics.Vector.IsHardwareAccelerated)
                return;
                
            lock (_lock)
            {
                if (_warmedUp)
                    return;
                    
                try
                {
                    // Trigger SIMD initialization with minimal operations
                    var dummy = new System.Numerics.Vector<float>(1.0f);
                    var result = dummy + dummy;
                    
                    // Touch Vector<int> as well for comprehensive warmup
                    var intDummy = new System.Numerics.Vector<int>(1);
                    var intResult = intDummy + intDummy;
                    
                    _warmedUp = true;
                }
                catch (Exception)
                {
                    // Ignore SIMD warmup failures to maintain compatibility
                    _warmedUp = true; // Mark as warmed up to prevent retries
                }
            }
        }
    }
    
    /// <summary>
    /// Registers a component type for optimized operations.
    /// Call this for custom components to avoid reflection.
    /// </summary>
    public void RegisterComponent<T>() where T : unmanaged
    {
        try
        {
            ComponentRegistry.Register<T>();
            ComponentStorageFactory.Register<T>();
            
            // Use selective cache invalidation for newly registered component
            var componentTypes = new Type[] { typeof(T) };
            _archetypeIndex.InvalidateCacheForComponents(componentTypes);
            ComponentDeltaCache.InvalidateCacheForComponents(componentTypes);
            PurlieuEcs.Query.QueryCompiler.InvalidateCacheForComponents(componentTypes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EcsOperation.SystemExecute, 0, CorrelationContext.Current);
            throw new EcsException($"Failed to register component type {typeof(T).Name}", ex);
        }
    }
    
    /// <summary>
    /// Adds a component to an entity.
    /// </summary>
    public void AddComponent<T>(Entity entity, T component) where T : unmanaged
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Component addition tracking
            var componentTypeId = ComponentTypeId.Get<T>();
            
            // Use write lock for component mutation
            _queryMutationLock.EnterWriteLock();
            try
            {
                if (!IsAlive(entity))
                    throw new EntityNotFoundException(entity.Id, "Cannot add component to non-existent entity");
                
                // Validate component type and entity operation
                var componentValidation = _validator.ValidateComponentType<T>();
                if (!componentValidation.IsValid && componentValidation.Severity == ValidationSeverity.Error)
                {
                    throw new ComponentException(entity.Id, typeof(T), componentValidation.Message, 
                        new ValidationException(componentValidation.Message));
                }
                
                var operationValidation = _validator.ValidateEntityOperation(EntityOperation.AddComponent, entity.Id, typeof(T));
                if (!operationValidation.IsValid && operationValidation.Severity == ValidationSeverity.Error)
                {
                    throw new ComponentException(entity.Id, typeof(T), operationValidation.Message,
                        new ValidationException(operationValidation.Message));
                }
                
                // Ensure component type is registered for efficient operations
                ComponentRegistry.Register<T>();
                ComponentStorageFactory.Register<T>();
                
                // Log component addition
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.ComponentAdd, entity.Id, typeof(T).Name);
                }
                    
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
            finally
            {
                _queryMutationLock.ExitWriteLock();
            }
        }
        catch (Exception ex) when (!(ex is ComponentException || ex is EntityNotFoundException))
        {
            _logger.LogError(ex, EcsOperation.ComponentAdd, entity.Id, CorrelationContext.Current);
            throw new ComponentException(entity.Id, typeof(T), "Failed to add component", ex);
        }
        finally
        {
            stopwatch.Stop();
            _healthMonitor.RecordEntityOperation(EcsOperation.ComponentAdd, stopwatch.ElapsedTicks);
        }
    }
    
    /// <summary>
    /// Removes a component from an entity.
    /// </summary>
    public void RemoveComponent<T>(Entity entity) where T : unmanaged
    {
        try
        {
            // Use write lock for component mutation
            _queryMutationLock.EnterWriteLock();
            try
            {
                if (!IsAlive(entity))
                    throw new EntityNotFoundException(entity.Id, "Cannot remove component from non-existent entity");
                
                // Log component removal
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.ComponentRemove, entity.Id, typeof(T).Name);
                }
                    
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
            finally
            {
                _queryMutationLock.ExitWriteLock();
            }
        }
        catch (Exception ex) when (!(ex is ComponentException || ex is EntityNotFoundException))
        {
            _logger.LogError(ex, EcsOperation.ComponentRemove, entity.Id, CorrelationContext.Current);
            throw new ComponentException(entity.Id, typeof(T), "Failed to remove component", ex);
        }
    }
    
    /// <summary>
    /// Gets a component from an entity.
    /// </summary>
    public ref T GetComponent<T>(Entity entity) where T : unmanaged
    {
        ThrowIfDisposed();
        try
        {
            if (!IsAlive(entity))
                throw new EntityNotFoundException(entity.Id, "Cannot get component from non-existent entity");
            
            // Log component access at trace level (very frequent operation)
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogEntityOperation(LogLevel.Trace, EcsOperation.ComponentGet, entity.Id, typeof(T).Name);
            }
                
            var record = GetRecord(entity);
            var archetype = _idToArchetype[record.ArchetypeId];
            
            if (!archetype.Signature.Has<T>())
                throw new ComponentException(entity.Id, typeof(T), $"Entity does not have component {typeof(T).Name}");
            
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
            
            throw new ComponentException(entity.Id, typeof(T), $"Entity component {typeof(T).Name} not found in chunk");
        }
        catch (Exception ex) when (!(ex is ComponentException || ex is EntityNotFoundException))
        {
            _logger.LogError(ex, EcsOperation.ComponentGet, entity.Id, CorrelationContext.Current);
            throw new ComponentException(entity.Id, typeof(T), "Failed to get component", ex);
        }
    }
    
    /// <summary>
    /// Checks if an entity has a specific component.
    /// </summary>
    public bool HasComponent<T>(Entity entity) where T : unmanaged
    {
        try
        {
            if (!IsAlive(entity))
                return false;
                
            var record = GetRecord(entity);
            var archetype = _idToArchetype[record.ArchetypeId];
            return archetype.Signature.Has<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EcsOperation.ComponentGet, entity.Id, CorrelationContext.Current);
            return false; // Gracefully handle errors by returning false
        }
    }
    
    /// <summary>
    /// Gets or creates a lock object for the specified archetype ID.
    /// </summary>
    private object GetArchetypeLock(ulong archetypeId)
    {
        return _archetypeLocks.GetOrAdd(archetypeId, _ => new object());
    }
    
    /// <summary>
    /// Moves an entity between archetypes when adding a component.
    /// Uses ordered locking to prevent deadlocks during concurrent archetype transitions.
    /// </summary>
    private void MoveEntityToArchetype<T>(Entity entity, Archetype fromArchetype, Archetype toArchetype, T newComponent = default) where T : unmanaged
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Validate archetype transition
        var transitionValidation = _validator.ValidateArchetypeTransition(
            fromArchetype.ComponentTypes.ToArray(), 
            toArchetype.ComponentTypes.ToArray());
        
        if (!transitionValidation.IsValid && transitionValidation.Severity == ValidationSeverity.Warning)
        {
            // Log warnings but don't throw
            _logger.LogEntityOperation(LogLevel.Warning, EcsOperation.ArchetypeTransition, entity.Id, 
                details: $"Validation warning: {transitionValidation.Message}");
        }
        
        // Log archetype transition
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.ArchetypeTransition, entity.Id, 
                details: $"From:{fromArchetype.Id} To:{toArchetype.Id}");
        }
        
        // Handle self-transitions (same archetype) with single lock
        if (fromArchetype.Id == toArchetype.Id)
        {
            var singleLock = GetArchetypeLock(fromArchetype.Id);
            lock (singleLock)
            {
                PerformArchetypeTransition();
            }
        }
        else
        {
            // Acquire archetype locks in consistent order to prevent deadlocks
            var fromLock = GetArchetypeLock(fromArchetype.Id);
            var toLock = GetArchetypeLock(toArchetype.Id);
            
            // Order locks by archetype ID to ensure consistent lock ordering
            var (firstLock, secondLock) = fromArchetype.Id < toArchetype.Id ? (fromLock, toLock) : (toLock, fromLock);
            
            // Use ordered locking to prevent deadlocks during concurrent archetype transitions
            lock (firstLock)
            {
                lock (secondLock)
                {
                    PerformArchetypeTransition();
                }
            }
        }
        
        void PerformArchetypeTransition()
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
                        
                        // CRITICAL FIX: Thread-safe component copying to prevent enumeration modifications
                        // Create snapshot of component types to avoid enumeration issues during concurrent operations
                        var componentTypes = fromArchetype.ComponentTypes.ToArray();
                        foreach (var componentType in componentTypes)
                        {
                            if (toArchetype.ComponentTypes.Contains(componentType))
                            {
                                // This component exists in both archetypes - copy it
                                var success = ComponentRegistry.TryCopy(componentType, oldChunk, oldLocalRow, newChunk, newLocalRow);
                                if (!success)
                                    throw new EcsException($"Failed to copy component {componentType.Name} during archetype transition for entity {entity}");
                            }
                        }
                    }
                }
                
                // Update entity record
                record.ArchetypeId = toArchetype.Id;
                record.Row = newRow;
                
                // CRITICAL FIX: Ensure archetype is properly indexed after entity is moved
                _archetypeIndex.InvalidateCacheForNewArchetype(toArchetype.Signature);
                
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
        
        stopwatch.Stop();
        _healthMonitor.RecordArchetypeTransition((int)fromArchetype.Id, (int)toArchetype.Id, stopwatch.ElapsedTicks);
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
            // Check if the event type has the OneFrame attribute directly
            bool isOneFrame = ComponentRegistry.IsOneFrame(eventType) || 
                             eventType.GetCustomAttributes(typeof(Components.OneFrameAttribute), false).Length > 0;
            
            if (isOneFrame)
            {
                // Use dynamic dispatch since we have type-erased channels
                if (kvp.Value is IEventChannel channel)
                {
                    channel.Clear();
                }
            }
        }
        
        // Clear one-frame components from all chunks
        var oneFrameTypes = ComponentRegistry.GetOneFrameComponents().ToList();
        if (oneFrameTypes.Count > 0)
        {
            ClearOneFrameComponents(oneFrameTypes);
        }
    }
    
    #region Query Methods
    
    /// <summary>
    /// Creates a query builder for fluent query construction.
    /// </summary>
    public WorldQuery Query() => new WorldQuery(this);
    
    /// <summary>
    /// Executes an action for each entity matching the query (Arch ECS style).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<T1>(ForEachDelegate<T1> action) where T1 : unmanaged
    {
        Query().With<T1>().ForEach(action);
    }
    
    /// <summary>
    /// Executes an action for each entity matching the query (Arch ECS style).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<T1, T2>(ForEachDelegate<T1, T2> action) 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        Query().With<T1>().With<T2>().ForEach(action);
    }
    
    /// <summary>
    /// Executes an action for each entity matching the query (Arch ECS style).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<T1, T2, T3>(ForEachDelegate<T1, T2, T3> action)
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        Query().With<T1>().With<T2>().With<T3>().ForEach(action);
    }
    
    /// <summary>
    /// Executes an action for each entity matching the query (Arch ECS style).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<T1, T2, T3, T4>(ForEachDelegate<T1, T2, T3, T4> action)
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
    {
        Query().With<T1>().With<T2>().With<T3>().With<T4>().ForEach(action);
    }
    
    /// <summary>
    /// Executes an action for each entity matching the query in parallel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ParallelQuery<T1, T2>(ForEachRefDelegate<T1, T2> action)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        Query().With<T1>().With<T2>().ParallelForEach(action);
    }
    
    /// <summary>
    /// Executes an action for each entity matching the query with SIMD optimization when available.
    /// </summary>
    public void QuerySimd<T1, T2>(
        SimdProcessDelegate<T1, T2> simdProcessor,
        ForEachRefDelegate<T1, T2> scalarFallback)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        Query().With<T1>().With<T2>().ForEachSimd(simdProcessor, scalarFallback);
    }
    
    #endregion
    
    /// <summary>
    /// Efficiently clears one-frame components by transitioning entities to new archetypes.
    /// </summary>
    private void ClearOneFrameComponents(List<Type> oneFrameTypes)
    {
        // Keep track of entities that need to be transitioned to avoid concurrent modification
        var entityTransitions = new List<(Entity entity, Archetype fromArchetype, Archetype toArchetype, int row)>();
        
        // Find all archetypes that contain one-frame components
        foreach (var archetype in _allArchetypes.ToList()) // ToList to avoid modification during iteration
        {
            var archetypeTypes = archetype.ComponentTypes;
            var hasOneFrameComponents = false;
            var oneFrameTypesInArchetype = new List<Type>();
            
            // Check which one-frame components this archetype has
            foreach (var oneFrameType in oneFrameTypes)
            {
                if (archetypeTypes.Contains(oneFrameType))
                {
                    hasOneFrameComponents = true;
                    oneFrameTypesInArchetype.Add(oneFrameType);
                }
            }
            
            if (!hasOneFrameComponents) continue;
            
            // Create new archetype without the one-frame components
            var newComponentTypes = archetypeTypes.Where(t => !oneFrameTypesInArchetype.Contains(t)).ToArray();
            var newSignature = BuildSignatureFromTypes(newComponentTypes);
            var targetArchetype = GetOrCreateArchetype(newSignature, newComponentTypes);
            
            // Collect all entities in this archetype for transition
            var chunks = archetype.GetChunks();
            foreach (var chunk in chunks)
            {
                for (int row = 0; row < chunk.Count; row++)
                {
                    var entity = chunk.GetEntity(row);
                    entityTransitions.Add((entity, archetype, targetArchetype, row));
                }
            }
        }
        
        // Perform all transitions
        foreach (var (entity, fromArchetype, toArchetype, oldRow) in entityTransitions)
        {
            if (!IsAlive(entity)) continue; // Entity might have been destroyed
            
            // Get current row position (may have changed due to previous transitions)
            ref var currentRecord = ref GetRecord(entity);
            var currentRow = currentRecord.Row;
            
            // Ensure entity is still in the expected archetype
            if (currentRecord.ArchetypeId != fromArchetype.Id)
                continue; // Entity already moved
            
            // Transfer all components except the one-frame ones
            TransferNonOneFrameComponents(entity, fromArchetype, toArchetype, currentRow, oneFrameTypes);
        }
    }
    
    /// <summary>
    /// Transfers non-one-frame components from source to target archetype.
    /// </summary>
    private void TransferNonOneFrameComponents(Entity entity, Archetype fromArchetype, Archetype toArchetype, 
                                             int oldRow, List<Type> oneFrameTypes)
    {
        // Add entity to target archetype first
        var localRow = toArchetype.AddEntity(entity);
        
        // Copy components that are not one-frame BEFORE removing from source
        var sourceChunks = fromArchetype.GetChunks();
        var targetChunks = toArchetype.GetChunks();
        
        var targetChunkIndex = localRow / ChunkCapacity;
        var targetRowInChunk = localRow % ChunkCapacity;
        
        if (oldRow >= 0 && targetChunkIndex < targetChunks.Count && 
            oldRow / ChunkCapacity < sourceChunks.Count)
        {
            var sourceChunk = sourceChunks[oldRow / ChunkCapacity];
            var targetChunk = targetChunks[targetChunkIndex];
            var sourceRow = oldRow % ChunkCapacity;
            
            // Copy each component that's not a one-frame component
            foreach (var componentType in fromArchetype.ComponentTypes)
            {
                if (!oneFrameTypes.Contains(componentType) && toArchetype.ComponentTypes.Contains(componentType))
                {
                    CopyComponentBetweenChunks(sourceChunk, targetChunk, componentType, sourceRow, targetRowInChunk);
                }
            }
        }
        
        // Update entity record AFTER copying components
        ref var record = ref GetRecord(entity);
        record.ArchetypeId = toArchetype.Id;
        record.Row = localRow;
        
        // Remove from source archetype (this handles entity swapping)
        if (fromArchetype.Id != toArchetype.Id)
        {
            var swappedEntity = fromArchetype.RemoveEntity(entity, oldRow);
            
            // Update swapped entity's record if needed
            if (swappedEntity != Entity.Invalid && swappedEntity != entity)
            {
                ref var swappedRecord = ref GetRecord(swappedEntity);
                swappedRecord.Row = oldRow;
            }
        }
    }
    
    /// <summary>
    /// Copies a component between chunks using reflection (cached for performance).
    /// </summary>
    private void CopyComponentBetweenChunks(Chunk sourceChunk, Chunk targetChunk, Type componentType, 
                                          int sourceRow, int targetRow)
    {
        // This would use the same cached delegate approach as WorldSnapshot
        // For now, using a simplified approach
        
        // Use reflection to copy components generically
        var copyMethod = typeof(World).GetMethod(nameof(CopyComponentGeneric), 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .MakeGenericMethod(componentType);
            
        copyMethod?.Invoke(this, new object[] { sourceChunk, targetChunk, sourceRow, targetRow });
    }
    
    /// <summary>
    /// Generic method to copy a component between chunks.
    /// </summary>
    private void CopyComponentGeneric<T>(Chunk sourceChunk, Chunk targetChunk, int sourceRow, int targetRow) where T : unmanaged
    {
        var sourceSpan = sourceChunk.GetSpan<T>();
        var targetSpan = targetChunk.GetSpan<T>();
        targetSpan[targetRow] = sourceSpan[sourceRow];
    }
    
    /// <summary>
    /// Builds an archetype signature from an array of component types.
    /// </summary>
    private ArchetypeSignature BuildSignatureFromTypes(Type[] componentTypes)
    {
        if (componentTypes.Length == 0)
            return new ArchetypeSignature();
        
        var signature = new ArchetypeSignature();
        foreach (var type in componentTypes)
        {
            var typeId = ComponentTypeId.GetOrCreate(type);
            signature = signature.Add(typeId);
        }
        return signature;
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
    /// Forces immediate memory cleanup and optimization.
    /// </summary>
    public void ForceMemoryCleanup(CleanupLevel level = CleanupLevel.Normal)
    {
        _memoryManager.ForceCleanup(level);
    }
    
    /// <summary>
    /// Gets current memory management statistics.
    /// </summary>
    public MemoryStatistics GetMemoryStatistics()
    {
        return _memoryManager.GetStatistics();
    }
    
    #region System Management
    
    /// <summary>
    /// Register a system for execution in this world
    /// </summary>
    public void RegisterSystem<T>(T system) where T : class, ISystem
    {
        ArgumentNullException.ThrowIfNull(system);
        _systemScheduler.RegisterSystem(system);
        
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.SystemExecute, 0, details: $"Registered system: {typeof(T).Name}");
        }
    }
    
    /// <summary>
    /// Register a function as a system
    /// </summary>
    public void RegisterSystem(SystemFunction function, SystemDependencies dependencies, 
        SystemPhase phase = SystemPhase.Update, int priority = 0, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        _systemScheduler.RegisterSystem(function, dependencies, phase, priority, name);
        
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.SystemExecute, 0, details: $"Registered function system: {name ?? "Anonymous"}");
        }
    }
    
    /// <summary>
    /// Unregister a system
    /// </summary>
    public void UnregisterSystem<T>() where T : class, ISystem
    {
        _systemScheduler.UnregisterSystem<T>();
        
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.SystemExecute, 0, details: $"Unregistered system: {typeof(T).Name}");
        }
    }
    
    /// <summary>
    /// Execute all systems for a specific phase
    /// </summary>
    public void ExecutePhase(SystemPhase phase, float deltaTime)
    {
        try
        {
            _systemScheduler.ExecutePhase(phase, this, deltaTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EcsOperation.SystemExecute, 0, CorrelationContext.Current);
            throw;
        }
    }
    
    /// <summary>
    /// Execute all system phases in order (complete frame update)
    /// </summary>
    public void Update(float deltaTime)
    {
        try
        {
            _systemScheduler.ExecuteAllPhases(this, deltaTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EcsOperation.SystemExecute, 0, CorrelationContext.Current);
            throw;
        }
    }
    
    /// <summary>
    /// Get system execution statistics
    /// </summary>
    public SystemExecutionStatistics GetSystemStatistics()
    {
        return _systemScheduler.GetStatistics();
    }
    
    #endregion
    
    /// <summary>
    /// Throws ObjectDisposedException if the world has been disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(World));
    }
    
    /// <summary>
    /// Disposes the world and cleans up all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        _memoryManager?.Dispose();
        
        // Clear all event channels
        foreach (var channel in _eventChannels.Values)
        {
            if (channel is IEventChannel eventChannel)
            {
                eventChannel.Clear();
            }
        }
        
        _eventChannels.Clear();
        
        // Dispose thread safety infrastructure
        _queryMutationLock?.Dispose();
        _healthMonitor?.Dispose();
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