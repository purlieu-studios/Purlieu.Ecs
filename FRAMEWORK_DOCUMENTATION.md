# Purlieu ECS Framework Documentation

## Overview

Purlieu.Ecs is a high-performance, memory-efficient Entity Component System (ECS) framework for .NET 8+. It provides cache-friendly data structures, SIMD optimizations, and zero-allocation query systems designed for game engines and high-performance simulations.

## Key Features

- **Zero-Allocation Operations**: Pooled collections and allocation-free iteration
- **SIMD Optimization**: Hardware-accelerated vectorized operations 
- **Cache-Line Alignment**: Memory layout optimized for modern CPU caches
- **Dirty Component Tracking**: Efficient change detection with BitSet tracking
- **Query Result Caching**: Generation-based cache invalidation system
- **Delta-Based Migration**: Efficient component copying during archetype transitions
- **Deterministic Serialization**: Binary snapshots for save/load and networking
- **Event System**: Ring-buffer based event channels with zero allocations

## Architecture

### Core Components

#### World (`PurlieuEcs.Core.World`)
The central ECS world managing entities, archetypes, and systems.

**Key Properties:**
- `EntityCount`: Total number of active entities
- `ArchetypeCount`: Number of unique component combinations

**Main Methods:**
```csharp
// Entity Management
Entity CreateEntity()
void DestroyEntity(Entity entity)
bool IsAlive(Entity entity)

// Component Operations
void AddComponent<T>(Entity entity, T component) where T : unmanaged
void RemoveComponent<T>(Entity entity) where T : unmanaged
ref T GetComponent<T>(Entity entity) where T : unmanaged
bool HasComponent<T>(Entity entity) where T : unmanaged

// Querying
WorldQuery Query()

// Events
EventChannel<T> Events<T>() where T : unmanaged

// Dirty Tracking
void MarkComponentDirty<T>(Entity entity) where T : unmanaged
bool IsComponentDirty<T>(Entity entity) where T : unmanaged
void ClearDirtyFlags<T>() where T : unmanaged
```

#### Entity (`PurlieuEcs.Core.Entity`)
Lightweight entity identifier with versioning for stale reference detection.

```csharp
public readonly struct Entity
{
    public uint Id { get; }           // Entity identifier
    public uint Version { get; }      // Version for recycled IDs
    public bool IsValid { get; }      // True if not Entity.Invalid
    
    // Factories
    public static Entity FromPacked(ulong packed)
    public ulong ToPacked()
}
```

#### Archetype (`PurlieuEcs.Core.Archetype`)
Represents a unique component combination storing entities in chunks.

**Key Features:**
- **Spatial Locality**: Components ordered by access patterns and size
- **Bloom Filters**: Fast component existence checking with false positives
- **Component Delta Caching**: Efficient archetype migration paths

```csharp
public sealed class Archetype
{
    public ulong Id { get; }
    public ArchetypeSignature Signature { get; }
    public IReadOnlyList<Type> ComponentTypes { get; }
    public int EntityCount { get; }
    
    // Chunk access
    public List<Chunk> GetChunks()
    
    // Bloom filter operations
    public bool MightHaveComponent(Type componentType)
    public bool MightHaveAllComponents(Type[] componentTypes)
}
```

#### Chunk (`PurlieuEcs.Core.Chunk`)
Stores entities and components in Structure-of-Arrays (SoA) layout for cache efficiency.

**Key Features:**
- **Cache-Line Alignment**: Optimal memory layout for CPU caches
- **SIMD Support**: Vectorized operations where possible
- **Dirty Tracking**: BitSet-based change detection per component/entity

