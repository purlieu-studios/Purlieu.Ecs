using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class BENCH_QueryCaching
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

    [Params(1000, 10_000)]
    public int EntityCount { get; set; }

    [Params(5, 20)]
    public int ArchetypeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
        _world.RegisterComponent<UniqueComponent>(); // Register benchmark-specific component
        _entities = new Entity[EntityCount];
        
        // Create entities with various component combinations to generate many archetypes
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
            
            // Distribute entities across archetypes
            var archetypeIndex = i % ArchetypeCount;
            
            // Base components for all entities
            _world.AddComponent(_entities[i], new Position(i, i * 2, i * 3));
            
            // Create varied archetype combinations
            switch (archetypeIndex % 10)
            {
                case 0:
                    // Position only
                    break;
                case 1:
                    // Position + Velocity
                    _world.AddComponent(_entities[i], new Velocity(1, 1, 1));
                    break;
                case 2:
                    // Position + Velocity + MoveIntent
                    _world.AddComponent(_entities[i], new Velocity(1, 1, 1));
                    _world.AddComponent(_entities[i], new MoveIntent(2, 2, 2));
                    break;
                case 3:
                    // Position + MoveIntent
                    _world.AddComponent(_entities[i], new MoveIntent(3, 3, 3));
                    break;
                case 4:
                    // Position + Stunned
                    _world.AddComponent(_entities[i], new Stunned());
                    break;
                case 5:
                    // Position + Velocity + Stunned
                    _world.AddComponent(_entities[i], new Velocity(1, 1, 1));
                    _world.AddComponent(_entities[i], new Stunned());
                    break;
                case 6:
                    // Position + TestComponentA
                    _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i, Z = i, Value = i });
                    break;
                case 7:
                    // Position + TestComponentA + TestComponentB
                    _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i, Z = i, Value = i });
                    _world.AddComponent(_entities[i], new TestComponentB { X = i, Y = i, Timestamp = i });
                    break;
                case 8:
                    // Position + TestComponentB + TestComponentC
                    _world.AddComponent(_entities[i], new TestComponentB { X = i, Y = i, Timestamp = i });
                    _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)i, Priority = (short)i });
                    break;
                case 9:
                    // All components
                    _world.AddComponent(_entities[i], new Velocity(1, 1, 1));
                    _world.AddComponent(_entities[i], new MoveIntent(2, 2, 2));
                    _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i, Z = i, Value = i });
                    _world.AddComponent(_entities[i], new TestComponentB { X = i, Y = i, Timestamp = i });
                    break;
            }
        }
        
        // Print cache statistics after setup
        var stats = _world.GetQueryCacheStatistics();
        Console.WriteLine($"Setup complete - Cache: {stats}");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        var finalStats = _world.GetQueryCacheStatistics();
        Console.WriteLine($"Final cache statistics: {finalStats}");
        
        _world = null!;
        _entities = null!;
    }

    [Benchmark(Baseline = true)]
    public long ColdQuery_FirstTime()
    {
        // Reset cache to ensure cold start
        _world.ResetQueryCacheStatistics();
        
        var query = _world.Query().With<Position>().With<Velocity>();
        long sum = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += (long)(positions[i].X + positions[i].Y + velocities[i].X + velocities[i].Y);
            }
        }
        
        return sum;
    }

    [Benchmark]
    public long WarmQuery_Repeated()
    {
        // This should benefit from caching - same query signature repeated
        var query = _world.Query().With<Position>().With<Velocity>();
        long sum = 0;
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                sum += (long)(positions[i].X + positions[i].Y + velocities[i].X + velocities[i].Y);
            }
        }
        
        return sum;
    }

    [Benchmark]
    public long MultipleRepeatedQueries()
    {
        // Test multiple different queries that should all benefit from caching
        long sum = 0;
        
        // Query 1: Position + Velocity (should be cached)
        var query1 = _world.Query().With<Position>().With<Velocity>();
        foreach (var chunk in query1.Chunks())
        {
            sum += chunk.Count;
        }
        
        // Query 2: Position + MoveIntent (should be cached)
        var query2 = _world.Query().With<Position>().With<MoveIntent>();
        foreach (var chunk in query2.Chunks())
        {
            sum += chunk.Count;
        }
        
        // Query 3: Position without Stunned (should be cached)
        var query3 = _world.Query().With<Position>().Without<Stunned>();
        foreach (var chunk in query3.Chunks())
        {
            sum += chunk.Count;
        }
        
        // Query 4: Complex query (should be cached)
        var query4 = _world.Query().With<Position>().With<TestComponentA>().Without<Velocity>();
        foreach (var chunk in query4.Chunks())
        {
            sum += chunk.Count;
        }
        
        return sum;
    }

    [Benchmark]
    public int CacheHitRateTest()
    {
        // Reset statistics
        _world.ResetQueryCacheStatistics();
        
        // Run the same query multiple times to build up cache hits
        var query = _world.Query().With<Position>().With<TestComponentA>();
        int totalCount = 0;
        
        // Execute query 10 times - first should be cache miss, rest should be hits
        for (int run = 0; run < 10; run++)
        {
            totalCount += query.Count();
        }
        
        return totalCount;
    }

    [Benchmark]
    public long VariedQueryPatterns()
    {
        // Test queries with different patterns to exercise cache effectively
        long sum = 0;
        
        // Pattern A: Simple queries (high cache hit potential)
        for (int i = 0; i < 5; i++)
        {
            sum += _world.Query().With<Position>().Count();
            sum += _world.Query().With<Position>().With<Velocity>().Count();
            sum += _world.Query().With<TestComponentA>().Count();
        }
        
        // Pattern B: Complex queries (medium cache hit potential)
        for (int i = 0; i < 3; i++)
        {
            sum += _world.Query().With<Position>().With<Velocity>().Without<Stunned>().Count();
            sum += _world.Query().With<Position>().With<TestComponentA>().With<TestComponentB>().Count();
        }
        
        // Pattern C: Unique queries (cache miss, but builds cache for next benchmark run)
        sum += _world.Query().With<Position>().With<MoveIntent>().Without<TestComponentB>().Count();
        sum += _world.Query().With<TestComponentA>().Without<Velocity>().Without<MoveIntent>().Count();
        
        return sum;
    }

    [Benchmark]
    public long CacheInvalidationTest()
    {
        // This test measures performance when cache gets invalidated
        long sum = 0;
        
        // Build up cache with some queries
        sum += _world.Query().With<Position>().Count();
        sum += _world.Query().With<Position>().With<Velocity>().Count();
        
        // Force cache invalidation by creating new archetype
        var tempEntity = _world.CreateEntity();
        _world.AddComponent(tempEntity, new Position(1, 2, 3));
        _world.AddComponent(tempEntity, new UniqueComponent { Value = 999 });
        
        // These queries will have to rebuild cache
        sum += _world.Query().With<Position>().Count();
        sum += _world.Query().With<Position>().With<Velocity>().Count();
        
        // Clean up temp entity
        _world.DestroyEntity(tempEntity);
        
        return sum;
    }

    [IterationCleanup]
    public void PrintCacheStats()
    {
        var stats = _world.GetQueryCacheStatistics();
        if (stats.TotalQueries > 0)
        {
            Console.WriteLine($"Cache stats: {stats}");
        }
    }
    
    // Helper component for cache invalidation test
    private struct UniqueComponent
    {
        public int Value;
    }
}