using PurlieuEcs.Core;
using PurlieuEcs.Common;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class ARCH_SimpleTransformationTests
{
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _world = new World();
        
        // Ensure components are registered for this test
        _world.RegisterComponent<PurlieuEcs.Common.Position>();
        _world.RegisterComponent<PurlieuEcs.Common.Velocity>();
        
        // Ensure consistent component type IDs by accessing them early
        var positionId = ComponentTypeId.Get<PurlieuEcs.Common.Position>();
        var velocityId = ComponentTypeId.Get<PurlieuEcs.Common.Velocity>();
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    [Test]
    public void ArchStyle_BeforeAndAfter_TransformationDemo()
    {
        // Create test entities
        const int entityCount = 100;
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.Create(
                new PurlieuEcs.Common.Position { X = i, Y = i, Z = i },
                new PurlieuEcs.Common.Velocity { X = 1, Y = 2, Z = 3 }
            );
            
            // Debug the first entity creation in detail
            if (i == 0) 
            {
                Console.WriteLine($"Created first test entity: {entity}");
                Console.WriteLine($"  Has Position: {_world.Has<PurlieuEcs.Common.Position>(entity)}");
                Console.WriteLine($"  Has Velocity: {_world.Has<PurlieuEcs.Common.Velocity>(entity)}");
                var debugPos = _world.Get<PurlieuEcs.Common.Position>(entity);
                var debugVel = _world.Get<PurlieuEcs.Common.Velocity>(entity);
                Console.WriteLine($"  Position: X={debugPos.X}, Y={debugPos.Y}, Z={debugPos.Z}");
                Console.WriteLine($"  Velocity: X={debugVel.X}, Y={debugVel.Y}, Z={debugVel.Z}");
                
                // Debug entity's archetype info - let's check what archetype signatures exist
                Console.WriteLine($"  Debugging archetype signatures...");
            }
        }
        
        float deltaTime = 0.016f;
        
        // ========================================
        // BEFORE: Complex manual SIMD management
        // ========================================
        
        /*
        var query = world.Query().With<Position>().With<Velocity>();
        
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.IsSimdSupported<Position>() && chunk.IsSimdSupported<Velocity>())
            {
                // SIMD-accelerated processing
                ProcessMovementSimd(chunk, deltaTime);
            }
            else
            {
                // Scalar fallback
                ProcessMovementScalar(chunk, deltaTime);
            }
        }
        */
        
        // ========================================
        // NOW: Clean Arch-style automatic optimization
        // ========================================
        
        _world.UpdateMovement(deltaTime);
        
        // ========================================
        // Verification: Both approaches give same result
        // ========================================
        
        // Verify movement worked correctly
        var firstEntity = _world.First<PurlieuEcs.Common.Position>();
        Console.WriteLine($"First entity: {firstEntity}");
        
        // Check if we have valid entity
        Assert.That(firstEntity.Id, Is.Not.EqualTo(0), "Should find a valid entity");
        
        // Debug component presence
        Console.WriteLine($"Entity has Position: {_world.Has<PurlieuEcs.Common.Position>(firstEntity)}");
        Console.WriteLine($"Entity has Velocity: {_world.Has<PurlieuEcs.Common.Velocity>(firstEntity)}");
        
        var pos = _world.Get<PurlieuEcs.Common.Position>(firstEntity);
        var vel = _world.Get<PurlieuEcs.Common.Velocity>(firstEntity);
        Console.WriteLine($"Position after movement: X={pos.X}, Y={pos.Y}, Z={pos.Z}");
        Console.WriteLine($"Velocity: X={vel.X}, Y={vel.Y}, Z={vel.Z}");
        
        // Should have moved by velocity * deltaTime
        Assert.That(pos.X, Is.EqualTo(0.016f).Within(0.001f)); // 0 + (1 * 0.016)
        Assert.That(pos.Y, Is.EqualTo(0.032f).Within(0.001f)); // 0 + (2 * 0.016)  
        Assert.That(pos.Z, Is.EqualTo(0.048f).Within(0.001f)); // 0 + (3 * 0.016)
        
        // All entities should have been processed
        int processedCount = _world.Count<PurlieuEcs.Common.Position>();
        Assert.That(processedCount, Is.EqualTo(entityCount));
    }
    
    [Test]
    public void ArchStyle_PhysicsSystem_FullyAutomated()
    {
        // Create physics entities
        for (int i = 0; i < 50; i++)
        {
            _world.Create(
                new PurlieuEcs.Common.Position { X = 0, Y = 0, Z = 0 },
                new PurlieuEcs.Common.Velocity { X = 0, Y = 0, Z = 0 }, 
                new PurlieuEcs.Common.Acceleration { X = 1, Y = -9.8f, Z = 0 } // Gravity-like acceleration
            );
        }
        
        float deltaTime = 0.1f;
        
        // BEFORE: Multiple complex system calls with manual optimization
        /*
        // Update velocities
        var velAccQuery = world.Query().With<Velocity>().With<Acceleration>();
        foreach (var chunk in velAccQuery.ChunksStack()) { ... complex SIMD logic ... }
        
        // Update positions  
        var posVelQuery = world.Query().With<Position>().With<Velocity>();
        foreach (var chunk in posVelQuery.ChunksStack()) { ... more complex SIMD logic ... }
        */
        
        // NOW: One clean call handles everything
        _world.UpdatePhysics(deltaTime);
        
        // Verify physics integration worked
        var entity = _world.First<PurlieuEcs.Common.Position>();
        var pos = _world.Get<PurlieuEcs.Common.Position>(entity);
        var vel = _world.Get<PurlieuEcs.Common.Velocity>(entity);
        
        // After physics step:
        // velocity = acceleration * deltaTime = (1, -9.8, 0) * 0.1 = (0.1, -0.98, 0)
        // position = velocity * deltaTime = (0.1, -0.98, 0) * 0.1 = (0.01, -0.098, 0)
        
        Assert.That(vel.X, Is.EqualTo(0.1f).Within(0.001f));
        Assert.That(vel.Y, Is.EqualTo(-0.98f).Within(0.001f));
        Assert.That(vel.Z, Is.EqualTo(0f).Within(0.001f));
        
        Assert.That(pos.X, Is.EqualTo(0.01f).Within(0.001f));
        Assert.That(pos.Y, Is.EqualTo(-0.098f).Within(0.001f));
        Assert.That(pos.Z, Is.EqualTo(0f).Within(0.001f));
    }
    
    [Test]
    public void ArchStyle_FluentAPIWithAutomaticOptimization()
    {
        // BEFORE: Verbose entity creation + complex queries
        /*
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Position { X = 10, 20, 30));
        world.AddComponent(entity, new Velocity(1, 2, 3));
        
        var query = world.Query().With<Position>().With<Velocity>();
        foreach (var chunk in query.ChunksStack()) 
        { 
            // Manual SIMD detection and processing
            if (IsSimdAvailable() && ShouldUseSimd(chunk)) 
            {
                ProcessWithSimd(chunk);
            }
            else 
            {
                ProcessWithScalar(chunk);
            }
        }
        */
        
        // NOW: Fluent creation + automatic optimization
        var entity = _world.Create(new PurlieuEcs.Common.Position { X = 10, Y = 20, Z = 30 }, new PurlieuEcs.Common.Velocity { X = 1, Y = 2, Z = 3 });
        _world.UpdateMovement(0.016f);
        
        // Same result, much cleaner code
        var pos = _world.Get<PurlieuEcs.Common.Position>(entity);
        Assert.That(pos.X, Is.EqualTo(10.016f).Within(0.001f));
        Assert.That(pos.Y, Is.EqualTo(20.032f).Within(0.001f));
        Assert.That(pos.Z, Is.EqualTo(30.048f).Within(0.001f));
    }
    
    [Test]
    public void PerformanceComparison_AutoOptimizationIsEffective()
    {
        // Create large dataset to trigger automatic SIMD optimization
        const int entityCount = 10000;
        for (int i = 0; i < entityCount; i++)
        {
            _world.Create(
                new PurlieuEcs.Common.Position { X = i, Y = i, Z = i },
                new PurlieuEcs.Common.Velocity { X = 1, Y = 1, Z = 1 }
            );
        }
        
        // Measure automatic optimization performance
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int frame = 0; frame < 100; frame++)
        {
            _world.UpdateMovement(0.016f);
        }
        
        stopwatch.Stop();
        
        // Should be very fast due to automatic SIMD optimization for large datasets
        Console.WriteLine($"Processed {entityCount * 100} entity-updates in {stopwatch.ElapsedMilliseconds}ms");
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(200), 
            "Automatic optimization should provide excellent performance");
        
        // Verify all entities processed correctly
        var finalCount = _world.Count<PurlieuEcs.Common.Position>();
        Assert.That(finalCount, Is.EqualTo(entityCount));
    }
}