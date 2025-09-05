using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class WorldTests
{
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _world = new World();
    }
    
    [Test]
    public void CreateEntity_ReturnsValidEntity()
    {
        var entity = _world.CreateEntity();
        
        Assert.That(entity.IsValid, Is.True);
        Assert.That(entity.Id, Is.GreaterThan(0u));
        Assert.That(entity.Version, Is.GreaterThan(0u));
    }
    
    [Test]
    public void CreateEntity_MultipleCalls_ReturnsDifferentIds()
    {
        var entity1 = _world.CreateEntity();
        var entity2 = _world.CreateEntity();
        var entity3 = _world.CreateEntity();
        
        Assert.That(entity1.Id, Is.Not.EqualTo(entity2.Id));
        Assert.That(entity2.Id, Is.Not.EqualTo(entity3.Id));
        Assert.That(entity1.Id, Is.Not.EqualTo(entity3.Id));
    }
    
    [Test]
    public void IsAlive_NewEntity_ReturnsTrue()
    {
        var entity = _world.CreateEntity();
        
        Assert.That(_world.IsAlive(entity), Is.True);
    }
    
    [Test]
    public void IsAlive_InvalidEntity_ReturnsFalse()
    {
        Assert.That(_world.IsAlive(Entity.Invalid), Is.False);
    }
    
    [Test]
    public void DestroyEntity_MakesEntityNotAlive()
    {
        var entity = _world.CreateEntity();
        Assert.That(_world.IsAlive(entity), Is.True);
        
        _world.DestroyEntity(entity);
        
        Assert.That(_world.IsAlive(entity), Is.False);
    }
    
    [Test]
    public void DestroyEntity_NonExistentEntity_DoesNotThrow()
    {
        var fakeEntity = new Entity(999, 1);
        
        Assert.DoesNotThrow(() => _world.DestroyEntity(fakeEntity));
    }
    
    [Test]
    public void DestroyEntity_AlreadyDestroyed_DoesNotThrow()
    {
        var entity = _world.CreateEntity();
        _world.DestroyEntity(entity);
        
        Assert.DoesNotThrow(() => _world.DestroyEntity(entity));
    }
    
    [Test]
    public void EntityVersionIncrementsOnReuse()
    {
        // Create and destroy an entity
        var entity1 = _world.CreateEntity();
        var originalId = entity1.Id;
        var originalVersion = entity1.Version;
        _world.DestroyEntity(entity1);
        
        // Create a new entity (should reuse the ID)
        var entity2 = _world.CreateEntity();
        
        Assert.That(entity2.Id, Is.EqualTo(originalId), "ID should be reused");
        Assert.That(entity2.Version, Is.GreaterThan(originalVersion), "Version should increment");
    }
    
    [Test]
    public void StaleHandleReturnsFalse()
    {
        var entity = _world.CreateEntity();
        _world.DestroyEntity(entity);
        
        // The original entity handle should now be stale
        Assert.That(_world.IsAlive(entity), Is.False);
        
        // Create a new entity that reuses the ID
        var newEntity = _world.CreateEntity();
        
        // The old handle should still be invalid even though ID was reused
        Assert.That(_world.IsAlive(entity), Is.False);
        Assert.That(_world.IsAlive(newEntity), Is.True);
    }
}