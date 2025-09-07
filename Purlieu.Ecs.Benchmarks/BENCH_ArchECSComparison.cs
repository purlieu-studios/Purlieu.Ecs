using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;
using System.Runtime.CompilerServices;

namespace Purlieu.Ecs.Benchmarks;

/// <summary>
/// Benchmark suite comparing Purlieu.Ecs performance against ArchECS-style operations.
/// These benchmarks provide concrete data for performance claims and identify optimization opportunities.
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class BENCH_ArchECSComparison
{
    private World _world = null!;
    private Entity[] _entities = null!;
    
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
        
        // Create realistic entity distribution
        _entities = new Entity[10000];
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new Position(i, i, i));
            
            if (i % 2 == 0) _world.AddComponent(_entities[i], new Velocity(1, 1, 1));
            if (i % 5 == 0) _world.AddComponent(_entities[i], new Stunned());
        }
    }

    [Benchmark]
    public int EntityCreation_Purlieu()
    {
        var world = new World();
        LogicBootstrap.RegisterComponents(world);
        
        int count = 0;
        for (int i = 0; i < 1000; i++)
        {
            var entity = world.CreateEntity();
            world.AddComponent(entity, new Position(i, i, i));
            world.AddComponent(entity, new Velocity(1, 1, 1));
            count++;
        }
        return count;
    }

    [Benchmark]
    public int SimpleQuery_Purlieu()
    {
        var query = _world.Query().With<Position>();
        
        int count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        return count;
    }

    [Benchmark]
    public int ComplexQuery_Purlieu()
    {
        var query = _world.Query()
            .With<Position>()
            .With<Velocity>()
            .Without<Stunned>();
        
        int count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        return count;
    }

    [Benchmark]
    public float ComponentIteration_Purlieu()
    {
        var query = _world.Query().With<Position>().With<Velocity>();
        
        float sum = 0;
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < positions.Length; i++)
            {
                sum += positions[i].X + velocities[i].X;
            }
        }
        return sum;
    }

    [Benchmark]
    public float SIMDFriendlyIteration_Purlieu()
    {
        var query = _world.Query().With<Position>().With<Velocity>();
        
        float sum = 0;
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.IsSimdSupported<Position>())
            {
                var positions = chunk.GetSpan<Position>();
                var velocities = chunk.GetSpan<Velocity>();
                
                // SIMD-friendly tight loop
                for (int i = 0; i < positions.Length; i++)
                {
                    sum += positions[i].X * velocities[i].X;
                }
            }
        }
        return sum;
    }

    [Benchmark]
    public int QueryCreation_Purlieu()
    {
        // Measure query creation overhead
        int count = 0;
        for (int i = 0; i < 100; i++)
        {
            var query = _world.Query().With<Position>().With<Velocity>();
            count += query.Count();
        }
        return count;
    }

    [Benchmark]
    public int ArchetypeMatching_Purlieu()
    {
        // Test archetype matching performance
        var signatures = new[]
        {
            // Common query patterns
            (_world.Query().With<Position>(), "Position"),
            (_world.Query().With<Position>().With<Velocity>(), "Position+Velocity"),
            (_world.Query().With<Position>().Without<Stunned>(), "Position-Stunned"),
            (_world.Query().With<Position>().With<Velocity>().Without<Stunned>(), "Position+Velocity-Stunned")
        };

        int totalMatches = 0;
        foreach (var (query, name) in signatures)
        {
            totalMatches += query.Count();
        }
        return totalMatches;
    }

    // Placeholder benchmarks for ArchECS comparison
    // These would be implemented with actual ArchECS once available for comparison
    
    /*
    [Benchmark]
    public int EntityCreation_ArchECS()
    {
        // TODO: Implement ArchECS equivalent
        return 0;
    }

    [Benchmark] 
    public int SimpleQuery_ArchECS()
    {
        // TODO: Implement ArchECS equivalent
        return 0;
    }

    [Benchmark]
    public float ComponentIteration_ArchECS()
    {
        // TODO: Implement ArchECS equivalent
        return 0;
    }
    */

    [Benchmark]
    public long AllocationMeasurement_QueryCreation()
    {
        // Measure allocations during query creation and execution
        long before = GC.GetTotalMemory(false);
        
        var query = _world.Query().With<Position>().With<Velocity>();
        int count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        
        long after = GC.GetTotalMemory(false);
        return after - before;
    }

    // Helper method to create different archetype patterns for testing
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CreateArchetypePattern(World world, int entityCount, string pattern)
    {
        for (int i = 0; i < entityCount; i++)
        {
            var entity = world.CreateEntity();
            
            switch (pattern)
            {
                case "P": // Position only
                    world.AddComponent(entity, new Position(i, i, i));
                    break;
                case "PV": // Position + Velocity
                    world.AddComponent(entity, new Position(i, i, i));
                    world.AddComponent(entity, new Velocity(1, 1, 1));
                    break;
                case "PVS": // Position + Velocity + Stunned
                    world.AddComponent(entity, new Position(i, i, i));
                    world.AddComponent(entity, new Velocity(1, 1, 1));
                    world.AddComponent(entity, new Stunned());
                    break;
            }
        }
    }
}