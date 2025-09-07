using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Logging;
using PurlieuEcs.Monitoring;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class THREAD_ConcurrencyTests
{
    private World _world;
    private TestLogger _logger;
    private EcsHealthMonitor _healthMonitor;
    
    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger();
        _healthMonitor = new EcsHealthMonitor(_logger);
        _world = new World(logger: _logger, healthMonitor: _healthMonitor);
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
        _healthMonitor?.Dispose();
    }
    
    #region Concurrent Readers and Writers Tests
    
    [Test]
    public void ConcurrentReadersAndWriters_BasicScenario()
    {
        // Arrange
        const int readerCount = 4;
        const int writerCount = 2;
        const int operationsPerThread = 1000;
        var tasks = new Task[readerCount + writerCount];
        var exceptions = new ConcurrentBag<Exception>();
        var totalEntitiesRead = new ConcurrentDictionary<int, int>();
        var entitiesCreated = new ConcurrentBag<Entity>();
        
        // Pre-populate with some entities for readers
        var initialEntities = new List<Entity>();
        for (int i = 0; i < 500; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            _world.AddComponent(entity, new Velocity(1, 1, 1));
            initialEntities.Add(entity);
        }
        
        Console.WriteLine($"Pre-populated {initialEntities.Count} entities");
        
        // Act - Start concurrent readers
        for (int i = 0; i < readerCount; i++)
        {
            var readerId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    int readCount = 0;
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        // Query entities with Position component
                        _world.Select()
                            .With<Position>()
                            .ForEach((Entity entity, ref Position pos) =>
                            {
                                readCount++;
                                // Simulate some work
                                var x = pos.X + pos.Y + pos.Z;
                            });
                        
                        // Also test other query methods
                        var count = _world.Select().With<Position>().Count();
                        var hasAny = _world.Select().With<Velocity>().Any();
                        
                        if (op % 100 == 0)
                        {
                            Thread.Sleep(1); // Allow writers to interleave
                        }
                    }
                    totalEntitiesRead[readerId] = readCount;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        // Start concurrent writers
        for (int i = 0; i < writerCount; i++)
        {
            var writerId = i;
            tasks[readerCount + i] = Task.Run(() =>
            {
                try
                {
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        // Create entities
                        var entity = _world.CreateEntity();
                        _world.AddComponent(entity, new Position(writerId * 1000 + op, op, writerId));
                        
                        // Add/remove components randomly
                        if (op % 3 == 0)
                        {
                            _world.AddComponent(entity, new Velocity(op, op, op));
                        }
                        
                        entitiesCreated.Add(entity);
                        
                        // Occasionally destroy some initial entities
                        if (op % 50 == 0 && initialEntities.Count > 100)
                        {
                            var randomIndex = Random.Shared.Next(initialEntities.Count);
                            var entityToDestroy = initialEntities[randomIndex];
                            if (_world.IsAlive(entityToDestroy))
                            {
                                _world.DestroyEntity(entityToDestroy);
                                initialEntities.RemoveAt(randomIndex);
                            }
                        }
                        
                        if (op % 100 == 0)
                        {
                            Thread.Sleep(1); // Allow readers to interleave
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        // Wait for all tasks to complete
        Task.WaitAll(tasks);
        
        // Assert
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Expected no exceptions, but got: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        Console.WriteLine($"Readers processed entities: {string.Join(", ", totalEntitiesRead.Values)}");
        Console.WriteLine($"Writers created {entitiesCreated.Count} entities");
        Console.WriteLine($"Final entity count: {_world.Select().With<Position>().Count()}");
        
        // Verify final state integrity
        var finalCount = _world.Select().With<Position>().Count();
        Assert.That(finalCount, Is.GreaterThan(0), "Should have entities remaining after concurrent operations");
    }
    
    [Test]
    public void ConcurrentReadersAndWriters_ArchetypeTransitions()
    {
        // Arrange
        const int threadCount = 6;
        const int operationsPerThread = 500;
        var tasks = new Task[threadCount];
        var exceptions = new ConcurrentBag<Exception>();
        
        // Pre-populate with entities in different archetypes
        var entities = new ConcurrentBag<Entity>();
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            if (i % 2 == 0) _world.AddComponent(entity, new Velocity(1, 1, 1));
            if (i % 3 == 0) _world.AddComponent(entity, new Health(100, 100));
            entities.Add(entity);
        }
        
        // Act - Concurrent archetype transitions and queries
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        if (threadId % 2 == 0)
                        {
                            // Reader threads - query different archetypes
                            var positionCount = _world.Select().With<Position>().Count();
                            var velocityCount = _world.Select().With<Velocity>().Count();
                            var healthCount = _world.Select().With<Health>().Count();
                            
                            // Query with multiple components
                            _world.Select()
                                .With<Position>()
                                .With<Velocity>()
                                .ForEach((Entity e, ref Position pos, ref Velocity vel) =>
                                {
                                    pos.X += vel.X * 0.1f;
                                });
                        }
                        else
                        {
                            // Writer threads - perform archetype transitions
                            var entityArray = entities.ToArray();
                            if (entityArray.Length > 0)
                            {
                                var randomEntity = entityArray[Random.Shared.Next(entityArray.Length)];
                                
                                if (_world.IsAlive(randomEntity))
                                {
                                    // Add or remove components randomly
                                    switch (op % 6)
                                    {
                                        case 0:
                                            if (!_world.HasComponent<Velocity>(randomEntity))
                                                _world.AddComponent(randomEntity, new Velocity(op, op, op));
                                            break;
                                        case 1:
                                            if (_world.HasComponent<Velocity>(randomEntity))
                                                _world.RemoveComponent<Velocity>(randomEntity);
                                            break;
                                        case 2:
                                            if (!_world.HasComponent<Health>(randomEntity))
                                                _world.AddComponent(randomEntity, new Health(op, op));
                                            break;
                                        case 3:
                                            if (_world.HasComponent<Health>(randomEntity))
                                                _world.RemoveComponent<Health>(randomEntity);
                                            break;
                                        case 4:
                                            // Create new entity
                                            var newEntity = _world.CreateEntity();
                                            _world.AddComponent(newEntity, new Position(op, op, op));
                                            entities.Add(newEntity);
                                            break;
                                        case 5:
                                            // Destroy entity
                                            _world.DestroyEntity(randomEntity);
                                            break;
                                    }
                                }
                            }
                        }
                        
                        if (op % 50 == 0)
                        {
                            Thread.Sleep(1); // Allow thread interleaving
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        // Wait for all tasks to complete
        Task.WaitAll(tasks);
        
        // Assert
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Expected no exceptions during concurrent archetype transitions, but got: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        // Verify final state consistency
        var finalPositionCount = _world.Select().With<Position>().Count();
        var finalVelocityCount = _world.Select().With<Velocity>().Count();
        var finalHealthCount = _world.Select().With<Health>().Count();
        
        Console.WriteLine($"Final counts - Position: {finalPositionCount}, Velocity: {finalVelocityCount}, Health: {finalHealthCount}");
        
        Assert.That(finalPositionCount, Is.GreaterThan(0), "Should have Position entities after concurrent operations");
    }
    
    #endregion
    
    #region Stress Testing
    
    [Test]
    public void StressTest_HighConcurrencyEntityOperations()
    {
        // Arrange
        var threadCount = Environment.ProcessorCount * 2;
        const int operationsPerThread = 2000;
        var tasks = new Task[threadCount];
        var exceptions = new ConcurrentBag<Exception>();
        var stopwatch = Stopwatch.StartNew();
        
        Console.WriteLine($"Starting stress test with {threadCount} threads, {operationsPerThread} operations each");
        
        // Act
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    var localEntities = new List<Entity>();
                    
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        switch (op % 8)
                        {
                            case 0:
                            case 1:
                                // Create entities (more frequent)
                                var entity = _world.CreateEntity();
                                _world.AddComponent(entity, new Position(threadId, op, threadId * 1000 + op));
                                localEntities.Add(entity);
                                break;
                                
                            case 2:
                                // Add velocity component
                                if (localEntities.Count > 0)
                                {
                                    var e = localEntities[Random.Shared.Next(localEntities.Count)];
                                    if (_world.IsAlive(e) && !_world.HasComponent<Velocity>(e))
                                    {
                                        _world.AddComponent(e, new Velocity(op, op, op));
                                    }
                                }
                                break;
                                
                            case 3:
                                // Add health component
                                if (localEntities.Count > 0)
                                {
                                    var e = localEntities[Random.Shared.Next(localEntities.Count)];
                                    if (_world.IsAlive(e) && !_world.HasComponent<Health>(e))
                                    {
                                        _world.AddComponent(e, new Health(100, 100));
                                    }
                                }
                                break;
                                
                            case 4:
                                // Remove velocity component
                                if (localEntities.Count > 0)
                                {
                                    var e = localEntities[Random.Shared.Next(localEntities.Count)];
                                    if (_world.IsAlive(e) && _world.HasComponent<Velocity>(e))
                                    {
                                        _world.RemoveComponent<Velocity>(e);
                                    }
                                }
                                break;
                                
                            case 5:
                                // Query operations
                                var count = _world.Select().With<Position>().Count();
                                var hasEntities = _world.Select().With<Position>().Any();
                                break;
                                
                            case 6:
                                // ForEach query
                                _world.Select()
                                    .With<Position>()
                                    .ForEach((Entity en, ref Position pos) =>
                                    {
                                        pos.X += 0.01f; // Tiny modification
                                    });
                                break;
                                
                            case 7:
                                // Destroy entity
                                if (localEntities.Count > 10) // Keep some entities alive
                                {
                                    var index = Random.Shared.Next(localEntities.Count);
                                    var e = localEntities[index];
                                    if (_world.IsAlive(e))
                                    {
                                        _world.DestroyEntity(e);
                                        localEntities.RemoveAt(index);
                                    }
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        // Wait for all tasks to complete
        Task.WaitAll(tasks);
        stopwatch.Stop();
        
        // Assert
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Expected no exceptions during stress test, but got: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        // Performance metrics
        var totalOperations = threadCount * operationsPerThread;
        var operationsPerSecond = totalOperations / stopwatch.Elapsed.TotalSeconds;
        var finalEntityCount = _world.Select().With<Position>().Count();
        
        Console.WriteLine($"Stress test completed in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"Total operations: {totalOperations:N0}");
        Console.WriteLine($"Operations per second: {operationsPerSecond:N0}");
        Console.WriteLine($"Final entity count: {finalEntityCount:N0}");
        
        // Health monitoring verification
        var healthStatus = _healthMonitor.GetHealthStatus();
        var performanceMetrics = _healthMonitor.GetPerformanceMetrics();
        var memoryMetrics = _healthMonitor.GetMemoryMetrics();
        
        Console.WriteLine($"Health status: {healthStatus}");
        Console.WriteLine($"Total entity operations: {performanceMetrics.TotalEntityOperations:N0}");
        Console.WriteLine($"Average operation time: {performanceMetrics.AverageEntityOperationMicros:F2} μs");
        Console.WriteLine($"Managed memory: {memoryMetrics.TotalManagedBytes / 1024:N0} KB");
        
        Assert.That(finalEntityCount, Is.GreaterThan(0), "Should have entities after stress test");
        Assert.That(performanceMetrics.TotalEntityOperations, Is.GreaterThan(0), "Should have recorded operations");
        Assert.That(operationsPerSecond, Is.GreaterThan(1000), "Should achieve at least 1000 operations/second");
    }
    
    [Test]
    public void StressTest_DeadlockPrevention()
    {
        // Arrange - Test for potential deadlocks with cross-archetype operations
        const int threadCount = 8;
        const int operationsPerThread = 1000;
        var tasks = new Task[threadCount];
        var exceptions = new ConcurrentBag<Exception>();
        
        // Create entities across multiple archetypes
        var archetypeEntities = new List<Entity>[4];
        for (int a = 0; a < 4; a++)
        {
            archetypeEntities[a] = new List<Entity>();
            for (int i = 0; i < 50; i++)
            {
                var entity = _world.CreateEntity();
                _world.AddComponent(entity, new Position(a, i, 0));
                
                if (a >= 1) _world.AddComponent(entity, new Velocity(1, 1, 1));
                if (a >= 2) _world.AddComponent(entity, new Health(100, 100));
                
                archetypeEntities[a].Add(entity);
            }
        }
        
        Console.WriteLine($"Created {archetypeEntities.Sum(list => list.Count)} entities across 4 archetypes");
        
        // Act - Concurrent operations that could cause deadlocks
        var startSignal = new ManualResetEventSlim(false);
        
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks[i] = Task.Run(() =>
            {
                startSignal.Wait(); // Synchronized start
                
                try
                {
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        var sourceArchetype = Random.Shared.Next(4);
                        var targetArchetype = Random.Shared.Next(4);
                        
                        if (sourceArchetype != targetArchetype && 
                            archetypeEntities[sourceArchetype].Count > 0)
                        {
                            var entity = archetypeEntities[sourceArchetype][Random.Shared.Next(archetypeEntities[sourceArchetype].Count)];
                            
                            if (_world.IsAlive(entity))
                            {
                                // Perform archetype transition that involves multiple locks
                                switch ((sourceArchetype, targetArchetype))
                                {
                                    case (0, 1): // Position -> Position+Velocity
                                        if (!_world.HasComponent<Velocity>(entity))
                                            _world.AddComponent(entity, new Velocity(op, op, op));
                                        break;
                                    case (1, 0): // Position+Velocity -> Position
                                        if (_world.HasComponent<Velocity>(entity))
                                            _world.RemoveComponent<Velocity>(entity);
                                        break;
                                    case (1, 2): // Position+Velocity -> Position+Velocity+Health
                                        if (!_world.HasComponent<Health>(entity))
                                            _world.AddComponent(entity, new Health(100, 100));
                                        break;
                                    case (2, 1): // Position+Velocity+Health -> Position+Velocity
                                        if (_world.HasComponent<Health>(entity))
                                            _world.RemoveComponent<Health>(entity);
                                        break;
                                }
                            }
                        }
                        
                        // Also perform queries during transitions
                        if (op % 10 == 0)
                        {
                            var count = _world.Select()
                                .With<Position>()
                                .Without<Velocity>()
                                .Count();
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        // Start all threads simultaneously
        startSignal.Set();
        
        // Wait for completion with timeout to detect deadlocks
        var completed = Task.WaitAll(tasks, TimeSpan.FromSeconds(30));
        
        // Assert
        Assert.That(completed, Is.True, "All tasks should complete within timeout (no deadlocks)");
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Expected no exceptions during deadlock prevention test, but got: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        Console.WriteLine("Deadlock prevention test completed successfully");
        
        // Verify final state consistency
        var totalEntities = _world.Select().With<Position>().Count();
        Console.WriteLine($"Final entity count: {totalEntities}");
        Assert.That(totalEntities, Is.GreaterThan(0), "Should have entities after deadlock test");
    }
    
    #endregion
    
    #region Component Types for Testing
    
    private struct Position
    {
        public float X, Y, Z;
        
        public Position(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
    
    private struct Velocity
    {
        public float X, Y, Z;
        
        public Velocity(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
    
    private struct Health
    {
        public int Current, Max;
        
        public Health(int current, int max)
        {
            Current = current;
            Max = max;
        }
    }
    
    #endregion
}