```csharp
public sealed class Chunk
{
    public int Count { get; }         // Number of entities in chunk
    public int Capacity { get; }      // Maximum chunk capacity (typically 512)
    
    // Component Access
    public Span<T> GetSpan<T>() where T : unmanaged
    public ReadOnlyMemory<T> GetMemory<T>() where T : unmanaged
    public ref T GetComponent<T>(int row) where T : unmanaged
    
    // SIMD Operations
    public Span<T> GetSimdSpan<T>() where T : unmanaged
    public Span<T> GetRemainderSpan<T>() where T : unmanaged
    public bool IsSimdSupported<T>() where T : unmanaged
    
    // Dirty Tracking
    public void MarkDirty<T>(int row) where T : unmanaged
    public bool IsDirty<T>(int row) where T : unmanaged
    public bool IsEntityDirty(int row)
    public IEnumerable<int> GetDirtyRows<T>() where T : unmanaged
}
```

### Query System

#### WorldQuery (`PurlieuEcs.Query.WorldQuery`)
Fluent API for querying entities by component requirements with caching.

```csharp
// Basic Usage
var query = world.Query()
    .With<Position>()
    .With<Velocity>()
    .Without<Destroyed>();

// Iteration
using var chunks = query.Chunks();
foreach (var chunk in chunks)
{
    var positions = chunk.GetSpan<Position>();
    var velocities = chunk.GetSpan<Velocity>();
    
    // Process components
    for (int i = 0; i < chunk.Count; i++)
    {
        positions[i].X += velocities[i].X * deltaTime;
        positions[i].Y += velocities[i].Y * deltaTime;
    }
}

// Zero-allocation iteration
foreach (var chunk in query.ChunksStack())
{
    // Same processing as above
}
```

#### Query Performance Features

- **Archetype Index**: O(1) lookup of matching archetypes using bloom filters
- **Result Caching**: Query results cached and invalidated only when necessary
- **Selective Cache Invalidation**: Only affected queries invalidated on archetype changes
- **Pool-based Collections**: Reused memory for query results

### Event System

#### EventChannel<T> (`PurlieuEcs.Events.EventChannel<T>`)
Ring-buffer based event system for decoupled communication.

```csharp
// Publishing Events
public struct PlayerDeathEvent
{
    public Entity Player;
    public float Damage;
    public Vector3 Position;
}

var events = world.Events<PlayerDeathEvent>();
events.Publish(new PlayerDeathEvent 
{ 
    Player = playerEntity, 
    Damage = 100f, 
    Position = playerPos 
});

// Consuming Events
events.ConsumeAll(evt => 
{
    // Handle player death
    SpawnDeathEffect(evt.Position);
    RemoveFromLeaderboard(evt.Player);
});

// Zero-allocation consumption
var state = new MyEventState();
events.ConsumeAll(state, (in evt, ref state) => 
{
    // Process event without lambda allocation
});
```

### System Interface

#### ISystem (`PurlieuEcs.Systems.ISystem`)
Base interface for ECS systems with deterministic execution.

```csharp
public class MovementSystem : ISystem
{
    public void Update(World world, float deltaTime)
    {
        var query = world.Query()
            .With<Position>()
            .With<Velocity>();
            
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            // SIMD-optimized movement
            if (chunk.IsSimdSupported<Position>() && chunk.IsSimdSupported<Velocity>())
            {
                ProcessMovementSimd(chunk, deltaTime);
            }
            else
            {
                ProcessMovementScalar(positions, velocities, deltaTime);
            }
        }
    }
    
    private void ProcessMovementSimd(Chunk chunk, float deltaTime)
    {
        // Use chunk.GetSimdSpan<T>() for vectorized operations
        // Handle remainder with chunk.GetRemainderSpan<T>()
    }
}
```

### Performance Optimizations

#### SIMD Vectorization
The framework automatically detects SIMD-compatible component types and provides vectorized access:

```csharp
public struct Position  // SIMD-compatible (3 floats)
{
    public float X, Y, Z;
}

public struct Velocity  // SIMD-compatible (3 floats)
{
    public float X, Y, Z;
}

// In system update:
if (chunk.IsSimdSupported<Position>())
{
    var simdPositions = chunk.GetSimdSpan<Position>();
    var remainderPositions = chunk.GetRemainderSpan<Position>();
    
    // Process SIMD-aligned elements with Vector<T>
    // Process remainder elements with scalar operations
}
```

#### Memory Layout Optimization

