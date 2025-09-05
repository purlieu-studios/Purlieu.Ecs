using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
public class BENCH_EntityCreation
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithWarmupCount(3).WithIterationCount(10));
        }
    }

    private World _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world = null!;
    }

    [Benchmark(Baseline = true)]
    [Arguments(100)]
    [Arguments(10_000)]
    [Arguments(100_000)]
    public void EntityCreation(int entityCount)
    {
        _world = new World(); // Reset for clean measurement
        
        for (int i = 0; i < entityCount; i++)
        {
            _world.CreateEntity();
        }
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(10_000)]
    [Arguments(100_000)]
    public void EntityCreationAndDestruction(int entityCount)
    {
        _world = new World(); // Reset for clean measurement
        var entities = new Entity[entityCount];
        
        // Create entities
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = _world.CreateEntity();
        }
        
        // Destroy entities
        for (int i = 0; i < entityCount; i++)
        {
            _world.DestroyEntity(entities[i]);
        }
    }

    [Benchmark]
    [Arguments(1_000)]
    public void EntityRecycling(int cycles)
    {
        _world = new World(); // Reset for clean measurement
        
        for (int i = 0; i < cycles; i++)
        {
            var entity = _world.CreateEntity();
            _world.DestroyEntity(entity);
        }
    }
}