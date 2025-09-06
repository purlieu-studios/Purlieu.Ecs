using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class BENCH_ComponentAccess
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
    private TestComponentA[] _directArray = null!;
    private Chunk _chunk = null!;

    [Params(512, 2048, 8192)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _entities = new Entity[EntityCount];
        _directArray = new TestComponentA[EntityCount];
        
        // Create entities with components to get a populated chunk
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            // TODO: Add components to entities to populate chunks
            _directArray[i] = new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.5f };
        }
        
        // Get the chunk for testing (will need component management first)
        // For now, create a chunk directly for testing
        var componentTypes = new[] { typeof(TestComponentA) };
        _chunk = new Chunk(componentTypes, EntityCount);
        
        // Populate chunk with test entities
        for (int i = 0; i < EntityCount; i++)
        {
            _chunk.AddEntity(_entities[i]);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world = null!;
        _entities = null!;
        _directArray = null!;
        _chunk = null!;
    }

    [Benchmark(Baseline = true)]
    public long DirectArrayAccess()
    {
        long sum = 0;
        for (int i = 0; i < _directArray.Length; i++)
        {
            sum += _directArray[i].X + _directArray[i].Y + _directArray[i].Z;
        }
        return sum;
    }

    [Benchmark]
    public long ChunkSpanAccess()
    {
        var span = _chunk.GetSpan<TestComponentA>();
        long sum = 0;
        for (int i = 0; i < span.Length; i++)
        {
            sum += span[i].X + span[i].Y + span[i].Z;
        }
        return sum;
    }

    [Benchmark]
    public long ChunkSpanAccessWithPattern()
    {
        // Simulate typical ECS access pattern
        long sum = 0;
        var span = _chunk.GetSpan<TestComponentA>();
        
        for (int i = 0; i < _chunk.Count; i++)
        {
            ref var component = ref span[i];
            component.Value += 0.1f; // Typical mutation
            sum += component.X + component.Y + component.Z;
        }
        return sum;
    }

    [Benchmark] 
    public long ChunkRefAccess()
    {
        long sum = 0;
        for (int i = 0; i < _chunk.Count; i++)
        {
            ref var component = ref _chunk.GetComponent<TestComponentA>(i);
            component.Value += 0.1f;
            sum += component.X + component.Y + component.Z;
        }
        return sum;
    }

    [Benchmark]
    public long MultipleSpanAccess()
    {
        // Test overhead of multiple GetSpan calls
        long sum = 0;
        for (int iteration = 0; iteration < 10; iteration++)
        {
            var span = _chunk.GetSpan<TestComponentA>();
            for (int i = 0; i < span.Length; i++)
            {
                sum += span[i].X;
            }
        }
        return sum;
    }
}