using PurlieuEcs.Core;
using System.Collections.Concurrent;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class THREAD_SafetyTests
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
    public void ComponentTypeId_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        const int threadCount = 10;
        const int operationsPerThread = 1000;
        var results = new ConcurrentBag<int>();
        var tasks = new Task[threadCount];
        
        // Act - Multiple threads accessing ComponentTypeId simultaneously
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < operationsPerThread; j++)
                {
                    // Different types per thread to test concurrent registration
                    switch (threadId % 3)
                    {
                        case 0:
                            results.Add(ComponentTypeId.Get<TestComponent1>());
                            break;
                        case 1:
                            results.Add(ComponentTypeId.Get<TestComponent2>());
                            break;
                        case 2:
                            results.Add(ComponentTypeId.Get<TestComponent3>());
                            break;
                    }
                }
            });
        }
        
        // Wait for all threads to complete
        Task.WaitAll(tasks);
        
        // Assert - All operations completed successfully
        Assert.That(results.Count, Is.EqualTo(threadCount * operationsPerThread));
        
        // Same type should always return same ID
        var id1 = ComponentTypeId.Get<TestComponent1>();
        var id2 = ComponentTypeId.Get<TestComponent1>();
        Assert.That(id1, Is.EqualTo(id2));
    }
    
    [Test]
    public void SystemScheduler_ConcurrentRegistration_ThreadSafe()
    {
        // Arrange
        var scheduler = new SystemScheduler();
        const int threadCount = 5;
        var tasks = new Task[threadCount];
        var exceptions = new ConcurrentBag<Exception>();
        
        // Act - Multiple threads registering systems
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 100; j++)
                    {
                        scheduler.RegisterSystem(new TestSystem());
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        // Assert - No exceptions should occur
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Concurrent registration caused exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
    }
    
    [Test] 
    public void World_ConcurrentEntityCreation_ThreadSafe()
    {
        // Arrange
        const int threadCount = 10;
        const int entitiesPerThread = 100;
        var allEntities = new ConcurrentBag<Entity>();
        var tasks = new Task[threadCount];
        
        // Act - Multiple threads creating entities
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < entitiesPerThread; j++)
                {
                    var entity = _world.CreateEntity();
                    allEntities.Add(entity);
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        // Assert - All entities should be unique and valid
        Assert.That(allEntities.Count, Is.EqualTo(threadCount * entitiesPerThread));
        
        var entityIds = allEntities.Select(e => e.Id).ToHashSet();
        Assert.That(entityIds.Count, Is.EqualTo(allEntities.Count), "All entity IDs should be unique");
        
        // All entities should be alive
        foreach (var entity in allEntities)
        {
            Assert.That(_world.IsAlive(entity), Is.True, $"Entity {entity.Id} should be alive");
        }
    }
    
    [Test, Timeout(5000)]
    public void MemoryManager_ConcurrentCleanup_ThreadSafe()
    {
        // Arrange - Create some entities to clean up
        for (int i = 0; i < 1000; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponent1 { Value = i });
        }
        
        // Act - Multiple threads triggering cleanup
        var tasks = new Task[5];
        for (int i = 0; i < tasks.Length; i++)
        {
            CleanupLevel level = (CleanupLevel)(i % 3); // Rotate through cleanup levels
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                {
                    _world.ForceMemoryCleanup(level);
                    Thread.Sleep(1); // Small delay to encourage race conditions
                }
            });
        }
        
        // Assert - Should complete without deadlocks or exceptions
        Assert.DoesNotThrow(() => Task.WaitAll(tasks));
        
        var stats = _world.GetMemoryStatistics();
        Assert.That(stats.CleanupCount, Is.GreaterThan(0));
    }
    
    [Test]
    public void OneFrameClearing_ConcurrentWithEntityOperations_ThreadSafe()
    {
        // Arrange - Create entities with one-frame components
        var entities = new List<Entity>();
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new OneFrameTestComponent { Value = i });
            entities.Add(entity);
        }
        
        var exceptions = new ConcurrentBag<Exception>();
        
        // Act - Concurrent one-frame clearing and entity operations
        var clearTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    _world.ClearOneFrameData();
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });
        
        var entityTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    var entity = _world.CreateEntity();
                    _world.AddComponent(entity, new OneFrameTestComponent { Value = 999 });
                    Thread.Sleep(5);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });
        
        Task.WaitAll(clearTask, entityTask);
        
        // Assert - No exceptions should occur
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Concurrent operations caused exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
    }
    
    private struct TestComponent1 { public int Value; }
    private struct TestComponent2 { public float Value; }
    private struct TestComponent3 { public bool Value; }
    
    [PurlieuEcs.Components.OneFrame]
    private struct OneFrameTestComponent { public int Value; }
    
    private class TestSystem : ISystem
    {
        public void Execute(World world, float deltaTime) { }
        public SystemDependencies GetDependencies() => new SystemDependencies();
    }
}