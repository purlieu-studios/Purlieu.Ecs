using NUnit.Framework;
using PurlieuEcs.Core;
using System.Collections.Concurrent;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Fuzzing and robustness tests using random operation sequences.
/// Tests system stability under unpredictable usage patterns.
/// </summary>
[TestFixture]
[Category("Fuzzing")]
[Category("LongRunning")]
public class FUZZ_RobustnessTests
{
    private World _world = null!;

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
    [Description("Fuzz: Random entity lifecycle operations")]
    public void RandomEntityLifecycle_Stability()
    {
        var random = new Random(12345); // Fixed seed for reproducibility
        var entities = new List<Entity>();
        const int operationCount = 10000;
        
        for (int op = 0; op < operationCount; op++)
        {
            var operation = random.Next(4);
            
            switch (operation)
            {
                case 0: // Create entity
                    var entity = _world.CreateEntity();
                    entities.Add(entity);
                    break;
                    
                case 1: // Destroy entity
                    if (entities.Count > 0)
                    {
                        var index = random.Next(entities.Count);
                        var entityToDestroy = entities[index];
                        if (_world.IsAlive(entityToDestroy))
                        {
                            _world.DestroyEntity(entityToDestroy);
                        }
                        entities.RemoveAt(index);
                    }
                    break;
                    
                case 2: // Add component
                    if (entities.Count > 0)
                    {
                        var index = random.Next(entities.Count);
                        var targetEntity = entities[index];
                        if (_world.IsAlive(targetEntity))
                        {
                            var componentType = random.Next(4);
                            switch (componentType)
                            {
                                case 0:
                                    if (!_world.HasComponent<FuzzComponent1>(targetEntity))
                                        _world.AddComponent(targetEntity, new FuzzComponent1 { Value = random.Next() });
                                    break;
                                case 1:
                                    if (!_world.HasComponent<FuzzComponent2>(targetEntity))
                                        _world.AddComponent(targetEntity, new FuzzComponent2 { Value = random.NextSingle() });
                                    break;
                                case 2:
                                    if (!_world.HasComponent<FuzzComponent3>(targetEntity))
                                        _world.AddComponent(targetEntity, new FuzzComponent3 { Flag = random.Next(2) == 1 });
                                    break;
                                case 3:
                                    if (!_world.HasComponent<FuzzComponent4>(targetEntity))
                                        _world.AddComponent(targetEntity, new FuzzComponent4 { Data = (byte)random.Next(256) });
                                    break;
                            }
                        }
                    }
                    break;
                    
                case 3: // Remove component
                    if (entities.Count > 0)
                    {
                        var index = random.Next(entities.Count);
                        var targetEntity = entities[index];
                        if (_world.IsAlive(targetEntity))
                        {
                            var componentType = random.Next(4);
                            switch (componentType)
                            {
                                case 0:
                                    if (_world.HasComponent<FuzzComponent1>(targetEntity))
                                        _world.RemoveComponent<FuzzComponent1>(targetEntity);
                                    break;
                                case 1:
                                    if (_world.HasComponent<FuzzComponent2>(targetEntity))
                                        _world.RemoveComponent<FuzzComponent2>(targetEntity);
                                    break;
                                case 2:
                                    if (_world.HasComponent<FuzzComponent3>(targetEntity))
                                        _world.RemoveComponent<FuzzComponent3>(targetEntity);
                                    break;
                                case 3:
                                    if (_world.HasComponent<FuzzComponent4>(targetEntity))
                                        _world.RemoveComponent<FuzzComponent4>(targetEntity);
                                    break;
                            }
                        }
                    }
                    break;
            }
        }
        
        // Verify world is still functional
        var testEntity = _world.CreateEntity();
        _world.AddComponent(testEntity, new FuzzComponent1 { Value = 42 });
        
        Assert.That(_world.IsAlive(testEntity), Is.True, "World should remain functional after fuzzing");
        Assert.That(_world.GetComponent<FuzzComponent1>(testEntity).Value, Is.EqualTo(42), 
                   "Component operations should work normally after fuzzing");
    }

