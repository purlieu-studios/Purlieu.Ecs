using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests for dirty component tracking with bitsets.
/// Verifies that component modifications are correctly tracked and can be queried efficiently.
/// </summary>
[TestFixture]
[Category("DirtyTracking")]
public class DIRTY_TrackingTests
{
    private World _world;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void DirtyTracking_ComponentModification_MarksAsDirty()
    {
        // Create entity with components
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
        _world.AddComponent(entity, new TestComponentA { Value = 42 });

        // Initially, components should not be dirty (they were just added)
        Assert.That(_world.IsComponentDirty<Position>(entity), Is.False, 
            "Position should not be dirty initially");

        // Mark component as dirty and verify
        _world.MarkComponentDirty<Position>(entity);
        
        Assert.That(_world.IsComponentDirty<Position>(entity), Is.True,
            "Position should be marked as dirty");
        Assert.That(_world.IsComponentDirty<TestComponentA>(entity), Is.False,
            "TestComponentA should not be dirty");
        Assert.That(_world.IsEntityDirty(entity), Is.True,
            "Entity should be dirty when any component is dirty");
    }
    
    [Test]
    public void DirtyTracking_GetComponentForWrite_AutomaticallyMarksDirty()
    {
        // Create entity and get archetype/chunk info
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
        
        // Get chunk and modify component using GetComponentForWrite
        var query = _world.Query().With<Position>();
        foreach (var chunk in query.Chunks())
        {
            if (chunk.Count > 0)
            {
                // This should automatically mark the component as dirty
                ref var pos = ref chunk.GetComponentForWrite<Position>(0);
                pos.X = 100;
                
                // Verify the component is marked as dirty
                Assert.That(chunk.IsDirty<Position>(0), Is.True,
                    "Component should be automatically marked dirty after GetComponentForWrite");
                Assert.That(chunk.IsEntityDirty(0), Is.True,
                    "Entity should be dirty after component modification");
            }
        }
    }
    
