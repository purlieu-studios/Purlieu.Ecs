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

    [Test]
    public void Isolate_16KB_AllocationSource()
    {
        Console.WriteLine("=== ISOLATING 16KB ALLOCATION SOURCE ===");
        
        // Create some entities first to make the world realistic
        for (int i = 0; i < 100; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            if (i % 2 == 0) _world.AddComponent(entity, new Velocity(1, 1, 1));
        }
        
        // Test 1: Baseline measurement - pure query creation
        MeasureStep("Baseline: Pure WorldQuery creation", () =>
        {
            var query = _world.Query();
            return query;
        });
        
        // Test 2: Single component query
        MeasureStep("Single component: With<Position>", () =>
        {
            var query = _world.Query().With<Position>();
            return query;
        });
        
        // Test 3: The problematic dual component query
        MeasureStep("Dual component: With<Position>().With<Velocity>", () =>
        {
            var query = _world.Query().With<Position>().With<Velocity>();
            return query;
        });
        
        // Test 4: Query enumeration (the failing operation)
        var testQuery = _world.Query().With<Position>().With<Velocity>();
        MeasureStep("Query enumeration: ChunksStack().GetEnumerator()", () =>
        {
            var enumerator = testQuery.ChunksStack().GetEnumerator();
            return enumerator;
        });
        
        // Test 5: Full enumeration loop
        var testQuery2 = _world.Query().With<Position>().With<Velocity>();
        MeasureStep("Full enumeration: foreach chunk loop", () =>
        {
            int count = 0;
            foreach (var chunk in testQuery2.ChunksStack())
            {
                count += chunk.Count;
            }
            return count;
        });
        
        // Test 6: Test ArchetypeIndex operations directly
        MeasureStep("ArchetypeIndex operations", () =>
        {
            var signature = new PurlieuEcs.Core.ArchetypeSignature();
            signature = signature.Add<Position>();
            signature = signature.Add<Velocity>();
            // Access the internal archetype matching - this might be the culprit
            var query = _world.Query().With<Position>().With<Velocity>();
            var chunks = query.ChunksStack();
            var enumerator = chunks.GetEnumerator();
            enumerator.MoveNext(); // Force archetype matching
            return enumerator.Current;
        });
        
        // Test 7: Pool operations in isolation
        MeasureStep("Pool operations in sequence", () =>
        {
            var arr1 = PurlieuEcs.Core.SignatureArrayPool.TestRent(4);
            var arr2 = PurlieuEcs.Core.SignatureArrayPool.TestRent(4);
            var list1 = PurlieuEcs.Core.ListPool<object>.Rent();
            var small1 = PurlieuEcs.Core.SmallArchetypeArrayPool.Rent();
            
            // Return them
            PurlieuEcs.Core.SignatureArrayPool.TestReturn(arr1);
            PurlieuEcs.Core.SignatureArrayPool.TestReturn(arr2);
            PurlieuEcs.Core.ListPool<object>.Return(list1);
            PurlieuEcs.Core.SmallArchetypeArrayPool.Return(small1);
            
            return "Pools exercised";
        });
        
        Console.WriteLine("=== END ISOLATION TEST ===");
    }

    [Test]
    public void Debug_WorldQueryCreation_Granular()
    {
        Console.WriteLine("=== GRANULAR WORLD.QUERY() ANALYSIS ===");
        
        // Test 1: Pure object allocation
        MeasureStep("Raw WorldQuery constructor", () =>
        {
            return new PurlieuEcs.Query.WorldQuery(_world);
        });
        
        // Test 2: Through World.Query() method
        MeasureStep("World.Query() method", () =>
        {
            return _world.Query();
        });
        
        // Test 3: Test if it's the World reference that's causing it
        GC.Collect();
        GC.WaitForPendingFinalizers();  
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        var testWorld = new PurlieuEcs.Core.World();
        long after = GC.GetTotalMemory(false);
        Console.WriteLine($"Creating new World: {after - before} bytes");
        
        // Test 4: Multiple queries from same world
        MeasureStep("Second query from same world", () =>
        {
            return _world.Query();
        });
        
        MeasureStep("Third query from same world", () =>
        {
            return _world.Query();
        });
        
        Console.WriteLine("=== END GRANULAR ANALYSIS ===");
    }

    [Test]
    public void Debug_WorldQueryConstructor_Components()
    {
        Console.WriteLine("=== WORLDQUERY CONSTRUCTOR COMPONENT ANALYSIS ===");
        
        // Test 1: Just the field assignments
        MeasureStep("World field assignment", () =>
        {
            var world = _world; // Just assign the reference
            return world;
        });
        
        // Test 2: Create List<int> with capacity 4
        MeasureStep("New List<int>(4)", () =>
        {
            return new List<int>(4);
        });
        
        // Test 3: Create List<int> with capacity 2
        MeasureStep("New List<int>(2)", () =>
        {
            return new List<int>(2);
        });
        
        // Test 4: Create both Lists together
        MeasureStep("Both Lists together", () =>
        {
            var list1 = new List<int>(4);
            var list2 = new List<int>(2);
            return (list1, list2);
        });
        
        // Test 5: Test if it's generic instantiation related
        MeasureStep("List<string> creation", () =>
        {
            return new List<string>(4);
        });
        
        // Test 6: Test if it's the specific capacity numbers
        MeasureStep("List<int> with different capacity", () =>
        {
            return new List<int>(16);
        });
        
        // Test 7: Test array vs List
        MeasureStep("Array creation int[4]", () =>
        {
            return new int[4];
        });
        
        Console.WriteLine("=== END CONSTRUCTOR COMPONENT ANALYSIS ===");
    }
    
    private T MeasureStep<T>(string stepName, Func<T> operation)
    {
        // Force GC to get clean baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long before = GC.GetTotalMemory(false);
        var result = operation();
        long after = GC.GetTotalMemory(false);
        
        var allocated = after - before;
        Console.WriteLine($"{stepName}: {allocated} bytes allocated");
        
        // Flag the 16KB-ish allocations
        if (allocated > 15000)
        {
            Console.WriteLine($"*** FOUND LARGE ALLOCATION: {allocated} bytes in '{stepName}' ***");
        }
        
        return result;
    }
}