    [Test]
    [Description("Fuzz: Random query patterns")]
    public void RandomQueryPatterns_Stability()
    {
        var random = new Random(54321);
        
        // Create diverse entity population
        for (int i = 0; i < 1000; i++)
        {
            var entity = _world.CreateEntity();
            
            if (random.Next(2) == 1) _world.AddComponent(entity, new FuzzComponent1 { Value = i });
            if (random.Next(2) == 1) _world.AddComponent(entity, new FuzzComponent2 { Value = i * 0.5f });
            if (random.Next(2) == 1) _world.AddComponent(entity, new FuzzComponent3 { Flag = i % 2 == 0 });
            if (random.Next(2) == 1) _world.AddComponent(entity, new FuzzComponent4 { Data = (byte)(i % 256) });
        }
        
        // Generate random queries
        for (int queryTest = 0; queryTest < 100; queryTest++)
        {
            var queryBuilder = _world.Query();
            var componentCount = random.Next(1, 5); // 1-4 components
            
            for (int comp = 0; comp < componentCount; comp++)
            {
                var componentType = random.Next(4);
                var isWithout = random.Next(3) == 0; // 1/3 chance of Without
                
                switch (componentType)
                {
                    case 0:
                        if (isWithout) queryBuilder = queryBuilder.Without<FuzzComponent1>();
                        else queryBuilder = queryBuilder.With<FuzzComponent1>();
                        break;
                    case 1:
                        if (isWithout) queryBuilder = queryBuilder.Without<FuzzComponent2>();
                        else queryBuilder = queryBuilder.With<FuzzComponent2>();
                        break;
                    case 2:
                        if (isWithout) queryBuilder = queryBuilder.Without<FuzzComponent3>();
                        else queryBuilder = queryBuilder.With<FuzzComponent3>();
                        break;
                    case 3:
                        if (isWithout) queryBuilder = queryBuilder.Without<FuzzComponent4>();
                        else queryBuilder = queryBuilder.With<FuzzComponent4>();
                        break;
                }
            }
            
            // Execute query and verify no crashes
            Assert.DoesNotThrow(() =>
            {
                var query = queryBuilder;
                var count = 0;
                foreach (var chunk in query.Chunks())
                {
                    count += chunk.Count;
                }
                // Count should be non-negative
                Assert.That(count, Is.GreaterThanOrEqualTo(0), "Query count should be non-negative");
            }, $"Random query pattern {queryTest} should not crash");
        }
    }

