using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic.Events;
using Purlieu.Logic.Systems;
using System.Numerics;

namespace Purlieu.Logic.Tests;

/// <summary>
/// Tests for SIMD-optimized movement system functionality.
/// </summary>
[TestFixture]
public class SIMD_MovementTests
{
    private World _world = null!;
    private MovementSystem _movementSystem = null!;

    [SetUp]
    public void SetUp()
    {
        _world = new World();
        _movementSystem = new MovementSystem();
        
        // Register Logic components
        LogicBootstrap.RegisterComponents(_world);
        
        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void SIMD_MovementSystem_ProcessesVelocityCorrectly()
    {
        // Create test entities with position and velocity
        var entities = new Entity[100];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i * 10f, i * 20f, i * 30f));
            _world.AddComponent(entities[i], new Velocity(1f, 2f, 3f));
        }
        
        // Execute movement system
        _movementSystem.Execute(_world, 0.016f);
        
        // Verify positions were updated correctly (60 FPS = 0.016f deltaTime)
        const float expectedDelta = 0.016f;
        for (int i = 0; i < entities.Length; i++)
        {
            ref var position = ref _world.GetComponent<Position>(entities[i]);
            
            Assert.That(position.X, Is.EqualTo(i * 10f + 1f * expectedDelta).Within(0.001f));
            Assert.That(position.Y, Is.EqualTo(i * 20f + 2f * expectedDelta).Within(0.001f));
            Assert.That(position.Z, Is.EqualTo(i * 30f + 3f * expectedDelta).Within(0.001f));
        }
    }

    [Test]
    public void SIMD_MovementSystem_RespectsStunnedEntities()
    {
        var entity = _world.CreateEntity();
        var initialPosition = new Position(10f, 20f, 30f);
        
        _world.AddComponent(entity, initialPosition);
        _world.AddComponent(entity, new Velocity(5f, 10f, 15f));
        _world.AddComponent(entity, new Stunned(1.0f));
        
        _movementSystem.Execute(_world, 0.016f);
        
        // Position should not change for stunned entities
        ref var position = ref _world.GetComponent<Position>(entity);
        Assert.That(position.X, Is.EqualTo(initialPosition.X));
        Assert.That(position.Y, Is.EqualTo(initialPosition.Y));
        Assert.That(position.Z, Is.EqualTo(initialPosition.Z));
    }

    [Test]
    public void SIMD_MovementSystem_ProcessesForceAccumulation()
    {
        var entity = _world.CreateEntity();
        var initialVelocity = new Velocity(1f, 2f, 3f);
        
        _world.AddComponent(entity, new Position(0f, 0f, 0f));
        _world.AddComponent(entity, initialVelocity);
        _world.AddComponent(entity, new Force(10f, 20f, 30f));
        
        _movementSystem.Execute(_world, 0.016f);
        
        // Velocity should be updated by force (F = ma, a = F/m, m = 1, dt = 0.016)
        const float expectedAcceleration = 0.016f; // dt/mass
        ref var velocity = ref _world.GetComponent<Velocity>(entity);
        
        Assert.That(velocity.X, Is.EqualTo(initialVelocity.X + 10f * expectedAcceleration).Within(0.001f));
        Assert.That(velocity.Y, Is.EqualTo(initialVelocity.Y + 20f * expectedAcceleration).Within(0.001f));
        Assert.That(velocity.Z, Is.EqualTo(initialVelocity.Z + 30f * expectedAcceleration).Within(0.001f));
        
        // Force should be cleared after application
        ref var force = ref _world.GetComponent<Force>(entity);
        Assert.That(force.X, Is.EqualTo(0f));
        Assert.That(force.Y, Is.EqualTo(0f));
        Assert.That(force.Z, Is.EqualTo(0f));
    }

    [Test]
    public void SIMD_MovementSystem_ProcessesMoveIntents()
    {
        var entity = _world.CreateEntity();
        var initialPosition = new Position(10f, 20f, 30f);
        
        _world.AddComponent(entity, initialPosition);
        _world.AddComponent(entity, new MoveIntent(1f, 0f, -1f, 2.0f)); // Speed = 2.0
        
        var eventChannel = _world.Events<PositionChangedEvent>();
        var initialEventCount = eventChannel.Count;
        
        _movementSystem.Execute(_world, 0.016f);
        
        // Position should be updated by intent * speed
        ref var position = ref _world.GetComponent<Position>(entity);
        Assert.That(position.X, Is.EqualTo(initialPosition.X + 1f * 2.0f));
        Assert.That(position.Y, Is.EqualTo(initialPosition.Y + 0f * 2.0f));
        Assert.That(position.Z, Is.EqualTo(initialPosition.Z + -1f * 2.0f));
        
        // Position changed event should be published
        Assert.That(eventChannel.Count, Is.EqualTo(initialEventCount + 1));
    }

    [Test]
    public void SIMD_MovementSystem_NoAllocationsInHotPath()
    {
        // Create many entities to test allocation behavior
        var entities = new Entity[1000];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i, i, i));
            _world.AddComponent(entities[i], new Velocity(1f, 1f, 1f));
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        long initialGen1 = GC.CollectionCount(1);
        
        // Run movement system multiple times
        for (int i = 0; i < 100; i++)
        {
            _movementSystem.Execute(_world, 0.016f);
        }
        
        long finalGen0 = GC.CollectionCount(0);
        long finalGen1 = GC.CollectionCount(1);
        
        Assert.That(finalGen0 - initialGen0, Is.EqualTo(0), "No Gen0 collections should occur in movement hot path");
        Assert.That(finalGen1 - initialGen1, Is.EqualTo(0), "No Gen1 collections should occur in movement hot path");
    }

    [Test]
    public void SIMD_Components_CanBeProcessedManually()
    {
        if (!Vector.IsHardwareAccelerated)
        {
            Assert.Inconclusive("SIMD not supported on this hardware");
            return;
        }
        
        // Create entity with components that can be processed with SIMD manually
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position(1f, 2f, 3f));
        _world.AddComponent(entity, new Velocity(4f, 5f, 6f));
        _world.AddComponent(entity, new Force(7f, 8f, 9f));
        
        var query = _world.Query()
            .With<Position>()
            .With<Velocity>()
            .With<Force>();
        
        foreach (var chunk in query.ChunksStack())
        {
            // Note: Composite structs (Position, Velocity, Force) don't automatically support 
            // Vector<T> operations, but can be processed manually with unsafe code
            // The MovementSystem demonstrates manual SIMD processing of these components
            
            // Regular spans should be available
            var posSpan = chunk.GetSpan<Position>();
            var velSpan = chunk.GetSpan<Velocity>();
            var forceSpan = chunk.GetSpan<Force>();
            
            Assert.That(posSpan.Length, Is.GreaterThan(0));
            Assert.That(velSpan.Length, Is.GreaterThan(0));
            Assert.That(forceSpan.Length, Is.GreaterThan(0));
            
            // Verify component data is correct
            Assert.That(posSpan[0].X, Is.EqualTo(1f));
            Assert.That(velSpan[0].Y, Is.EqualTo(5f)); // Velocity(4f, 5f, 6f) - Y = 5f
            Assert.That(forceSpan[0].Z, Is.EqualTo(9f));
        }
    }
}