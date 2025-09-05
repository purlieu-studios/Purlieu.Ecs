using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

// Test component structs
public struct TestComponentA { public int Value; }
public struct TestComponentB { public float Data; }
public struct TestComponentC { public bool Flag; }

[TestFixture]
public class ArchetypeSignatureTests
{
    [Test]
    public void EmptySignature_IsEmpty()
    {
        var signature = new ArchetypeSignature();
        
        Assert.That(signature.Has<TestComponentA>(), Is.False);
        Assert.That(signature.Has<TestComponentB>(), Is.False);
        Assert.That(signature.Has<TestComponentC>(), Is.False);
    }
    
    [Test]
    public void With_SingleComponent_HasComponent()
    {
        var signature = ArchetypeSignature.With<TestComponentA>();
        
        Assert.That(signature.Has<TestComponentA>(), Is.True);
        Assert.That(signature.Has<TestComponentB>(), Is.False);
        Assert.That(signature.Has<TestComponentC>(), Is.False);
    }
    
    [Test]
    public void Add_AddsComponent()
    {
        var signature = new ArchetypeSignature()
            .Add<TestComponentA>()
            .Add<TestComponentB>();
        
        Assert.That(signature.Has<TestComponentA>(), Is.True);
        Assert.That(signature.Has<TestComponentB>(), Is.True);
        Assert.That(signature.Has<TestComponentC>(), Is.False);
    }
    
    [Test]
    public void Remove_RemovesComponent()
    {
        var signature = new ArchetypeSignature()
            .Add<TestComponentA>()
            .Add<TestComponentB>()
            .Remove<TestComponentA>();
        
        Assert.That(signature.Has<TestComponentA>(), Is.False);
        Assert.That(signature.Has<TestComponentB>(), Is.True);
    }
    
    [Test]
    public void Remove_NonExistentComponent_DoesNotThrow()
    {
        var signature = new ArchetypeSignature();
        
        Assert.DoesNotThrow(() => signature.Remove<TestComponentA>());
    }
    
    [Test]
    public void Equality_SameComponents_AreEqual()
    {
        var signature1 = new ArchetypeSignature()
            .Add<TestComponentA>()
            .Add<TestComponentB>();
            
        var signature2 = new ArchetypeSignature()
            .Add<TestComponentB>()
            .Add<TestComponentA>(); // Different order
        
        Assert.That(signature1, Is.EqualTo(signature2));
        Assert.That(signature1.GetHashCode(), Is.EqualTo(signature2.GetHashCode()));
    }
    
    [Test]
    public void Equality_DifferentComponents_AreNotEqual()
    {
        var signature1 = new ArchetypeSignature().Add<TestComponentA>();
        var signature2 = new ArchetypeSignature().Add<TestComponentB>();
        
        Assert.That(signature1, Is.Not.EqualTo(signature2));
    }
    
    [Test]
    public void IsSupersetOf_ContainsAllComponents_ReturnsTrue()
    {
        var superset = new ArchetypeSignature()
            .Add<TestComponentA>()
            .Add<TestComponentB>()
            .Add<TestComponentC>();
            
        var subset = new ArchetypeSignature()
            .Add<TestComponentA>()
            .Add<TestComponentB>();
        
        Assert.That(superset.IsSupersetOf(subset), Is.True);
    }
    
    [Test]
    public void IsSupersetOf_MissingComponents_ReturnsFalse()
    {
        var signature1 = new ArchetypeSignature().Add<TestComponentA>();
        var signature2 = new ArchetypeSignature().Add<TestComponentB>();
        
        Assert.That(signature1.IsSupersetOf(signature2), Is.False);
    }
    
    [Test]
    public void IsSupersetOf_SameSignature_ReturnsTrue()
    {
        var signature = new ArchetypeSignature()
            .Add<TestComponentA>()
            .Add<TestComponentB>();
        
        Assert.That(signature.IsSupersetOf(signature), Is.True);
    }
    
    [Test]
    public void ComponentTypeId_ConsistentAcrossCalls()
    {
        var id1 = ComponentTypeId.Get<TestComponentA>();
        var id2 = ComponentTypeId.Get<TestComponentA>();
        var id3 = ComponentTypeId.Get<TestComponentB>();
        
        Assert.That(id1, Is.EqualTo(id2));
        Assert.That(id1, Is.Not.EqualTo(id3));
    }
    
    [Test]
    public void ToString_ContainsComponentInfo()
    {
        var signature = new ArchetypeSignature().Add<TestComponentA>();
        var str = signature.ToString();
        
        Assert.That(str, Contains.Substring("ArchetypeSignature"));
    }
}