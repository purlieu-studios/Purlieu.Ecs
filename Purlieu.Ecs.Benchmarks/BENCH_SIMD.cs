using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;
using System.Numerics;

namespace Purlieu.Ecs.Benchmarks;

/// <summary>
/// Benchmarks comparing SIMD-optimized operations vs scalar operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, baseline: true)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BENCH_SIMD
{
    private World _world = null!;
    private Entity[] _entities = null!;
    private const int EntityCount = 10000;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        
        // Register SIMD-compatible components
        _world.RegisterComponent<SimdPosition>();
        _world.RegisterComponent<SimdVelocity>();
        _world.RegisterComponent<SimdForce>();
        
        _entities = new Entity[EntityCount];
        var random = new Random(42); // Deterministic seed
        
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i] = _world.CreateEntity();
            
            _world.AddComponent(_entities[i], new SimdPosition 
            { 
                X = (float)(random.NextDouble() * 100), 
                Y = (float)(random.NextDouble() * 100),
                Z = (float)(random.NextDouble() * 100)
            });
            
            _world.AddComponent(_entities[i], new SimdVelocity 
            { 
                X = (float)(random.NextDouble() * 10 - 5), 
                Y = (float)(random.NextDouble() * 10 - 5),
                Z = (float)(random.NextDouble() * 10 - 5)
            });
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Movement")]
    public void ScalarMovementUpdate()
    {
        var query = _world.Query().With<SimdPosition>().With<SimdVelocity>();
        const float deltaTime = 0.016f;
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<SimdPosition>();
            var velocities = chunk.GetSpan<SimdVelocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                positions[i].X += velocities[i].X * deltaTime;
                positions[i].Y += velocities[i].Y * deltaTime;
                positions[i].Z += velocities[i].Z * deltaTime;
            }
        }
    }

    [Benchmark]
    [BenchmarkCategory("Movement")]
    public void SimdMovementUpdate()
    {
        var query = _world.Query().With<SimdPosition>().With<SimdVelocity>();
        const float deltaTime = 0.016f;
        
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.IsSimdSupported<SimdPosition>() && chunk.IsSimdSupported<SimdVelocity>())
            {
                // Process SIMD-aligned elements
                var positionsSimd = chunk.GetSimdSpan<SimdPosition>();
                var velocitiesSimd = chunk.GetSimdSpan<SimdVelocity>();
                
                ProcessMovementSimd(positionsSimd, velocitiesSimd, deltaTime);
                
                // Process remainder elements
                var positionsRemainder = chunk.GetRemainderSpan<SimdPosition>();
                var velocitiesRemainder = chunk.GetRemainderSpan<SimdVelocity>();
                
                for (int i = 0; i < positionsRemainder.Length; i++)
                {
                    positionsRemainder[i].X += velocitiesRemainder[i].X * deltaTime;
                    positionsRemainder[i].Y += velocitiesRemainder[i].Y * deltaTime;
                    positionsRemainder[i].Z += velocitiesRemainder[i].Z * deltaTime;
                }
            }
            else
            {
                // Fallback to scalar
                var positions = chunk.GetSpan<SimdPosition>();
                var velocities = chunk.GetSpan<SimdVelocity>();
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    positions[i].X += velocities[i].X * deltaTime;
                    positions[i].Y += velocities[i].Y * deltaTime;
                    positions[i].Z += velocities[i].Z * deltaTime;
                }
            }
        }
    }
    
    private unsafe void ProcessMovementSimd(Span<SimdPosition> positions, Span<SimdVelocity> velocities, float deltaTime)
    {
        var vectorSize = Vector<float>.Count;
        var deltaVector = new Vector<float>(deltaTime);
        
        // Process positions and velocities in chunks compatible with Vector<float>
        // Since SimdPosition has X,Y,Z we need to process each component separately
        fixed (SimdPosition* posPtr = positions)
        fixed (SimdVelocity* velPtr = velocities)
        {
            float* posFloatPtr = (float*)posPtr;
            float* velFloatPtr = (float*)velPtr;
            
            int floatCount = positions.Length * 3; // 3 floats per position (X,Y,Z)
            
            for (int i = 0; i <= floatCount - vectorSize; i += vectorSize)
            {
                var posVec = new Vector<float>(new Span<float>(posFloatPtr + i, vectorSize));
                var velVec = new Vector<float>(new Span<float>(velFloatPtr + i, vectorSize));
                
                var result = posVec + velVec * deltaVector;
                result.CopyTo(new Span<float>(posFloatPtr + i, vectorSize));
            }
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ForceAccumulation")]
    public void ScalarForceAccumulation()
    {
        var query = _world.Query().With<SimdVelocity>().With<SimdForce>();
        const float deltaTime = 0.016f;
        const float mass = 1.0f;
        
        foreach (var chunk in query.ChunksStack())
        {
            var velocities = chunk.GetSpan<SimdVelocity>();
            var forces = chunk.GetSpan<SimdForce>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                velocities[i].X += forces[i].X / mass * deltaTime;
                velocities[i].Y += forces[i].Y / mass * deltaTime;
                velocities[i].Z += forces[i].Z / mass * deltaTime;
                
                // Clear forces
                forces[i].X = 0;
                forces[i].Y = 0;
                forces[i].Z = 0;
            }
        }
    }

    [Benchmark]
    [BenchmarkCategory("ForceAccumulation")]
    public void SimdForceAccumulation()
    {
        var query = _world.Query().With<SimdVelocity>().With<SimdForce>();
        const float deltaTime = 0.016f;
        const float mass = 1.0f;
        
        foreach (var chunk in query.ChunksStack())
        {
            chunk.ProcessVectorized(new ForceProcessor { DeltaTime = deltaTime, Mass = mass });
        }
    }
}

/// <summary>
/// SIMD-compatible position component.
/// </summary>
public struct SimdPosition
{
    public float X, Y, Z;
}

/// <summary>
/// SIMD-compatible velocity component.
/// </summary>
public struct SimdVelocity
{
    public float X, Y, Z;
}

/// <summary>
/// SIMD-compatible force component.
/// </summary>
public struct SimdForce
{
    public float X, Y, Z;
}

/// <summary>
/// Example VectorProcessor for force accumulation.
/// </summary>
public struct ForceProcessor : VectorProcessor<SimdVelocity>
{
    public float DeltaTime;
    public float Mass;
    
    public void ProcessSimd(Span<SimdVelocity> simdSpan)
    {
        // For demonstration - in real implementation would need to coordinate
        // with forces span and use proper SIMD operations
        var deltaVector = new Vector<float>(DeltaTime / Mass);
        // Actual SIMD implementation would be more complex
    }
    
    public void ProcessScalar(Span<SimdVelocity> scalarSpan)
    {
        // Fallback scalar processing
        for (int i = 0; i < scalarSpan.Length; i++)
        {
            // Would need access to forces here
            // This is simplified for demonstration
        }
    }
}