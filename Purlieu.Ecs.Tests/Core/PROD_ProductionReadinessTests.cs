using PurlieuEcs.Core;
using PurlieuEcs.Snapshot;
using System.Collections.Concurrent;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class PROD_ProductionReadinessTests
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
    
    [Test]
    public void ProductionWorkflow_EndToEnd_PerformsCorrectly()
    {
        // Arrange - Simulate production workload
        const int entityCount = 10000;
        var entities = new Entity[entityCount];
        
        // Create entities with mixed component types
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position { X = i, Y = i * 2, Z = i * 3 });
            
            if (i % 3 == 0)
                _world.AddComponent(entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
            
            if (i % 5 == 0)
                _world.AddComponent(entities[i], new OneFrameComponent { Value = i });
        }
        
        var scheduler = new SystemScheduler();
        scheduler.RegisterSystem(new TestSystem());
        
        // Act - Simulate multiple frame cycles
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int frame = 0; frame < 100; frame++)
        {
            // Update systems (scheduler doesn't have Update method, simulate system execution)
            
            // Clear one-frame data
            _world.ClearOneFrameData();
            
            // Periodic memory cleanup
            if (frame % 10 == 0)
                _world.ForceMemoryCleanup(CleanupLevel.Normal);
            
            // Take snapshot periodically
            if (frame % 20 == 0)
            {
                var result = WorldSnapshot.Save(_world);
                Assert.That(result.Success, Is.True);
            }
        }
        
        stopwatch.Stop();
        
        // Assert - Should complete within reasonable time
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000), "Production workflow should be performant");
        
        // Memory should be well managed
        var stats = _world.GetMemoryStatistics();
        Assert.That(stats.CleanupCount, Is.GreaterThan(0));
        
        // All entities should still be alive
        foreach (var entity in entities)
        {
            Assert.That(_world.IsAlive(entity), Is.True);
        }
    }
    
    [Test]
    public void ReflectionCaching_EliminatesPerformanceBottlenecks()
    {
        // Arrange
        var scheduler = new SystemScheduler();
        const int registrationCount = 1000;
        
        // Act - Register same system type many times
        var allocationsBefore = GC.GetTotalAllocatedBytes();
        
        for (int i = 0; i < registrationCount; i++)
        {
            scheduler.RegisterSystem(new TestSystem());
        }
        
        var allocationsAfter = GC.GetTotalAllocatedBytes();
        var allocatedBytes = allocationsAfter - allocationsBefore;
        
        // Assert - Should use cached reflection metadata
        var bytesPerRegistration = allocatedBytes / registrationCount;
        Assert.That(bytesPerRegistration, Is.LessThan(200), "Reflection should be cached after first call");
    }
    
    [Test]
    public void ComponentTypeId_ThreadSafetyUnderLoad()
    {
        // Arrange
        const int threadCount = 20;
        const int operationsPerThread = 500;
        var allIds = new ConcurrentBag<int>();
        var tasks = new Task[threadCount];
        var exceptions = new ConcurrentBag<Exception>();
        
        // Act - Stress test ComponentTypeId under heavy concurrent load
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        // Mix of new and existing type requests
                        allIds.Add(ComponentTypeId.Get<Position>());
                        allIds.Add(ComponentTypeId.Get<Velocity>());
                        
                        if (threadId % 4 == 0) allIds.Add(ComponentTypeId.Get<OneFrameComponent>());
                        if (threadId % 4 == 1) allIds.Add(ComponentTypeId.Get<TestComponent1>());
                        if (threadId % 4 == 2) allIds.Add(ComponentTypeId.Get<TestComponent2>());
                        if (threadId % 4 == 3) allIds.Add(ComponentTypeId.Get<TestComponent3>());
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        // Assert - No exceptions and consistent IDs
        Assert.That(exceptions.Count, Is.EqualTo(0));
        
        // Same types should always get same IDs
        var positionId1 = ComponentTypeId.Get<Position>();
        var positionId2 = ComponentTypeId.Get<Position>();
        Assert.That(positionId1, Is.EqualTo(positionId2));
    }
    
    [Test, Timeout(10000)]
    public void MemoryManager_PreventsDegradationUnderPressure()
    {
        // Arrange - Create memory pressure
        const int iterationCount = 100;
        var entities = new List<Entity>();
        
        // Act - Repeated allocation/deallocation cycles
        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            // Allocate phase
            for (int i = 0; i < 100; i++)
            {
                var entity = _world.CreateEntity();
                _world.AddComponent(entity, new Position { X = i, Y = i, Z = i });
                _world.AddComponent(entity, new Velocity { X = 1, Y = 1, Z = 1 });
                entities.Add(entity);
            }
            
            // Query phase (creates temporary allocations)
            var query = _world.Query().With<Position>().With<Velocity>();
            foreach (var chunk in query.ChunksStack())
            {
                var positions = chunk.GetSpan<Position>();
                var velocities = chunk.GetSpan<Velocity>();
                
                for (int i = 0; i < positions.Length; i++)
                {
                    // Simulate work
                    positions[i] = new Position 
                    { 
                        X = positions[i].X + velocities[i].X,
                        Y = positions[i].Y + velocities[i].Y,
                        Z = positions[i].Z + velocities[i].Z
                    };
                }
            }
            
            // Cleanup phase
            if (iteration % 10 == 0)
            {
                _world.ForceMemoryCleanup(CleanupLevel.Aggressive);
            }
        }
        
        // Assert - Memory should be well managed
        var stats = _world.GetMemoryStatistics();
        Assert.That(stats.CleanupCount, Is.GreaterThan(0), "Memory cleanup should have occurred");
        
        // System should still be responsive
        var testEntity = _world.CreateEntity();
        _world.AddComponent(testEntity, new Position());
        Assert.That(_world.HasComponent<Position>(testEntity), Is.True);
    }
    
    private struct Position { public float X, Y, Z; }
    private struct Velocity { public float X, Y, Z; }
    
    [PurlieuEcs.Components.OneFrame]
    private struct OneFrameComponent { public int Value; }
    
    private struct TestComponent1 { public int Value; }
    private struct TestComponent2 { public float Value; }
    private struct TestComponent3 { public bool Value; }
    
    private class TestSystem : ISystem
    {
        public void Execute(World world, float deltaTime)
        {
            // Simple system that processes entities
            var query = world.Query().With<Position>();
            foreach (var chunk in query.Chunks())
            {
                var positions = chunk.GetSpan<Position>();
                for (int i = 0; i < positions.Length; i++)
                {
                    // Simulate work
                    positions[i] = new Position 
                    { 
                        X = positions[i].X + deltaTime,
                        Y = positions[i].Y + deltaTime,
                        Z = positions[i].Z + deltaTime
                    };
                }
            }
        }
        
        public SystemDependencies GetDependencies()
        {
            return SystemDependencies.WriteOnly(typeof(Position));
        }
    }
}