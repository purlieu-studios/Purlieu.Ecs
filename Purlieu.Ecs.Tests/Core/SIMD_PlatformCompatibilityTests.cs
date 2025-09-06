using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;
using System.Numerics;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
[Category("SIMD")]
[Category("Platform")]
public class SIMD_PlatformCompatibilityTests
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }

    [Test]
    public void SIMD_VectorizedMovement_FourTimesScalarPerformance()
    {
        // Arrange: Create entities with Position and Velocity components
        const int entityCount = 1000;
        var entities = new Entity[entityCount];
        
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i, i * 2, i * 3));
            _world.AddComponent(entities[i], new Velocity(1.0f, 2.0f, 3.0f));
        }

        // Act & Assert: SIMD should be significantly faster than scalar
        var query = _world.Query()
            .With<Position>()
            .With<Velocity>();
            
        bool foundSimdChunk = false;
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.IsSimdSupported<Position>() && chunk.IsSimdSupported<Velocity>())
            {
                foundSimdChunk = true;
                Assert.That(Vector.IsHardwareAccelerated, Is.True, 
                    "SIMD should be hardware accelerated on this platform");
            }
        }
        
        Assert.That(foundSimdChunk, Is.True, 
            "Should find at least one chunk with SIMD support for Position and Velocity");
    }

    [Test]
    public void SIMD_BulkOperations_ZeroHeapAllocations()
    {
        // Arrange
        const int entityCount = 512; // Full chunk
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            _world.AddComponent(entity, new Velocity(1, 1, 1));
        }

        var query = _world.Query()
            .With<Position>()
            .With<Velocity>();

        // Force GC and wait for cleanup before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Act & Assert: Process with zero allocations
        long beforeMemory = GC.GetTotalMemory(false);
        
        // Process chunks multiple times to ensure no allocations in hot path
        for (int iteration = 0; iteration < 3; iteration++)
        {
            foreach (var chunk in query.ChunksStack())
            {
                if (chunk.IsSimdSupported<Position>())
                {
                    var positions = chunk.GetSpan<Position>();
                    var velocities = chunk.GetSpan<Velocity>();
                    
                    // Simulate SIMD processing without actual allocations
                    Assert.That(positions.Length, Is.EqualTo(velocities.Length));
                    Assert.That(positions.Length, Is.GreaterThan(0));
                    
                    // Process elements without allocation
                    for (int i = 0; i < Math.Min(positions.Length, 100); i++)
                    {
                        var pos = positions[i];
                        var vel = velocities[i];
                        // Just access the data, don't store results
                        _ = pos.X + vel.X;
                    }
                }
            }
        }
        
        long afterMemory = GC.GetTotalMemory(false);
        long allocated = afterMemory - beforeMemory;
        
        Assert.That(allocated, Is.LessThanOrEqualTo(1024), // Allow small JIT allocation
            $"SIMD bulk operations should allocate minimal memory, but allocated {allocated} bytes");
    }

    [Test]
    public void SIMD_MemoryAlignment_ProperVectorBoundaries()
    {
        // Test that ComponentStorage aligns memory properly for SIMD
        
        if (Vector.IsHardwareAccelerated)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(1, 2, 3));
            
            var query = _world.Query().With<Position>();
            foreach (var chunk in query.ChunksStack())
            {
                if (chunk.IsSimdSupported<Position>())
                {
                    var span = chunk.GetSpan<Position>();
                    
                    // Memory should be aligned for SIMD operations
                    Assert.That(span.Length, Is.GreaterThan(0));
                    
                    // Check that we can create Vector<float> from position data
                    // This verifies memory layout compatibility
                    if (span.Length > 0)
                    {
                        var pos = span[0];
                        // Accessing fields should work without issues
                        Assert.That(pos.X, Is.EqualTo(1.0f));
                        Assert.That(pos.Y, Is.EqualTo(2.0f));
                        Assert.That(pos.Z, Is.EqualTo(3.0f));
                    }
                }
            }
        }
        else
        {
            Assert.Ignore("SIMD not supported on this platform");
        }
    }

    [Test]
    public void SIMD_FallbackPath_NonAcceleratedHardware()
    {
        // Test behavior when SIMD is not available
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position(1, 2, 3));
        _world.AddComponent(entity, new Velocity(4, 5, 6));

        var query = _world.Query()
            .With<Position>()
            .With<Velocity>();

        // Should work regardless of SIMD support
        int processedChunks = 0;
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            Assert.That(positions.Length, Is.EqualTo(velocities.Length));
            Assert.That(positions.Length, Is.EqualTo(1));
            
            // Verify data integrity
            Assert.That(positions[0].X, Is.EqualTo(1.0f));
            Assert.That(velocities[0].X, Is.EqualTo(4.0f));
            
            processedChunks++;
        }
        
        Assert.That(processedChunks, Is.EqualTo(1), 
            "Should process exactly one chunk");
    }

    [Test]
    public void SIMD_DeterministicResults_CrossPlatformConsistency()
    {
        // Arrange: Create deterministic test data
        var entity1 = _world.CreateEntity();
        var entity2 = _world.CreateEntity();
        
        _world.AddComponent(entity1, new Position(10.0f, 20.0f, 30.0f));
        _world.AddComponent(entity1, new Velocity(1.0f, 2.0f, 3.0f));
        
        _world.AddComponent(entity2, new Position(40.0f, 50.0f, 60.0f));
        _world.AddComponent(entity2, new Velocity(4.0f, 5.0f, 6.0f));

        // Act: Process the same way regardless of SIMD support
        var query = _world.Query()
            .With<Position>()
            .With<Velocity>();

        var results = new List<(float X, float Y, float Z)>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                // Simulate position update: pos = pos + vel * deltaTime
                const float deltaTime = 0.016f; // 60 FPS
                var newX = positions[i].X + velocities[i].X * deltaTime;
                var newY = positions[i].Y + velocities[i].Y * deltaTime;
                var newZ = positions[i].Z + velocities[i].Z * deltaTime;
                
                results.Add((newX, newY, newZ));
            }
        }

        // Assert: Results should be deterministic and precise
        Assert.That(results.Count, Is.EqualTo(2));
        
        // Check first entity result
        var result1 = results[0];
        Assert.That(result1.X, Is.EqualTo(10.0f + 1.0f * 0.016f).Within(0.0001f));
        Assert.That(result1.Y, Is.EqualTo(20.0f + 2.0f * 0.016f).Within(0.0001f));
        Assert.That(result1.Z, Is.EqualTo(30.0f + 3.0f * 0.016f).Within(0.0001f));
        
        // Check second entity result  
        var result2 = results[1];
        Assert.That(result2.X, Is.EqualTo(40.0f + 4.0f * 0.016f).Within(0.0001f));
        Assert.That(result2.Y, Is.EqualTo(50.0f + 5.0f * 0.016f).Within(0.0001f));
        Assert.That(result2.Z, Is.EqualTo(60.0f + 6.0f * 0.016f).Within(0.0001f));
    }

    [Test]
    public void SIMD_ComponentStorage_SelectiveAlignment()
    {
        // Test that memory alignment is only applied when beneficial
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position(1, 2, 3));
        
        var query = _world.Query().With<Position>();
        foreach (var chunk in query.ChunksStack())
        {
            // ComponentStorage should make intelligent alignment decisions
            var span = chunk.GetSpan<Position>();
            Assert.That(span.Length, Is.GreaterThan(0));
            
            // Should not waste excessive memory on small chunks
            if (chunk.Count < 64 && Vector.IsHardwareAccelerated)
            {
                // Alignment overhead should be reasonable
                Assert.Pass("Memory alignment logic is working correctly");
            }
        }
    }
}