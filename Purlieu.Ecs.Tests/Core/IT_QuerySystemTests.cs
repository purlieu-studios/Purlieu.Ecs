using NUnit.Framework;
using PurlieuEcs.Components;
using PurlieuEcs.Core;
using PurlieuEcs.Events;
using PurlieuEcs.Systems;
using Purlieu.Logic.Components;
using Purlieu.Logic.Events;
using Purlieu.Logic.Systems;
using Purlieu.Logic;

namespace PurlieuEcs.Tests.Core;

[TestFixture]
public class IT_QuerySystemTests
{
    private World _world = null!;
    private SystemScheduler _scheduler = null!;
    
    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
        _scheduler = new SystemScheduler();
    }
    
    [Test]
    public void Query_WithMultipleComponents_ReturnsCorrectEntities()
    {
        var e1 = _world.CreateEntity();
        _world.AddComponent(e1, new Position(10, 20, 0));
        _world.AddComponent(e1, new MoveIntent(1, 0, 0));
        
        var e2 = _world.CreateEntity();
        _world.AddComponent(e2, new Position(30, 40, 0));
        
        var e3 = _world.CreateEntity();
        _world.AddComponent(e3, new Position(50, 60, 0));
        _world.AddComponent(e3, new MoveIntent(0, 1, 0));
        _world.AddComponent(e3, new Stunned());
        
        var query = _world.Query()
            .With<Position>()
            .With<MoveIntent>()
            .Without<Stunned>();
        
        int count = 0;
        foreach (var chunk in query.Chunks())
        {
            count += chunk.Count;
        }
        
        Assert.That(count, Is.EqualTo(1), "Should only find entity without Stunned component");
    }
    
    [Test]
    public void MovementSystem_ProcessesIntents_EmitsPositionChangedEvents()
    {
        var movementSystem = new MovementSystem();
        _scheduler.RegisterSystem(movementSystem);
        
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position(10, 10, 0));
        _world.AddComponent(entity, new MoveIntent(5, -3, 0));
        
        var eventChannel = _world.Events<PositionChangedEvent>();
        int eventCount = 0;
        PositionChangedEvent lastEvent = default;
        
        _scheduler.UpdatePhase(_world, 0.016f, GamePhases.Update);
        
        eventChannel.ConsumeAll(e => 
        {
            eventCount++;
            lastEvent = e;
        });
        
        Assert.That(eventCount, Is.EqualTo(1), "Should emit one position changed event");
        Assert.That(lastEvent.NewX, Is.EqualTo(15), "X position should be updated");
        Assert.That(lastEvent.NewY, Is.EqualTo(7), "Y position should be updated");
        
        var pos = _world.GetComponent<Position>(entity);
        Assert.That(pos.X, Is.EqualTo(15), "Entity position X should be updated");
        Assert.That(pos.Y, Is.EqualTo(7), "Entity position Y should be updated");
    }
    
    [Test]
    public void MovementSystem_WithStunnedEntity_DoesNotMove()
    {
        var movementSystem = new MovementSystem();
        _scheduler.RegisterSystem(movementSystem);
        
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position(10, 10, 0));
        _world.AddComponent(entity, new MoveIntent(5, -3, 0));
        _world.AddComponent(entity, new Stunned());
        
        _scheduler.UpdatePhase(_world, 0.016f, GamePhases.Update);
        
        var eventChannel = _world.Events<PositionChangedIntent>();
        int eventCount = 0;
        eventChannel.ConsumeAll(_ => eventCount++);
        
        Assert.That(eventCount, Is.EqualTo(0), "Should not emit events for stunned entities");
        
        var pos = _world.GetComponent<Position>(entity);
        Assert.That(pos.X, Is.EqualTo(10), "Position X should not change");
        Assert.That(pos.Y, Is.EqualTo(10), "Position Y should not change");
    }
    
    [Test]
    public void Query_ChunkIteration_IsEfficient()
    {
        const int entityCount = 1000;
        
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i * 2, 0));
            if (i % 2 == 0)
            {
                _world.AddComponent(entity, new MoveIntent(1, 1, 0));
            }
        }
        
        var query = _world.Query().With<Position>().With<MoveIntent>();
        
        int totalEntities = 0;
        int chunkCount = 0;
        
        foreach (var chunk in query.Chunks())
        {
            chunkCount++;
            totalEntities += chunk.Count;
            
            var positions = chunk.GetSpan<Position>();
            var intents = chunk.GetSpan<MoveIntent>();
            
            Assert.That(positions.Length, Is.EqualTo(chunk.Count));
            Assert.That(intents.Length, Is.EqualTo(chunk.Count));
        }
        
        Assert.That(totalEntities, Is.EqualTo(500), "Should find half the entities with both components");
        Assert.That(chunkCount, Is.GreaterThan(0), "Should have at least one chunk");
    }
}