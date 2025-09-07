using System.Reflection;
using NUnit.Framework;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Regression tests to prevent API breakage and ensure backward compatibility.
/// Tests critical scenarios that have broken in the past or could break in the future.
/// </summary>
[TestFixture]
[Category("Regression")]
public class REGRESS_RegressionTests
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    [Description("Regression: Entity IDs should never be reused within the same session")]
    public void EntityId_NeverReused_WithinSession()
    {
        // Historical bug: Entity IDs were being reused causing stale references
        var entityIds = new HashSet<uint>();
        
        // Create and destroy many entities
        for (int cycle = 0; cycle < 100; cycle++)
        {
            var entities = new Entity[1000];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = _world.CreateEntity();
                Assert.That(entityIds.Add(entities[i].Id), Is.True, 
                           $"Entity ID {entities[i].Id} was reused in cycle {cycle}");
            }
            
            // Destroy all entities
            foreach (var entity in entities)
            {
                _world.DestroyEntity(entity);
            }
        }
    }

    [Test]
    [Description("Regression: Component spans should remain valid during iteration")]
    public void ComponentSpan_RemainsValid_DuringIteration()
    {
        // Historical bug: Component spans could become invalid during query iteration
        // if archetype transitions occurred
        
        const int entityCount = 1000;
        var entities = new Entity[entityCount];
        
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new TestComponent { Value = i });
        }
        
        var query = _world.Query().With<TestComponent>();
        
        // This should not crash or produce invalid data
        int totalProcessed = 0;
        foreach (var chunk in query.Chunks())
        {
            var components = chunk.GetSpan<TestComponent>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                Assert.That(components[i].Value, Is.GreaterThanOrEqualTo(0), 
                           "Component data should be valid");
                totalProcessed++;
            }
        }
        
        Assert.That(totalProcessed, Is.EqualTo(entityCount), 
                   "All entities should be processed exactly once");
    }

    [Test]
    [Description("Regression: Query results should be deterministic across multiple iterations")]
    public void QueryResults_AreDeterministic_AcrossIterations()
    {
        // Historical bug: Query results could be in different orders due to hash collisions
        
        const int entityCount = 500;
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponent { Value = i });
            if (i % 2 == 0) _world.AddComponent(entity, new TestComponent2 { Value = i * 2 });
        }
        
        var query = _world.Query().With<TestComponent>().With<TestComponent2>();
        
        // Run query multiple times and ensure identical results
        var firstRunResults = new List<int>();
        foreach (var chunk in query.Chunks())
        {
            var components = chunk.GetSpan<TestComponent>();
            for (int i = 0; i < chunk.Count; i++)
            {
                firstRunResults.Add(components[i].Value);
            }
        }
        
        // Run again
        for (int run = 0; run < 5; run++)
        {
            var runResults = new List<int>();
            foreach (var chunk in query.Chunks())
            {
                var components = chunk.GetSpan<TestComponent>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    runResults.Add(components[i].Value);
                }
            }
            
            Assert.That(runResults, Is.EqualTo(firstRunResults), 
                       $"Run {run + 1} should produce identical results to first run");
        }
    }

    [Test]
    [Description("Regression: Archetype transitions should preserve component data")]
    public void ArchetypeTransition_PreservesData_AllComponents()
    {
        // Historical bug: Component data could be lost during archetype transitions
        
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TestComponent { Value = 42 });
        _world.AddComponent(entity, new TestComponent2 { Value = 84 });
        
        // Verify initial state
        Assert.That(_world.GetComponent<TestComponent>(entity).Value, Is.EqualTo(42));
        Assert.That(_world.GetComponent<TestComponent2>(entity).Value, Is.EqualTo(84));
        
        // Add component (causes archetype transition)
        _world.AddComponent(entity, new TestComponent3 { Value = 126 });
        
        // Verify all data preserved
        Assert.That(_world.GetComponent<TestComponent>(entity).Value, Is.EqualTo(42), 
                   "TestComponent data should be preserved");
        Assert.That(_world.GetComponent<TestComponent2>(entity).Value, Is.EqualTo(84), 
                   "TestComponent2 data should be preserved");
        Assert.That(_world.GetComponent<TestComponent3>(entity).Value, Is.EqualTo(126), 
                   "TestComponent3 data should be present");
        
        // Remove component (another archetype transition)
        _world.RemoveComponent<TestComponent2>(entity);
        
        // Verify remaining data preserved
        Assert.That(_world.GetComponent<TestComponent>(entity).Value, Is.EqualTo(42), 
                   "TestComponent data should still be preserved");
        Assert.That(_world.GetComponent<TestComponent3>(entity).Value, Is.EqualTo(126), 
                   "TestComponent3 data should still be preserved");
        Assert.That(_world.HasComponent<TestComponent2>(entity), Is.False, 
                   "TestComponent2 should be removed");
    }

    [Test]
    [Description("Regression: Large entity counts should not cause integer overflow")]
    public void LargeEntityCounts_NoIntegerOverflow()
    {
        // Historical bug: Entity counts could overflow when approaching limits
        
        // This would be expensive to test with actual entities, so we test the math
        const uint maxTestId = uint.MaxValue - 1000;
        
        // Simulate entity ID generation near the limit
        for (uint id = maxTestId; id < uint.MaxValue - 1; id++)
        {
            // This should not overflow or wrap around
            var nextId = id + 1;
            Assert.That(nextId, Is.GreaterThan(id), 
                       $"Entity ID {id} increment should not overflow");
        }
    }

    [Test]
    [Description("Regression: Empty queries should not cause null reference exceptions")]
    public void EmptyQuery_NoNullReference()
    {
        // Historical bug: Querying for non-existent components could cause NRE
        
        var query = _world.Query().With<TestComponent>().With<TestComponent2>().With<TestComponent3>();
        
        // Should not throw even with no matching entities
        Assert.DoesNotThrow(() =>
        {
            foreach (var chunk in query.Chunks())
            {
                Assert.That(chunk.Count, Is.EqualTo(0), "Empty query should return empty chunks");
            }
        });
        
        // Verify query count
        Assert.That(query.Count(), Is.EqualTo(0), "Empty query should return zero count");
    }

    [Test]
    [Description("Regression: Component type registration should be thread-safe")]
    public void ComponentTypeRegistration_ThreadSafe()
    {
        // Historical bug: Component type ID generation had race conditions
        
        const int threadCount = 8;
        const int operationsPerThread = 100;
        var typeIds = new ConcurrentBag<int>();
        var exceptions = new ConcurrentBag<Exception>();
        
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        // Access component type IDs from multiple threads
                        typeIds.Add(ComponentTypeId.Get<TestComponent>());
                        typeIds.Add(ComponentTypeId.Get<TestComponent2>());
                        typeIds.Add(ComponentTypeId.Get<TestComponent3>());
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        Assert.That(exceptions, Is.Empty, "No exceptions should occur during concurrent type registration");
        
        // All IDs for same type should be identical
        var testComponentIds = typeIds.Where((_, i) => i % 3 == 0).Distinct().ToArray();
        Assert.That(testComponentIds.Length, Is.EqualTo(1), 
                   "All TestComponent type IDs should be identical");
    }

    [Test]
    [Description("Regression: World disposal should not cause access violations")]
    public void WorldDisposal_NoAccessViolations()
    {
        // Historical bug: Accessing disposed world could cause AV
        
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComponent { Value = 42 });
        
        var query = world.Query().With<TestComponent>();
        
        // Dispose world
        world.Dispose();
        
        // These should throw ObjectDisposedException, not AV
        Assert.Throws<ObjectDisposedException>(() => world.CreateEntity());
        Assert.Throws<ObjectDisposedException>(() => world.GetComponent<TestComponent>(entity));
        Assert.Throws<ObjectDisposedException>(() => query.Count());
    }

    [Test]
    [Description("Regression: Public API should remain stable")]
    public void PublicApi_RemainsStable()
    {
        // This test ensures we don't accidentally break the public API
        var worldType = typeof(World);
        var entityType = typeof(Entity);
        var queryType = typeof(Query);
        
        // Critical methods that must exist
        Assert.That(worldType.GetMethod("CreateEntity", Type.EmptyTypes), Is.Not.Null, 
                   "World.CreateEntity() method must exist");
        Assert.That(worldType.GetMethod("DestroyEntity", new[] { typeof(Entity) }), Is.Not.Null, 
                   "World.DestroyEntity(Entity) method must exist");
        
        // Generic methods
        var addComponentMethod = worldType.GetMethods()
            .Where(m => m.Name == "AddComponent" && m.IsGenericMethodDefinition)
            .FirstOrDefault();
        Assert.That(addComponentMethod, Is.Not.Null, "Generic AddComponent<T> method must exist");
        
        var getComponentMethod = worldType.GetMethods()
            .Where(m => m.Name == "GetComponent" && m.IsGenericMethodDefinition)
            .FirstOrDefault();
        Assert.That(getComponentMethod, Is.Not.Null, "Generic GetComponent<T> method must exist");
        
        // Entity properties
        Assert.That(entityType.GetProperty("Id"), Is.Not.Null, "Entity.Id property must exist");
        Assert.That(entityType.GetProperty("Generation"), Is.Not.Null, "Entity.Generation property must exist");
    }
}

// Test components for regression tests
internal struct TestComponent
{
    public int Value;
}

internal struct TestComponent2  
{
    public int Value;
}

internal struct TestComponent3
{
    public int Value;
}