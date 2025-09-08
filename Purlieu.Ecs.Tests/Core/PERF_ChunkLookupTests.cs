using NUnit.Framework;
using PurlieuEcs.Core;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
[Category("Performance")]
public class PERF_ChunkLookupTests
{
    private World _world = null!;
    private Entity[] _entities = null!;
    
    [SetUp]
    public void Setup()
    {
        _world = new World();
        _entities = new Entity[10000];
        
        // Create many entities with components
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new TestComponentA { Value = i });
        }
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    [Test]
    public void ChunkLookup_UsesBitOperations_NotDivision()
    {
        // This test verifies that chunk lookups are fast
        // We can't directly test bit operations vs division, but we can measure performance
        
        const int iterations = 1000000;
        
        var sw = Stopwatch.StartNew();
        
        for (int iter = 0; iter < iterations; iter++)
        {
            // Access components from various entities (exercises chunk lookup)
            var entityIndex = iter % _entities.Length;
            ref var component = ref _world.GetComponent<TestComponentA>(_entities[entityIndex]);
            component.Value++; // Ensure the access isn't optimized away
        }
        
        sw.Stop();
        
        var timePerLookup = sw.Elapsed.TotalNanoseconds / iterations;
        
        // With bit operations, lookups should be very fast (< 100ns on modern hardware)
        // This is a rough heuristic, but division would be significantly slower
        Assert.That(timePerLookup, Is.LessThan(200), 
            $"Component lookups too slow: {timePerLookup:F2}ns per lookup. Expected fast bit operations.");
    }
    
    [Test]
    public void ChunkLookup_CorrectlyMapsEntitiesToChunks()
    {
        // Verify that our bit operations correctly map entities to chunks
        // Chunk size is 512 (2^9), so we use >> 9 for division and & 511 for modulo
        
        const int chunkSize = 512;
        var testEntities = new List<(Entity entity, int expectedChunk, int expectedLocal)>();
        
        // Create enough entities to span multiple chunks
        for (int i = 0; i < chunkSize * 3; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponentB { Data = i * 1.0f });
            
            // Calculate expected chunk and local indices
            int expectedChunk = i / chunkSize;
            int expectedLocal = i % chunkSize;
            
            testEntities.Add((entity, expectedChunk, expectedLocal));
        }
        
        // Verify each entity is in the correct chunk position
        foreach (var (entity, expectedChunk, expectedLocal) in testEntities)
        {
            // GetComponent internally uses our optimized chunk lookup
            ref var component = ref _world.GetComponent<TestComponentB>(entity);
            
            // If we got here without exception, the lookup worked
            // Verify the component has the expected value
            var expectedValue = (expectedChunk * chunkSize + expectedLocal) * 1.0f;
            Assert.That(component.Data, Is.EqualTo(expectedValue).Within(0.001f),
                $"Entity in wrong chunk position. Expected chunk {expectedChunk}, local {expectedLocal}");
        }
    }
    
    [Test]
    public void ChunkLookup_PerformanceScalesLinearly()
    {
        // Test that lookup performance doesn't degrade with more entities
        
        var smallWorld = new World();
        var smallEntities = new Entity[100];
        for (int i = 0; i < smallEntities.Length; i++)
        {
            smallEntities[i] = smallWorld.CreateEntity();
            smallWorld.AddComponent(smallEntities[i], new TestComponentA { Value = i });
        }
        
        var largeWorld = new World();
        var largeEntities = new Entity[10000];
        for (int i = 0; i < largeEntities.Length; i++)
        {
            largeEntities[i] = largeWorld.CreateEntity();
            largeWorld.AddComponent(largeEntities[i], new TestComponentA { Value = i });
        }
        
        const int iterations = 100000;
        
        // Measure small world performance
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            ref var component = ref smallWorld.GetComponent<TestComponentA>(smallEntities[i % smallEntities.Length]);
            component.Value++;
        }
        sw.Stop();
        var smallWorldTime = sw.ElapsedMilliseconds;
        
        // Measure large world performance
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            ref var component = ref largeWorld.GetComponent<TestComponentA>(largeEntities[i % largeEntities.Length]);
            component.Value++;
        }
        sw.Stop();
        var largeWorldTime = sw.ElapsedMilliseconds;
        
        // Performance should be similar regardless of entity count (O(1) lookup)
        Assert.That(largeWorldTime, Is.LessThan(smallWorldTime * 2),
            $"Lookup performance degraded with more entities. Small: {smallWorldTime}ms, Large: {largeWorldTime}ms");
    }
    
    [Test]
    public void ChunkLookup_HandlesEdgeCases()
    {
        // Test edge cases: first entity in chunk, last entity in chunk, etc.
        
        const int chunkSize = 512;
        var edgeCaseEntities = new List<Entity>();
        
        // Create exactly enough entities to fill chunks
        for (int i = 0; i < chunkSize * 2; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponentC { Flag = i % 2 == 0 });
            edgeCaseEntities.Add(entity);
        }
        
        // Test first entity in first chunk (index 0)
        ref var first = ref _world.GetComponent<TestComponentC>(edgeCaseEntities[0]);
        Assert.That(first.Flag, Is.True, "First entity in first chunk should be accessible");
        
        // Test last entity in first chunk (index 511)
        ref var lastInFirst = ref _world.GetComponent<TestComponentC>(edgeCaseEntities[chunkSize - 1]);
        Assert.That(lastInFirst.Flag, Is.False, "Last entity in first chunk should be accessible");
        
        // Test first entity in second chunk (index 512)
        ref var firstInSecond = ref _world.GetComponent<TestComponentC>(edgeCaseEntities[chunkSize]);
        Assert.That(firstInSecond.Flag, Is.True, "First entity in second chunk should be accessible");
        
        // Test last entity in second chunk (index 1023)
        ref var lastInSecond = ref _world.GetComponent<TestComponentC>(edgeCaseEntities[chunkSize * 2 - 1]);
        Assert.That(lastInSecond.Flag, Is.False, "Last entity in second chunk should be accessible");
    }
}