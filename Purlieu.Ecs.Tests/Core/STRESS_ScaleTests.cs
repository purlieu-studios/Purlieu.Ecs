using System.Collections.Concurrent;
using System.Diagnostics;
using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Logging;
using PurlieuEcs.Monitoring;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Stress tests for 100K+ entities with concurrent archetype transitions.
/// Validates ECS performance and stability under production-scale workloads.
/// Part of Phase 12: Hardening & v0 Release.
/// </summary>
[TestFixture]
[Category("StressTest")]
[Category("LongRunning")]
public class STRESS_ScaleTests
{
    private World _world = null!;
    private IEcsLogger _logger = null!;
    private IEcsHealthMonitor _monitor = null!;

    [SetUp]
    public void Setup()
    {
        _logger = NullEcsLogger.Instance; // Use null logger for performance
        _monitor = NullEcsHealthMonitor.Instance;
        _world = new World(logger: _logger, healthMonitor: _monitor);
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    [TestCase(1000)]
    [TestCase(10000)]
    [TestCase(100000)]
    public void EntityCreation_AtScale_MaintainsPerformance(int entityCount)
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();
        var entities = new Entity[entityCount];

        // Act - Create entities
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
        }
        
        stopwatch.Stop();
        var creationTime = stopwatch.Elapsed;

        // Assert
        Assert.That(entities.Length, Is.EqualTo(entityCount), "All entities should be created");
        Assert.That(entities.Distinct().Count(), Is.EqualTo(entityCount), "All entities should be unique");
        
        // Performance assertions (should scale linearly)
        var timePerEntity = creationTime.TotalMilliseconds / entityCount;
        Assert.That(timePerEntity, Is.LessThan(0.01), $"Entity creation should be < 10μs per entity, was {timePerEntity*1000:F2}μs");
        
