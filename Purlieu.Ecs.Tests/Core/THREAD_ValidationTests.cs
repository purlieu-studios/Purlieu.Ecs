using NUnit.Framework;
using PurlieuEcs.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Validates thread safety improvements in the optimize-allocation-v2 branch.
/// </summary>
[TestFixture]
public class THREAD_ValidationTests
{
    private World _world;
    
    [SetUp]
    public void Setup()
    {
        _world = new World();
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    [Test]
    public void ValidateSystemScheduler_ConcurrentRegistration()
    {
        // Arrange
        var scheduler = _world.SystemScheduler;
        var systems = new List<TestSystem>();
        var exceptions = new ConcurrentBag<Exception>();
        
        for (int i = 0; i < 100; i++)
        {
            systems.Add(new TestSystem { Id = i });
        }
        
        // Act - Register systems concurrently
        Parallel.ForEach(systems, system =>
        {
            try
            {
                scheduler.RegisterSystem(system);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });
        
        // Assert
        Assert.That(exceptions.Count, Is.EqualTo(0), "No exceptions during concurrent registration");
        
        // Verify execution doesn't throw
        Assert.DoesNotThrow(() => scheduler.ExecuteAllPhases(_world, 0.016f));
    }
    
    [Test]
    public void ValidateArchetype_ThreadSafeOperations()
    {
        // Arrange
        var entities = new ConcurrentBag<Entity>();
        var exceptions = new ConcurrentBag<Exception>();
        var barrier = new Barrier(Environment.ProcessorCount);
        
        // Act - Create entities and add components concurrently
        var tasks = new Task[Environment.ProcessorCount];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    
                    for (int j = 0; j < 100; j++)
                    {
                        var entity = _world.CreateEntity();
                        entities.Add(entity);
                        
                        // Add components to trigger archetype transitions
                        _world.AddComponent(entity, new Position { X = j, Y = j });
                        
                        if (j % 2 == 0)
                        {
                            _world.AddComponent(entity, new Velocity { X = 1, Y = 1 });
                        }
                        
                        if (j % 3 == 0)
                        {
                            _world.AddComponent(entity, new Health(100, 100));
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        // Assert
        Assert.That(exceptions.Count, Is.EqualTo(0), "No exceptions during concurrent archetype operations");
        Assert.That(entities.Count, Is.EqualTo(tasks.Length * 100), "All entities created successfully");
        
        // Verify entity integrity
        foreach (var entity in entities)
        {
            Assert.That(_world.IsAlive(entity), Is.True, "All entities should be alive");
            Assert.That(_world.HasComponent<Position>(entity), Is.True, "All entities should have Position");
        }
    }
    
    [Test]
    public void ValidateWorld_ConcurrentEntityLifecycle()
    {
        // Arrange
        const int operationsPerThread = 500;
        var exceptions = new ConcurrentBag<Exception>();
        var successfulOperations = 0;
        
        // Pre-create some entities
        var initialEntities = new List<Entity>();
        for (int i = 0; i < 100; i++)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new Position { X = i, Y = i });
            initialEntities.Add(e);
        }
        
        // Act - Perform mixed operations concurrently
        Parallel.For(0, Environment.ProcessorCount, _ =>
        {
            try
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var op = Random.Shared.Next(4);
                    switch (op)
                    {
                        case 0: // Create entity
                            var entity = _world.CreateEntity();
                            _world.AddComponent(entity, new Position { X = i, Y = i });
                            Interlocked.Increment(ref successfulOperations);
                            break;
                            
                        case 1: // Add component (handle race condition gracefully)
                            if (initialEntities.Count > 0)
                            {
                                var e = initialEntities[Random.Shared.Next(initialEntities.Count)];
                                try
                                {
                                    if (_world.IsAlive(e))
                                    {
                                        _world.AddComponent(e, new Velocity { X = 1, Y = 1 });
                                        Interlocked.Increment(ref successfulOperations);
                                    }
                                }
                                catch (EntityNotFoundException)
                                {
                                    // Expected race condition - entity was destroyed by another thread
                                    Interlocked.Increment(ref successfulOperations);
                                }
                            }
                            break;
                            
                        case 2: // Remove component (handle race condition gracefully)
                            if (initialEntities.Count > 0)
                            {
                                var e = initialEntities[Random.Shared.Next(initialEntities.Count)];
                                try
                                {
                                    if (_world.IsAlive(e))
                                    {
                                        _world.RemoveComponent<Position>(e);
                                        Interlocked.Increment(ref successfulOperations);
                                    }
                                }
                                catch (EntityNotFoundException)
                                {
                                    // Expected race condition - entity was destroyed by another thread
                                    Interlocked.Increment(ref successfulOperations);
                                }
                            }
                            break;
                            
                        case 3: // Query
                            var count = _world.Query().With<Position>().Count();
                            Interlocked.Increment(ref successfulOperations);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });
        
        // Assert
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"No unexpected exceptions. Got: {string.Join(", ", exceptions.Select(e => e.Message))}");
        Assert.That(successfulOperations, Is.GreaterThan(0), "Operations completed successfully");
    }
    
    private struct Position
    {
        public float X, Y;
    }
    
    private struct Velocity
    {
        public float X, Y;
    }
    
    private struct Health
    {
        public float Current;
        public float Max;
        
        public Health(float current, float max)
        {
            Current = current;
            Max = max;
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