    [Test]
    public void DirtyTracking_GetComponentReadOnly_DoesNotMarkDirty()
    {
        // Create entity with component
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 5, Y = 10, Z = 15 });
        
        // Access component read-only
        var query = _world.Query().With<Position>();
        foreach (var chunk in query.Chunks())
        {
            if (chunk.Count > 0)
            {
                // This should NOT mark the component as dirty
                ref readonly var pos = ref chunk.GetComponentReadOnly<Position>(0);
                var x = pos.X; // Read access only
                
                // Verify the component is NOT marked as dirty
                Assert.That(chunk.IsDirty<Position>(0), Is.False,
                    "Component should not be dirty after read-only access");
                Assert.That(chunk.IsEntityDirty(0), Is.False,
                    "Entity should not be dirty after read-only access");
            }
        }
    }
    
    [Test]
    public void DirtyTracking_ClearDirtyFlags_RemovesDirtyState()
    {
        // Create entity and mark component as dirty
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
        _world.MarkComponentDirty<Position>(entity);
        
        // Verify it's dirty
        Assert.That(_world.IsComponentDirty<Position>(entity), Is.True);
        
        // Clear dirty flags for Position components
        _world.ClearDirtyFlags<Position>();
        
        // Verify it's no longer dirty
        Assert.That(_world.IsComponentDirty<Position>(entity), Is.False,
            "Component should not be dirty after clearing dirty flags");
    }
    
    [Test]
    public void DirtyTracking_ClearAllDirtyFlags_RemovesAllDirtyStates()
    {
        // Create multiple entities with different dirty components
        var entity1 = _world.CreateEntity();
        var entity2 = _world.CreateEntity();
        
        _world.AddComponent(entity1, new Position { X = 1, Y = 2, Z = 3 });
        _world.AddComponent(entity1, new TestComponentA { Value = 10 });
        _world.AddComponent(entity2, new Position { X = 4, Y = 5, Z = 6 });
        _world.AddComponent(entity2, new TestComponentB { Id = 1, Value = 3.14f });
        
        // Mark various components as dirty
        _world.MarkComponentDirty<Position>(entity1);
        _world.MarkComponentDirty<TestComponentA>(entity1);
        _world.MarkComponentDirty<Position>(entity2);
        _world.MarkComponentDirty<TestComponentB>(entity2);
        
        // Verify they're all dirty
        Assert.That(_world.IsComponentDirty<Position>(entity1), Is.True);
        Assert.That(_world.IsComponentDirty<TestComponentA>(entity1), Is.True);
        Assert.That(_world.IsComponentDirty<Position>(entity2), Is.True);
        Assert.That(_world.IsComponentDirty<TestComponentB>(entity2), Is.True);
        
        // Clear all dirty flags
        _world.ClearAllDirtyFlags();
        
        // Verify they're all clean
        Assert.That(_world.IsComponentDirty<Position>(entity1), Is.False);
        Assert.That(_world.IsComponentDirty<TestComponentA>(entity1), Is.False);
        Assert.That(_world.IsComponentDirty<Position>(entity2), Is.False);
        Assert.That(_world.IsComponentDirty<TestComponentB>(entity2), Is.False);
    }
    
    [Test]
    public void DirtyTracking_GetEntitiesWithDirtyComponent_ReturnsCorrectEntities()
    {
        // Create multiple entities
        var entity1 = _world.CreateEntity();
        var entity2 = _world.CreateEntity();
        var entity3 = _world.CreateEntity();
        
        _world.AddComponent(entity1, new Position { X = 1, Y = 2, Z = 3 });
        _world.AddComponent(entity2, new Position { X = 4, Y = 5, Z = 6 });
        _world.AddComponent(entity3, new TestComponentA { Value = 100 }); // No Position
        
        // Mark only some Position components as dirty
        _world.MarkComponentDirty<Position>(entity1);
        _world.MarkComponentDirty<Position>(entity2);
        // entity3 doesn't have Position, so it won't be in results
        
        // Get entities with dirty Position components
        var dirtyEntities = _world.GetEntitiesWithDirtyComponent<Position>().ToList();
        
        Assert.That(dirtyEntities.Count, Is.EqualTo(2),
            "Should find exactly 2 entities with dirty Position components");
        Assert.That(dirtyEntities, Contains.Item(entity1));
        Assert.That(dirtyEntities, Contains.Item(entity2));
        Assert.That(dirtyEntities, Does.Not.Contain(entity3));
    }
    
    [Test]
    public void DirtyTracking_GetDirtyEntities_ReturnsAllDirtyEntities()
    {
        // Create multiple entities
        var entity1 = _world.CreateEntity();
        var entity2 = _world.CreateEntity();
        var entity3 = _world.CreateEntity();
        
        _world.AddComponent(entity1, new Position { X = 1, Y = 2, Z = 3 });
        _world.AddComponent(entity2, new TestComponentA { Value = 42 });
        _world.AddComponent(entity3, new TestComponentB { Id = 2, Value = 2.71f }); // Will stay clean
        
        // Mark some entities as dirty
        _world.MarkComponentDirty<Position>(entity1);
        _world.MarkComponentDirty<TestComponentA>(entity2);
        // entity3 remains clean
        
        // Get all dirty entities
        var dirtyEntities = _world.GetDirtyEntities().ToList();
        
        Assert.That(dirtyEntities.Count, Is.EqualTo(2),
            "Should find exactly 2 dirty entities");
        Assert.That(dirtyEntities, Contains.Item(entity1));
        Assert.That(dirtyEntities, Contains.Item(entity2));
        Assert.That(dirtyEntities, Does.Not.Contain(entity3));
    }
    
    [Test]
    public void DirtyTracking_ChunkDirtyVersion_IncrementsOnChanges()
    {
        // Create entity and get chunk
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
        
        var query = _world.Query().With<Position>();
        foreach (var chunk in query.Chunks())
        {
            if (chunk.Count > 0)
            {
                var initialVersion = chunk.DirtyVersion;
                
                // Mark component as dirty
                chunk.MarkDirty<Position>(0);
                
                var newVersion = chunk.DirtyVersion;
                Assert.That(newVersion, Is.GreaterThan(initialVersion),
                    "Dirty version should increment when component is marked dirty");
                
                // Mark again
                chunk.MarkDirty<Position>(0);
                
                var thirdVersion = chunk.DirtyVersion;
                Assert.That(thirdVersion, Is.GreaterThan(newVersion),
                    "Dirty version should increment on each dirty operation");
            }
        }
    }
    
    [Test]
    public void DirtyTracking_MultipleComponentTypes_TrackIndependently()
    {
        // Create entity with multiple component types
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
        _world.AddComponent(entity, new TestComponentA { Value = 42 });
        _world.AddComponent(entity, new TestComponentB { Id = 3, Value = 1.41f });
        
        // Mark only one component type as dirty
        _world.MarkComponentDirty<Position>(entity);
        
        // Verify independent tracking
        Assert.That(_world.IsComponentDirty<Position>(entity), Is.True,
            "Position should be dirty");
        Assert.That(_world.IsComponentDirty<TestComponentA>(entity), Is.False,
            "TestComponentA should not be dirty");
        Assert.That(_world.IsComponentDirty<TestComponentB>(entity), Is.False,
            "TestComponentB should not be dirty");
        Assert.That(_world.IsEntityDirty(entity), Is.True,
            "Entity should be dirty when any component is dirty");
        
        // Mark another component type as dirty
        _world.MarkComponentDirty<TestComponentA>(entity);
        
        Assert.That(_world.IsComponentDirty<Position>(entity), Is.True,
            "Position should still be dirty");
        Assert.That(_world.IsComponentDirty<TestComponentA>(entity), Is.True,
            "TestComponentA should now be dirty");
        Assert.That(_world.IsComponentDirty<TestComponentB>(entity), Is.False,
            "TestComponentB should still not be dirty");
    }
    
    [Test]
    public void DirtyTracking_PerformanceTest_FastBitsetOperations()
    {
        // Create many entities to test bitset performance
        const int entityCount = 1000;
        var entities = new Entity[entityCount];
        
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position { X = i, Y = i * 2, Z = i * 3 });
        }
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Mark half the entities as dirty
        for (int i = 0; i < entityCount / 2; i++)
        {
            _world.MarkComponentDirty<Position>(entities[i]);
        }
        
        sw.Stop();
        Console.WriteLine($"Marked {entityCount / 2} components dirty in {sw.ElapsedMilliseconds}ms");
        
        sw.Restart();
        
        // Count dirty entities
        var dirtyEntities = _world.GetEntitiesWithDirtyComponent<Position>().Count();
        
        sw.Stop();
        Console.WriteLine($"Counted {dirtyEntities} dirty entities in {sw.ElapsedMilliseconds}ms");
        
        Assert.That(dirtyEntities, Is.EqualTo(entityCount / 2),
            "Should find exactly half the entities marked as dirty");
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(50),
            "Dirty entity enumeration should be fast even with many entities");
    }
    
    // Test component types for dirty tracking tests
    public struct TestComponentA
    {
        public int Value;
    }
    
    public struct TestComponentB
    {
        public int Id;
        public float Value;
    }
}