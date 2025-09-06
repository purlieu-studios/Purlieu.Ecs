using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class BENCH_QueryIteration
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithWarmupCount(3).WithIterationCount(10));
        }
    }

    private World _world = null!;
    private Entity[] _entities = null!;

    [Params(1000, 10_000, 50_000)]
    public int EntityCount { get; set; }

    [Params(2, 5, 10)]
    public int ArchetypeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _entities = new Entity[EntityCount];
        
        // Create entities with various component combinations
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            
            // Distribute entities across archetypes
            var archetypeIndex = i % ArchetypeCount;
            
            // All entities get ComponentA
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            
            // Vary component combinations based on archetype
            switch (archetypeIndex)
            {
                case 0:
                    // A only
                    break;
                case 1:
                    // A + B
                    _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
                    break;
                case 2:
                    // A + B + C
                    _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
                    _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
                    break;
                case 3:
                    // A + C
                    _world.AddComponent(_entities[i], new TestComponentC { IsActive = i % 2 == 0, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
                    break;
                case 4:
                    // A + D
                    _world.AddComponent(_entities[i], new TestComponentD { Id = i, Hash = (uint)(i * 31) });
                    break;
                default:
                    // A + B + C + D + E for remaining archetypes
                    _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
                    _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
                    _world.AddComponent(_entities[i], new TestComponentD { Id = i, Hash = (uint)(i * 31) });
                    _world.AddComponent(_entities[i], new TestComponentE { X = i, Y = i, Z = i, W = i, R = i, G = i, B = i, A = i });
                    break;
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world = null!;
        _entities = null!;
    }

    [Benchmark(Baseline = true)]
    public long SingleComponentQuery()
    {
        var query = _world.Query().With<TestComponentA>();
        long sum = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var componentA = chunk.GetSpan<TestComponentA>();
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += componentA[i].X + componentA[i].Y + componentA[i].Z;
            }
        }
        
        return sum;
    }

    [Benchmark]
    public long TwoComponentQuery()
    {
        var query = _world.Query().With<TestComponentA>().With<TestComponentB>();
        long sum = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var componentA = chunk.GetSpan<TestComponentA>();
            var componentB = chunk.GetSpan<TestComponentB>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += componentA[i].X + componentA[i].Y + (long)componentB[i].X + (long)componentB[i].Y;
            }
        }
        
        return sum;
    }

    [Benchmark]
    public long ThreeComponentQuery()
    {
        var query = _world.Query().With<TestComponentA>().With<TestComponentB>().With<TestComponentC>();
        long sum = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var componentA = chunk.GetSpan<TestComponentA>();
            var componentB = chunk.GetSpan<TestComponentB>();
            var componentC = chunk.GetSpan<TestComponentC>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += componentA[i].X + componentA[i].Y + (long)componentB[i].X + (long)componentB[i].Y + componentC[i].Priority;
            }
        }
        
        return sum;
    }

    [Benchmark]
    public long QueryWithWithout()
    {
        var query = _world.Query().With<TestComponentA>().With<TestComponentB>().Without<TestComponentE>();
        long sum = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var componentA = chunk.GetSpan<TestComponentA>();
            var componentB = chunk.GetSpan<TestComponentB>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += componentA[i].X + componentA[i].Y + (long)componentB[i].X + (long)componentB[i].Y;
            }
        }
        
        return sum;
    }

    [Benchmark]
    public long ComplexQuery()
    {
        var query = _world.Query()
            .With<TestComponentA>()
            .With<TestComponentC>()
            .Without<TestComponentB>()
            .Without<TestComponentE>();
        long sum = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var componentA = chunk.GetSpan<TestComponentA>();
            var componentC = chunk.GetSpan<TestComponentC>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += componentA[i].X + componentA[i].Y + componentC[i].Priority + (componentC[i].IsActive ? 1 : 0);
            }
        }
        
        return sum;
    }

    [Benchmark]
    public int QueryCount()
    {
        var query = _world.Query().With<TestComponentA>().With<TestComponentB>();
        return query.Count();
    }

    [Benchmark]
    public int ArchetypeScalingTest()
    {
        // Test how query performance scales with archetype count
        int matchCount = 0;
        var query = _world.Query().With<TestComponentA>();
        
        foreach (var chunk in query.Chunks())
        {
            matchCount += chunk.Count;
        }
        
        return matchCount;
    }
}