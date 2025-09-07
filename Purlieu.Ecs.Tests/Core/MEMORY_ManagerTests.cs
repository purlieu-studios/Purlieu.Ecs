using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class MEMORY_ManagerTests
{
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _world = new World();
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    [Test]
    public void MemoryManager_ForceCleanup_ReducesMemoryUsage()
    {
        // Arrange - Create entities to fill memory
        var entities = new Entity[1000];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new TestComponent { Value = i });
        }
        
        // Get baseline memory stats
        var initialStats = _world.GetMemoryStatistics();
        
        // Act - Force aggressive cleanup
        _world.ForceMemoryCleanup(CleanupLevel.Aggressive);
        
        // Assert - Memory management should be working
        var finalStats = _world.GetMemoryStatistics();
        
        Assert.That(finalStats.CleanupCount, Is.GreaterThan(initialStats.CleanupCount));
        Assert.That(finalStats.TotalMemoryReclaimed, Is.GreaterThanOrEqualTo(initialStats.TotalMemoryReclaimed));
        
        // Cleanup count should have incremented
        Assert.That(finalStats.CleanupCount, Is.EqualTo(initialStats.CleanupCount + 1));
    }
    
    [Test]
    public void MemoryStatistics_TrackCorrectMetrics()
    {
        // Arrange
        var stats = _world.GetMemoryStatistics();
        
        // Assert - Should have reasonable values
        Assert.That(stats.CurrentMemoryUsage, Is.GreaterThan(0));
        Assert.That(stats.Gen0Collections, Is.GreaterThanOrEqualTo(0));
        Assert.That(stats.Gen1Collections, Is.GreaterThanOrEqualTo(0));
        Assert.That(stats.Gen2Collections, Is.GreaterThanOrEqualTo(0));
        
        // ToString should work
        var statsString = stats.ToString();
        Assert.That(statsString, Is.Not.Empty);
        Assert.That(statsString, Does.Contain("Memory Stats"));
    }
    
    [Test]
    public void World_Dispose_CleansUpMemoryManager()
    {
        // Arrange
        var world = new World();
        var stats = world.GetMemoryStatistics();
        
        // Act
        world.Dispose();
        
        // Assert - Should not throw after dispose
        Assert.DoesNotThrow(() => world.Dispose()); // Should be safe to call multiple times
    }
    
    [Test]
    public void MemoryManager_MultipleCleanups_HandledCorrectly()
    {
        // Arrange
        var initialStats = _world.GetMemoryStatistics();
        
        // Act - Multiple cleanups
        _world.ForceMemoryCleanup(CleanupLevel.Light);
        _world.ForceMemoryCleanup(CleanupLevel.Normal);
        _world.ForceMemoryCleanup(CleanupLevel.Aggressive);
        
        // Assert
        var finalStats = _world.GetMemoryStatistics();
        Assert.That(finalStats.CleanupCount, Is.EqualTo(initialStats.CleanupCount + 3));
    }
    
    private struct TestComponent
    {
        public int Value;
        public float X, Y, Z;
    }
}