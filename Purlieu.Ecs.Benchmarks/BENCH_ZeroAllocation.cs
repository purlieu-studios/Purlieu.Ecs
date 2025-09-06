using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, baseline: true)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BENCH_ZeroAllocation
{
    private World _world = null!;
    private Entity[] _entities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        
        // Register test components to avoid reflection
        _world.RegisterComponent<TestComponentA>();
        _world.RegisterComponent<TestComponentB>();
        
        _entities = new Entity[1000];
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new TestComponentA { Value = i, X = i, Y = i, Z = i });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.25f });
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Query")]
    public long QueryIteration_SingleComponent()
    {
        var query = _world.Query().With<TestComponentA>();
        long sum = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            var components = chunk.GetSpan<TestComponentA>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += (long)components[i].Value;
            }
        }
        
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public long QueryIteration_TwoComponents()
    {
        var query = _world.Query().With<TestComponentA>().With<TestComponentB>();
        long sum = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            var componentA = chunk.GetSpan<TestComponentA>();
            var componentB = chunk.GetSpan<TestComponentB>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += (long)(componentA[i].Value + componentB[i].X);
            }
        }
        
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("ComponentAccess")]
    public long GetSpanOperations()
    {
        var query = _world.Query().With<TestComponentA>();
        long sum = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            var span = chunk.GetSpan<TestComponentA>();
            // Access elements to ensure span is actually used
            for (int i = 0; i < chunk.Count; i++)
            {
                var component = span[i];
                sum += (long)component.Value;
            }
        }
        
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("EntityOperations")]
    public void AddRemoveComponents()
    {
        for (int i = 0; i < 100; i++)
        {
            _world.RemoveComponent<TestComponentA>(_entities[i]);
            _world.RemoveComponent<TestComponentB>(_entities[i]);
            
            _world.AddComponent(_entities[i], new TestComponentA { Value = i, X = i, Y = i, Z = i });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.25f });
        }
    }
}