**Cache-Line Alignment**: Arrays aligned to 64-byte cache line boundaries
**Component Ordering**: Components sorted by:
1. Access frequency (Position/Velocity first)
2. Size (smaller components grouped together) 
3. Alignment requirements (larger aligned first within size groups)

```csharp
// Framework automatically orders these for optimal cache usage:
public struct Transform    // High priority, medium size (64 bytes)
public struct Position     // High priority, small size (12 bytes)  
public struct Velocity     // High priority, small size (12 bytes)
public struct Health       // Medium priority, small size (4 bytes)
public struct Configuration // Low priority, large size (128+ bytes)
```

#### Dirty Tracking System

Efficient change detection using BitSets for selective processing:

```csharp
// Mark components as dirty when modified
world.MarkComponentDirty<Position>(entity);

// Query only dirty entities
foreach (var entity in world.GetEntitiesWithDirtyComponent<Position>())
{
    // Update only changed positions
    ProcessChangedPosition(entity);
}

// Clear dirty flags after processing
world.ClearDirtyFlags<Position>();
```

### Serialization System

#### WorldSnapshot (`PurlieuEcs.Snapshot.WorldSnapshot`)
Deterministic binary serialization for save/load and networking.

```csharp
// Save world state
var result = WorldSnapshot.Save(world);
if (result.Success)
{
    var snapshotData = result.Value;
    File.WriteAllBytes("world_save.dat", snapshotData);
}

// Load world state  
var loadData = File.ReadAllBytes("world_save.dat");
var loadResult = WorldSnapshot.Load(world, loadData);
if (loadResult.Success)
{
    Console.WriteLine("World loaded successfully");
}
```

**Features:**
- **Deterministic Output**: Identical input always produces identical binary output
- **Cache-Line Aligned**: Binary format optimized for loading performance
- **Checksum Validation**: Detects corruption and version mismatches
- **Component Type Mapping**: Handles type resolution across application versions

## Usage Examples

### Basic Entity Management

```csharp
// Create world
var world = new World(initialCapacity: 10000);

// Register component types for optimal performance
world.RegisterComponent<Position>();
world.RegisterComponent<Velocity>();
world.RegisterComponent<Health>();

// Create entities
var player = world.CreateEntity();
var enemy = world.CreateEntity();

// Add components
world.AddComponent(player, new Position { X = 0, Y = 0, Z = 0 });
world.AddComponent(player, new Velocity { X = 1, Y = 0, Z = 0 });
world.AddComponent(player, new Health { Current = 100, Max = 100 });

world.AddComponent(enemy, new Position { X = 10, Y = 0, Z = 0 });
world.AddComponent(enemy, new Health { Current = 50, Max = 50 });

// Query and process
var movableQuery = world.Query().With<Position>().With<Velocity>();
var aliveQuery = world.Query().With<Health>().Without<Destroyed>();
```

### High-Performance System Processing

```csharp
public class OptimizedMovementSystem : ISystem
{
    public void Update(World world, float deltaTime)
    {
        var query = world.Query()
            .With<Position>()
            .With<Velocity>();
            
        // Zero-allocation iteration
        foreach (var chunk in query.ChunksStack())
        {
            ProcessChunk(chunk, deltaTime);
        }
    }
    
    private static void ProcessChunk(Chunk chunk, float deltaTime)
    {
        // Check for SIMD support
        if (chunk.IsSimdSupported<Position>() && chunk.IsSimdSupported<Velocity>())
        {
            ProcessSimd(chunk, deltaTime);
        }
        else
        {
            ProcessScalar(chunk, deltaTime);
        }
    }
    
    private static void ProcessSimd(Chunk chunk, float deltaTime)
    {
        var positions = chunk.GetSimdSpan<Position>();
        var velocities = chunk.GetSimdSpan<Velocity>();
        
        // Vectorized processing
        for (int i = 0; i < positions.Length; i += Vector<float>.Count)
        {
            // SIMD operations on position/velocity data
        }
        
        // Handle remainder elements
        var remainderPos = chunk.GetRemainderSpan<Position>();
        var remainderVel = chunk.GetRemainderSpan<Velocity>();
        ProcessScalarSpan(remainderPos, remainderVel, deltaTime);
    }
    
    private static void ProcessScalar(Chunk chunk, float deltaTime)
    {
        var positions = chunk.GetSpan<Position>();
        var velocities = chunk.GetSpan<Velocity>();
        ProcessScalarSpan(positions, velocities, deltaTime);
    }
    
    private static void ProcessScalarSpan(Span<Position> positions, 
                                         Span<Velocity> velocities, float deltaTime)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X * deltaTime;
            positions[i].Y += velocities[i].Y * deltaTime; 
            positions[i].Z += velocities[i].Z * deltaTime;
        }
    }
}
```

