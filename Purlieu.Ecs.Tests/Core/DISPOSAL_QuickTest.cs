using NUnit.Framework;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class DISPOSAL_QuickTest
{
    [Test]
    public void World_ThrowsObjectDisposedException_AfterDispose()
    {
        // Arrange
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComp { Value = 42 });
        var query = world.Query();
        
        // Act - dispose the world
        world.Dispose();
        
        // Assert - all operations should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => world.CreateEntity());
        Assert.Throws<ObjectDisposedException>(() => world.GetComponent<TestComp>(entity));
        Assert.Throws<ObjectDisposedException>(() => query.Count());
    }
    
    private struct TestComp
    {
        public int Value;
    }
}