using NUnit.Framework;
using PurlieuEcs.Core;
using System;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class ALLOC_SimpleAllocationTests
{
    private World _world = null!;
    private Entity[] _entities = null!;

    [SetUp]
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
        }
    }

    [TearDown]
    public void Cleanup()
    {
        _world = null!;
        _entities = null!;
    }

    private void AssertZeroAllocations(Action action)
    {
        // Force garbage collection to clean slate
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeBytes = GC.GetTotalMemory(false);
        long beforeGen0 = GC.CollectionCount(0);

        action();

        long afterBytes = GC.GetTotalMemory(false);
        long afterGen0 = GC.CollectionCount(0);

        long allocatedBytes = afterBytes - beforeBytes;
        long gen0Collections = afterGen0 - beforeGen0;

        Assert.That(gen0Collections, Is.EqualTo(0), 
            $"Gen 0 collections occurred: {gen0Collections}");
        
        // Allow small memory fluctuations but flag significant allocations
        Assert.That(Math.Abs(allocatedBytes), Is.LessThan(1024), 
            $"Allocated bytes: {allocatedBytes}. Expected near-zero heap allocations.");
    }

    [Test]
    public void ALLOC_QueryIteration_SingleComponent()
    {
        // Setup: Add components to entities
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { Value = i });
        }

        // Test: Query iteration should allocate zero bytes
        AssertZeroAllocations(() =>
        {
            var query = _world.Query().With<TestComponentA>();
            long sum = 0;
            
            foreach (var chunk in query.Chunks())
            {
                var components = chunk.GetSpan<TestComponentA>();
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    sum += components[i].Value;
                }
            }
        });
    }

    [Test]
    public void ALLOC_QueryIteration_TwoComponents()
    {
        // Setup: Add components to entities
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { Value = i });
            _world.AddComponent(_entities[i], new TestComponentB { Data = i * 0.5f });
        }

        // Test: Query iteration should allocate zero bytes
        AssertZeroAllocations(() =>
        {
            var query = _world.Query().With<TestComponentA>().With<TestComponentB>();
            long sum = 0;
            
            foreach (var chunk in query.Chunks())
            {
                var componentA = chunk.GetSpan<TestComponentA>();
                var componentB = chunk.GetSpan<TestComponentB>();
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    sum += componentA[i].Value + (long)componentB[i].Data;
                }
            }
        });
    }

    [Test]
    public void ALLOC_AddRemoveComponents()
    {
        // Test: Add/Remove operations should have minimal allocations
        AssertZeroAllocations(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                _world.AddComponent(_entities[i], new TestComponentA { Value = i });
                _world.AddComponent(_entities[i], new TestComponentB { Data = i * 0.5f });
            }
            
            for (int i = 0; i < 100; i++)
            {
                _world.RemoveComponent<TestComponentA>(_entities[i]);
                _world.RemoveComponent<TestComponentB>(_entities[i]);
            }
        });
    }

    [Test]
    public void ALLOC_GetSpanOperations()
    {
        // Setup: Add components
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.AddComponent(_entities[i], new TestComponentA { Value = i });
        }

        // Test: GetSpan operations should allocate zero bytes
        AssertZeroAllocations(() =>
        {
            var query = _world.Query().With<TestComponentA>();
            
            foreach (var chunk in query.Chunks())
            {
                var span = chunk.GetSpan<TestComponentA>();
                // Access elements to ensure span is actually used
                for (int i = 0; i < chunk.Count; i++)
                {
                    var component = span[i];
                    var sum = component.Value;
                }
            }
        });
    }
}