    [Test]
    [Description("Fuzz: Concurrent random operations")]
    public void ConcurrentRandomOperations_ThreadSafety()
    {
        const int threadCount = 8;
        const int operationsPerThread = 1000;
        var exceptions = new ConcurrentBag<Exception>();
        var random = new Random(98765);
        
        // Pre-populate with some entities
        var sharedEntities = new ConcurrentBag<Entity>();
        for (int i = 0; i < 100; i++)
        {
            sharedEntities.Add(_world.CreateEntity());
        }
        
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            tasks[t] = Task.Run(() =>
            {
                var threadRandom = new Random(random.Next() + threadId);
                
                try
                {
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        var operation = threadRandom.Next(5);
                        
                        switch (operation)
                        {
                            case 0: // Create entity
                                var entity = _world.CreateEntity();
                                sharedEntities.Add(entity);
                                break;
                                
                            case 1: // Add component to random entity
                                var entities = sharedEntities.ToArray();
                                if (entities.Length > 0)
                                {
                                    var randomEntity = entities[threadRandom.Next(entities.Length)];
                                    if (_world.IsAlive(randomEntity))
                                    {
                                        try
                                        {
                                            _world.AddComponent(randomEntity, new FuzzComponent1 { Value = threadRandom.Next() });
                                        }
                                        catch (InvalidOperationException)
                                        {
                                            // Component might already exist - that's ok
                                        }
                                    }
                                }
                                break;
                                
                            case 2: // Query entities
                                var query = _world.Query().With<FuzzComponent1>();
                                var count = 0;
                                foreach (var chunk in query.Chunks())
                                {
                                    count += chunk.Count;
                                }
                                break;
                                
                            case 3: // Check entity state
                                var entitiesArray = sharedEntities.ToArray();
                                if (entitiesArray.Length > 0)
                                {
                                    var randomEntity = entitiesArray[threadRandom.Next(entitiesArray.Length)];
                                    _world.IsAlive(randomEntity);
                                }
                                break;
                                
                            case 4: // Remove component
                                var allEntities = sharedEntities.ToArray();
                                if (allEntities.Length > 0)
                                {
                                    var randomEntity = allEntities[threadRandom.Next(allEntities.Length)];
                                    if (_world.IsAlive(randomEntity) && _world.HasComponent<FuzzComponent1>(randomEntity))
                                    {
                                        _world.RemoveComponent<FuzzComponent1>(randomEntity);
                                    }
                                }
                                break;
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
        
        Assert.That(exceptions, Is.Empty, 
                   $"No exceptions should occur during concurrent fuzzing: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        // Verify world is still functional
        var testEntity = _world.CreateEntity();
        _world.AddComponent(testEntity, new FuzzComponent1 { Value = 999 });
        Assert.That(_world.GetComponent<FuzzComponent1>(testEntity).Value, Is.EqualTo(999), 
                   "World should remain functional after concurrent fuzzing");
    }

    [Test]
    [Description("Fuzz: Random archetype transitions")]
    public void RandomArchetypeTransitions_DataIntegrity()
    {
        var random = new Random(11111);
        var entities = new List<Entity>();
        
        // Create entities with known data
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new FuzzComponent1 { Value = i });
            entities.Add(entity);
        }
        
        // Perform random archetype transitions
        for (int transition = 0; transition < 1000; transition++)
        {
            var entityIndex = random.Next(entities.Count);
            var entity = entities[entityIndex];
            
            if (!_world.IsAlive(entity)) continue;
            
            var operation = random.Next(8);
            
            switch (operation)
            {
                case 0: // Add Component2
                    if (!_world.HasComponent<FuzzComponent2>(entity))
                        _world.AddComponent(entity, new FuzzComponent2 { Value = entityIndex * 2.5f });
                    break;
                case 1: // Remove Component2
                    if (_world.HasComponent<FuzzComponent2>(entity))
                        _world.RemoveComponent<FuzzComponent2>(entity);
                    break;
                case 2: // Add Component3
                    if (!_world.HasComponent<FuzzComponent3>(entity))
                        _world.AddComponent(entity, new FuzzComponent3 { Flag = entityIndex % 2 == 0 });
                    break;
                case 3: // Remove Component3
                    if (_world.HasComponent<FuzzComponent3>(entity))
                        _world.RemoveComponent<FuzzComponent3>(entity);
                    break;
                case 4: // Add Component4
                    if (!_world.HasComponent<FuzzComponent4>(entity))
                        _world.AddComponent(entity, new FuzzComponent4 { Data = (byte)(entityIndex % 256) });
                    break;
                case 5: // Remove Component4
                    if (_world.HasComponent<FuzzComponent4>(entity))
                        _world.RemoveComponent<FuzzComponent4>(entity);
                    break;
                case 6: // Modify Component1
                    if (_world.HasComponent<FuzzComponent1>(entity))
                    {
                        var comp = _world.GetComponent<FuzzComponent1>(entity);
                        comp.Value += 1000;
                        _world.SetComponent(entity, comp);
                    }
                    break;
                case 7: // Verify Component1 still exists and has valid data
                    if (_world.HasComponent<FuzzComponent1>(entity))
                    {
                        var comp = _world.GetComponent<FuzzComponent1>(entity);
                        // Should be original value or original + increments of 1000
                        Assert.That((comp.Value - entityIndex) % 1000, Is.EqualTo(0), 
                                   $"Component1 data integrity violation for entity {entityIndex}");
                    }
                    break;
            }
        }
        
        // Final verification - all entities should maintain data integrity
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (_world.IsAlive(entity) && _world.HasComponent<FuzzComponent1>(entity))
            {
                var comp = _world.GetComponent<FuzzComponent1>(entity);
                Assert.That((comp.Value - i) % 1000, Is.EqualTo(0), 
                           $"Final data integrity check failed for entity {i}");
            }
        }
    }

    [Test]
    [Description("Fuzz: Random memory stress patterns")]
    public void RandomMemoryStress_StabilityCheck()
    {
        var random = new Random(22222);
        const int cycles = 50;
        
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            var entities = new List<Entity>();
            
            // Random entity creation burst
            var entityCount = random.Next(100, 2000);
            for (int i = 0; i < entityCount; i++)
            {
                var entity = _world.CreateEntity();
                entities.Add(entity);
                
                // Random component assignment
                var componentMask = random.Next(16); // 4 bits for 4 components
                
                if ((componentMask & 1) != 0)
                    _world.AddComponent(entity, new FuzzComponent1 { Value = i });
                if ((componentMask & 2) != 0)
                    _world.AddComponent(entity, new FuzzComponent2 { Value = i * 0.1f });
                if ((componentMask & 4) != 0)
                    _world.AddComponent(entity, new FuzzComponent3 { Flag = i % 2 == 0 });
                if ((componentMask & 8) != 0)
                    _world.AddComponent(entity, new FuzzComponent4 { Data = (byte)(i % 256) });
            }
            
            // Random operations on entities
            for (int op = 0; op < entityCount / 2; op++)
            {
                if (entities.Count == 0) break;
                
                var entityIndex = random.Next(entities.Count);
                var entity = entities[entityIndex];
                
                if (!_world.IsAlive(entity)) continue;
                
                // Random component modifications
                if (random.Next(2) == 0)
                {
                    var componentType = random.Next(4);
                    switch (componentType)
                    {
                        case 0 when _world.HasComponent<FuzzComponent1>(entity):
                            _world.RemoveComponent<FuzzComponent1>(entity);
                            break;
                        case 1 when _world.HasComponent<FuzzComponent2>(entity):
                            _world.RemoveComponent<FuzzComponent2>(entity);
                            break;
                        case 2 when _world.HasComponent<FuzzComponent3>(entity):
                            _world.RemoveComponent<FuzzComponent3>(entity);
                            break;
                        case 3 when _world.HasComponent<FuzzComponent4>(entity):
                            _world.RemoveComponent<FuzzComponent4>(entity);
                            break;
                    }
                }
            }
            
            // Random entity destruction
            var entitiesToDestroy = random.Next(entities.Count / 4);
            for (int i = 0; i < entitiesToDestroy && entities.Count > 0; i++)
            {
                var index = random.Next(entities.Count);
                if (_world.IsAlive(entities[index]))
                {
                    _world.DestroyEntity(entities[index]);
                }
                entities.RemoveAt(index);
            }
            
            // Memory should remain stable
            if (cycle % 10 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        
        // Final functionality check
        var testEntity = _world.CreateEntity();
        _world.AddComponent(testEntity, new FuzzComponent1 { Value = 12345 });
        
        Assert.That(_world.IsAlive(testEntity), Is.True, "World should be functional after memory stress");
        Assert.That(_world.GetComponent<FuzzComponent1>(testEntity).Value, Is.EqualTo(12345), 
                   "Component data should be intact after memory stress");
    }
}

// Fuzzing test components
internal struct FuzzComponent1
{
    public int Value;
}

internal struct FuzzComponent2
{
    public float Value;
}

internal struct FuzzComponent3
{
    public bool Flag;
}

internal struct FuzzComponent4
{
    public byte Data;
}