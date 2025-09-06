using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Components;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
[Category("Integration")]
public class IT_OptimizationIntegrationTests
{
    [Test]
    public void AllOptimizations_WorkTogetherUnderLoad()
    {
        // Test that all optimizations work together in a realistic scenario
        
        var world = new World();
        var entities = new List<Entity>();
        
        // Create diverse entity population (exercises archetype index)
        for (int i = 0; i < 10000; i++)
        {
            var entity = world.CreateEntity();
            entities.Add(entity);
            
            // Various component combinations create many archetypes
            if (i % 2 == 0) world.AddComponent(entity, new TestComponentA { Value = i });
            if (i % 3 == 0) world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
            if (i % 5 == 0) world.AddComponent(entity, new TestComponentC { Flag = true });
            if (i % 7 == 0) world.AddComponent(entity, new Position { X = i, Y = i });
            if (i % 11 == 0) world.AddComponent(entity, new MoveIntent { DX = 1, DY = 1 });
        }
        
        // Perform many operations that exercise all optimizations
        var sw = Stopwatch.StartNew();
        
        // 1. Query operations (uses ArchetypeIndex with caching)
        for (int iter = 0; iter < 100; iter++)
        {
            var query = world.Query()
                .With<TestComponentA>()
                .With<TestComponentB>()
                .Without<TestComponentC>();
            
            int count = 0;
            foreach (var chunk in query.ChunksStack()) // Zero-allocation iteration
            {
                count += chunk.Count;
            }
            
            Assert.That(count, Is.GreaterThan(0), "Query should find entities");
        }
        
        // 2. Component access (uses optimized chunk lookup with bit operations)
        for (int i = 0; i < 1000; i++)
        {
            var entity = entities[i % entities.Count];
            if (world.HasComponent<TestComponentA>(entity))
            {
                ref var component = ref world.GetComponent<TestComponentA>(entity);
                component.Value++;
            }
        }
        
        // 3. Component add/remove (uses signature array pooling)
        for (int i = 0; i < 100; i++)
        {
            var entity = entities[i];
            world.AddComponent(entity, new Stunned());
            world.RemoveComponent<Stunned>(entity);
        }
        
        // 4. New archetype creation (uses component storage factory)
        for (int i = 0; i < 10; i++)
        {
            var entity = world.CreateEntity();
            // Unique component combination to force new archetype
            world.AddComponent(entity, new TestComponentA { Value = i * 1000 });
            world.AddComponent(entity, new TestComponentB { Data = i * 1000.0f });
            world.AddComponent(entity, new TestComponentC { Flag = i % 2 == 0 });
            world.AddComponent(entity, new Position { X = i * 100, Y = i * 100 });
            world.AddComponent(entity, new MoveIntent { DX = i, DY = i });
            world.AddComponent(entity, new Stunned());
        }
        
        sw.Stop();
        
        // All operations should complete quickly with optimizations
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100),
            $"Operations took too long: {sw.ElapsedMilliseconds}ms. Optimizations may not be working.");
    }
    
    [Test]
    public void Optimizations_MaintainCorrectness()
    {
        // Ensure optimizations don't break correctness
        
        var world = new World();
        var testEntities = new Dictionary<Entity, (int a, float b, bool c)>();
        
        // Create entities with known component values
        for (int i = 0; i < 100; i++)
        {
            var entity = world.CreateEntity();
            
            int aValue = i * 2;
            float bValue = i * 3.14f;
            bool cValue = i % 2 == 0;
            
            world.AddComponent(entity, new TestComponentA { Value = aValue });
            world.AddComponent(entity, new TestComponentB { Data = bValue });
            world.AddComponent(entity, new TestComponentC { Flag = cValue });
            
            testEntities[entity] = (aValue, bValue, cValue);
        }
        
        // Verify all components are correctly stored and retrievable
        foreach (var (entity, (expectedA, expectedB, expectedC)) in testEntities)
        {
            ref var compA = ref world.GetComponent<TestComponentA>(entity);
            ref var compB = ref world.GetComponent<TestComponentB>(entity);
            ref var compC = ref world.GetComponent<TestComponentC>(entity);
            
            Assert.That(compA.Value, Is.EqualTo(expectedA), 
                $"Component A value mismatch for entity {entity}");
            Assert.That(compB.Data, Is.EqualTo(expectedB).Within(0.001f), 
                $"Component B value mismatch for entity {entity}");
            Assert.That(compC.Flag, Is.EqualTo(expectedC), 
                $"Component C value mismatch for entity {entity}");
        }
        
        // Test query correctness
        var queryEven = world.Query().With<TestComponentC>();
        int evenCount = 0;
        foreach (var chunk in queryEven.ChunksStack())
        {
            var flags = chunk.GetSpan<TestComponentC>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (flags[i].Flag) evenCount++;
            }
        }
        
        Assert.That(evenCount, Is.EqualTo(50), "Should find exactly 50 entities with Flag=true");
        
        // Test component removal correctness
        var firstEntity = testEntities.Keys.First();
        world.RemoveComponent<TestComponentB>(firstEntity);
        
        Assert.That(world.HasComponent<TestComponentA>(firstEntity), Is.True);
        Assert.That(world.HasComponent<TestComponentB>(firstEntity), Is.False);
        Assert.That(world.HasComponent<TestComponentC>(firstEntity), Is.True);
    }
    
    [Test]
    public void Optimizations_ScaleToLargeEntityCounts()
    {
        // Test that optimizations enable scaling to large entity counts
        
        var world = new World();
        const int entityCount = 100000; // 100k entities
        
        var sw = Stopwatch.StartNew();
        
        // Create many entities
        var entities = new Entity[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = world.CreateEntity();
            
            // Add components (creates many archetypes)
            if (i % 10 == 0) world.AddComponent(entities[i], new TestComponentA { Value = i });
        }
        
        sw.Stop();
        var creationTime = sw.ElapsedMilliseconds;
        
        // Should handle 100k entities reasonably fast
        Assert.That(creationTime, Is.LessThan(5000), 
            $"Creating 100k entities took {creationTime}ms, too slow");
        
        // Query performance should still be good with many entities
        sw.Restart();
        
        var query = world.Query().With<TestComponentA>();
        int foundCount = 0;
        foreach (var chunk in query.ChunksStack())
        {
            foundCount += chunk.Count;
        }
        
        sw.Stop();
        var queryTime = sw.ElapsedMilliseconds;
        
        Assert.That(foundCount, Is.EqualTo(10000), "Should find 10k entities with component");
        Assert.That(queryTime, Is.LessThan(10), 
            $"Query on 100k entities took {queryTime}ms, too slow");
    }
    
    [Test]
    public void Optimizations_MinimalAllocationsInHotPath()
    {
        // Verify that hot paths have minimal allocations
        
        var world = new World();
        
        // Setup
        for (int i = 0; i < 1000; i++)
        {
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TestComponentA { Value = i });
            world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
        }
        
        // Measure allocations in hot path
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var beforeBytes = GC.GetTotalMemory(false);
        var beforeGen0 = GC.CollectionCount(0);
        
        // Hot path operations
        for (int iter = 0; iter < 100; iter++)
        {
            // Query iteration (should be zero-alloc with ChunksStack)
            var query = world.Query().With<TestComponentA>().With<TestComponentB>();
            
            foreach (var chunk in query.ChunksStack())
            {
                var compA = chunk.GetSpan<TestComponentA>();
                var compB = chunk.GetSpan<TestComponentB>();
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    compA[i].Value++;
                    compB[i].Data += 0.1f;
                }
            }
        }
        
        var afterBytes = GC.GetTotalMemory(false);
        var afterGen0 = GC.CollectionCount(0);
        
        var allocatedBytes = afterBytes - beforeBytes;
        var gen0Collections = afterGen0 - beforeGen0;
        
        // Should have minimal allocations in hot path
        Assert.That(gen0Collections, Is.EqualTo(0), 
            $"Gen0 collections occurred in hot path: {gen0Collections}");
        
        // Allow some allocation for test overhead but should be minimal
        Assert.That(allocatedBytes, Is.LessThan(100000), 
            $"Hot path allocated {allocatedBytes} bytes, expected minimal");
    }
}