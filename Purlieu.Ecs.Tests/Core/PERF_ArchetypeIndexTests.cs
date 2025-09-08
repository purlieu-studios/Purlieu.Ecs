using NUnit.Framework;
using PurlieuEcs.Core;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
[Category("Performance")]
public class PERF_ArchetypeIndexTests
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
    public void ArchetypeIndex_QueriesAreConstantTime_RegardlessOfArchetypeCount()
    {
        // Create many different archetypes (simulating a complex game)
        var archetypes = new List<(Entity, int)>();
        
        // Create 100 different component combinations
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            
            // Add different combinations of components
            if (i % 2 == 0) _world.AddComponent(entity, new TestComponentA { Value = i });
            if (i % 3 == 0) _world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
            if (i % 5 == 0) _world.AddComponent(entity, new TestComponentC { Flag = true });
            
            archetypes.Add((entity, i));
        }
        
        // Measure query time with many archetypes
        var sw = Stopwatch.StartNew();
        var query = _world.Query().With<TestComponentA>();
        
        int count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        
        sw.Stop();
        var firstQueryTime = sw.ElapsedTicks;
        
        // Add 900 more archetypes
        for (int i = 100; i < 1000; i++)
        {
            var entity = _world.CreateEntity();
            
            // Create unique combinations
            if (i % 7 == 0) _world.AddComponent(entity, new TestComponentA { Value = i });
            if (i % 11 == 0) _world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
            if (i % 13 == 0) _world.AddComponent(entity, new TestComponentC { Flag = true });
        }
        
        // Measure query time with 10x more archetypes
        sw.Restart();
        query = _world.Query().With<TestComponentA>();
        
        count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        
        sw.Stop();
        var secondQueryTime = sw.ElapsedTicks;
        
        // With O(1) index, query time should be similar despite 10x more archetypes
        // Allow 3x variance for cache effects and other factors
        Assert.That(secondQueryTime, Is.LessThan(firstQueryTime * 3),
            $"Query time degraded too much with more archetypes. First: {firstQueryTime} ticks, Second: {secondQueryTime} ticks");
    }
    
    [Test]
    public void ArchetypeIndex_CachesQueryResults()
    {
        // Create entities with components
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponentA { Value = i });
            if (i % 2 == 0)
                _world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
        }
        
        // First query (cache miss)
        var sw = Stopwatch.StartNew();
        var query1 = _world.Query().With<TestComponentA>().With<TestComponentB>();
        int count1 = 0;
        foreach (var chunk in query1.ChunksStack())
        {
            count1 += chunk.Count;
        }
        sw.Stop();
        var firstTime = sw.ElapsedTicks;
        
        // Second identical query (cache hit)
        sw.Restart();
        var query2 = _world.Query().With<TestComponentA>().With<TestComponentB>();
        int count2 = 0;
        foreach (var chunk in query2.ChunksStack())
        {
            count2 += chunk.Count;
        }
        sw.Stop();
        var secondTime = sw.ElapsedTicks;
        
        Assert.That(count1, Is.EqualTo(count2), "Query results should be identical");
        
        // Second query should be faster due to caching (allow some variance)
        Assert.That(secondTime, Is.LessThanOrEqualTo(firstTime * 1.5),
            $"Cached query was not faster. First: {firstTime} ticks, Second: {secondTime} ticks");
    }
    
    [Test]
    public void ArchetypeIndex_InvalidatesCacheOnArchetypeChange()
    {
        var entity1 = _world.CreateEntity();
        _world.AddComponent(entity1, new TestComponentA { Value = 1 });
        
        // First query
        var query1 = _world.Query().With<TestComponentA>();
        int count1 = 0;
        foreach (var chunk in query1.ChunksStack())
        {
            count1 += chunk.Count;
        }
        
        // Add new entity with same component (creates/modifies archetype)
        var entity2 = _world.CreateEntity();
        _world.AddComponent(entity2, new TestComponentA { Value = 2 });
        
        // Second query should reflect the change
        var query2 = _world.Query().With<TestComponentA>();
        int count2 = 0;
        foreach (var chunk in query2.ChunksStack())
        {
            count2 += chunk.Count;
        }
        
        Assert.That(count2, Is.EqualTo(count1 + 1), "Cache should be invalidated when archetypes change");
    }
    
    [Test]
    public void ArchetypeIndex_HandlesComplexQueries()
    {
        // Create diverse entity set
        var entities = new List<Entity>();
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            entities.Add(entity);
            
            // Various component combinations
            if (i < 30) // 30 with A only
            {
                _world.AddComponent(entity, new TestComponentA { Value = i });
            }
            else if (i < 60) // 30 with A and B
            {
                _world.AddComponent(entity, new TestComponentA { Value = i });
                _world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
            }
            else if (i < 80) // 20 with A, B, and C
            {
                _world.AddComponent(entity, new TestComponentA { Value = i });
                _world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
                _world.AddComponent(entity, new TestComponentC { Flag = true });
            }
            else // 20 with B and C only
            {
                _world.AddComponent(entity, new TestComponentB { Data = i * 0.5f });
                _world.AddComponent(entity, new TestComponentC { Flag = false });
            }
        }
        
        // Test complex query: With A and B, Without C
        var complexQuery = _world.Query()
            .With<TestComponentA>()
            .With<TestComponentB>()
            .Without<TestComponentC>();
        
        int complexCount = 0;
        foreach (var chunk in complexQuery.ChunksStack())
        {
            complexCount += chunk.Count;
        }
        
        // Should only match entities 30-59 (30 entities with A and B but not C)
        Assert.That(complexCount, Is.EqualTo(30), "Complex query should correctly filter entities");
    }
}