using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using PurlieuEcs.Core;
using PurlieuEcs.Logging;
using PurlieuEcs.Monitoring;
using PurlieuEcs.Query;

namespace Purlieu.Ecs.Benchmarks;

/// <summary>
/// Comprehensive performance benchmarking suite for Purlieu ECS.
/// Measures core operations against reference implementations.
/// Part of Phase 12: Hardening & v0 Release.
/// </summary>
[Config(typeof(EcsBenchmarkConfig))]
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 2)]
[HardwareCounters(HardwareCounter.BranchMispredictions, HardwareCounter.CacheMisses)]
public class BENCH_CorePerformance
{
    private World _world = null!;
    private Entity[] _entities = null!;
    private WorldQuery _simpleQuery = null!;
    private WorldQuery _complexQuery = null!;

    [Params(100, 1000, 10000, 100000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(logger: NullEcsLogger.Instance, healthMonitor: NullEcsHealthMonitor.Instance);
        _entities = new Entity[EntityCount];
        
        // Create diverse entity population
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new BenchPosition { X = i, Y = i, Z = i });
            
            if (i % 2 == 0) _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
            if (i % 3 == 0) _world.AddComponent(_entities[i], new Health { Value = 100, Max = 100 });
            if (i % 5 == 0) _world.AddComponent(_entities[i], new Damage { Value = 10 });
            if (i % 7 == 0) _world.AddComponent(_entities[i], new Tag());
        }
        
        _simpleQuery = _world.Query().With<BenchPosition>();
        _complexQuery = _world.Query().With<BenchPosition>().With<Velocity>().Without<Tag>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world?.Dispose();
    }

    // ============= Entity Creation Benchmarks =============

    [Benchmark(Description = "Entity Creation")]
    public Entity EntityCreation()
    {
        return _world.CreateEntity();
    }

    [Benchmark(Description = "Entity Creation Batch x100")]
    public void EntityCreationBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            _world.CreateEntity();
        }
    }

    // ============= Component Operations Benchmarks =============

    [Benchmark(Description = "Add Component")]
    public void AddComponent()
    {
        var entity = _entities[EntityCount / 2];
        _world.AddComponent(entity, new Damage { Value = 50 });
        _world.RemoveComponent<Damage>(entity); // Clean up for next iteration
    }

    [Benchmark(Description = "Remove Component")]
    public void RemoveComponent()
    {
        var entity = _entities[0]; // Entity with BenchPosition, maybe others
        if (_world.HasComponent<Velocity>(entity))
        {
            _world.RemoveComponent<Velocity>(entity);
            _world.AddComponent(entity, new Velocity { X = 1, Y = 1, Z = 1 }); // Restore
        }
    }

    [Benchmark(Description = "Get Component")]
    public BenchPosition GetComponent()
    {
        return _world.GetComponent<BenchPosition>(_entities[EntityCount / 2]);
    }

    [Benchmark(Description = "Set Component")]
    public void SetComponent()
    {
        var entity = _entities[EntityCount / 2];
        ref var pos = ref _world.GetComponent<BenchPosition>(entity);
        pos.X = 100; pos.Y = 200; pos.Z = 300;
    }

    [Benchmark(Description = "Has Component Check")]
    public bool HasComponent()
    {
        return _world.HasComponent<Velocity>(_entities[EntityCount / 2]);
    }

    // ============= Archetype Transition Benchmarks =============

    [Benchmark(Description = "Archetype Transition (Add)")]
    public void ArchetypeTransitionAdd()
    {
        var entity = _entities[0];
        if (!_world.HasComponent<Damage>(entity))
        {
            _world.AddComponent(entity, new Damage { Value = 25 });
            _world.RemoveComponent<Damage>(entity);
        }
    }

    [Benchmark(Description = "Archetype Transition (Remove)")]
    public void ArchetypeTransitionRemove()
    {
        var entity = _entities[0];
        if (_world.HasComponent<Health>(entity))
        {
            _world.RemoveComponent<Health>(entity);
            _world.AddComponent(entity, new Health { Value = 100, Max = 100 });
        }
    }

    // ============= Query Benchmarks =============

    [Benchmark(Description = "Simple Query Iteration")]
    public int SimpleQueryIteration()
    {
        int count = 0;
        foreach (var chunk in _simpleQuery.Chunks())
        {
            count += chunk.Count;
        }
        return count;
    }

    [Benchmark(Description = "Complex Query Iteration")]
    public int ComplexQueryIteration()
    {
        int count = 0;
        foreach (var chunk in _complexQuery.Chunks())
        {
            count += chunk.Count;
        }
        return count;
    }

    [Benchmark(Description = "Query with Component Access")]
    public float QueryWithComponentAccess()
    {
        float sum = 0;
        var query = _world.Query().With<BenchPosition>().With<Velocity>();
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<BenchPosition>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += positions[i].X * velocities[i].X;
            }
        }
        
        return sum;
    }

    // ============= System Execution Benchmarks =============

    [Benchmark(Description = "System Execution (Movement)")]
    public void SystemExecutionMovement()
    {
        var system = new BenchmarkMovementSystem();
        system.Execute(_world, 0.016f);
    }

    [Benchmark(Description = "System Execution (Multiple)")]
    public void SystemExecutionMultiple()
    {
        var movement = new BenchmarkMovementSystem();
        var health = new BenchmarkHealthSystem();
        var damage = new BenchmarkDamageSystem();
        
        movement.Execute(_world, 0.016f);
        health.Execute(_world, 0.016f);
        damage.Execute(_world, 0.016f);
    }

    // ============= Memory/Allocation Benchmarks =============

    [Benchmark(Description = "Query Construction")]
    public WorldQuery QueryConstruction()
    {
        return _world.Query().With<BenchPosition>().With<Velocity>().Without<Health>();
    }

    [Benchmark(Description = "Chunk Iteration Overhead")]
    public int ChunkIterationOverhead()
    {
        int chunkCount = 0;
        foreach (var chunk in _simpleQuery.Chunks())
        {
            chunkCount++;
        }
        return chunkCount;
    }

    // ============= Concurrent Operations Benchmarks =============

    [Benchmark(Description = "Parallel Query Processing")]
    public void ParallelQueryProcessing()
    {
        var query = _world.Query().With<BenchPosition>().With<Velocity>();
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<BenchPosition>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                positions[i].X += velocities[i].X * 0.016f;
                positions[i].Y += velocities[i].Y * 0.016f;
                positions[i].Z += velocities[i].Z * 0.016f;
            }
        }
    }

    [Benchmark(Description = "Concurrent Entity Creation")]
    public void ConcurrentEntityCreation()
    {
        Parallel.For(0, 100, _ =>
        {
            _world.CreateEntity();
        });
    }
}

