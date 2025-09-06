using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class BENCH_AddRemove_Migrate_2Comp
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

    [Params(1000, 10_000, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _entities = new Entity[EntityCount];
        
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Reset world for each iteration to ensure clean state
        _world = new World();
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world = null!;
        _entities = null!;
    }

    [Benchmark(Baseline = true)]
    public void AddTwoComponents()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
    }

    [Benchmark]
    public void RemoveTwoComponents()
    {
        // Setup: Add components first
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
        
        // Benchmark: Remove components
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.RemoveComponent<TestComponentA>(_entities[i]);
            _world.RemoveComponent<TestComponentB>(_entities[i]);
        }
    }

    [Benchmark]
    public void MigrateBetweenArchetypes()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            var entity = _entities[i];
            
            // Empty -> A
            _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            
            // A -> A+B
            _world.AddComponent(entity, new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            
            // A+B -> B (remove A)
            _world.RemoveComponent<TestComponentA>(entity);
            
            // B -> B+A (add A back)
            _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
    }

    [Benchmark]
    public void BatchedAddTwoComponents()
    {
        // Add first component to all entities
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
        
        // Add second component to all entities
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
    }

    [Benchmark]
    public void InterleavedAddRemove()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            var entity = _entities[i];
            
            // Add A
            _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            
            // Add B
            _world.AddComponent(entity, new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            
            // Remove A (immediately after adding both)
            _world.RemoveComponent<TestComponentA>(entity);
            
            // Add A back
            _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
    }

    [Benchmark]
    public void ComponentReplacement()
    {
        // Setup: Add initial components
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
        
        // Replace A with modified values
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.RemoveComponent<TestComponentA>(_entities[i]);
            _world.AddComponent(_entities[i], new TestComponentA { X = i * 2, Y = i * 4, Z = i * 6, Value = i * 0.2f });
        }
    }

    [Benchmark]
    public void AlternatingMigration()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            var entity = _entities[i];
            
            if (i % 2 == 0)
            {
                // Even entities: Empty -> A -> A+B
                _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
                _world.AddComponent(entity, new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            }
            else
            {
                // Odd entities: Empty -> B -> A+B
                _world.AddComponent(entity, new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
                _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            }
        }
    }

    [Benchmark]
    public void HighFrequencyMigration()
    {
        // Simulate high-frequency component changes (like buffs/debuffs)
        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Add components
            for (int i = 0; i < _entities.Length; i++)
            {
                if (!_world.HasComponent<TestComponentA>(_entities[i]))
                    _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            }
            
            // Remove components
            for (int i = 0; i < _entities.Length; i++)
            {
                if (_world.HasComponent<TestComponentA>(_entities[i]))
                    _world.RemoveComponent<TestComponentA>(_entities[i]);
            }
        }
    }

    [Benchmark]
    public void MassArchetypeConstruction()
    {
        // Create different archetypes by adding components in different orders
        int entitiesPerArchetype = EntityCount / 4;
        
        // Archetype 1: A then B
        for (int i = 0; i < entitiesPerArchetype; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
        
        // Archetype 2: B then A (should result in same archetype as above)
        for (int i = entitiesPerArchetype; i < entitiesPerArchetype * 2; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
        
        // Archetype 3: A only
        for (int i = entitiesPerArchetype * 2; i < entitiesPerArchetype * 3; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
        
        // Archetype 4: B only
        for (int i = entitiesPerArchetype * 3; i < EntityCount; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
    }
}