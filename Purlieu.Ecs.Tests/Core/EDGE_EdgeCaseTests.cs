using NUnit.Framework;
using PurlieuEcs.Core;
using System.Collections.Concurrent;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Edge case tests for boundary conditions and extreme scenarios.
/// Tests behavior at limits, with unusual inputs, and corner cases.
/// </summary>
[TestFixture]
[Category("EdgeCase")]
public class EDGE_EdgeCaseTests
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
    [Description("Edge: Single entity with maximum components")]
    public void SingleEntity_MaximumComponents()
    {
        var entity = _world.CreateEntity();
        
        // Add many different component types to test archetype limits
        _world.AddComponent(entity, new Component1 { Value = 1 });
        _world.AddComponent(entity, new Component2 { Value = 2 });
        _world.AddComponent(entity, new Component3 { Value = 3 });
        _world.AddComponent(entity, new Component4 { Value = 4 });
        _world.AddComponent(entity, new Component5 { Value = 5 });
        _world.AddComponent(entity, new Component6 { Value = 6 });
        _world.AddComponent(entity, new Component7 { Value = 7 });
        _world.AddComponent(entity, new Component8 { Value = 8 });
        
        // Should still be queryable and functional
        var query = _world.Query()
            .With<Component1>()
            .With<Component2>()
            .With<Component3>()
            .With<Component4>();
            
        Assert.That(query.Count(), Is.EqualTo(1), "Entity should be found by complex query");
        
        // All components should be retrievable
        Assert.That(_world.GetComponent<Component1>(entity).Value, Is.EqualTo(1));
        Assert.That(_world.GetComponent<Component8>(entity).Value, Is.EqualTo(8));
    }

    [Test]
    [Description("Edge: Zero entities with complex query")]
    public void ZeroEntities_ComplexQuery()
    {
        // Complex query with no matching entities
        var query = _world.Query()
            .With<Component1>()
            .With<Component2>()
            .Without<Component3>()
            .Without<Component4>();
            
        Assert.That(query.Count(), Is.EqualTo(0), "Query should return zero entities");
        
        // Iteration should work without issues
        int iterations = 0;
        foreach (var chunk in query.Chunks())
        {
            iterations++;
        }
        Assert.That(iterations, Is.EqualTo(0), "Should not iterate any chunks");
    }

    [Test]
    [Description("Edge: Rapid archetype transitions")]
    public void RapidArchetypeTransitions()
    {
        var entity = _world.CreateEntity();
        
        // Rapidly add and remove components to stress archetype transitions
        for (int cycle = 0; cycle < 1000; cycle++)
        {
            _world.AddComponent(entity, new Component1 { Value = cycle });
            _world.AddComponent(entity, new Component2 { Value = cycle * 2 });
            
            Assert.That(_world.HasComponent<Component1>(entity), Is.True);
            Assert.That(_world.HasComponent<Component2>(entity), Is.True);
            
            _world.RemoveComponent<Component1>(entity);
            Assert.That(_world.HasComponent<Component1>(entity), Is.False);
            Assert.That(_world.HasComponent<Component2>(entity), Is.True);
            
            _world.RemoveComponent<Component2>(entity);
            Assert.That(_world.HasComponent<Component2>(entity), Is.False);
        }
        
        Assert.That(_world.IsAlive(entity), Is.True, "Entity should still be alive after rapid transitions");
    }

    [Test]
    [Description("Edge: Entity destruction during query iteration")]
    public void EntityDestruction_DuringQueryIteration()
    {
        const int entityCount = 1000;
        var entities = new Entity[entityCount];
        
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Component1 { Value = i });
        }
        
        var query = _world.Query().With<Component1>();
        
        // Destroy every other entity during iteration
        int processedCount = 0;
        int destroyedCount = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var components = chunk.GetSpan<Component1>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                processedCount++;
                
                // Destroy every 3rd entity we encounter
                if (components[i].Value % 3 == 0 && _world.IsAlive(entities[components[i].Value]))
                {
                    _world.DestroyEntity(entities[components[i].Value]);
                    destroyedCount++;
                }
            }
        }
        
        Assert.That(processedCount, Is.EqualTo(entityCount), "Should process all entities");
        Assert.That(destroyedCount, Is.GreaterThan(0), "Should have destroyed some entities");
        
        // Query should still work after destruction
        var finalCount = query.Count();
        Assert.That(finalCount, Is.EqualTo(entityCount - destroyedCount), 
                   "Final count should reflect destroyed entities");
    }

    [Test]
    [Description("Edge: Very large component data")]
    public void VeryLargeComponentData()
    {
        var entity = _world.CreateEntity();
        
        // Large component with significant data
        var largeComponent = new LargeComponent();
        for (int i = 0; i < 1000; i++)
        {
            largeComponent.Data[i] = i * 2;
        }
        
        _world.AddComponent(entity, largeComponent);
        
        var retrieved = _world.GetComponent<LargeComponent>(entity);
        
        // Verify data integrity
        for (int i = 0; i < 1000; i++)
        {
            Assert.That(retrieved.Data[i], Is.EqualTo(i * 2), 
                       $"Large component data should be preserved at index {i}");
        }
    }

    [Test]
    [Description("Edge: Concurrent entity creation at limits")]
    public void ConcurrentEntityCreation_AtLimits()
    {
        const int threadCount = 16;
        const int entitiesPerThread = 1000;
        
        var allEntities = new ConcurrentBag<Entity>();
        var exceptions = new ConcurrentBag<Exception>();
        
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < entitiesPerThread; i++)
                    {
                        var entity = _world.CreateEntity();
                        allEntities.Add(entity);
                        
                        // Add component to stress the system
                        _world.AddComponent(entity, new Component1 { Value = i });
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        Assert.That(exceptions, Is.Empty, "No exceptions should occur during concurrent creation");
        Assert.That(allEntities.Count, Is.EqualTo(threadCount * entitiesPerThread), 
                   "All entities should be created");
        
        // Verify all entities are unique
        var uniqueIds = allEntities.Select(e => e.Id).Distinct().Count();
        Assert.That(uniqueIds, Is.EqualTo(allEntities.Count), "All entity IDs should be unique");
    }

    [Test]
    [Description("Edge: Query with all possible operators")]
    public void QueryWithAllOperators()
    {
        // Create entities with various component combinations
        var e1 = _world.CreateEntity();
        _world.AddComponent(e1, new Component1 { Value = 1 });
        _world.AddComponent(e1, new Component2 { Value = 2 });
        
        var e2 = _world.CreateEntity();
        _world.AddComponent(e2, new Component1 { Value = 1 });
        _world.AddComponent(e2, new Component3 { Value = 3 });
        
        var e3 = _world.CreateEntity();
        _world.AddComponent(e3, new Component2 { Value = 2 });
        _world.AddComponent(e3, new Component3 { Value = 3 });
        
        var e4 = _world.CreateEntity();
        _world.AddComponent(e4, new Component1 { Value = 1 });
        _world.AddComponent(e4, new Component2 { Value = 2 });
        _world.AddComponent(e4, new Component3 { Value = 3 });
        
        // Complex query: Has Component1 AND Component2 BUT NOT Component3
        var query = _world.Query()
            .With<Component1>()
            .With<Component2>()
            .Without<Component3>();
            
        var results = new List<Entity>();
        foreach (var chunk in query.Chunks())
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                results.Add(chunk.GetEntity(i));
            }
        }
        
        Assert.That(results.Count, Is.EqualTo(1), "Should match exactly one entity (e1)");
        Assert.That(results[0].Id, Is.EqualTo(e1.Id), "Should match entity e1");
    }

    [Test]
    [Description("Edge: Component modification during query")]
    public void ComponentModification_DuringQuery()
    {
        const int entityCount = 100;
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Component1 { Value = i });
        }
        
        var query = _world.Query().With<Component1>();
        
        // Modify components during iteration
        int modificationCount = 0;
        foreach (var chunk in query.Chunks())
        {
            var components = chunk.GetSpan<Component1>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var originalValue = components[i].Value;
                components[i].Value = originalValue * 2;
                modificationCount++;
            }
        }
        
        Assert.That(modificationCount, Is.EqualTo(entityCount), 
                   "Should have modified all entities");
        
        // Verify modifications persisted
        int verifiedCount = 0;
        foreach (var chunk in query.Chunks())
        {
            var components = chunk.GetSpan<Component1>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                Assert.That(components[i].Value % 2, Is.EqualTo(0), 
                           "Component value should be even (doubled)");
                verifiedCount++;
            }
        }
        
        Assert.That(verifiedCount, Is.EqualTo(entityCount), 
                   "Should verify all modified entities");
    }

    [Test]
    [Description("Edge: Empty archetype cleanup")]
    public void EmptyArchetypeCleanup()
    {
        // Create entities, then destroy all of them to create empty archetypes
        var entities = new List<Entity>();
        
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Component1 { Value = i });
            if (i % 2 == 0) _world.AddComponent(entity, new Component2 { Value = i * 2 });
            entities.Add(entity);
        }
        
        // Verify entities exist
        var initialCount = _world.Query().With<Component1>().Count();
        Assert.That(initialCount, Is.EqualTo(100));
        
        // Destroy all entities
        foreach (var entity in entities)
        {
            _world.DestroyEntity(entity);
        }
        
        // Verify all destroyed
        var finalCount = _world.Query().With<Component1>().Count();
        Assert.That(finalCount, Is.EqualTo(0), "All entities should be destroyed");
        
        // System should still function normally
        var newEntity = _world.CreateEntity();
        _world.AddComponent(newEntity, new Component1 { Value = 999 });
        
        Assert.That(_world.GetComponent<Component1>(newEntity).Value, Is.EqualTo(999), 
                   "New entity should work normally after cleanup");
    }

    [Test]
    [Description("Edge: Null and default value components")]
    public void NullAndDefaultValueComponents()
    {
        var entity = _world.CreateEntity();
        
        // Add component with default values
        _world.AddComponent<Component1>(entity, default);
        
        var component = _world.GetComponent<Component1>(entity);
        Assert.That(component.Value, Is.EqualTo(0), "Default component should have default values");
        
        // Modify to non-default
        _world.SetComponent(entity, new Component1 { Value = 42 });
        Assert.That(_world.GetComponent<Component1>(entity).Value, Is.EqualTo(42));
        
        // Set back to default
        _world.SetComponent<Component1>(entity, default);
        Assert.That(_world.GetComponent<Component1>(entity).Value, Is.EqualTo(0), 
                   "Should be able to set back to default");
    }
}

// Edge case test components
internal struct Component1 { public int Value; }
internal struct Component2 { public int Value; }
internal struct Component3 { public int Value; }  
internal struct Component4 { public int Value; }
internal struct Component5 { public int Value; }
internal struct Component6 { public int Value; }
internal struct Component7 { public int Value; }
internal struct Component8 { public int Value; }

internal unsafe struct LargeComponent
{
    public fixed int Data[1000]; // Large fixed array
}