// ============= Comparison Benchmarks =============

/// <summary>
/// Benchmarks comparing Purlieu ECS against reference implementations.
/// </summary>
[Config(typeof(EcsBenchmarkConfig))]
[MemoryDiagnoser]
public class BENCH_Comparison
{
    private World _world = null!;
    private Entity[] _entities = null!;
    
    // Simulated "competing" ECS for comparison
    private NaiveEcs _naiveEcs = null!;
    private OptimizedArrayEcs _arrayEcs = null!;

    [Params(10000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup Purlieu ECS
        _world = new World(logger: NullEcsLogger.Instance, healthMonitor: NullEcsHealthMonitor.Instance);
        _entities = new Entity[EntityCount];
        
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            _world.AddComponent(_entities[i], new BenchPosition { X = i, Y = i, Z = i });
            if (i % 2 == 0) _world.AddComponent(_entities[i], new Velocity { X = 1, Y = 1, Z = 1 });
        }
        
        // Setup comparison implementations
        _naiveEcs = new NaiveEcs(EntityCount);
        _arrayEcs = new OptimizedArrayEcs(EntityCount);
        
        for (int i = 0; i < EntityCount; i++)
        {
            _naiveEcs.CreateEntity(i);
            _arrayEcs.CreateEntity(i);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Purlieu: Query & Update")]
    public void PurlieuQueryUpdate()
    {
        var query = _world.Query().With<BenchPosition>().With<Velocity>();
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<BenchPosition>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                positions[i].X += velocities[i].X * 0.016f;
                positions[i].Y += velocities[i].Y * 0.016f;
                positions[i].Z += velocities[i].Z * 0.016f;
            }
        }
    }

    [Benchmark(Description = "Naive: Dictionary Lookup")]
    public void NaiveDictionaryUpdate()
    {
        _naiveEcs.UpdateBenchPositions(0.016f);
    }

    [Benchmark(Description = "Optimized: Array Iteration")]
    public void OptimizedArrayUpdate()
    {
        _arrayEcs.UpdateBenchPositions(0.016f);
    }
}

// ============= Test Components =============

public struct BenchPosition
{
    public float X, Y, Z;
}