### Event-Driven Architecture

```csharp
// Define events
public struct CollisionEvent
{
    public Entity EntityA, EntityB;
    public Vector3 ContactPoint;
    public float Impulse;
}

public struct DamageEvent  
{
    public Entity Target;
    public float Amount;
    public Entity Source;
}

// Physics system publishes collision events
public class PhysicsSystem : ISystem
{
    public void Update(World world, float deltaTime)
    {
        var events = world.Events<CollisionEvent>();
        
        // Detect collisions and publish events
        foreach (var collision in DetectCollisions(world))
        {
            events.Publish(new CollisionEvent
            {
                EntityA = collision.EntityA,
                EntityB = collision.EntityB, 
                ContactPoint = collision.Point,
                Impulse = collision.Force
            });
        }
    }
}

// Damage system consumes collision events
public class DamageSystem : ISystem
{
    public void Update(World world, float deltaTime)
    {
        var collisionEvents = world.Events<CollisionEvent>();
        var damageEvents = world.Events<DamageEvent>();
        
        collisionEvents.ConsumeAll(evt =>
        {
            // Convert collision to damage if one entity has weapon component
            if (world.HasComponent<Weapon>(evt.EntityA))
            {
                damageEvents.Publish(new DamageEvent
                {
                    Target = evt.EntityB,
                    Amount = CalculateDamage(evt.Impulse),
                    Source = evt.EntityA
                });
            }
        });
        
        // Process damage events
        damageEvents.ConsumeAll(evt =>
        {
            if (world.HasComponent<Health>(evt.Target))
            {
                ref var health = ref world.GetComponent<Health>(evt.Target);
                health.Current -= evt.Amount;
                
                if (health.Current <= 0)
                {
                    world.AddComponent<Destroyed>(evt.Target);
                }
            }
        });
    }
}
```

## Performance Characteristics

### Benchmarks (Typical Results)

- **Entity Creation**: 1-2M entities/second
- **Component Access**: <1ns per component (direct span access)
- **Query Iteration**: 10-50M entities/second (depends on component count)
- **Archetype Migration**: 100K-1M entities/second (with delta caching)
- **Event Publishing**: 10-50M events/second (ring buffer)

### Memory Usage

- **Entity Overhead**: 16 bytes per entity record
- **Chunk Overhead**: ~200 bytes per chunk + component storage
- **Query Cache**: ~50-200 bytes per cached query result
- **Component Storage**: Aligned to cache boundaries (typically +5-25% overhead)

### Scalability

- **Entities**: Supports millions of entities efficiently
- **Archetypes**: Hundreds to thousands of unique component combinations
- **Components per Entity**: Optimal with 1-10 components, supports 50+
- **Chunk Size**: 512 entities per chunk (optimized for cache lines)

## Best Practices

### Component Design

```csharp
// Good: Small, data-focused components
public struct Position { public float X, Y, Z; }
public struct Velocity { public float X, Y, Z; }
public struct Health { public int Current, Max; }

// Avoid: Large components with mixed concerns
public struct BadComponent 
{
    public Vector3 Position;           // Spatial data
    public Texture2D Sprite;          // Rendering data (reference type!)
    public string Name;               // Meta data (reference type!)
    public PlayerController Logic;    // Behavior (reference type!)
}

// Better: Split into focused components
public struct Transform { public float X, Y, Z; }
public struct Renderable { public int SpriteId; }
public struct NameComponent { public int NameIndex; } // Reference to string table
public struct Controllable; // Tag component for player-controlled entities
```

