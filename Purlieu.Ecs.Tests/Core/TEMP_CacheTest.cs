using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class TEMP_CacheTest
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }

    [Test]
    public void Debug_ArchetypeIndexCache()
    {
        // Arrange
        const int entityCount = 512; // Full chunk
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
            _world.AddComponent(entity, new Velocity(1, 1, 1));
        }

        var withSig = new ArchetypeSignature()
            .Add<Position>()
            .Add<Velocity>();
        var withoutSig = new ArchetypeSignature();

        // Force GC and wait for cleanup before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        Console.WriteLine("=== ArchetypeIndex Cache Test ===");
        
        // Test 1: First call should allocate
        long beforeFirst = GC.GetTotalMemory(false);
        var result1 = _world._archetypeIndex.GetMatchingArchetypes(withSig, withoutSig);
        long afterFirst = GC.GetTotalMemory(false);
        Console.WriteLine($"First GetMatchingArchetypes(): {afterFirst - beforeFirst} bytes");
        Console.WriteLine($"Result1 archetype count: {result1.Count}");
        
        // Test 2: Second call should hit cache
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeSecond = GC.GetTotalMemory(false);
        var result2 = _world._archetypeIndex.GetMatchingArchetypes(withSig, withoutSig);
        long afterSecond = GC.GetTotalMemory(false);
        Console.WriteLine($"Second GetMatchingArchetypes(): {afterSecond - beforeSecond} bytes");
        Console.WriteLine($"Result2 archetype count: {result2.Count}");
        
        // Test 3: Same signatures but new instance (to test equality)
        var withSig2 = new ArchetypeSignature()
            .Add<Position>()
            .Add<Velocity>();
        var withoutSig2 = new ArchetypeSignature();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long beforeThird = GC.GetTotalMemory(false);
        var result3 = _world._archetypeIndex.GetMatchingArchetypes(withSig2, withoutSig2);
        long afterThird = GC.GetTotalMemory(false);
        Console.WriteLine($"Third GetMatchingArchetypes() with new sig instances: {afterThird - beforeThird} bytes");
        Console.WriteLine($"Result3 archetype count: {result3.Count}");
        
        // Verify results are equivalent
        Assert.That(result1.Count, Is.EqualTo(result2.Count));
        Assert.That(result2.Count, Is.EqualTo(result3.Count));
    }
}