internal struct Velocity
{
    public float X, Y, Z;
    
    public Velocity(float x, float y, float z)
    {
        X = x; Y = y; Z = z;
    }
}

internal struct Health
{
    public int Value;
    public int Max;
}

internal struct Damage
{
    public int Value;
}

internal struct Tag { }

// ============= Test Systems =============

internal class BenchmarkMovementSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        var query = world.Query().With<BenchPosition>().With<Velocity>();
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<BenchPosition>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                positions[i].X += velocities[i].X * deltaTime;
                positions[i].Y += velocities[i].Y * deltaTime;
                positions[i].Z += velocities[i].Z * deltaTime;
            }
        }
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadWrite(
            new[] { typeof(Velocity) },
            new[] { typeof(BenchPosition) }
        );
    }
}

internal class BenchmarkHealthSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        var query = world.Query().With<Health>();
        
        foreach (var chunk in query.Chunks())
        {
            var healths = chunk.GetSpan<Health>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                if (healths[i].Value < healths[i].Max)
                {
                    healths[i].Value = Math.Min(healths[i].Max, healths[i].Value + 1);
                }
            }
        }
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.WriteOnly(typeof(Health));
    }
}

internal class BenchmarkDamageSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        var query = world.Query().With<Health>().With<Damage>();
        
        foreach (var chunk in query.Chunks())
        {
            var healths = chunk.GetSpan<Health>();
            var damages = chunk.GetSpan<Damage>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                healths[i].Value = Math.Max(0, healths[i].Value - damages[i].Value);
            }
        }
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadWrite(
            new[] { typeof(Damage) },
            new[] { typeof(Health) }
        );
    }
}

// ============= Comparison Implementations =============

/// <summary>
/// Naive ECS implementation using dictionaries.
/// </summary>
internal class NaiveEcs
{
    private readonly Dictionary<int, BenchPosition> _positions = new();
    private readonly Dictionary<int, Velocity> _velocities = new();
    private readonly List<int> _entities = new();

    public NaiveEcs(int capacity)
    {
        // Pre-size for fairness
        _positions = new Dictionary<int, BenchPosition>(capacity);
        _velocities = new Dictionary<int, Velocity>(capacity);
        _entities = new List<int>(capacity);
    }

    public void CreateEntity(int id)
    {
        _entities.Add(id);
        _positions[id] = new BenchPosition { X = id, Y = id, Z = id };
        if (id % 2 == 0)
            _velocities[id] = new Velocity { X = 1, Y = 1, Z = 1 };
    }

    public void UpdateBenchPositions(float deltaTime)
    {
        foreach (var id in _entities)
        {
            if (_velocities.TryGetValue(id, out var velocity) && _positions.TryGetValue(id, out var position))
            {
                position.X += velocity.X * deltaTime;
                position.Y += velocity.Y * deltaTime;
                position.Z += velocity.Z * deltaTime;
                _positions[id] = position;
            }
        }
    }
}

/// <summary>
/// Optimized array-based ECS implementation.
/// </summary>
internal class OptimizedArrayEcs
{
    private readonly BenchPosition[] _positions;
    private readonly Velocity[] _velocities;
    private readonly bool[] _hasVelocity;
    private readonly int _capacity;

    public OptimizedArrayEcs(int capacity)
    {
        _capacity = capacity;
        _positions = new BenchPosition[capacity];
        _velocities = new Velocity[capacity];
        _hasVelocity = new bool[capacity];
    }

    public void CreateEntity(int id)
    {
        _positions[id] = new BenchPosition { X = id, Y = id, Z = id };
        if (id % 2 == 0)
        {
            _velocities[id] = new Velocity { X = 1, Y = 1, Z = 1 };
            _hasVelocity[id] = true;
        }
    }

    public void UpdateBenchPositions(float deltaTime)
    {
        for (int i = 0; i < _capacity; i++)
        {
            if (_hasVelocity[i])
            {
                _positions[i].X += _velocities[i].X * deltaTime;
                _positions[i].Y += _velocities[i].Y * deltaTime;
                _positions[i].Z += _velocities[i].Z * deltaTime;
            }
        }
    }
}

// ============= Benchmark Configuration =============

public class EcsBenchmarkConfig : ManualConfig
{
    public EcsBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithInvocationCount(1)
            .WithId("ECS"));
            
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.P95);
        AddColumn(RankColumn.Arabic);
        
        AddDiagnoser(MemoryDiagnoser.Default);
        
        WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));
    }
}