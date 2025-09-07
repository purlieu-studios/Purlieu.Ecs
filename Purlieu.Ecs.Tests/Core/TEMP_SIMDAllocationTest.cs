using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class TEMP_SIMDAllocationTest
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }

    [Test]
    public void Debug_SIMDAllocationBreakdown()
    {
        // Arrange
        const int entityCount = 512; // Full chunk
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            _world.AddComponent(entity, new Velocity(1, 1, 1));
        }

        var query = _world.Query()
            .With<Position>()
            .With<Velocity>();

        // Pre-trigger signature building to exclude from allocation measurement  
        _ = query.ChunksStack().GetEnumerator();

        // Force GC and wait for cleanup before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        Console.WriteLine("=== SIMD Allocation Breakdown ===");
        
        // Test 1: Just calling ChunksStack()
        long beforeChunks = GC.GetTotalMemory(false);
        var chunkEnumerable = query.ChunksStack();
        long afterChunks = GC.GetTotalMemory(false);
        Console.WriteLine($"ChunksStack() creation: {afterChunks - beforeChunks} bytes");
        
        // Test 2: Getting the enumerator
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeEnumerator = GC.GetTotalMemory(false);
        var enumerator = chunkEnumerable.GetEnumerator();
        long afterEnumerator = GC.GetTotalMemory(false);
        Console.WriteLine($"GetEnumerator(): {afterEnumerator - beforeEnumerator} bytes");
        
        // Test 3: First MoveNext() call
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeMoveNext = GC.GetTotalMemory(false);
        bool hasNext = enumerator.MoveNext();
        long afterMoveNext = GC.GetTotalMemory(false);
        Console.WriteLine($"First MoveNext(): {afterMoveNext - beforeMoveNext} bytes, hasNext: {hasNext}");
        
        if (hasNext)
        {
            var chunk = enumerator.Current;
            Console.WriteLine($"Chunk count: {chunk.Count}");
            
            // Test 4: Getting spans
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            long beforeSpans = GC.GetTotalMemory(false);
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            long afterSpans = GC.GetTotalMemory(false);
            Console.WriteLine($"GetSpan calls: {afterSpans - beforeSpans} bytes");
            Console.WriteLine($"Position span length: {positions.Length}");
            Console.WriteLine($"Velocity span length: {velocities.Length}");
            
            // Test 5: Accessing spans
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            long beforeAccess = GC.GetTotalMemory(false);
            for (int i = 0; i < Math.Min(positions.Length, 100); i++)
            {
                var pos = positions[i];
                var vel = velocities[i];
                _ = pos.X + vel.X;
            }
            long afterAccess = GC.GetTotalMemory(false);
            Console.WriteLine($"Span access loop: {afterAccess - beforeAccess} bytes");
        }
        
        // Test 6: Full iteration like the original test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeIteration = GC.GetTotalMemory(false);
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.IsSimdSupported<Position>())
            {
                var positions = chunk.GetSpan<Position>();
                var velocities = chunk.GetSpan<Velocity>();
                
                for (int i = 0; i < Math.Min(positions.Length, 100); i++)
                {
                    var pos = positions[i];
                    var vel = velocities[i];
                    _ = pos.X + vel.X;
                }
            }
        }
        long afterIteration = GC.GetTotalMemory(false);
        Console.WriteLine($"Full foreach iteration: {afterIteration - beforeIteration} bytes");
    }
}