using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class EASY_SimpleApiTests
{
    private World _world;
    
    // Test components
    public struct Position
    {
        public float X, Y, Z;
        
        public Position(float x, float y, float z = 0)
        {
            X = x; Y = y; Z = z;
        }
    }
    
    public struct Velocity
    {
        public float X, Y, Z;
        
        public Velocity(float x, float y, float z = 0)
        {
            X = x; Y = y; Z = z;
        }
    }
    
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
    public void FluentEntityCreation_SingleComponent()
    {
        // Before: Arch-style API 
        // var entity = world.Create(new Position(10, 20, 30));
        
        // Now: Easy Purlieu API
        var entity = _world.Create(new Position(10, 20, 30));
        
        Assert.That(_world.Has<Position>(entity), Is.True);
        
        ref var pos = ref _world.Get<Position>(entity);
        Assert.That(pos.X, Is.EqualTo(10f));
        Assert.That(pos.Y, Is.EqualTo(20f));
        Assert.That(pos.Z, Is.EqualTo(30f));
    }
    
    [Test]
    public void FluentEntityCreation_MultipleComponents()
    {
        // Before: Complex multi-line entity creation
        // var entity = world.CreateEntity();
        // world.AddComponent(entity, new Position(0, 0));
        // world.AddComponent(entity, new Velocity(1, 1));
        
        // Now: One-liner like Arch ECS
        var entity = _world.Create(new Position(0, 0), new Velocity(1, 1));
        
        Assert.That(_world.Has<Position>(entity), Is.True);
        Assert.That(_world.Has<Velocity>(entity), Is.True);
        
        var pos = _world.Get<Position>(entity);
        var vel = _world.Get<Velocity>(entity);
        
        Assert.That(pos.X, Is.EqualTo(0f));
        Assert.That(pos.Y, Is.EqualTo(0f));
        Assert.That(vel.X, Is.EqualTo(1f));
        Assert.That(vel.Y, Is.EqualTo(1f));
    }
    
    [Test]
    public void FluentQueryBuilder_BasicUsage()
    {
        // Create test data
        _world.Create(new Position(0, 0), new Velocity(1, 1)); // Moving
        _world.Create(new Position(10, 10)); // Static
        
        // Before: Manual chunk iteration
        // var query = _world.Query().With<Position>().With<Velocity>();
        // foreach (var chunk in query.ChunksStack()) { ... }
        
        // Now: Fluent query counting
        var movingCount = _world.Select().With<Position>().With<Velocity>().Count();
        var totalCount = _world.Select().With<Position>().Count();
        
        Assert.That(movingCount, Is.EqualTo(1));
        Assert.That(totalCount, Is.EqualTo(2));
    }
    
    [Test]
    public void ConvenienceMethods_CountAndAny()
    {
        _world.Create(new Position(0, 0));
        _world.Create(new Position(10, 10), new Velocity(1, 1));
        
        // Before: Manual counting with chunk iteration
        // Now: Simple methods
        Assert.That(_world.Count<Position>(), Is.EqualTo(2));
        Assert.That(_world.Count<Velocity>(), Is.EqualTo(1));
        
        Assert.That(_world.Any<Position>(), Is.True);
        Assert.That(_world.Any<Velocity>(), Is.True);
        
        // Non-existent components
        Assert.That(_world.Count<Health>(), Is.EqualTo(0));
        Assert.That(_world.Any<Health>(), Is.False);
    }
    
    [Test]
    public void FluentEntityManagement()
    {
        var entity = _world.Create(new Position(0, 0));
        
        // Before: Separate method calls
        // _world.AddComponent(entity, new Velocity(1, 1));
        // _world.RemoveComponent<Velocity>(entity);
        
        // Now: Fluent chaining
        _world.Add(entity, new Velocity(1, 1))
              .Remove<Velocity>(entity);
        
        Assert.That(_world.Has<Position>(entity), Is.True);
        Assert.That(_world.Has<Velocity>(entity), Is.False);
        
        // Fluent destruction
        _world.Destroy(entity);
        Assert.That(_world.Alive(entity), Is.False);
    }
    
    [Test]
    public void OptimalPerformance_ChunkIteration()
    {
        // Create large dataset
        const int entityCount = 1000;
        for (int i = 0; i < entityCount; i++)
        {
            _world.Create(new Position(i, i), new Velocity(1, 1));
        }
        
        // Easy API still allows access to optimal chunk iteration when needed
        var query = _world.Select().With<Position>().With<Velocity>().AsQuery();
        int processedCount = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
                processedCount++;
            }
        }
        
        Assert.That(processedCount, Is.EqualTo(entityCount));
    }
    
    private struct Health
    {
        public int Current, Maximum;
    }
}