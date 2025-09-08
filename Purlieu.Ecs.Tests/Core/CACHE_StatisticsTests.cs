using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;
using Position = Purlieu.Logic.Components.Position;
using Velocity = Purlieu.Logic.Components.Velocity;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests for query cache statistics functionality.
/// </summary>
[TestFixture]
[Category("Cache")]
public class CACHE_StatisticsTests
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
        
        // Create some test entities with different component combinations
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position { X = i, Y = i, Z = i });
            
            if (i % 2 == 0)
                _world.AddComponent(entity, new Velocity { X = 1, Y = 1, Z = 1 });
            
            if (i % 3 == 0)
                _world.AddComponent(entity, new MoveIntent(2, 2, 2));
        }
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void CacheStatistics_InitialState_AllZero()
    {
        var stats = _world.GetQueryCacheStatistics();
        
        Assert.That(stats.Hits, Is.EqualTo(0));
        Assert.That(stats.Misses, Is.EqualTo(0));
        Assert.That(stats.Invalidations, Is.EqualTo(0));
        Assert.That(stats.CurrentSize, Is.EqualTo(0));
        Assert.That(stats.HitRate, Is.EqualTo(0.0));
        Assert.That(stats.TotalQueries, Is.EqualTo(0));
    }

    [Test]
    public void CacheStatistics_FirstQuery_RecordsMiss()
    {
        _world.ResetQueryCacheStatistics();
        
        var query = _world.Query().With<Position>();
        var count = query.Count();
        
        var stats = _world.GetQueryCacheStatistics();
        
        Assert.That(stats.Hits, Is.EqualTo(0));
        Assert.That(stats.Misses, Is.EqualTo(1));
        Assert.That(stats.CurrentSize, Is.EqualTo(1));
        Assert.That(stats.HitRate, Is.EqualTo(0.0));
        Assert.That(stats.TotalQueries, Is.EqualTo(1));
        Assert.That(count, Is.EqualTo(100));
    }

    [Test]
    public void CacheStatistics_RepeatedQuery_RecordsHits()
    {
        _world.ResetQueryCacheStatistics();
        
        var query = _world.Query().With<Position>().With<Velocity>();
        
        // First execution - should be cache miss
        var count1 = query.Count();
        
        var statsAfterFirst = _world.GetQueryCacheStatistics();
        Assert.That(statsAfterFirst.Hits, Is.EqualTo(0));
        Assert.That(statsAfterFirst.Misses, Is.EqualTo(1));
        Assert.That(statsAfterFirst.HitRate, Is.EqualTo(0.0));
        
        // Second execution - should be cache hit
        var count2 = query.Count();
        
        var statsAfterSecond = _world.GetQueryCacheStatistics();
        Assert.That(statsAfterSecond.Hits, Is.EqualTo(1));
        Assert.That(statsAfterSecond.Misses, Is.EqualTo(1));
        Assert.That(statsAfterSecond.HitRate, Is.EqualTo(0.5));
        Assert.That(statsAfterSecond.TotalQueries, Is.EqualTo(2));
        
        // Third execution - should be another hit
        var count3 = query.Count();
        
        var statsAfterThird = _world.GetQueryCacheStatistics();
        Assert.That(statsAfterThird.Hits, Is.EqualTo(2));
        Assert.That(statsAfterThird.Misses, Is.EqualTo(1));
        Assert.That(statsAfterThird.HitRate, Is.EqualTo(2.0 / 3.0).Within(0.001));
        Assert.That(statsAfterThird.TotalQueries, Is.EqualTo(3));
        
        Assert.That(count1, Is.EqualTo(count2));
        Assert.That(count2, Is.EqualTo(count3));
        Assert.That(count1, Is.EqualTo(50)); // 50 entities have both Position and Velocity
    }

    [Test]
    public void CacheStatistics_DifferentQueries_RecordsSeparateMisses()
    {
        _world.ResetQueryCacheStatistics();
        
        // Query 1: Position only
        var query1 = _world.Query().With<Position>();
        var count1 = query1.Count();
        
        // Query 2: Position + Velocity
        var query2 = _world.Query().With<Position>().With<Velocity>();
        var count2 = query2.Count();
        
        // Query 3: Position + MoveIntent
        var query3 = _world.Query().With<Position>().With<MoveIntent>();
        var count3 = query3.Count();
        
        var stats = _world.GetQueryCacheStatistics();
        Assert.That(stats.Hits, Is.EqualTo(0));
        Assert.That(stats.Misses, Is.EqualTo(3));
        Assert.That(stats.CurrentSize, Is.EqualTo(3));
        Assert.That(stats.HitRate, Is.EqualTo(0.0));
        Assert.That(stats.TotalQueries, Is.EqualTo(3));
        
        Assert.That(count1, Is.EqualTo(100));
        Assert.That(count2, Is.EqualTo(50));
        Assert.That(count3, Is.GreaterThanOrEqualTo(33)); // At least 33 entities (every 3rd has MoveIntent)
    }

    [Test]
    public void CacheStatistics_Invalidation_IncrementsCounter()
    {
        _world.ResetQueryCacheStatistics();
        
        // Build up some cache entries
        var query1 = _world.Query().With<Position>();
        var query2 = _world.Query().With<Position>().With<Velocity>();
        query1.Count();
        query2.Count();
        
        var statsBeforeInvalidation = _world.GetQueryCacheStatistics();
        Assert.That(statsBeforeInvalidation.CurrentSize, Is.EqualTo(2));
        Assert.That(statsBeforeInvalidation.Invalidations, Is.EqualTo(0));
        
        // Create new archetype to trigger cache invalidation
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 0, Y = 0, Z = 0 });
        _world.AddComponent(entity, new Stunned()); // This creates a new archetype
        
        var statsAfterInvalidation = _world.GetQueryCacheStatistics();
        Assert.That(statsAfterInvalidation.CurrentSize, Is.EqualTo(0)); // Cache should be cleared
        Assert.That(statsAfterInvalidation.Invalidations, Is.EqualTo(2)); // Should track that 2 entries were invalidated
        Assert.That(statsAfterInvalidation.ArchetypeGeneration, Is.GreaterThan(statsBeforeInvalidation.ArchetypeGeneration));
    }

    [Test]
    public void CacheStatistics_ResetStatistics_ClearsCounters()
    {
        // Build up some statistics
        var query = _world.Query().With<Position>();
        query.Count(); // Miss
        query.Count(); // Hit
        query.Count(); // Hit
        
        var statsBeforeReset = _world.GetQueryCacheStatistics();
        Assert.That(statsBeforeReset.Hits, Is.EqualTo(2));
        Assert.That(statsBeforeReset.Misses, Is.EqualTo(1));
        Assert.That(statsBeforeReset.CurrentSize, Is.EqualTo(1));
        
        // Reset statistics
        _world.ResetQueryCacheStatistics();
        
        var statsAfterReset = _world.GetQueryCacheStatistics();
        Assert.That(statsAfterReset.Hits, Is.EqualTo(0));
        Assert.That(statsAfterReset.Misses, Is.EqualTo(0));
        Assert.That(statsAfterReset.Invalidations, Is.EqualTo(0));
        Assert.That(statsAfterReset.CurrentSize, Is.EqualTo(1)); // Cache entries remain, only counters reset
        Assert.That(statsAfterReset.HitRate, Is.EqualTo(0.0));
    }

    [Test]
    public void CacheStatistics_MixedQueryPatterns_AccurateHitRate()
    {
        _world.ResetQueryCacheStatistics();
        
        var positionQuery = _world.Query().With<Position>();
        var velocityQuery = _world.Query().With<Position>().With<Velocity>();
        var moveQuery = _world.Query().With<Position>().With<MoveIntent>();
        
        // Execute pattern: P, V, M, P(hit), V(hit), M(hit), P(hit), new query
        positionQuery.Count(); // Miss (1 miss, 0 hits)
        velocityQuery.Count();  // Miss (2 misses, 0 hits)  
        moveQuery.Count();     // Miss (3 misses, 0 hits)
        positionQuery.Count(); // Hit (3 misses, 1 hit)
        velocityQuery.Count(); // Hit (3 misses, 2 hits)
        moveQuery.Count();    // Hit (3 misses, 3 hits)
        positionQuery.Count(); // Hit (3 misses, 4 hits)
        
        // New query pattern
        var complexQuery = _world.Query().With<Position>().Without<Velocity>();
        complexQuery.Count(); // Miss (4 misses, 4 hits)
        
        var finalStats = _world.GetQueryCacheStatistics();
        Assert.That(finalStats.Hits, Is.EqualTo(4));
        Assert.That(finalStats.Misses, Is.EqualTo(4));
        Assert.That(finalStats.TotalQueries, Is.EqualTo(8));
        Assert.That(finalStats.HitRate, Is.EqualTo(0.5));
        Assert.That(finalStats.CurrentSize, Is.EqualTo(4)); // 4 different cached queries
    }

    [Test]
    public void CacheStatistics_ToString_FormatsCorrectly()
    {
        _world.ResetQueryCacheStatistics();
        
        // Create some statistics
        var query = _world.Query().With<Position>();
        query.Count(); // Miss
        query.Count(); // Hit
        query.Count(); // Hit
        
        var stats = _world.GetQueryCacheStatistics();
        var toString = stats.ToString();
        
        Assert.That(toString, Does.Contain("2H/1M")); // 2 hits, 1 miss
        Assert.That(toString, Does.Contain("66.7%")); // Hit rate
        Assert.That(toString, Does.Contain("1 cached")); // Cache size
        Assert.That(toString, Does.Contain("gen ")); // Generation
        
        Console.WriteLine($"Cache stats format: {toString}");
    }
}