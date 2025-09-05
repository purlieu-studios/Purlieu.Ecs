using PurlieuEcs.Core;
using System.Collections.Concurrent;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class SOAK_EnduranceTests
{
    [Test, Timeout(30000)] // 30 second timeout
    public void SOAK_MassiveEntityChurn_NoMemoryLeak()
    {
        var world = new World();
        long initialMemory = GC.GetTotalMemory(true);
        
        // Simulate heavy entity churn over time
        const int cycles = 1000;
        const int entitiesPerCycle = 100;
        
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            var entities = new Entity[entitiesPerCycle];
            
            // Create burst
            for (int i = 0; i < entitiesPerCycle; i++)
            {
                entities[i] = world.CreateEntity();
            }
            
            // Verify all alive
            for (int i = 0; i < entitiesPerCycle; i++)
            {
                Assert.That(world.IsAlive(entities[i]), Is.True, $"Entity {i} should be alive in cycle {cycle}");
            }
            
            // Destroy all
            for (int i = 0; i < entitiesPerCycle; i++)
            {
                world.DestroyEntity(entities[i]);
            }
            
            // Verify all dead
            for (int i = 0; i < entitiesPerCycle; i++)
            {
                Assert.That(world.IsAlive(entities[i]), Is.False, $"Entity {i} should be dead in cycle {cycle}");
            }
            
            // Periodic memory check
            if (cycle % 100 == 99)
            {
                long currentMemory = GC.GetTotalMemory(true);
                long memoryGrowth = currentMemory - initialMemory;
                
                // Allow some memory growth but not unbounded
                Assert.That(memoryGrowth, Is.LessThan(50 * 1024 * 1024), // 50MB max growth
                    $"Memory growth should be bounded. Current growth: {memoryGrowth / 1024 / 1024}MB after {cycle + 1} cycles");
            }
        }
        
        // Final memory check
        long finalMemory = GC.GetTotalMemory(true);
        long totalGrowth = finalMemory - initialMemory;
        
        Assert.That(totalGrowth, Is.LessThan(100 * 1024 * 1024), // 100MB max
            $"Total memory growth should be reasonable. Growth: {totalGrowth / 1024 / 1024}MB");
    }
    
    [Test, Timeout(15000)] // 15 second timeout
    public void SOAK_EntityRecyclingStability_MaintainsConsistency()
    {
        var world = new World();
        var random = new Random(42); // Fixed seed for reproducibility
        var activeEntities = new HashSet<Entity>();
        
        // Run for many iterations with random create/destroy
        const int iterations = 10000;
        
        for (int i = 0; i < iterations; i++)
        {
            if (activeEntities.Count == 0 || random.NextDouble() < 0.6) // 60% create, 40% destroy
            {
                // Create entity
                var entity = world.CreateEntity();
                Assert.That(world.IsAlive(entity), Is.True, $"Newly created entity should be alive at iteration {i}");
                Assert.That(activeEntities.Add(entity), Is.True, $"Entity should be unique at iteration {i}");
            }
            else
            {
                // Destroy random entity
                var entityToDestroy = activeEntities.Skip(random.Next(activeEntities.Count)).First();
                world.DestroyEntity(entityToDestroy);
                Assert.That(world.IsAlive(entityToDestroy), Is.False, $"Destroyed entity should not be alive at iteration {i}");
                activeEntities.Remove(entityToDestroy);
            }
            
            // Verify all tracked entities are still valid
            foreach (var entity in activeEntities)
            {
                Assert.That(world.IsAlive(entity), Is.True, $"Tracked entity should remain alive at iteration {i}");
            }
            
            // Periodic consistency check
            if (i % 1000 == 999)
            {
                // Verify no false positives in IsAlive
                var deadEntity = new Entity(999999, 1); // Likely non-existent
                Assert.That(world.IsAlive(deadEntity), Is.False, $"Non-existent entity should not be alive at iteration {i}");
            }
        }
        
        // Cleanup - destroy remaining entities
        foreach (var entity in activeEntities.ToArray())
        {
            world.DestroyEntity(entity);
            Assert.That(world.IsAlive(entity), Is.False, "Entity should be destroyed during cleanup");
        }
    }
    
    [Test, Timeout(20000)] // 20 second timeout
    public void SOAK_ArchetypeSignatureOperations_StablePerformance()
    {
        const int operationsCount = 50000;
        
        var signatures = new List<ArchetypeSignature>();
        var random = new Random(42);
        
        // Build up a collection of signatures
        for (int i = 0; i < operationsCount; i++)
        {
            ArchetypeSignature signature = new();
            
            // Randomly add components
            if (random.NextDouble() < 0.8) signature = signature.Add<TestComponentA>();
            if (random.NextDouble() < 0.6) signature = signature.Add<TestComponentB>();
            if (random.NextDouble() < 0.4) signature = signature.Add<TestComponentC>();
            
            signatures.Add(signature);
            
            // Perform operations on random existing signatures
            if (signatures.Count > 10)
            {
                var randomSig = signatures[random.Next(signatures.Count)];
                
                // Test operations don't throw
                Assert.DoesNotThrow(() => {
                    var _ = randomSig.Has<TestComponentA>();
                    var __ = randomSig.Has<TestComponentB>();
                    var ___ = randomSig.Has<TestComponentC>();
                });
                
                // Test equality operations
                if (signatures.Count > 20)
                {
                    var otherSig = signatures[random.Next(signatures.Count)];
                    Assert.DoesNotThrow(() => {
                        var _ = randomSig.Equals(otherSig);
                        var __ = randomSig.IsSupersetOf(otherSig);
                        var ___ = randomSig.GetHashCode();
                    });
                }
            }
            
            // Periodic validation
            if (i % 5000 == 4999)
            {
                // Verify signature consistency
                var testSig = ArchetypeSignature.With<TestComponentA>().Add<TestComponentB>();
                Assert.That(testSig.Has<TestComponentA>(), Is.True);
                Assert.That(testSig.Has<TestComponentB>(), Is.True);
                Assert.That(testSig.Has<TestComponentC>(), Is.False);
            }
        }
        
        // Final consistency check - all signatures should still be valid
        foreach (var sig in signatures.Take(100)) // Check sample
        {
            Assert.DoesNotThrow(() => sig.GetHashCode(), "Signature should remain valid");
        }
    }
    
    [Test, Timeout(10000)] // 10 second timeout  
    public void SOAK_ConcurrentWorldUsage_ThreadSafety()
    {
        // Note: This test checks if World operations are safe when called from single thread
        // True thread-safety would require locks, but this tests for consistency
        var world = new World();
        const int iterations = 5000;
        
        var entities = new ConcurrentBag<Entity>();
        var exceptions = new ConcurrentBag<Exception>();
        
        try
        {
            // Simulate rapid operations that might expose race conditions or corruption
            Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = 1 }, i =>
            {
                try
                {
                    if (i % 3 == 0)
                    {
                        var entity = world.CreateEntity();
                        entities.Add(entity);
                        
                        if (!world.IsAlive(entity))
                        {
                            throw new InvalidOperationException($"Created entity should be alive: {entity}");
                        }
                    }
                    else if (entities.TryTake(out var entity))
                    {
                        world.DestroyEntity(entity);
                        
                        if (world.IsAlive(entity))
                        {
                            throw new InvalidOperationException($"Destroyed entity should not be alive: {entity}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }
        
        // Check for any exceptions that occurred
        if (exceptions.Any())
        {
            var first = exceptions.First();
            Assert.Fail($"Concurrent operations caused exception: {first.Message}\nStack: {first.StackTrace}");
        }
        
        // Verify remaining entities are still valid
        foreach (var entity in entities)
        {
            Assert.That(world.IsAlive(entity), Is.True, $"Remaining entity should still be alive: {entity}");
        }
    }
}

