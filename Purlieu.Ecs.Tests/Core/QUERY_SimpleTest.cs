using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Query;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class QUERY_SimpleTest
{
    private struct TestComponent
    {
        public int Value;
        public TestComponent(int value) { Value = value; }
    }

    [Test]
    public void BasicEntityCreation_Works()
    {
        using var world = new World();
        
        var entity = world.CreateEntity();
        Console.WriteLine($"Created entity: ID={entity.Id}, Ver={entity.Version}, Valid={entity != Entity.Invalid}");
        
        Assert.That(entity, Is.Not.EqualTo(Entity.Invalid));
        Assert.That(world.IsAlive(entity), Is.True);
        
        // This should not throw
        world.AddComponent(entity, new TestComponent(42));
        
        var component = world.GetComponent<TestComponent>(entity);
        Assert.That(component.Value, Is.EqualTo(42));
    }
    
    [Test]
    public void SimpleQuery_Works()
    {
        using var world = new World();
        
        // Create entities
        for (int i = 0; i < 5; i++)
        {
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TestComponent(i));
        }
        
        // Count entities
        int count = 0;
        world.Query<TestComponent>((Entity e, ref TestComponent c) =>
        {
            count++;
            Console.WriteLine($"Entity {e.Id}: {c.Value}");
        });
        
        Assert.That(count, Is.EqualTo(5));
    }
}