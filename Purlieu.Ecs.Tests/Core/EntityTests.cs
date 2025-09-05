using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class EntityTests
{
    [Test]
    public void Entity_PackedConstructor_StoresIdAndVersionCorrectly()
    {
        var entity = new Entity(123, 456);
        
        Assert.That(entity.Id, Is.EqualTo(123));
        Assert.That(entity.Version, Is.EqualTo(456));
    }
    
    [Test]
    public void Entity_FromPacked_RoundTripWorks()
    {
        var original = new Entity(42, 17);
        var packed = original.ToPacked();
        var restored = Entity.FromPacked(packed);
        
        Assert.That(restored, Is.EqualTo(original));
        Assert.That(restored.Id, Is.EqualTo(42));
        Assert.That(restored.Version, Is.EqualTo(17));
    }
    
    [Test]
    public void Entity_Invalid_IsDefaultValue()
    {
        var invalid = Entity.Invalid;
        
        Assert.That(invalid.Id, Is.EqualTo(0));
        Assert.That(invalid.Version, Is.EqualTo(0));
        Assert.That(invalid.IsValid, Is.False);
    }
    
    [Test]
    public void Entity_ValidEntity_IsValidReturnsTrue()
    {
        var entity = new Entity(1, 1);
        
        Assert.That(entity.IsValid, Is.True);
    }
    
    [Test]
    public void Entity_Equality_WorksCorrectly()
    {
        var entity1 = new Entity(10, 5);
        var entity2 = new Entity(10, 5);
        var entity3 = new Entity(10, 6); // Different version
        var entity4 = new Entity(11, 5); // Different ID
        
        Assert.That(entity1, Is.EqualTo(entity2));
        Assert.That(entity1 == entity2, Is.True);
        Assert.That(entity1 != entity3, Is.True);
        Assert.That(entity1 != entity4, Is.True);
    }
    
    [Test]
    public void Entity_GetHashCode_ConsistentForEqualEntities()
    {
        var entity1 = new Entity(100, 200);
        var entity2 = new Entity(100, 200);
        
        Assert.That(entity1.GetHashCode(), Is.EqualTo(entity2.GetHashCode()));
    }
    
    [Test]
    public void Entity_ToString_ContainsIdAndVersion()
    {
        var entity = new Entity(42, 3);
        var str = entity.ToString();
        
        Assert.That(str, Contains.Substring("42"));
        Assert.That(str, Contains.Substring("3"));
        Assert.That(str, Contains.Substring("Entity"));
    }
}