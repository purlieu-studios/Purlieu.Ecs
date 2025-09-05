using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class ALLOC_AllocationTests
{
    private World _world;

    [SetUp]
    public void SetUp()
    {
        _world = new World();
        // Force a garbage collection before each test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Test]
    public void ALLOC_EntityCreation_NoHeapAllocation()
    {
        // Warm up
        for (int i = 0; i < 10; i++)
        {
            _world.CreateEntity();
        }
        
        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        long initialGen1 = GC.CollectionCount(1);
        long initialGen2 = GC.CollectionCount(2);
        
        // Test allocation-free entity creation (hot path)
        for (int i = 0; i < 1000; i++)
        {
            _world.CreateEntity();
        }
        
        long finalGen0 = GC.CollectionCount(0);
        long finalGen1 = GC.CollectionCount(1);
        long finalGen2 = GC.CollectionCount(2);
        
        // No garbage collections should have occurred (allowing for 1 Gen0 due to array resize)
        Assert.That(finalGen0 - initialGen0, Is.LessThanOrEqualTo(1), "Gen0 collections should be minimal");
        Assert.That(finalGen1 - initialGen1, Is.EqualTo(0), "No Gen1 collections should occur");
        Assert.That(finalGen2 - initialGen2, Is.EqualTo(0), "No Gen2 collections should occur");
    }

    [Test]
    public void ALLOC_EntityDestruction_NoHeapAllocation()
    {
        // Pre-create entities
        var entities = new Entity[1000];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
        }
        
        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        long initialGen1 = GC.CollectionCount(1);
        long initialGen2 = GC.CollectionCount(2);
        
        // Test allocation-free entity destruction
        for (int i = 0; i < entities.Length; i++)
        {
            _world.DestroyEntity(entities[i]);
        }
        
        long finalGen0 = GC.CollectionCount(0);
        long finalGen1 = GC.CollectionCount(1);
        long finalGen2 = GC.CollectionCount(2);
        
        Assert.That(finalGen0 - initialGen0, Is.EqualTo(0), "No Gen0 collections should occur");
        Assert.That(finalGen1 - initialGen1, Is.EqualTo(0), "No Gen1 collections should occur");
        Assert.That(finalGen2 - initialGen2, Is.EqualTo(0), "No Gen2 collections should occur");
    }

    [Test]
    public void ALLOC_ArchetypeSignatureOperations_NoHeapAllocation()
    {
        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        
        // Test signature operations (will allocate on first creation due to static caching)
        var signature1 = ArchetypeSignature.With<TestComponentA>();
        var signature2 = signature1.Add<TestComponentB>();
        var signature3 = signature2.Remove<TestComponentA>();
        
        // Check equality operations
        for (int i = 0; i < 1000; i++)
        {
            var _ = signature1.Equals(signature2);
            var __ = signature2.Has<TestComponentA>();
            var ___ = signature3.IsSupersetOf(signature1);
        }
        
        long finalGen0 = GC.CollectionCount(0);
        
        // Allow some allocations for signature creation but not for operations
        Assert.That(finalGen0 - initialGen0, Is.LessThanOrEqualTo(2), "Minimal allocations for signature operations");
    }

    [Test]
    public void ALLOC_IsAliveChecks_NoHeapAllocation()
    {
        // Create entities to check
        var entities = new Entity[100];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
        }
        
        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        
        // Perform many IsAlive checks (hot path)
        for (int i = 0; i < 10000; i++)
        {
            for (int j = 0; j < entities.Length; j++)
            {
                _world.IsAlive(entities[j]);
            }
        }
        
        long finalGen0 = GC.CollectionCount(0);
        
        Assert.That(finalGen0 - initialGen0, Is.EqualTo(0), "IsAlive checks should not allocate");
    }
}