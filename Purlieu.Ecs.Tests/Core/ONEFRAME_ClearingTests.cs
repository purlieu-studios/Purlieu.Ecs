using PurlieuEcs.Core;
using PurlieuEcs.Components;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class ONEFRAME_ClearingTests
{
    private World _world;
    
    [OneFrame]
    public struct OneFrameComponent
    {
        public int Value;
        public float Timer;
    }
    
    [OneFrame]
    public struct DamageEvent
    {
        public Entity Target;
        public float Amount;
    }
    
    public struct Position
    {
        public float X, Y, Z;
    }
    
    public struct Velocity
    {
        public float X, Y, Z;
    }
    
    [SetUp]
    public void SetUp()
    {
        _world = new World();
        
        // Register components
        _world.RegisterComponent<OneFrameComponent>();
        _world.RegisterComponent<Position>();
        _world.RegisterComponent<Velocity>();
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    [Test]
    public void OneFrameComponents_AfterClear_AreRemovedFromEntities()
    {
        // Arrange - Test only with one-frame components first
        var entity1 = _world.CreateEntity();
        var entity2 = _world.CreateEntity();
        
        // Add only one-frame components 
        _world.AddComponent(entity1, new OneFrameComponent { Value = 42, Timer = 1.5f });
        _world.AddComponent(entity2, new OneFrameComponent { Value = 100, Timer = 0.5f });
        
        // Verify components exist before clearing
        Assert.That(_world.HasComponent<OneFrameComponent>(entity1), Is.True);
        Assert.That(_world.HasComponent<OneFrameComponent>(entity2), Is.True);
        Assert.That(_world.GetComponent<OneFrameComponent>(entity1).Value, Is.EqualTo(42));
        Assert.That(_world.GetComponent<OneFrameComponent>(entity2).Value, Is.EqualTo(100));
        
        // Act
        _world.ClearOneFrameData();
        
        // Assert - One-frame components removed
        Assert.That(_world.HasComponent<OneFrameComponent>(entity1), Is.False);
        Assert.That(_world.HasComponent<OneFrameComponent>(entity2), Is.False);
        
        // Entities should still be alive
        Assert.That(_world.IsAlive(entity1), Is.True);
        Assert.That(_world.IsAlive(entity2), Is.True);
    }
    
    [Test]
    public void OneFrameEvents_AfterClear_AreRemovedFromEventChannels()
    {
        // Arrange
        var damageChannel = _world.Events<DamageEvent>();
        var entity = _world.CreateEntity();
        
        // Publish some events
        damageChannel.Publish(new DamageEvent { Target = entity, Amount = 50f });
        damageChannel.Publish(new DamageEvent { Target = entity, Amount = 25f });
        
        Assert.That(damageChannel.Count, Is.EqualTo(2));
        
        // Act
        _world.ClearOneFrameData();
        
        // Assert
        Assert.That(damageChannel.Count, Is.EqualTo(0));
    }
    
    [Test]
    public void ClearOneFrameData_WithNoOneFrameComponents_DoesNotAffectEntities()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
        _world.AddComponent(entity, new Velocity { X = 1, Y = 2, Z = 3 });
        
        // Act
        _world.ClearOneFrameData();
        
        // Assert - Nothing should change
        Assert.That(_world.HasComponent<Position>(entity), Is.True);
        Assert.That(_world.HasComponent<Velocity>(entity), Is.True);
        
        var pos = _world.GetComponent<Position>(entity);
        Assert.That(pos.X, Is.EqualTo(10f));
        Assert.That(pos.Y, Is.EqualTo(20f));
        Assert.That(pos.Z, Is.EqualTo(30f));
    }
    
    [Test]
    public void ClearOneFrameData_MultipleFrames_WorksConsistently()
    {
        // Arrange
        var entity = _world.CreateEntity();
        
        for (int frame = 0; frame < 10; frame++)
        {
            // Add one-frame component (this gets cleared each frame)
            _world.AddComponent(entity, new OneFrameComponent { Value = frame * 10 });
            
            // Update Position component (proper ECS pattern: get reference and modify)
            if (frame == 0)
            {
                // First frame: add the Position component
                _world.AddComponent(entity, new Position { X = frame, Y = frame * 2, Z = frame * 3 });
            }
            else
            {
                // Subsequent frames: update existing Position by reference
                ref var pos = ref _world.GetComponent<Position>(entity);
                pos.X = frame;
                pos.Y = frame * 2;
                pos.Z = frame * 3;
            }
            
            // Verify component exists
            Assert.That(_world.HasComponent<OneFrameComponent>(entity), Is.True);
            Assert.That(_world.GetComponent<OneFrameComponent>(entity).Value, Is.EqualTo(frame * 10));
            
            // Clear one-frame data
            _world.ClearOneFrameData();
            
            // Verify one-frame component removed, persistent component remains
            Assert.That(_world.HasComponent<OneFrameComponent>(entity), Is.False);
            Assert.That(_world.HasComponent<Position>(entity), Is.True);
            
            var position = _world.GetComponent<Position>(entity);
            Assert.That(position.X, Is.EqualTo((float)frame));
        }
    }
    
    [Test]
    public void ClearOneFrameData_PerformanceTest_HandlesLargeNumbers()
    {
        // Arrange - Create many entities with one-frame components
        const int entityCount = 10000;
        var entities = new Entity[entityCount];
        
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new OneFrameComponent { Value = i });
            _world.AddComponent(entities[i], new Position { X = i, Y = i * 2, Z = i * 3 });
        }
        
        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _world.ClearOneFrameData();
        stopwatch.Stop();
        
        // Assert - Should complete quickly and correctly
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100), "One-frame clearing should be fast");
        
        // Verify all one-frame components removed
        for (int i = 0; i < Math.Min(100, entityCount); i++) // Check first 100 entities
        {
            Assert.That(_world.HasComponent<OneFrameComponent>(entities[i]), Is.False);
            Assert.That(_world.HasComponent<Position>(entities[i]), Is.True);
        }
    }
}