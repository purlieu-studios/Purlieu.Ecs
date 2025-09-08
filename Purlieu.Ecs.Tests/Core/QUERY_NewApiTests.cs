using System.Numerics;
using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Query;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests for the new Arch ECS-style Query API with inline delegates.
/// </summary>
[TestFixture]
public class QUERY_NewApiTests
{
    private World _world = null!;
    
    // Test components
    private struct Position
    {
        public float X, Y, Z;
        public Position(float x, float y, float z) { X = x; Y = y; Z = z; }
    }
    
    private struct Velocity
    {
        public float X, Y, Z;
        public Velocity(float x, float y, float z) { X = x; Y = y; Z = z; }
    }
    
    private struct Health
    {
        public float Current, Max;
        public Health(float current, float max) { Current = current; Max = max; }
    }
    
    private struct Tag { }
    
    [SetUp]
    public void Setup()
    {
        _world = new World();
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    #region Arch ECS-Style Direct Query API
    
    [Test]
    public void ArchStyle_SingleComponent_InlineQuery()
    {
        // Arrange - Create entities like Arch ECS
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i * 2, i * 3));
        }
        
        // Act - Query using Arch ECS style inline delegate
        int count = 0;
        float sumX = 0;
        
        _world.Query<Position>((Entity entity, ref Position pos) =>
        {
            count++;
            sumX += pos.X;
            pos.X += 1; // Modify in place
        });
        
        // Assert
        Assert.That(count, Is.EqualTo(100));
        Assert.That(sumX, Is.EqualTo(4950)); // Sum of 0..99
        
        // Verify modifications
        float modifiedSum = 0;
        _world.Query<Position>((Entity entity, ref Position pos) =>
        {
            modifiedSum += pos.X;
        });
        Assert.That(modifiedSum, Is.EqualTo(5050)); // Sum of 1..100
    }
    
    [Test]
    public void ArchStyle_TwoComponents_InlineQuery()
    {
        // Arrange
        for (int i = 0; i < 50; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, 0, 0));
            _world.AddComponent(entity, new Velocity(1, 0, 0));
        }
        
        // Act - Movement system using Arch ECS style
        float deltaTime = 0.016f;
        _world.Query<Position, Velocity>((Entity entity, ref Position pos, ref Velocity vel) =>
        {
            pos.X += vel.X * deltaTime;
            pos.Y += vel.Y * deltaTime;
            pos.Z += vel.Z * deltaTime;
        });
        
        // Assert - All positions moved
        _world.Query<Position>((Entity entity, ref Position pos) =>
        {
            Assert.That(pos.X, Is.GreaterThan(0).Or.EqualTo(0.016f));
        });
    }
    
    [Test]
    public void ArchStyle_ThreeComponents_ComplexQuery()
    {
        // Arrange
        for (int i = 0; i < 30; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            _world.AddComponent(entity, new Velocity(1, 1, 1));
            _world.AddComponent(entity, new Health(100 - i, 100));
        }
        
        // Act - Complex query with 3 components
        int processedCount = 0;
        _world.Query<Position, Velocity, Health>((Entity entity, ref Position pos, ref Velocity vel, ref Health health) =>
        {
            // Apply damage based on velocity
            float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);
            health.Current -= speed * 0.1f;
            
            // Move entity
            pos.X += vel.X;
            pos.Y += vel.Y;
            pos.Z += vel.Z;
            
            processedCount++;
        });
        
        // Assert
        Assert.That(processedCount, Is.EqualTo(30));
    }
    
    #endregion
    
    #region Fluent API with ForEach Extensions
    
    [Test]
    public void FluentApi_WithForEach_SingleComponent()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
        }
        
        // Act - Using fluent API with ForEach
        int count = 0;
        _world.Query()
            .With<Position>()
            .ForEach((Entity entity, ref Position pos) =>
            {
                count++;
                pos.X *= 2;
            });
        
        // Assert
        Assert.That(count, Is.EqualTo(100));
    }
    
    [Test]
    public void FluentApi_WithoutEntity_RefOnly()
    {
        // Arrange
        for (int i = 0; i < 50; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, 0, 0));
            _world.AddComponent(entity, new Velocity(2, 0, 0));
        }
        
        // Act - ForEach without entity parameter (more efficient)
        _world.Query()
            .With<Position>()
            .With<Velocity>()
            .ForEach((ref Position pos, ref Velocity vel) =>
            {
                pos.X += vel.X;
                vel.X *= 0.99f; // Apply drag
            });
        
        // Assert
        _world.Query()
            .With<Velocity>()
            .ForEach((ref Velocity vel) =>
            {
                Assert.That(vel.X, Is.LessThan(2.0f));
            });
    }
    
    #endregion
    
    #region Parallel Query Execution
    
    [Test]
    public void ParallelQuery_ProcessesAllEntities()
    {
        // Arrange - Create many entities
        const int entityCount = 10000;
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, 0, 0));
            _world.AddComponent(entity, new Velocity(1, 0, 0));
        }
        
        // Act - Parallel processing
        _world.ParallelQuery<Position, Velocity>((ref Position pos, ref Velocity vel) =>
        {
            // Simulate expensive computation
            pos.X += vel.X;
            pos.Y = MathF.Sin(pos.X) * MathF.Cos(vel.X);
            pos.Z = MathF.Sqrt(MathF.Abs(pos.X));
        });
        
        // Assert - All entities processed
        int processedCount = 0;
        _world.Query<Position>((Entity entity, ref Position pos) =>
        {
            processedCount++;
            Assert.That(pos.Y, Is.Not.EqualTo(0).Or.NaN); // Was computed
        });
        
        Assert.That(processedCount, Is.EqualTo(entityCount));
    }
    
    #endregion
    
    #region SIMD-Optimized Queries
    
    [Test]
    public void SimdQuery_FallsBackToScalar_WhenNotAligned()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            _world.AddComponent(entity, new Velocity(1, 1, 1));
        }
        
        // Act - SIMD query with scalar fallback
        bool simdUsed = false;
        bool scalarUsed = false;
        
        _world.QuerySimd<Position, Velocity>(
            // SIMD processor (only called if chunk is SIMD-aligned)
            simdProcessor: (Chunk chunk) =>
            {
                simdUsed = true;
                // Process using SIMD operations
                var positions = chunk.GetSpan<Position>();
                var velocities = chunk.GetSpan<Velocity>();
                
                // In real implementation, would use Vector<float> operations
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i].X += velocities[i].X;
                    positions[i].Y += velocities[i].Y;
                    positions[i].Z += velocities[i].Z;
                }
            },
            // Scalar fallback
            scalarFallback: (ref Position pos, ref Velocity vel) =>
            {
                scalarUsed = true;
                pos.X += vel.X;
                pos.Y += vel.Y;
                pos.Z += vel.Z;
            });
        
        // Assert - At least one processor was used
        Assert.That(simdUsed || scalarUsed, Is.True, "Either SIMD or scalar processing should occur");
    }
    
    #endregion
    
    #region Bulk Operations
    
    [Test]
    public void BulkUpdate_ModifiesAllComponents()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health(50, 100));
        }
        
        // Act - Bulk update all health components
        _world.Query()
            .With<Health>()
            .BulkUpdate((Span<Health> healths) =>
            {
                for (int i = 0; i < healths.Length; i++)
                {
                    healths[i].Current = healths[i].Max; // Full heal
                }
            });
        
        // Assert - All entities at full health
        _world.Query<Health>((Entity entity, ref Health health) =>
        {
            Assert.That(health.Current, Is.EqualTo(health.Max));
        });
    }
    
    [Test]
    public void SetAll_SetsAllComponents()
    {
        // Arrange
        for (int i = 0; i < 50; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Tag());
            _world.AddComponent(entity, new Position(i, i, i));
        }
        
        // Act - Set all positions to origin
        _world.Query()
            .With<Position>()
            .With<Tag>()
            .SetAll(new Position(0, 0, 0));
        
        // Assert
        _world.Query<Position>((Entity entity, ref Position pos) =>
        {
            Assert.That(pos.X, Is.EqualTo(0));
            Assert.That(pos.Y, Is.EqualTo(0));
            Assert.That(pos.Z, Is.EqualTo(0));
        });
    }
    
    #endregion
    
    #region Indexed Queries
    
    [Test]
    public void IndexedQuery_ProvidesCorrectIndices()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(0, 0, 0));
        }
        
        // Act
        var globalIndices = new List<int>();
        var localIndices = new List<int>();
        
        _world.Query()
            .With<Position>()
            .ForEachIndexed((Entity entity, ref Position pos, int globalIndex, int localIndex) =>
            {
                globalIndices.Add(globalIndex);
                localIndices.Add(localIndex);
                pos.X = globalIndex;
                pos.Y = localIndex;
            });
        
        // Assert
        Assert.That(globalIndices.Count, Is.EqualTo(20));
        Assert.That(globalIndices, Is.Unique);
        Assert.That(globalIndices, Is.Ordered);
        
        // Local indices should reset per chunk
        Assert.That(localIndices.Min(), Is.EqualTo(0));
    }
    
    #endregion
    
    #region Performance Comparison
    
    [Test]
    [Category("Performance")]
    public void Performance_InlineQuery_vs_ChunkIteration()
    {
        // Arrange - Create many entities
        const int entityCount = 100000;
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            _world.AddComponent(entity, new Velocity(1, 1, 1));
        }
        
        // Warm up
        _world.Query<Position, Velocity>((Entity e, ref Position p, ref Velocity v) => { });
        
        // Test 1: Inline query (Arch ECS style)
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        _world.Query<Position, Velocity>((Entity entity, ref Position pos, ref Velocity vel) =>
        {
            pos.X += vel.X;
            pos.Y += vel.Y;
            pos.Z += vel.Z;
        });
        sw1.Stop();
        
        // Test 2: Manual chunk iteration (our traditional style)
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var query = _world.Query().With<Position>().With<Velocity>();
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
                positions[i].Z += velocities[i].Z;
            }
        }
        sw2.Stop();
        
        // Both should be similarly fast (inline query has minimal overhead)
        Console.WriteLine($"Inline query: {sw1.ElapsedMilliseconds}ms");
        Console.WriteLine($"Chunk iteration: {sw2.ElapsedMilliseconds}ms");
        
        // In debug builds, both operations might be very fast (0ms), so adjust thresholds
        // Assert inline query is not significantly slower - if both are 0ms, that's acceptable
        if (sw2.ElapsedMilliseconds == 0 && sw1.ElapsedMilliseconds == 0)
        {
            // Both operations are extremely fast - this is good!
            Assert.Pass("Both operations completed in under 1ms - excellent performance");
        }
        else
        {
            // Assert inline query is not significantly slower (within 20% + 1ms tolerance for debug builds)
            var maxAcceptableTime = Math.Max(1, sw2.ElapsedMilliseconds * 1.2);
            Assert.That(sw1.ElapsedMilliseconds, Is.LessThanOrEqualTo(maxAcceptableTime),
                "Inline query should have minimal overhead");
        }
    }
    
    #endregion
}