### System Organization

```csharp
// Group related systems by execution phase
public enum GamePhase
{
    Input,      // Process player input
    Logic,      // Game logic and AI
    Physics,    // Physics simulation
    Animation,  // Update animations
    Rendering,  // Prepare rendering data
    Cleanup     // Remove destroyed entities, clear one-frame data
}

[GamePhase(GamePhase.Physics)]
public class MovementSystem : ISystem { }

[GamePhase(GamePhase.Physics)] 
public class CollisionSystem : ISystem { }

[GamePhase(GamePhase.Cleanup)]
public class DestroyedEntityCleanup : ISystem { }
```

### Memory Management

```csharp
// Use object pooling for temporary collections
public class SystemWithPooling : ISystem
{
    private readonly List<Entity> _tempEntities = new(capacity: 1000);
    
    public void Update(World world, float deltaTime)
    {
        // Reuse list, clear at start
        _tempEntities.Clear();
        
        // Collect entities for processing
        foreach (var chunk in world.Query().With<Position>().ChunksStack())
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                _tempEntities.Add(chunk.GetEntity(i));
            }
        }
        
        // Process collected entities
        ProcessEntities(_tempEntities);
    }
}
```

## Migration Guide

### From Other ECS Frameworks

**From Unity DOTS:**
- Components must be `unmanaged` structs (no class components)
- No `IComponentData` interface required, just `unmanaged` constraint
- Queries use fluent API instead of `EntityQuery`
- Systems implement `ISystem` with `Update(World, float)` signature

**From Arch ECS:**
- Similar archetype-based storage with performance focus
- Purlieu adds SIMD optimizations and cache-line alignment
- Event system built-in vs external event bus
- Dirty tracking integrated vs manual change detection

**From DefaultEcs:**
- More explicit archetype management 
- Built-in serialization and determinism features
- Higher focus on zero-allocation operations
- SIMD optimizations not available in DefaultEcs

## Troubleshooting

### Common Issues

**"Vector<T> boxing in SIMD operations"**
- Ensure component types contain only compatible fields (primitives or float arrays)
- Use `Vector<float>` for composite components rather than `Vector<ComponentType>`

**"Poor query performance"**
- Register component types with `world.RegisterComponent<T>()` for optimal performance
- Avoid queries with too many `Without<T>` clauses (prefer positive queries)
- Cache commonly used queries rather than recreating them

**"Memory allocation in tight loops"**
- Use `query.ChunksStack()` instead of `query.Chunks()` for zero-allocation iteration
- Avoid LINQ operations on component spans
- Pool temporary collections and clear rather than recreate

**"Archetype thrashing"**
- Minimize frequent component add/remove operations
- Use tag components rather than optional data components
- Consider component pools for temporary states

## Advanced Topics

### Custom Component Storage

```csharp
// For special component types, implement custom storage
public class CustomComponentStorage<T> : IComponentStorage where T : unmanaged
{
    // Implement custom storage layout for specialized use cases
    // e.g., compressed storage, GPU-mapped memory, etc.
}
```

### SIMD Component Processing

```csharp
public struct Vector3Component  // SIMD-optimized component
{
    public float X, Y, Z;
    
    // Enable bulk SIMD operations
    public static void ProcessBulkSimd(Span<Vector3Component> components, 
                                      Vector<float> deltaTime)
    {
        var floatSpan = MemoryMarshal.Cast<Vector3Component, float>(components);
        var vectorSize = Vector<float>.Count;
        
        for (int i = 0; i < floatSpan.Length; i += vectorSize)
        {
            var vector = new Vector<float>(floatSpan.Slice(i, vectorSize));
            var result = vector * deltaTime; // Vectorized multiplication
            result.CopyTo(floatSpan.Slice(i, vectorSize));
        }
    }
}
```

This framework provides the foundation for high-performance ECS applications with modern optimization techniques and zero-allocation operation patterns.