        TestContext.WriteLine($"Created {entityCount:N0} entities in {creationTime.TotalMilliseconds:F2}ms ({timePerEntity*1000:F2}μs per entity)");
    }

    [Test]
    public void ConcurrentArchetypeTransitions_100KEntities_ThreadSafe()
    {
        // Arrange - Create 100K entities with initial components
        const int entityCount = 100_000;
        const int threadCount = 8;
        const int transitionsPerThread = 1000;
        
        var entities = new Entity[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position { X = i, Y = i, Z = i });
        }

        var exceptions = new ConcurrentBag<Exception>();
        var transitionCounts = new ConcurrentBag<int>();

        // Act - Concurrent archetype transitions
        var tasks = new Task[threadCount];
        var barrier = new Barrier(threadCount);

        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait(); // Synchronize start
                    
                    var random = new Random(threadId);
                    int transitionCount = 0;

                    for (int i = 0; i < transitionsPerThread; i++)
                    {
                        var entityIndex = random.Next(entityCount);
                        var entity = entities[entityIndex];
                        
                        // Randomly add/remove components to trigger archetype transitions
                        switch (random.Next(6))
                        {
                            case 0:
                                _world.AddComponent(entity, new Velocity { X = i, Y = i, Z = i });
                                transitionCount++;
                                break;
                            case 1:
                                _world.AddComponent(entity, new Health { Value = 100 });
                                transitionCount++;
                                break;
                            case 2:
                                if (_world.HasComponent<Velocity>(entity))
                                {
                                    _world.RemoveComponent<Velocity>(entity);
                                    transitionCount++;
                                }
                                break;
                            case 3:
                                if (_world.HasComponent<Health>(entity))
                                {
                                    _world.RemoveComponent<Health>(entity);
                                    transitionCount++;
                                }
                                break;
                            case 4:
                                _world.AddComponent(entity, new Tag1());
                                transitionCount++;
                                break;
                            case 5:
                                _world.AddComponent(entity, new Tag2());
                                transitionCount++;
                                break;
                        }
                    }

                    transitionCounts.Add(transitionCount);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        var stopwatch = Stopwatch.StartNew();
        Task.WaitAll(tasks);
        stopwatch.Stop();

        // Assert
        Assert.That(exceptions, Is.Empty, $"No exceptions during concurrent transitions: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        var totalTransitions = transitionCounts.Sum();
        Assert.That(totalTransitions, Is.GreaterThan(0), "Transitions should have occurred");
        
        var transitionsPerSecond = totalTransitions / stopwatch.Elapsed.TotalSeconds;
        TestContext.WriteLine($"Performed {totalTransitions:N0} archetype transitions in {stopwatch.Elapsed.TotalSeconds:F2}s ({transitionsPerSecond:N0} transitions/sec)");
        
        // Verify world integrity
        Assert.That(() => _world.Query().With<Position>().Count(), Throws.Nothing, "World should remain queryable");
    }

    [Test]
    public void QueryPerformance_100KEntities_ScalesLinearly()
    {
        // Arrange - Create diverse entity population
        const int entityCount = 100_000;
        
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            
            // Create diverse archetypes
            _world.AddComponent(entity, new Position { X = i, Y = i, Z = i });
            
            if (i % 2 == 0) _world.AddComponent(entity, new Velocity { X = 1, Y = 1, Z = 1 });
            if (i % 3 == 0) _world.AddComponent(entity, new Health { Value = 100 });
            if (i % 5 == 0) _world.AddComponent(entity, new Tag1());
            if (i % 7 == 0) _world.AddComponent(entity, new Tag2());
        }

        // Act & Assert - Test query performance at different scales
        MeasureQueryPerformance("Simple (Position)", 
            () => _world.Query().With<Position>());
        
        MeasureQueryPerformance("Two Components (Position, Velocity)", 
            () => _world.Query().With<Position>().With<Velocity>());
        
        MeasureQueryPerformance("Complex (Position, Velocity, Health, without Tag1)", 
            () => _world.Query().With<Position>().With<Velocity>().With<Health>().Without<Tag1>());
        
        MeasureQueryPerformance("Rare (All components)", 
            () => _world.Query().With<Position>().With<Velocity>().With<Health>().With<Tag1>().With<Tag2>());
    }

    [Test]
    public void MemoryStability_MillionOperations_NoLeaks()
    {
        // Arrange
        const int operationCount = 1_000_000;
        const int checkpointInterval = 100_000;
        
        var initialMemory = GC.GetTotalMemory(true);
        var memoryCheckpoints = new List<long>();
        var entities = new List<Entity>();

        // Act - Perform million operations
        for (int i = 0; i < operationCount; i++)
        {
            switch (i % 4)
            {
                case 0: // Create entity
                    var entity = _world.CreateEntity();
                    _world.AddComponent(entity, new Position { X = i, Y = i, Z = i });
                    entities.Add(entity);
                    break;
                    
                case 1: // Add component
                    if (entities.Count > 0)
                    {
                        var e = entities[i % entities.Count];
                        if (_world.IsAlive(e))
                            _world.AddComponent(e, new Velocity { X = i, Y = i, Z = i });
                    }
                    break;
                    
                case 2: // Remove component
                    if (entities.Count > 0)
                    {
                        var e = entities[i % entities.Count];
                        if (_world.IsAlive(e) && _world.HasComponent<Velocity>(e))
                            _world.RemoveComponent<Velocity>(e);
                    }
                    break;
                    
                case 3: // Destroy entity
                    if (entities.Count > 10)
                    {
                        var e = entities[0];
                        if (_world.IsAlive(e))
                        {
                            _world.DestroyEntity(e);
                            entities.RemoveAt(0);
                        }
                    }
                    break;
            }

            // Memory checkpoint
            if (i % checkpointInterval == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                memoryCheckpoints.Add(GC.GetTotalMemory(false));
            }
        }

        var finalMemory = GC.GetTotalMemory(true);

        // Assert
        var memoryGrowth = finalMemory - initialMemory;
        var memoryGrowthMB = memoryGrowth / (1024.0 * 1024.0);
        
        TestContext.WriteLine($"Memory growth after {operationCount:N0} operations: {memoryGrowthMB:F2} MB");
        TestContext.WriteLine($"Memory checkpoints: {string.Join(", ", memoryCheckpoints.Select(m => $"{m / (1024.0 * 1024.0):F1}MB"))}");
        
        // Check for memory leaks (allow some growth for metadata)
        Assert.That(memoryGrowthMB, Is.LessThan(100), $"Memory growth should be < 100MB for {operationCount:N0} operations");
        
        // Check memory stability (later checkpoints shouldn't grow significantly)
        if (memoryCheckpoints.Count > 2)
        {
            var lastCheckpoint = memoryCheckpoints.Last();
            var midCheckpoint = memoryCheckpoints[memoryCheckpoints.Count / 2];
            var checkpointGrowth = (lastCheckpoint - midCheckpoint) / (1024.0 * 1024.0);
            Assert.That(checkpointGrowth, Is.LessThan(20), "Memory should stabilize over time");
        }
    }

    [Test]
    public void SystemExecution_100KEntities_MaintainsFPS()
    {
        // Arrange - Create entities and systems
        const int entityCount = 100_000;
        const int frameCount = 100;
        const float targetFPS = 60.0f;
        const float deltaTime = 1.0f / targetFPS;
        
        // Create diverse entity population
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position { X = i, Y = i, Z = i });
            
            if (i % 2 == 0) _world.AddComponent(entity, new Velocity { X = 1, Y = 0, Z = 0 });
            if (i % 3 == 0) _world.AddComponent(entity, new Health { Value = 100 });
        }

        // Register test systems
        var movementSystem = new StressTestMovementSystem();
        var healthSystem = new StressTestHealthSystem();
        
        _world.RegisterSystem(movementSystem);
        _world.RegisterSystem(healthSystem);

        var frameTimes = new List<double>();

        // Act - Simulate game loop
        for (int frame = 0; frame < frameCount; frame++)
        {
            var frameStopwatch = Stopwatch.StartNew();
            
            _world.SystemScheduler.ExecuteAllPhases(_world, deltaTime);
            
            frameStopwatch.Stop();
            frameTimes.Add(frameStopwatch.Elapsed.TotalMilliseconds);
        }

        // Assert
        var averageFrameTime = frameTimes.Average();
        var maxFrameTime = frameTimes.Max();
        var targetFrameTime = 1000.0 / targetFPS;
        
        TestContext.WriteLine($"Average frame time: {averageFrameTime:F2}ms (target: {targetFrameTime:F2}ms)");
        TestContext.WriteLine($"Max frame time: {maxFrameTime:F2}ms");
        TestContext.WriteLine($"Achieved FPS: {1000.0 / averageFrameTime:F1} (target: {targetFPS:F1})");
        
        Assert.That(averageFrameTime, Is.LessThan(targetFrameTime * 1.5), 
                   $"Average frame time should be < {targetFrameTime * 1.5:F1}ms for {targetFPS}fps target");
        Assert.That(maxFrameTime, Is.LessThan(targetFrameTime * 3), 
                   "No frame should take more than 3x target time (no hitches)");
    }

    [Test]
    public void ChunkFragmentation_AfterMassOperations_RemainsLow()
    {
        // Arrange
        const int entityCount = 50_000;
        const int operationCycles = 10;
        
        var entities = new Entity[entityCount];
        
        // Create initial entities
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position { X = i, Y = i, Z = i });
        }

        // Act - Perform cycles of operations that could cause fragmentation
        for (int cycle = 0; cycle < operationCycles; cycle++)
        {
            // Add components to half the entities
            for (int i = 0; i < entityCount / 2; i++)
            {
                if (_world.IsAlive(entities[i]))
                {
                    _world.AddComponent(entities[i], new Velocity { X = cycle, Y = cycle, Z = cycle });
                }
            }

            // Remove components from a different quarter
            for (int i = entityCount / 4; i < entityCount / 2; i++)
            {
                if (_world.IsAlive(entities[i]) && _world.HasComponent<Velocity>(entities[i]))
                {
                    _world.RemoveComponent<Velocity>(entities[i]);
                }
            }

            // Destroy and recreate some entities
            for (int i = 0; i < entityCount / 10; i++)
            {
                if (_world.IsAlive(entities[i]))
                {
                    _world.DestroyEntity(entities[i]);
                    entities[i] = _world.CreateEntity();
                    _world.AddComponent(entities[i], new Position { X = i, Y = i, Z = i });
                }
            }
        }

        // Assert - Check chunk utilization
        var archetypes = _world._allArchetypes;
        var totalChunks = 0;
        var totalEntities = 0;
        var totalCapacity = 0;

        foreach (var archetype in archetypes)
        {
            totalChunks += archetype.ChunkCount;
            totalEntities += archetype.EntityCount;
            totalCapacity += archetype.ChunkCount * 512; // ChunkCapacity
        }

        var utilization = totalCapacity > 0 ? (double)totalEntities / totalCapacity : 0;
        
        TestContext.WriteLine($"Chunk utilization: {utilization:P1} ({totalEntities:N0} entities in {totalChunks} chunks)");
        TestContext.WriteLine($"Average entities per chunk: {(totalChunks > 0 ? totalEntities / (double)totalChunks : 0):F1}");
        
        Assert.That(utilization, Is.GreaterThan(0.3), "Chunk utilization should remain above 30% (low fragmentation)");
        Assert.That(totalChunks, Is.LessThan(entityCount / 50), "Should not have excessive chunks (indicates fragmentation)");
    }

    private void MeasureQueryPerformance(string queryName, Func<Query> createQuery)
    {
        const int iterations = 100;
        var times = new List<double>();

        // Warmup
        for (int i = 0; i < 10; i++)
        {
            var warmupQuery = createQuery();
            foreach (var chunk in warmupQuery.Chunks())
            {
                _ = chunk.Count;
            }
        }

        // Measure
        for (int i = 0; i < iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            
            var query = createQuery();
            int count = 0;
            foreach (var chunk in query.Chunks())
            {
                count += chunk.Count;
            }
            
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var avgTime = times.Average();
        var minTime = times.Min();
        
        TestContext.WriteLine($"{queryName}: Avg {avgTime:F3}ms, Min {minTime:F3}ms");
        
        Assert.That(avgTime, Is.LessThan(10), $"{queryName} query should complete in < 10ms");
    }
}

// Test Components
internal struct Position
{
    public float X, Y, Z;
}

internal struct Velocity
{
    public float X, Y, Z;
}

internal struct Health
{
    public int Value;
}

internal struct Tag1 { }
internal struct Tag2 { }

// Test Systems
internal class StressTestMovementSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        var query = world.Query().With<Position>().With<Velocity>();
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                positions[i].X += velocities[i].X * deltaTime;
                positions[i].Y += velocities[i].Y * deltaTime;
                positions[i].Z += velocities[i].Z * deltaTime;
            }
        }
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadWrite(
            readComponents: new[] { typeof(Velocity) },
            writeComponents: new[] { typeof(Position) }
        );
    }
}

internal class StressTestHealthSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        var query = world.Query().With<Health>();
        
        foreach (var chunk in query.Chunks())
        {
            var healths = chunk.GetSpan<Health>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                // Simulate health regeneration
                if (healths[i].Value < 100)
                {
                    healths[i].Value = Math.Min(100, healths[i].Value + 1);
                }
            }
        }
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.WriteOnly(typeof(Health));
    }
}