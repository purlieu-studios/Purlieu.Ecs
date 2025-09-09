using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;
using System.Threading.Tasks;

namespace Purlieu.Ecs.Benchmarks;

/// <summary>
/// Measures thread safety overhead from optimize-allocation-v2 concurrent fixes.
/// Compares single-threaded vs multi-threaded performance with new locks.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, baseline: true)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BENCH_ThreadSafetyOverhead
{
    private World _world = null!;
    private Entity[] _entities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        
        // Register components
        _world.RegisterComponent<TestComponentA>();
        _world.RegisterComponent<TestComponentB>();
        
        // Pre-create entities for modification tests
        _entities = new Entity[1000];
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new TestComponentA { Value = i, X = i, Y = i, Z = i });
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("EntityOperations")]
    public void SingleThreaded_EntityCreation()
    {
        for (int i = 0; i < 500; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TestComponentA { Value = i, X = i, Y = i, Z = i });
        }
    }

    [Benchmark]
    [BenchmarkCategory("EntityOperations")]
    public void MultiThreaded_EntityCreation()
    {
        var tasks = new Task[4];
        
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < 125; i++) // 125 * 4 = 500 total
                {
                    var entity = _world.CreateEntity();
                    _world.AddComponent(entity, new TestComponentA { Value = i, X = i, Y = i, Z = i });
                }
            });
        }
        
        Task.WaitAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("ArchetypeTransitions")]
    public void SingleThreaded_ArchetypeTransitions()
    {
        for (int i = 0; i < 200; i++)
        {
            var entity = _entities[i];
            _world.AddComponent(entity, new TestComponentB { X = i, Y = i });
            _world.RemoveComponent<TestComponentB>(entity);
        }
    }

    [Benchmark]
    [BenchmarkCategory("ArchetypeTransitions")]
    public void MultiThreaded_ArchetypeTransitions()
    {
        var tasks = new Task[4];
        
        for (int t = 0; t < tasks.Length; t++)
        {
            int threadId = t;
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < 50; i++) // 50 * 4 = 200 total
                {
                    var entity = _entities[threadId * 50 + i];
                    try
                    {
                        _world.AddComponent(entity, new TestComponentB { X = i, Y = i });
                        _world.RemoveComponent<TestComponentB>(entity);
                    }
                    catch (EntityNotFoundException)
                    {
                        // Expected race condition - entity might be modified by another thread
                    }
                }
            });
        }
        
        Task.WaitAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("SystemScheduler")]
    public void SingleThreaded_SystemRegistration()
    {
        var scheduler = _world.SystemScheduler;
        var systems = new TestSystem[50];
        
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i] = new TestSystem { Id = i };
            scheduler.RegisterSystem(systems[i]);
        }
        
        scheduler.ExecuteAllPhases(_world, 0.016f);
        
        // Cleanup
        for (int i = 0; i < systems.Length; i++)
        {
            scheduler.UnregisterSystem<TestSystem>();
        }
    }

    [Benchmark]
    [BenchmarkCategory("SystemScheduler")]
    public void MultiThreaded_SystemRegistration()
    {
        var scheduler = _world.SystemScheduler;
        var tasks = new Task[4];
        
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < 12; i++) // ~50 total systems
                {
                    var system = new TestSystem { Id = i };
                    scheduler.RegisterSystem(system);
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        scheduler.ExecuteAllPhases(_world, 0.016f);
        
        // Cleanup (single-threaded to avoid issues)
        for (int i = 0; i < 48; i++) // Conservative cleanup
        {
            try
            {
                scheduler.UnregisterSystem<TestSystem>();
            }
            catch
            {
                // Ignore cleanup issues in benchmark
            }
        }
    }

    [Benchmark]
    [BenchmarkCategory("QueryOperations")]
    public long SingleThreaded_QueryIteration()
    {
        long sum = 0;
        
        for (int iteration = 0; iteration < 10; iteration++)
        {
            var query = _world.Query().With<TestComponentA>();
            
            foreach (var chunk in query.ChunksStack())
            {
                var components = chunk.GetSpan<TestComponentA>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    sum += (long)components[i].Value;
                }
            }
        }
        
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("QueryOperations")]
    public long MultiThreaded_QueryIteration()
    {
        long totalSum = 0;
        var tasks = new Task<long>[4];
        
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                long sum = 0;
                
                for (int iteration = 0; iteration < 2; iteration++) // 2 * 4 = 8 total iterations
                {
                    var query = _world.Query().With<TestComponentA>();
                    
                    foreach (var chunk in query.ChunksStack())
                    {
                        var components = chunk.GetSpan<TestComponentA>();
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            sum += (long)components[i].Value;
                        }
                    }
                }
                
                return sum;
            });
        }
        
        Task.WaitAll(tasks);
        
        foreach (var task in tasks)
        {
            totalSum += task.Result;
        }
        
        return totalSum;
    }

    [Benchmark]
    [BenchmarkCategory("LockContention")]
    public void ChunkAccess_LockContention()
    {
        var tasks = new Task[8]; // More threads to increase contention
        
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                var query = _world.Query().With<TestComponentA>();
                
                // This will call GetChunks() which now makes a copy under lock
                foreach (var chunk in query.ChunksStack())
                {
                    var span = chunk.GetSpan<TestComponentA>();
                    
                    // Minimal work to focus on lock overhead
                    for (int i = 0; i < Math.Min(10, chunk.Count); i++)
                    {
                        var value = (long)span[i].Value;
                    }
                }
            });
        }
        
        Task.WaitAll(tasks);
    }

    private class TestSystem : ISystem
    {
        public int Id { get; set; }

        public void Execute(World world, float deltaTime)
        {
            // Minimal work
        }

        public SystemDependencies GetDependencies()
        {
            return new SystemDependencies();
        }
    }
}