using NUnit.Framework;
using PurlieuEcs.Core;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Performance regression tests that fail if performance drops below acceptable baselines.
/// These tests validate that optimize-allocation-v2 improvements are maintained.
/// </summary>
[TestFixture]
public class PERF_RegressionTests
{
    private World _world;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        _world.RegisterComponent<Position>();
        _world.RegisterComponent<Velocity>();
        _world.RegisterComponent<Health>();
    }

    [TearDown] 
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void EntityCreation_PerformanceBaseline()
    {
        // Baseline: Should create at least 100K entities/second
        const int entityCount = 5000;
        const int minEntitiesPerSecond = 100_000;

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position { X = i, Y = i });
        }

        sw.Stop();

        var entitiesPerSecond = entityCount / sw.Elapsed.TotalSeconds;
        
        Assert.That(entitiesPerSecond, Is.GreaterThan(minEntitiesPerSecond),
            $"Entity creation performance regression: {entitiesPerSecond:N0} < {minEntitiesPerSecond:N0} entities/sec");
        
        // Log performance for monitoring
        TestContext.WriteLine($"Entity creation rate: {entitiesPerSecond:N0} entities/sec");
    }

    [Test]
    public void ArchetypeTransition_PerformanceBaseline()
    {
        // Baseline: Should perform at least 50K archetype transitions/second
        const int transitionCount = 2000;
        const int minTransitionsPerSecond = 50_000;

        // Pre-create entities
        var entities = new Entity[transitionCount];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position { X = i, Y = i });
        }

        var sw = Stopwatch.StartNew();

        // Perform archetype transitions
        for (int i = 0; i < entities.Length; i++)
        {
            _world.AddComponent(entities[i], new Velocity { X = 1, Y = 1 });
        }

        sw.Stop();

        var transitionsPerSecond = transitionCount / sw.Elapsed.TotalSeconds;
        
        Assert.That(transitionsPerSecond, Is.GreaterThan(minTransitionsPerSecond),
            $"Archetype transition performance regression: {transitionsPerSecond:N0} < {minTransitionsPerSecond:N0} transitions/sec");
        
        TestContext.WriteLine($"Archetype transition rate: {transitionsPerSecond:N0} transitions/sec");
    }

    [Test]
    public void QueryIteration_AllocationRegression()
    {
        // Baseline: Query iteration should allocate less than 100KB per 1000 iterations (thread safety overhead)
        const int entityCount = 1000;
        const int iterations = 1000;
        const int maxAllocationBytes = 100_000;

        // Create test data
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position { X = i, Y = i });
        }

        // Warm up
        var query = _world.Query().With<Position>();
        query.Count();

        // Force GC to get clean baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = GC.GetTotalMemory(false);

        // Perform iterations
        long sum = 0;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            foreach (var chunk in query.ChunksStack())
            {
                var positions = chunk.GetSpan<Position>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    sum += (long)positions[i].X;
                }
            }
        }

        var memoryAfter = GC.GetTotalMemory(false);
        var allocatedBytes = memoryAfter - memoryBefore;

        Assert.That(allocatedBytes, Is.LessThan(maxAllocationBytes),
            $"Query iteration allocation regression: {allocatedBytes:N0} >= {maxAllocationBytes:N0} bytes");
        
        TestContext.WriteLine($"Query allocation: {allocatedBytes:N0} bytes for {iterations} iterations");
        TestContext.WriteLine($"Sum result: {sum:N0}"); // Prevent optimization
    }

    [Test]
    public void ThreadSafety_ConcurrentOperations()
    {
        // Baseline: Should handle concurrent operations without exceptions
        const int operationsPerThread = 500;
        const int threadCount = 4;

        var exceptions = new List<Exception>();
        var successfulOperations = 0;

        var tasks = new Task[threadCount];
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var entity = _world.CreateEntity();
                        _world.AddComponent(entity, new Position { X = i, Y = i });
                        
                        if (i % 2 == 0)
                        {
                            _world.AddComponent(entity, new Velocity { X = 1, Y = 1 });
                        }
                        
                        Interlocked.Increment(ref successfulOperations);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }

        Task.WaitAll(tasks);

        var expectedOperations = threadCount * operationsPerThread;
        
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Thread safety regression: {exceptions.Count} exceptions during concurrent operations");
        
        Assert.That(successfulOperations, Is.GreaterThanOrEqualTo(expectedOperations * 0.95), // Allow 5% variance
            $"Thread safety performance regression: {successfulOperations} < {expectedOperations * 0.95:F0} operations completed");
        
        TestContext.WriteLine($"Concurrent operations completed: {successfulOperations:N0}/{expectedOperations:N0}");
    }

    [Test]
    public void SystemScheduler_RegistrationPerformance()
    {
        // Baseline: Should register and execute systems without significant overhead
        const int systemCount = 25;
        const int maxRegistrationTimeMs = 50;
        const int maxExecutionTimeMs = 10;

        var scheduler = _world.SystemScheduler;
        var systems = new TestSystem[systemCount];
        
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i] = new TestSystem { Id = i };
        }

        // Test registration performance
        var sw = Stopwatch.StartNew();
        
        foreach (var system in systems)
        {
            scheduler.RegisterSystem(system);
        }
        
        var registrationTime = sw.ElapsedMilliseconds;
        
        Assert.That(registrationTime, Is.LessThan(maxRegistrationTimeMs),
            $"System registration performance regression: {registrationTime}ms >= {maxRegistrationTimeMs}ms");

        // Test execution performance
        sw.Restart();
        scheduler.ExecuteAllPhases(_world, 0.016f);
        var executionTime = sw.ElapsedMilliseconds;
        
        Assert.That(executionTime, Is.LessThan(maxExecutionTimeMs),
            $"System execution performance regression: {executionTime}ms >= {maxExecutionTimeMs}ms");
        
        TestContext.WriteLine($"System registration: {registrationTime}ms, execution: {executionTime}ms");
    }

    [Test]
    public void Memory_UsageBaseline()
    {
        // Baseline: Memory usage should be predictable and not excessive
        const int entityCount = 10000;
        const int maxMemoryMB = 50; // 50MB limit for 10K entities
        
        GC.Collect();
        GC.WaitForPendingFinalizers(); 
        GC.Collect();
        
        var memoryBefore = GC.GetTotalMemory(false);

        // Create entities with components
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position { X = i, Y = i });
            
            if (i % 2 == 0)
            {
                _world.AddComponent(entity, new Velocity { X = 1, Y = 1 });
            }
            
            if (i % 5 == 0)
            {
                _world.AddComponent(entity, new Health(100, 100));
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memoryAfter = GC.GetTotalMemory(false);
        var memoryUsedMB = (memoryAfter - memoryBefore) / (1024.0 * 1024.0);
        
        Assert.That(memoryUsedMB, Is.LessThan(maxMemoryMB),
            $"Memory usage regression: {memoryUsedMB:F1}MB >= {maxMemoryMB}MB for {entityCount:N0} entities");
        
        TestContext.WriteLine($"Memory usage: {memoryUsedMB:F1}MB for {entityCount:N0} entities");
    }

    private struct Position
    {
        public float X, Y;
    }

    private struct Velocity
    {
        public float X, Y;
    }

    private struct Health
    {
        public float Current;
        public float Max;

        public Health(float current, float max)
        {
            Current = current;
            Max = max;
        }
    }

    private class TestSystem : ISystem
    {
        public int Id { get; set; }

        public void Execute(World world, float deltaTime)
        {
            // Minimal work for performance testing
        }

        public SystemDependencies GetDependencies()
        {
            return new SystemDependencies();
        }
    }
}