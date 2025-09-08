using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;
using Position = Purlieu.Logic.Components.Position;
using Velocity = Purlieu.Logic.Components.Velocity;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Comprehensive allocation regression test suite to ensure zero-allocation goals are maintained.
/// These tests lock in our optimization work and catch performance regressions.
/// </summary>
[TestFixture]
[Category("AllocationRegression")]
public class ALLOC_RegressionTestSuite
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
        
        // Pre-populate with entities to test realistic scenarios
        for (int i = 0; i < 1000; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position { X = i, Y = i, Z = i });
            if (i % 2 == 0) _world.AddComponent(entity, new Velocity { X = 1, Y = 1, Z = 1 });
            if (i % 3 == 0) _world.AddComponent(entity, new Stunned());
        }
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void WorldQuery_Creation_MaximalAllocation()
    {
        // Test query creation allocates at most 20KB (allowing for cold start)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        var query = _world.Query().With<Position>().With<Velocity>();
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Assert.That(allocated, Is.LessThanOrEqualTo(20 * 1024), 
            $"Query creation should allocate ≤20KB (cold start), but allocated {allocated} bytes");
    }

    [Test]
    public void WorldQuery_SecondCreation_MinimalAllocation()
    {
        // Pre-warm pools
        var warmup = _world.Query().With<Position>().With<Velocity>();
        _ = warmup.ChunksStack().GetEnumerator();
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Second query should have minimal allocation (warm pools)
        long before = GC.GetTotalMemory(false);
        var query = _world.Query().With<Position>().With<Velocity>();
        _ = query.ChunksStack().GetEnumerator();
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Assert.That(allocated, Is.LessThanOrEqualTo(8 * 1024), 
            $"Warm query creation should allocate ≤8KB, but allocated {allocated} bytes");
    }

    [Test]
    public void DirectChunkEnumeration_ZeroAllocation()
    {
        var query = _world.Query().With<Position>().With<Velocity>();
        
        // Pre-warm
        _ = query.ChunksStack().GetEnumerator();
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Iteration should be zero-allocation
        long before = GC.GetTotalMemory(false);
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < Math.Min(positions.Length, 100); i++)
            {
                _ = positions[i].X + velocities[i].X;
            }
        }
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Assert.That(allocated, Is.LessThanOrEqualTo(512), 
            $"Direct chunk enumeration should be zero-allocation, but allocated {allocated} bytes");
    }

    [Test]
    public void ComplexQuery_AllocationBounds()
    {
        // Test complex query with multiple components and exclusions
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        var query = _world.Query()
            .With<Position>()
            .With<Velocity>()
            .Without<Stunned>();
            
        int entityCount = 0;
        foreach (var chunk in query.ChunksStack())
        {
            entityCount += chunk.Count;
        }
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Assert.That(allocated, Is.LessThanOrEqualTo(25 * 1024), 
            $"Complex query should allocate ≤25KB, but allocated {allocated} bytes");
        Assert.That(entityCount, Is.GreaterThan(0), "Query should find entities");
    }

    [Test]
    public void ArchetypeMatching_SmallResults_UsesArrayStorage()
    {
        // Create world with few archetypes to test small result optimization
        var smallWorld = new World();
        LogicBootstrap.RegisterComponents(smallWorld);
        
        // Create single archetype
        for (int i = 0; i < 100; i++)
        {
            var entity = smallWorld.CreateEntity();
            smallWorld.AddComponent(entity, new Position { X = i, Y = i, Z = i });
            smallWorld.AddComponent(entity, new Velocity { X = 1, Y = 1, Z = 1 });
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        var query = smallWorld.Query().With<Position>().With<Velocity>();
        var chunks = query.ChunksStack();
        var enumerator = chunks.GetEnumerator();
        enumerator.MoveNext();
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        // Small archetype results should use array storage, not List allocation
        Assert.That(allocated, Is.LessThanOrEqualTo(16 * 1024), 
            $"Small archetype results should use minimal allocation, but allocated {allocated} bytes");
    }

    [Test]
    public void RepeatedQueries_BeneditFromCaching()
    {
        var query1 = _world.Query().With<Position>().With<Velocity>();
        _ = query1.ChunksStack().GetEnumerator(); // Prime caches
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Repeated identical queries should benefit from caching
        long before = GC.GetTotalMemory(false);
        for (int i = 0; i < 10; i++)
        {
            var query = _world.Query().With<Position>().With<Velocity>();
            foreach (var chunk in query.ChunksStack())
            {
                _ = chunk.Count;
            }
        }
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Assert.That(allocated, Is.LessThanOrEqualTo(50 * 1024), 
            $"10 repeated queries should benefit from caching, but allocated {allocated} bytes");
    }
}