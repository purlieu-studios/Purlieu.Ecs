using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests for delta-based archetype migration optimization.
/// Verifies that component migrations use cached delta information for efficient copying.
/// </summary>
[TestFixture]
[Category("DeltaMigration")]
public class DELTA_MigrationTests
{
    private World _world;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
        
        // Register test component types
        _world.RegisterComponent<TestComponentA>();
        _world.RegisterComponent<TestComponentB>();
        _world.RegisterComponent<TestConfigComponent>();
        _world.RegisterComponent<TestVelocityComponent>();
        
        // Clear delta cache to ensure clean tests
        ComponentDeltaCache.ClearCache();
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void DeltaMigration_ComponentPreservation_MaintainsDataDuringMigration()
    {
        // Create entity with initial components
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 100, Y = 200, Z = 300 });
        _world.AddComponent(entity, new TestComponentA { Value = 42 });
        
        // Store initial values to verify preservation
        var initialPosition = _world.GetComponent<Position>(entity);
        var initialComponentA = _world.GetComponent<TestComponentA>(entity);
        
        // Add a component (triggers migration to new archetype)
        _world.AddComponent(entity, new TestComponentB { Id = 999, Value = 9.99f });
        
        // Verify that existing component data was preserved during migration
        var finalPosition = _world.GetComponent<Position>(entity);
        var finalComponentA = _world.GetComponent<TestComponentA>(entity);
        var finalComponentB = _world.GetComponent<TestComponentB>(entity);
        
        Assert.That(finalPosition.X, Is.EqualTo(initialPosition.X), "Position.X should be preserved during migration");
        Assert.That(finalPosition.Y, Is.EqualTo(initialPosition.Y), "Position.Y should be preserved during migration");  
        Assert.That(finalPosition.Z, Is.EqualTo(initialPosition.Z), "Position.Z should be preserved during migration");
        Assert.That(finalComponentA.Value, Is.EqualTo(initialComponentA.Value), "TestComponentA should be preserved during migration");
        Assert.That(finalComponentB.Id, Is.EqualTo(999), "New component should be properly initialized");
        Assert.That(finalComponentB.Value, Is.EqualTo(9.99f), "New component value should be correct");
    }

    [Test]
    public void DeltaMigration_ComponentDeltaCache_ImprovesMigrationPerformance()
    {
        // Verify cache starts empty
        var initialStats = ComponentDeltaCache.GetCacheStats();
        Assert.That(ComponentDeltaCache.CacheSize, Is.EqualTo(0), "Cache should start empty");
        
        const int entityCount = 50;
        var entities = new Entity[entityCount];
        
        // Create entities with same initial archetype (Position only)
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position { X = i, Y = i * 2, Z = i * 3 });
        }
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Trigger migrations (all entities migrate from same source to same target archetype)
        for (int i = 0; i < entityCount; i++)
        {
            _world.AddComponent(entities[i], new TestComponentA { Value = i });
        }
        
        sw.Stop();
        Console.WriteLine($"Completed {entityCount} migrations in {sw.ElapsedMilliseconds}ms");
        
        var finalStats = ComponentDeltaCache.GetCacheStats();
        Console.WriteLine($"Cache size: {ComponentDeltaCache.CacheSize}, hits: {finalStats.hits}, misses: {finalStats.misses}, hit rate: {finalStats.hitRate:F1}%");
        
        // Should be very fast due to delta caching after first migration
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "Delta-based migrations should be fast");
        Assert.That(ComponentDeltaCache.CacheSize, Is.GreaterThan(0), "Cache should contain migration deltas");
        Assert.That(finalStats.hits, Is.GreaterThan(0), "Should have cache hits after first migration");
    }

    [Test]
    public void DeltaMigration_RemoveComponent_PreservesRemainingComponents()
    {
        // Create entity with multiple components
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 100, Y = 200, Z = 300 });
        _world.AddComponent(entity, new TestComponentA { Value = 999 });
        _world.AddComponent(entity, new TestComponentB { Id = 456, Value = 4.56f });
        
        // Store initial values for components that should be preserved
        var initialPosition = _world.GetComponent<Position>(entity);
        var initialComponentB = _world.GetComponent<TestComponentB>(entity);
        
        // Remove one component (triggers archetype migration)
        _world.RemoveComponent<TestComponentA>(entity);
        
        // Verify remaining components were preserved during migration
        var finalPosition = _world.GetComponent<Position>(entity);
        var finalComponentB = _world.GetComponent<TestComponentB>(entity);
        
        Assert.That(finalPosition.X, Is.EqualTo(initialPosition.X), "Position should be preserved during removal migration");
        Assert.That(finalPosition.Y, Is.EqualTo(initialPosition.Y), "Position.Y should be preserved");
        Assert.That(finalPosition.Z, Is.EqualTo(initialPosition.Z), "Position.Z should be preserved");
        Assert.That(finalComponentB.Id, Is.EqualTo(initialComponentB.Id), "TestComponentB should be preserved");
        Assert.That(finalComponentB.Value, Is.EqualTo(initialComponentB.Value), "TestComponentB value should be preserved");
        Assert.That(_world.HasComponent<TestComponentA>(entity), Is.False, "TestComponentA should be removed");
    }

    [Test]
    public void DeltaMigration_MultipleComponentOperations_MaintainsConsistency()
    {
        var entity = _world.CreateEntity();
        
        // Add components one by one (each triggers migration)
        _world.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
        Assert.That(_world.GetComponent<Position>(entity).X, Is.EqualTo(1), "Position should be correct after first add");
        
        _world.AddComponent(entity, new TestComponentA { Value = 42 });
        Assert.That(_world.GetComponent<Position>(entity).X, Is.EqualTo(1), "Position should be preserved after second add");
        Assert.That(_world.GetComponent<TestComponentA>(entity).Value, Is.EqualTo(42), "TestComponentA should be correct");
        
        _world.AddComponent(entity, new TestComponentB { Id = 123, Value = 1.23f });
        Assert.That(_world.GetComponent<Position>(entity).X, Is.EqualTo(1), "Position should be preserved after third add");
        Assert.That(_world.GetComponent<TestComponentA>(entity).Value, Is.EqualTo(42), "TestComponentA should be preserved");
        Assert.That(_world.GetComponent<TestComponentB>(entity).Id, Is.EqualTo(123), "TestComponentB should be correct");
        
        // Remove middle component
        _world.RemoveComponent<TestComponentA>(entity);
        Assert.That(_world.GetComponent<Position>(entity).X, Is.EqualTo(1), "Position should be preserved after removal");
        Assert.That(_world.GetComponent<TestComponentB>(entity).Id, Is.EqualTo(123), "TestComponentB should be preserved after removal");
        Assert.That(_world.HasComponent<TestComponentA>(entity), Is.False, "TestComponentA should be removed");
    }

    [Test]
    public void DeltaMigration_BulkOperations_EfficientCacheUsage()
    {
        const int batchCount = 3;
        const int entitiesPerBatch = 20;
        
        for (int batch = 0; batch < batchCount; batch++)
        {
            var entities = new Entity[entitiesPerBatch];
            
            // Create entities with Position components
            for (int i = 0; i < entitiesPerBatch; i++)
            {
                entities[i] = _world.CreateEntity();
                _world.AddComponent(entities[i], new Position { X = batch * 100 + i, Y = i, Z = i });
            }
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Add same component to all entities (same archetype migration for all)
            for (int i = 0; i < entitiesPerBatch; i++)
            {
                _world.AddComponent(entities[i], new TestComponentA { Value = batch * 1000 + i });
            }
            
            sw.Stop();
            Console.WriteLine($"Batch {batch}: {entitiesPerBatch} migrations in {sw.ElapsedMilliseconds}ms");
            
            // Later batches should be faster due to cached deltas
            if (batch > 0)
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(50), $"Batch {batch} should be faster due to cached deltas");
            }
        }
        
        var stats = ComponentDeltaCache.GetCacheStats();
        Console.WriteLine($"Final cache stats - size: {ComponentDeltaCache.CacheSize}, hits: {stats.hits}, hit rate: {stats.hitRate:F1}%");
        
        Assert.That(stats.hits, Is.GreaterThan(0), "Should have cache hits from repeated migrations");
        Assert.That(stats.hitRate, Is.GreaterThan(50.0), "Cache hit rate should be good for repeated patterns");
    }

    [Test]
    public void DeltaMigration_CacheClearance_ResetsStats()
    {
        // Create some entities and trigger migrations to populate cache
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
        _world.AddComponent(entity, new TestComponentA { Value = 42 });
        
        // Verify cache has content
        Assert.That(ComponentDeltaCache.CacheSize, Is.GreaterThan(0), "Cache should have entries");
        
        var statsBeforeClear = ComponentDeltaCache.GetCacheStats();
        
        // Clear cache
        ComponentDeltaCache.ClearCache();
        
        // Verify cache is cleared
        Assert.That(ComponentDeltaCache.CacheSize, Is.EqualTo(0), "Cache should be empty after clear");
        
        var statsAfterClear = ComponentDeltaCache.GetCacheStats();
        Assert.That(statsAfterClear.hits, Is.EqualTo(0), "Cache hits should be reset");
        Assert.That(statsAfterClear.misses, Is.EqualTo(0), "Cache misses should be reset");
        Assert.That(statsAfterClear.hitRate, Is.EqualTo(0.0), "Hit rate should be reset");
    }

    // Test component types for delta migration tests
    public struct TestComponentA
    {
        public int Value;
    }

    public struct TestComponentB
    {
        public int Id;
        public float Value;
    }

    public struct TestConfigComponent
    {
        public int ConfigValue;
        public bool ConfigFlag;
    }

    public struct TestVelocityComponent
    {
        public float X, Y, Z;
    }
}