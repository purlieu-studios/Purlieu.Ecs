using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class TEMP_QueryAllocationTest
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }

    [Test]
    public void Debug_QueryCreationSteps()
    {
        // Check component type IDs first
        var positionId = PurlieuEcs.Core.ComponentTypeId.GetOrCreate(typeof(Position));
        var velocityId = PurlieuEcs.Core.ComponentTypeId.GetOrCreate(typeof(Velocity));
        
        Console.WriteLine($"Position component ID: {positionId}");
        Console.WriteLine($"Velocity component ID: {velocityId}");
        Console.WriteLine($"This means arrays need elementIndex: Position={positionId / 64}, Velocity={velocityId / 64}");
        
        // Test each step of query creation separately
        
        // Step 1: Just creating WorldQuery
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeWorldQuery = GC.GetTotalMemory(false);
        var query = _world.Query();
        long afterWorldQuery = GC.GetTotalMemory(false);
        
        Console.WriteLine($"WorldQuery creation allocated: {afterWorldQuery - beforeWorldQuery} bytes");
        
        // Step 2: First .With<T>() call
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeWith1 = GC.GetTotalMemory(false);
        query = query.With<Position>();
        long afterWith1 = GC.GetTotalMemory(false);
        
        Console.WriteLine($"First With<Position>() allocated: {afterWith1 - beforeWith1} bytes");
        
        // Step 3: Second .With<T>() call
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeWith2 = GC.GetTotalMemory(false);
        query = query.With<Velocity>();
        long afterWith2 = GC.GetTotalMemory(false);
        
        Console.WriteLine($"Second With<Velocity>() allocated: {afterWith2 - beforeWith2} bytes");
        
        // Step 4: Test direct signature creation to isolate the issue
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeSig = GC.GetTotalMemory(false);
        var emptySig = new PurlieuEcs.Core.ArchetypeSignature();
        var sigWithPos = emptySig.Add<Position>();
        var sigWithBoth = sigWithPos.Add<Velocity>();
        long afterSig = GC.GetTotalMemory(false);
        
        Console.WriteLine($"Direct signature creation allocated: {afterSig - beforeSig} bytes");
        
        // Step 5: Test SignatureArrayPool operations directly
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforePool1 = GC.GetTotalMemory(false);
        var array1 = PurlieuEcs.Core.SignatureArrayPool.TestRent(1);
        long afterPool1 = GC.GetTotalMemory(false);
        
        Console.WriteLine($"SignatureArrayPool.Rent(1) allocated: {afterPool1 - beforePool1} bytes");
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforePool2 = GC.GetTotalMemory(false);
        var array2 = PurlieuEcs.Core.SignatureArrayPool.TestRent(1);
        long afterPool2 = GC.GetTotalMemory(false);
        
        Console.WriteLine($"Second SignatureArrayPool.Rent(1) allocated: {afterPool2 - beforePool2} bytes");
        
        // Return arrays to pool
        PurlieuEcs.Core.SignatureArrayPool.TestReturn(array1);
        PurlieuEcs.Core.SignatureArrayPool.TestReturn(array2);
        
        // Test resize operations which are used by ArchetypeSignature.Add<T>()
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var sourceArray = new ulong[1];
        long beforeResize = GC.GetTotalMemory(false);
        var resizedArray = PurlieuEcs.Core.SignatureArrayPool.TestResize(sourceArray, 1);
        long afterResize = GC.GetTotalMemory(false);
        
        Console.WriteLine($"SignatureArrayPool.Resize() allocated: {afterResize - beforeResize} bytes");
        
        PurlieuEcs.Core.SignatureArrayPool.TestReturn(resizedArray);
    }
}