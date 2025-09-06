using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class BENCH_ArchetypeTransitions
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

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _entities = new Entity[EntityCount];
        
        // Create base entities
        for (int i = 0; i < EntityCount; i++)
        {
            _entities[i] = _world.CreateEntity();
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Reset entities to empty archetype for each iteration
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
    public void SingleComponentAddition()
    {
        // Add single component to all entities
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
    }

    [Benchmark]
    public void TwoComponentAddition()
    {
        // Add two components sequentially
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
    }

    [Benchmark]
    public void MultipleComponentAddition()
    {
        // Add multiple components to create complex archetypes
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
        }
    }

    [Benchmark]
    public void ComponentAddRemovePattern()
    {
        // Add component then remove it (common pattern)
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
        
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.RemoveComponent<TestComponentA>(_entities[i]);
        }
    }

    [Benchmark]
    public void ArchetypeFluctuations()
    {
        // Simulate entities moving between different archetypes
        for (int i = 0; i < _entities.Length; i++)
        {
            var entity = _entities[i];
            
            // Start with A
            _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            
            // Add B (A + B archetype)
            _world.AddComponent(entity, new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            
            // Remove A (B only archetype)
            _world.RemoveComponent<TestComponentA>(entity);
            
            // Add C (B + C archetype)
            _world.AddComponent(entity, new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
        }
    }

    [Benchmark]
    public void BatchedArchetypeTransitions()
    {
        // Batch entities into different archetypes
        int batchSize = EntityCount / 4;
        
        // Batch 1: A only
        for (int i = 0; i < batchSize; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
        }
        
        // Batch 2: A + B
        for (int i = batchSize; i < batchSize * 2; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
        }
        
        // Batch 3: A + C
        for (int i = batchSize * 2; i < batchSize * 3; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
        }
        
        // Batch 4: A + B + C
        for (int i = batchSize * 3; i < EntityCount; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
        }
    }

    [Benchmark]
    public void ArchetypeSignatureComparison()
    {
        // Test the cost of archetype signature operations during transitions
        var signatures = new List<ArchetypeSignature>();
        
        for (int i = 0; i < 100; i++)
        {
            var sig = new ArchetypeSignature();
            if (i % 2 == 0) sig = sig.Add<TestComponentA>();
            if (i % 3 == 0) sig = sig.Add<TestComponentB>();
            if (i % 5 == 0) sig = sig.Add<TestComponentC>();
            if (i % 7 == 0) sig = sig.Add<TestComponentD>();
            
            signatures.Add(sig);
        }
        
        // Compare signatures
        int matches = 0;
        for (int i = 0; i < signatures.Count; i++)
        {
            for (int j = i + 1; j < signatures.Count; j++)
            {
                if (signatures[i].Equals(signatures[j]))
                    matches++;
            }
        }
    }

    [Benchmark]
    public void ComponentRemovalPattern()
    {
        // Setup entities with all components first
        for (int i = 0; i < _entities.Length; i++)
        {
            var entity = _entities[i];
            _world.AddComponent(entity, new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            _world.AddComponent(entity, new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            _world.AddComponent(entity, new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
        }
        
        // Remove components in different patterns
        for (int i = 0; i < _entities.Length; i++)
        {
            switch (i % 3)
            {
                case 0:
                    _world.RemoveComponent<TestComponentA>(_entities[i]);
                    break;
                case 1:
                    _world.RemoveComponent<TestComponentB>(_entities[i]);
                    break;
                case 2:
                    _world.RemoveComponent<TestComponentC>(_entities[i]);
                    break;
            }
        }
    }

    [Benchmark]
    public void HasComponentChecks()
    {
        // Setup entities with various components
        for (int i = 0; i < _entities.Length; i++)
        {
            if (i % 2 == 0) _world.AddComponent(_entities[i], new TestComponentA { X = i, Y = i * 2, Z = i * 3, Value = i * 0.1f });
            if (i % 3 == 0) _world.AddComponent(_entities[i], new TestComponentB { X = i * 0.5f, Y = i * 0.7f, Timestamp = i });
            if (i % 5 == 0) _world.AddComponent(_entities[i], new TestComponentC { IsActive = true, Flags = (byte)(i % 256), Priority = (short)(i % 1000) });
        }
        
        // Check component existence
        int hasA = 0, hasB = 0, hasC = 0;
        for (int i = 0; i < _entities.Length; i++)
        {
            if (_world.HasComponent<TestComponentA>(_entities[i])) hasA++;
            if (_world.HasComponent<TestComponentB>(_entities[i])) hasB++;
            if (_world.HasComponent<TestComponentC>(_entities[i])) hasC++;
        }
    }
}