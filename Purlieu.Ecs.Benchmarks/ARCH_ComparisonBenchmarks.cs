using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;
using System.Runtime.CompilerServices;

namespace Purlieu.Ecs.Benchmarks;

[SimpleJob(RuntimeMoniker.Net80)]
[MemoryDiagnoser]
[MarkdownExporter]
public class ARCH_ComparisonBenchmarks
{
    private World _world = null!;
    private Entity[] _entities = null!;
    
    // Standard ECS benchmark components
    public struct Position
    {
        public float X, Y, Z;
    }
    
    public struct Velocity
    {
        public float X, Y, Z;
    }
    
    public struct Rotation
    {
        public float X, Y, Z, W;
    }
    
    [Params(1_000, 10_000, 100_000)]
    public int EntityCount { get; set; }
    
    [GlobalSetup]
    public void GlobalSetup()
    {
        _world = new World();
        _entities = new Entity[EntityCount];
    }
    
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _world?.Dispose();
    }
    
    [IterationSetup]
    public void IterationSetup()
    {
        // Clear any existing entities
        for (int i = 0; i < EntityCount; i++)
        {
            if (_entities[i].Id != 0 && _world.IsAlive(_entities[i]))
            {
                _world.DestroyEntity(_entities[i]);
            }
        }
    }
    
    // === ARCH COMPARISON BENCHMARKS ===
    
    [Benchmark(Description = "CreateEntityWithOneComponent")]
    public void CreateEntityWithOneComponent()
    {
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
        }
    }
    
    [Benchmark(Description = "CreateEntityWithTwoComponents")]
    public void CreateEntityWithTwoComponents()
    {
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
            _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
        }
    }
    
    [Benchmark(Description = "CreateEntityWithThreeComponents")]
    public void CreateEntityWithThreeComponents()
    {
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
            _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
            _world.AddComponent(_entities[i], new Rotation { X = 0, Y = 0, Z = 0, W = 1 });
        }
    }
    
    [Benchmark(Description = "SystemWithOneComponent")]
    public void SystemWithOneComponent()
    {
        // Setup entities
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
        }
        
        // Process system
        var query = _world.Query().With<Position>();
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += 1.0f;
                positions[i].Y += 1.0f;
                positions[i].Z += 1.0f;
            }
        }
    }
    
    [Benchmark(Description = "SystemWithTwoComponents")]
    public void SystemWithTwoComponents()
    {
        // Setup entities
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
            _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
        }
        
        // Process system - Movement system
        var query = _world.Query().With<Position>().With<Velocity>();
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
                positions[i].Z += velocities[i].Z;
            }
        }
    }
    
    [Benchmark(Description = "SystemWithThreeComponents")]
    public void SystemWithThreeComponents()
    {
        // Setup entities
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
            _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
            _world.AddComponent(_entities[i], new Rotation { X = 0, Y = 0, Z = 0, W = 1 });
        }
        
        // Process system - Complex physics system
        var query = _world.Query().With<Position>().With<Velocity>().With<Rotation>();
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            var rotations = chunk.GetSpan<Rotation>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                // Movement
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
                positions[i].Z += velocities[i].Z;
                
                // Rotation (simplified quaternion rotation)
                rotations[i].W += 0.01f;
                rotations[i].X += 0.001f;
            }
        }
    }
    
    [Benchmark(Description = "SystemWithTwoComponentsMultipleComposition")]
    public void SystemWithTwoComponentsMultipleComposition()
    {
        // Setup entities with mixed compositions
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
            
            // Create archetype fragmentation
            if (i % 2 == 0)
            {
                _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
            }
            if (i % 3 == 0)
            {
                _world.AddComponent(_entities[i], new Rotation { X = 0, Y = 0, Z = 0, W = 1 });
            }
        }
        
        // Process multiple different queries to test archetype iteration efficiency
        var positionQuery = _world.Query().With<Position>();
        var movementQuery = _world.Query().With<Position>().With<Velocity>();
        var complexQuery = _world.Query().With<Position>().With<Velocity>().With<Rotation>();
        
        // Process position-only entities
        foreach (var chunk in positionQuery.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += 0.1f;
            }
        }
        
        // Process movement entities
        foreach (var chunk in movementQuery.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X * 0.016f;
                positions[i].Y += velocities[i].Y * 0.016f;
                positions[i].Z += velocities[i].Z * 0.016f;
            }
        }
        
        // Process complex entities
        foreach (var chunk in complexQuery.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            var rotations = chunk.GetSpan<Rotation>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X * 0.016f;
                positions[i].Y += velocities[i].Y * 0.016f;
                positions[i].Z += velocities[i].Z * 0.016f;
                
                rotations[i].W += 0.01f;
            }
        }
    }
    
    // === PURLIEU-SPECIFIC OPTIMIZATIONS ===
    
    [Benchmark(Description = "PurlieuOptimized_SystemWithSIMD")]
    public void PurlieuOptimized_SystemWithSIMD()
    {
        // Setup entities
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
            _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
        }
        
        // Process system with SIMD optimization potential
        var query = _world.Query().With<Position>().With<Velocity>();
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            // Vectorized operations when possible
            if (System.Numerics.Vector.IsHardwareAccelerated && positions.Length >= 4)
            {
                ProcessWithVectorization(positions, velocities);
            }
            else
            {
                ProcessScalar(positions, velocities);
            }
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessWithVectorization(Span<Position> positions, Span<Velocity> velocities)
    {
        int vectorSize = System.Numerics.Vector<float>.Count;
        int vectorizedLength = positions.Length - (positions.Length % vectorSize);
        
        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            // Note: This is simplified - actual SIMD would need proper memory layout
            for (int j = 0; j < vectorSize && i + j < positions.Length; j++)
            {
                positions[i + j].X += velocities[i + j].X;
                positions[i + j].Y += velocities[i + j].Y;
                positions[i + j].Z += velocities[i + j].Z;
            }
        }
        
        // Handle remainder
        for (int i = vectorizedLength; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X;
            positions[i].Y += velocities[i].Y;
            positions[i].Z += velocities[i].Z;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessScalar(Span<Position> positions, Span<Velocity> velocities)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X;
            positions[i].Y += velocities[i].Y;
            positions[i].Z += velocities[i].Z;
        }
    }
    
    [Benchmark(Description = "PurlieuOptimized_CacheLineFriendly")]
    public void PurlieuOptimized_CacheLineFriendly()
    {
        // Setup entities
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
        }
        
        // Process with cache-friendly patterns
        var query = _world.Query().With<Position>();
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            
            // Our optimized chunk processing should be cache-line friendly
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += 1.0f;
                positions[i].Y += 1.0f;
                positions[i].Z += 1.0f;
            }
        }
    }
    
    [Benchmark(Description = "PurlieuOptimized_ZeroAllocation")]
    public void PurlieuOptimized_ZeroAllocation()
    {
        // Setup entities
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position { X = i, Y = i, Z = i });
        }
        
        // Process with our zero-allocation ChunksStack() iterator
        var query = _world.Query().With<Position>();
        float sum = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            for (int i = 0; i < positions.Length; i++)
            {
                sum += positions[i].X + positions[i].Y + positions[i].Z;
            }
        }
        
        // Ensure calculation isn't optimized away
        if (sum < 0) throw new InvalidOperationException("Unexpected result");
    }
}