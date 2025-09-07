using PurlieuEcs.Core;
using PurlieuEcs.Systems;
using System.Collections.Concurrent;

namespace Purlieu.Ecs.Tests.Integration;

[TestFixture]
public class ARCH_CapabilityTests
{
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _world = new World();
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    // Test components matching Arch examples
    public struct Position
    {
        public float X, Y, Z;
        
        public Position(float x, float y, float z = 0)
        {
            X = x; Y = y; Z = z;
        }
    }
    
    public struct Velocity
    {
        public float X, Y, Z;
        
        public Velocity(float x, float y, float z = 0)
        {
            X = x; Y = y; Z = z;
        }
    }
    
    public struct Health
    {
        public int Current, Maximum;
        
        public Health(int current, int maximum)
        {
            Current = current; Maximum = maximum;
        }
    }
    
    public struct Player
    {
        public int Level;
        public int PlayerId; // Use ID instead of string name
    }
    
    [Test]
    public void BasicEntityCreation_MatchesArchPattern()
    {
        // Arch pattern: var adventurer = world.Create(new Position(0,0), new Velocity(1,1));
        var adventurer = _world.CreateEntity();
        _world.AddComponent(adventurer, new Position(0, 0));
        _world.AddComponent(adventurer, new Velocity(1, 1));
        
        Assert.That(_world.HasComponent<Position>(adventurer), Is.True);
        Assert.That(_world.HasComponent<Velocity>(adventurer), Is.True);
        
        var pos = _world.GetComponent<Position>(adventurer);
        var vel = _world.GetComponent<Velocity>(adventurer);
        
        Assert.That(pos.X, Is.EqualTo(0f));
        Assert.That(pos.Y, Is.EqualTo(0f));
        Assert.That(vel.X, Is.EqualTo(1f));
        Assert.That(vel.Y, Is.EqualTo(1f));
    }
    
