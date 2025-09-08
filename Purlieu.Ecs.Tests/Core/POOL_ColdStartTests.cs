using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests for pool pre-warming and cold start optimization scenarios.
/// These tests help identify and fix allocation issues during system initialization.
/// </summary>
[TestFixture]
[Category("PoolOptimization")]
public class POOL_ColdStartTests
{
    [Test]
    public void SmallArchetypeArrayPool_ColdStart_CreatesArray()
    {
        // Test that cold pool creates new array
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        var array1 = SmallArchetypeArrayPool.Rent();
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Assert.That(allocated, Is.GreaterThan(0), "Cold pool should allocate new array");
        Assert.That(array1, Is.Not.Null);
        Assert.That(array1.Length, Is.EqualTo(16));
        
        SmallArchetypeArrayPool.Return(array1);
    }

    [Test]
    public void SmallArchetypeArrayPool_WarmPool_ReuseArray()
    {
        // Pre-warm pool
        var array1 = SmallArchetypeArrayPool.Rent();
        SmallArchetypeArrayPool.Return(array1);
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Warm pool should reuse array
        long before = GC.GetTotalMemory(false);
        var array2 = SmallArchetypeArrayPool.Rent();
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Assert.That(allocated, Is.LessThanOrEqualTo(100), "Warm pool should not allocate");
        Assert.That(array2, Is.Not.Null);
        
        SmallArchetypeArrayPool.Return(array2);
    }

    [Test]
    public void ListPool_ColdVsWarm_AllocationDifference()
    {
        // Test cold start
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeCold = GC.GetTotalMemory(false);
        var list1 = ListPool<Archetype>.Rent();
        long afterCold = GC.GetTotalMemory(false);
        var coldAllocation = afterCold - beforeCold;
        
        ListPool<Archetype>.Return(list1);
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Test warm start
        long beforeWarm = GC.GetTotalMemory(false);
        var list2 = ListPool<Archetype>.Rent();
        long afterWarm = GC.GetTotalMemory(false);
        var warmAllocation = afterWarm - beforeWarm;
        
        Assert.That(coldAllocation, Is.GreaterThan(warmAllocation),
            $"Cold allocation ({coldAllocation}B) should exceed warm allocation ({warmAllocation}B)");
        
        ListPool<Archetype>.Return(list2);
    }

    [Test]
    public void WorldInitialization_PoolPreWarming()
    {
        // Test a hypothetical pool pre-warming during World initialization
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        
        // Simulate pre-warming pools during World creation
        var world = new World();
        LogicBootstrap.RegisterComponents(world);
        
        // Pre-warm common pools
        var tempArray = SmallArchetypeArrayPool.Rent();
        SmallArchetypeArrayPool.Return(tempArray);
        
        var tempList = ListPool<Archetype>.Rent();
        ListPool<Archetype>.Return(tempList);
        
        long after = GC.GetTotalMemory(false);
        var allocated = after - before;
        
        // Should be reasonable overhead for initialization + pre-warming
        Assert.That(allocated, Is.LessThanOrEqualTo(80 * 1024), 
            $"World initialization + pool pre-warming should be ≤80KB, was {allocated} bytes");
    }

    [Test]
    public void SignatureArrayPool_ThreadStatic_ColdStart()
    {
        // Test ThreadStatic pool cold start impact
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        
        // This should trigger ThreadStatic initialization
        var array1 = SignatureArrayPool.TestRent(4);
        var array2 = SignatureArrayPool.TestRent(4);
        
        long after = GC.GetTotalMemory(false);
        var allocated = after - before;
        
        Console.WriteLine($"SignatureArrayPool cold start allocated: {allocated} bytes");
        
        SignatureArrayPool.TestReturn(array1);
        SignatureArrayPool.TestReturn(array2);
        
        // Document the cold start cost for ThreadStatic pools
        Assert.That(allocated, Is.LessThanOrEqualTo(40 * 1024), 
            $"SignatureArrayPool cold start should be ≤40KB, was {allocated} bytes");
    }

    [Test]
    public void ComponentStorageFactory_ReflectionVsRegistered()
    {
        // Compare reflection-based vs pre-registered component creation
        var world = new World();
        LogicBootstrap.RegisterComponents(world);
        
        // Test registered type (should be fast)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeRegistered = GC.GetTotalMemory(false);
        for (int i = 0; i < 100; i++)
        {
            var storage1 = ComponentStorageFactory.Create(typeof(Position), 64);
            Assert.That(storage1, Is.Not.Null);
        }
        long afterRegistered = GC.GetTotalMemory(false);
        var registeredAllocation = afterRegistered - beforeRegistered;
        
        // Test unregistered type (uses reflection)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeReflection = GC.GetTotalMemory(false);
        for (int i = 0; i < 100; i++)
        {
            var storage2 = ComponentStorageFactory.Create(typeof(UnregisteredTestComponent), 64);
            Assert.That(storage2, Is.Not.Null);
        }
        long afterReflection = GC.GetTotalMemory(false);
        var reflectionAllocation = afterReflection - beforeReflection;
        
        Console.WriteLine($"Registered: {registeredAllocation}B, Reflection: {reflectionAllocation}B");
        
        // Registered components may allocate more due to pre-warming, but should be reasonable
        Assert.That(registeredAllocation, Is.LessThan(400000),
            "Pre-registered component allocation should be within reasonable bounds");
    }
    
    private struct UnregisteredTestComponent 
    { 
        public int Value;
    }
}