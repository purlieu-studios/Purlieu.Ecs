using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

/// <summary>
/// Validates optimize-allocation-v2 branch improvements with allocation tracking.
/// Measures memory impact of thread safety fixes and optimization changes.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, baseline: true)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BENCH_OptimizationValidation
{
    private World _world = null!;
    private Entity[] _preCreatedEntities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        
        // Register components to avoid reflection in hot paths
        _world.RegisterComponent<TestComponentA>();
        _world.RegisterComponent<TestComponentB>();
        _world.RegisterComponent<TestComponentC>();
        
        // Pre-create some entities for modification tests
        _preCreatedEntities = new Entity[500];
        for (int i = 0; i < _preCreatedEntities.Length; i++)
        {
            _preCreatedEntities[i] = _world.CreateEntity();
            _world.AddComponent(_preCreatedEntities[i], new TestComponentA { Value = i, X = i, Y = i, Z = i });
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("EntityLifecycle")]
    public void EntityCreation_Baseline()
    {
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
        }
    }

    [Benchmark]
    [BenchmarkCategory("EntityLifecycle")]
    public void EntityCreation_WithComponents()
    {
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponentA { Value = i, X = i, Y = i, Z = i });
            _world.AddComponent(entity, new TestComponentB { X = i * 0.5f, Y = i * 0.25f });
        }
    }

    [Benchmark]
    [BenchmarkCategory("ArchetypeTransitions")]
    public void AddComponent_ArchetypeTransition()
    {
        // This triggers archetype transitions which should be optimized
        for (int i = 0; i < 50; i++)
        {
            var entity = _preCreatedEntities[i];
            
            // Add second component (triggers archetype transition)
            _world.AddComponent(entity, new TestComponentB { X = i, Y = i });
            
            // Add third component (another transition)
            _world.AddComponent(entity, new TestComponentC { IsActive = true, Priority = (short)i });
            
            // Remove components to reset for next iteration
            _world.RemoveComponent<TestComponentC>(entity);
            _world.RemoveComponent<TestComponentB>(entity);
        }
    }

    [Benchmark]
    [BenchmarkCategory("ThreadSafetyOverhead")]
    public void ArchetypeOperations_ThreadSafe()
    {
        // Tests the overhead of new thread-safe archetype operations
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponentA { Value = i });
            
            // This will use the thread-safe Archetype.AddEntity path
            _world.AddComponent(entity, new TestComponentB { X = i, Y = i });
            
            // Access component to test GetChunks() copy overhead
            ref var comp = ref _world.GetComponent<TestComponentA>(entity);
            comp.Value = i + 1;
        }
    }

    [Benchmark]
    [BenchmarkCategory("QueryAllocation")]
    public long Query_ZeroAllocation_Validation()
    {
        // Validate that queries remain zero-allocation after optimizations
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
    [BenchmarkCategory("SystemScheduler")]
    public void SystemScheduler_Registration()
    {
        var scheduler = _world.SystemScheduler;
        
        // Test concurrent registration overhead (now uses ConcurrentDictionary)
        for (int i = 0; i < 10; i++)
        {
            var system = new TestSystem { Id = i };
            scheduler.RegisterSystem(system);
        }
        
        // Execute to test lock overhead
        scheduler.ExecuteAllPhases(_world, 0.016f);
        
        // Cleanup
        for (int i = 0; i < 10; i++)
        {
            scheduler.UnregisterSystem<TestSystem>();
        }
    }

    [Benchmark]
    [BenchmarkCategory("MemoryPressure")]
    public void HighVolumeOperations()
    {
        // Stress test allocation patterns under high volume
        var entities = new Entity[200];
        
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new TestComponentA { Value = i });
        }
        
        // Trigger multiple archetype transitions
        for (int i = 0; i < entities.Length; i++)
        {
            if (i % 2 == 0)
            {
                _world.AddComponent(entities[i], new TestComponentB { X = i, Y = i });
            }
            if (i % 3 == 0)
            {
                _world.AddComponent(entities[i], new TestComponentC { IsActive = true });
            }
        }
        
        // Query operations
        var count = _world.Query().With<TestComponentA>().Count();
        
        // Cleanup
        for (int i = 0; i < entities.Length; i++)
        {
            _world.DestroyEntity(entities[i]);
        }
    }

    [Benchmark]
    [BenchmarkCategory("ChunkOperations")]
    public void ChunkMemoryLayout_Access()
    {
        // Test chunk memory access patterns with new thread-safe operations
        var query = _world.Query().With<TestComponentA>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var span = chunk.GetSpan<TestComponentA>();
            
            // Linear access pattern (should be cache-friendly)
            for (int i = 0; i < chunk.Count; i++)
            {
                var value = span[i].Value;
                span[i] = new TestComponentA { Value = value + 1, X = (int)value, Y = (int)value, Z = (int)value };
            }
        }
    }

    private class TestSystem : ISystem
    {
        public int Id { get; set; }
        public int ExecutionCount { get; private set; }

        public void Execute(World world, float deltaTime)
        {
            ExecutionCount++;
        }

        public SystemDependencies GetDependencies()
        {
            return new SystemDependencies();
        }
    }
}