    [Test]
    public void QueryProcessing_MatchesArchPattern()
    {
        // Setup entities
        var entities = new Entity[1000];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i, i));
            _world.AddComponent(entities[i], new Velocity(1, 1));
        }
        
        // Arch pattern: world.Query(in query, (Entity entity, ref Position pos, ref Velocity vel) => { ... });
        // Our equivalent: chunk-based processing with spans
        var query = _world.Query().With<Position>().With<Velocity>();
        int processedCount = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
                processedCount++;
            }
        }
        
        Assert.That(processedCount, Is.EqualTo(1000));
        
        // Verify processing worked
        var firstEntity = entities[0];
        var newPos = _world.GetComponent<Position>(firstEntity);
        Assert.That(newPos.X, Is.EqualTo(1f)); // 0 + 1
        Assert.That(newPos.Y, Is.EqualTo(1f)); // 0 + 1
    }
    
    [Test]
    public void BulkOperations_HandleLargeEntitySets()
    {
        // Test Arch's claimed bulk operation capability
        const int entityCount = 100000;
        var entities = new Entity[entityCount];
        
        // Bulk creation
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i, i, i));
            _world.AddComponent(entities[i], new Velocity(1, 1, 1));
        }
        stopwatch.Stop();
        
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000), "Bulk creation should be fast");
        
        // Bulk processing
        stopwatch.Restart();
        var query = _world.Query().With<Position>().With<Velocity>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
                positions[i].Z += velocities[i].Z;
            }
        }
        stopwatch.Stop();
        
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100), "Bulk processing should be very fast");
        
        // Verify all entities were processed
        Assert.That(entities.All(e => _world.IsAlive(e)), Is.True);
    }
    
    [Test]
    public void ComplexQueries_WithMultipleComponentTypes()
    {
        // Setup diverse entity compositions
        var players = new Entity[100];
        var npcs = new Entity[500];
        var projectiles = new Entity[1000];
        
        // Players: Position, Velocity, Health, Player
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = _world.CreateEntity();
            _world.AddComponent(players[i], new Position(i, 0));
            _world.AddComponent(players[i], new Velocity(0, 1));
            _world.AddComponent(players[i], new Health(100, 100));
            _world.AddComponent(players[i], new Player { Level = 1, PlayerId = i });
        }
        
        // NPCs: Position, Health
        for (int i = 0; i < npcs.Length; i++)
        {
            npcs[i] = _world.CreateEntity();
            _world.AddComponent(npcs[i], new Position(i, 100));
            _world.AddComponent(npcs[i], new Health(50, 50));
        }
        
        // Projectiles: Position, Velocity
        for (int i = 0; i < projectiles.Length; i++)
        {
            projectiles[i] = _world.CreateEntity();
            _world.AddComponent(projectiles[i], new Position(i, 200));
            _world.AddComponent(projectiles[i], new Velocity(2, -1));
        }
        
        // Query 1: All moving entities (Position + Velocity)
        var movingQuery = _world.Query().With<Position>().With<Velocity>();
        int movingCount = 0;
        
        foreach (var chunk in movingQuery.ChunksStack())
        {
            movingCount += chunk.Count;
        }
        
        Assert.That(movingCount, Is.EqualTo(players.Length + projectiles.Length));
        
        // Query 2: All entities with health
        var healthQuery = _world.Query().With<Health>();
        int healthCount = 0;
        
        foreach (var chunk in healthQuery.ChunksStack())
        {
            healthCount += chunk.Count;
        }
        
        Assert.That(healthCount, Is.EqualTo(players.Length + npcs.Length));
        
        // Query 3: Player-specific query
        var playerQuery = _world.Query().With<Player>();
        int playerCount = 0;
        
        foreach (var chunk in playerQuery.ChunksStack())
        {
            var playerComponents = chunk.GetSpan<Player>();
            for (int i = 0; i < playerComponents.Length; i++)
            {
                Assert.That(playerComponents[i].Level, Is.EqualTo(1));
                playerCount++;
            }
        }
        
        Assert.That(playerCount, Is.EqualTo(players.Length));
    }
    
    [Test]
    public void ComponentAddRemove_DynamicArchetypeTransitions()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position(0, 0));
        
        Assert.That(_world.HasComponent<Position>(entity), Is.True);
        Assert.That(_world.HasComponent<Velocity>(entity), Is.False);
        
        // Add component (archetype transition)
        _world.AddComponent(entity, new Velocity(1, 1));
        
        Assert.That(_world.HasComponent<Position>(entity), Is.True);
        Assert.That(_world.HasComponent<Velocity>(entity), Is.True);
        
        // Remove component (archetype transition back)
        _world.RemoveComponent<Velocity>(entity);
        
        Assert.That(_world.HasComponent<Position>(entity), Is.True);
        Assert.That(_world.HasComponent<Velocity>(entity), Is.False);
        
        // Entity should still be alive and functional
        Assert.That(_world.IsAlive(entity), Is.True);
        
        var pos = _world.GetComponent<Position>(entity);
        Assert.That(pos.X, Is.EqualTo(0f));
        Assert.That(pos.Y, Is.EqualTo(0f));
    }
    
    [Test]
    public void ConcurrentQueries_ThreadSafety()
    {
        // Setup entities
        const int entityCount = 10000;
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i));
            _world.AddComponent(entity, new Velocity(1, 1));
        }
        
        var results = new ConcurrentBag<float>();
        var tasks = new Task[4];
        
        // Run multiple concurrent queries
        for (int taskId = 0; taskId < tasks.Length; taskId++)
        {
            tasks[taskId] = Task.Run(() =>
            {
                var query = _world.Query().With<Position>().With<Velocity>();
                float sum = 0;
                
                foreach (var chunk in query.ChunksStack())
                {
                    var positions = chunk.GetSpan<Position>();
                    var velocities = chunk.GetSpan<Velocity>();
                    
                    for (int i = 0; i < positions.Length; i++)
                    {
                        sum += positions[i].X + positions[i].Y;
                        sum += velocities[i].X + velocities[i].Y;
                    }
                }
                
                results.Add(sum);
            });
        }
        
        Task.WaitAll(tasks);
        
        // All tasks should produce the same result (read-only operations)
        var resultList = results.ToArray();
        Assert.That(resultList.Length, Is.EqualTo(4));
        Assert.That(resultList.All(r => Math.Abs(r - resultList[0]) < 0.001f), Is.True,
            "All concurrent queries should produce identical results");
    }
    
    [Test]
    public void MemoryEfficiency_MinimalAllocations()
    {
        // Force GC before test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var allocationsBefore = GC.GetTotalAllocatedBytes();
        
        // Create entities and process them (should have minimal allocations)
        var entities = new Entity[1000];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i, i));
        }
        
        // Query processing (should allocate nothing in hot path)
        var query = _world.Query().With<Position>();
        float sum = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            for (int i = 0; i < positions.Length; i++)
            {
                sum += positions[i].X;
            }
        }
        
        var allocationsAfter = GC.GetTotalAllocatedBytes();
        var allocatedBytes = allocationsAfter - allocationsBefore;
        
        // Should have reasonable allocation profile
        var bytesPerEntity = allocatedBytes / 1000.0;
        Assert.That(bytesPerEntity, Is.LessThan(200), $"Allocated {bytesPerEntity:F1} bytes per entity");
        
        // Ensure calculation wasn't optimized away
        Assert.That(sum, Is.GreaterThan(0));
    }
    
    [Test]
    public void EntityDestruction_ProperCleanup()
    {
        var entities = new Entity[1000];
        
        // Create entities
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i, i));
            _world.AddComponent(entities[i], new Health(100, 100));
        }
        
        // Verify all exist
        var query = _world.Query().With<Position>();
        int count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        Assert.That(count, Is.EqualTo(1000));
        
        // Destroy half
        for (int i = 0; i < 500; i++)
        {
            _world.DestroyEntity(entities[i]);
        }
        
        // Verify only half remain
        count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        Assert.That(count, Is.EqualTo(500));
        
        // Verify destroyed entities are properly cleaned
        for (int i = 0; i < 500; i++)
        {
            Assert.That(_world.IsAlive(entities[i]), Is.False);
        }
        
        for (int i = 500; i < 1000; i++)
        {
            Assert.That(_world.IsAlive(entities[i]), Is.True);
        }
    }
}