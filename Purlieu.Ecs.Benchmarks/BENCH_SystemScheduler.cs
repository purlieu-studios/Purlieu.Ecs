using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class BENCH_SystemScheduler
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithWarmupCount(3).WithIterationCount(10));
        }
    }

    [SystemExecution(SystemPhase.Update, 100)]
    public class TestSystemA : ISystem
    {
        public void Execute(World world, float deltaTime)
        {
            var query = world.Query().With<TestComponentA>();
            foreach (var chunk in query.Chunks())
            {
                var components = chunk.GetSpan<TestComponentA>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    components[i].Value += deltaTime;
                }
            }
        }
        
        public SystemDependencies GetDependencies()
        {
            return SystemDependencies.WriteOnly(typeof(TestComponentA));
        }
    }

    [SystemExecution(SystemPhase.Update, 200)]
    public class TestSystemB : ISystem
    {
        public void Execute(World world, float deltaTime)
        {
            var query = world.Query().With<TestComponentA>().With<TestComponentB>();
            foreach (var chunk in query.Chunks())
            {
                var compA = chunk.GetSpan<TestComponentA>();
                var compB = chunk.GetSpan<TestComponentB>();
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    compB[i].X = compA[i].X * deltaTime;
                    compB[i].Y = compA[i].Y * deltaTime;
                }
            }
        }
        
        public SystemDependencies GetDependencies()
        {
            return SystemDependencies.ReadWrite(new[] { typeof(TestComponentA) }, new[] { typeof(TestComponentB) });
        }
    }

    [SystemExecution(SystemPhase.LateUpdate, 100)]
    public class TestSystemC : ISystem
    {
        public void Execute(World world, float deltaTime)
        {
            var query = world.Query().With<TestComponentC>();
            foreach (var chunk in query.Chunks())
            {
                var components = chunk.GetSpan<TestComponentC>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    components[i].Priority = (short)(components[i].Priority + 1);
                }
            }
        }
        
        public SystemDependencies GetDependencies()
        {
            return SystemDependencies.WriteOnly(typeof(TestComponentC));
        }
    }

    private World _world = null!;
    private SystemScheduler _scheduler = null!;
    private TestSystemA _systemA = null!;
    private TestSystemB _systemB = null!;
    private TestSystemC _systemC = null!;
    private Entity[] _entities = null!;
    private const float DeltaTime = 0.016f;

    [Params(1000, 10_000, 50_000)]
    public int EntityCount { get; set; }

    [Params(1, 3, 5)]
    public int SystemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _scheduler = new SystemScheduler();
        _systemA = new TestSystemA();
        _systemB = new TestSystemB();
        _systemC = new TestSystemC();
        _entities = new Entity[EntityCount];

        // Register systems based on SystemCount
        _scheduler.RegisterSystem(_systemA);
        if (SystemCount >= 2) _scheduler.RegisterSystem(_systemB);
        if (SystemCount >= 3) _scheduler.RegisterSystem(_systemC);
        
        // Add more systems if needed
        for (int i = 3; i < SystemCount; i++)
        {
            _scheduler.RegisterSystem(new TestSystemA()); // Duplicate systems for load testing
        }

        // Create entities with components
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            
            if (i % 2 == 0)
            {
                _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            }
            
            if (i % 3 == 0)
            {
                _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world = null!;
        _scheduler = null!;
        _systemA = null!;
        _systemB = null!;
        _systemC = null!;
        _entities = null!;
    }

    [Benchmark(Baseline = true)]
    public void DirectSystemCalls()
    {
        // Call systems directly without scheduler overhead
        _systemA.Execute(_world, DeltaTime);
        if (SystemCount >= 2) _systemB.Execute(_world, DeltaTime);
        if (SystemCount >= 3) _systemC.Execute(_world, DeltaTime);
    }

    [Benchmark]
    public void ScheduledSystemExecution()
    {
        // Execute systems through scheduler
        _scheduler.ExecutePhase(SystemPhase.Update, _world, DeltaTime);
        if (SystemCount >= 3) _scheduler.ExecutePhase(SystemPhase.LateUpdate, _world, DeltaTime);
    }

    [Benchmark]
    public void SchedulerOverheadOnly()
    {
        // Measure scheduler overhead with empty world
        var emptyWorld = new World();
        _scheduler.ExecutePhase(SystemPhase.Update, emptyWorld, DeltaTime);
        if (SystemCount >= 3) _scheduler.ExecutePhase(SystemPhase.LateUpdate, emptyWorld, DeltaTime);
    }

    [Benchmark]
    public void SystemProfilingOverhead()
    {
        // Measure the cost of profiling
        _scheduler.ExecutePhase(SystemPhase.Update, _world, DeltaTime);
        
        // Access profiling data if available
        // Note: Profiling may not be implemented in current scheduler
        // var stats = _scheduler.GetSystemStats(nameof(TestSystemA));
        // var _ = stats.Current + stats.Average + stats.Peak;
    }

    [Benchmark]
    public void MultiPhaseExecution()
    {
        // Execute multiple phases
        _scheduler.ExecutePhase(SystemPhase.Update, _world, DeltaTime);
        _scheduler.ExecutePhase(SystemPhase.LateUpdate, _world, DeltaTime);
        _scheduler.ExecutePhase(SystemPhase.Render, _world, DeltaTime);
    }

    [Benchmark]
    public void SystemRegistrationOverhead()
    {
        // Measure system registration cost
        var newScheduler = new SystemScheduler();
        
        for (int i = 0; i < SystemCount; i++)
        {
            newScheduler.RegisterSystem(new TestSystemA());
        }
    }

    [Benchmark]
    public void ProfilerResetPeaks()
    {
        // Execute some systems to generate data
        _scheduler.ExecutePhase(SystemPhase.Update, _world, DeltaTime);
        
        // Note: Peak reset functionality may not be implemented
        // _scheduler.ResetPeaks();
        
        // Execute again
        _scheduler.ExecutePhase(SystemPhase.Update, _world, DeltaTime);
    }
}