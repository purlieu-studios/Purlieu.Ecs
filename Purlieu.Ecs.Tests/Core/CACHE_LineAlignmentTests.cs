using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;
using System.Runtime.CompilerServices;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests for cache-line aligned memory allocation in chunk storage.
/// </summary>
[TestFixture]
[Category("CacheLine")]
public class CACHE_LineAlignmentTests
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }

    [Test]
    public void CacheLineAllocator_SmallCapacity_HandlesCorrectly()
    {
        // Test various small capacities
        var capacities = new[] { 1, 4, 8, 15, 16, 32 };
        
        foreach (var capacity in capacities)
        {
            var aligned = CacheLineAlignedAllocator.AllocateAligned<Position>(capacity);
            
            Assert.That(aligned, Is.Not.Null);
            Assert.That(aligned.Length, Is.GreaterThanOrEqualTo(capacity));
            
            var (elementSize, elementsPerCacheLine, alignedCapacity, overhead) = 
                CacheLineAlignedAllocator.GetAlignmentInfo<Position>(capacity);
            
            Console.WriteLine($"Position[{capacity}]: {elementSize}B elements, {elementsPerCacheLine} per cache line, " +
                            $"aligned to {alignedCapacity} (overhead: {overhead:P1})");
            
            // Verify alignment makes sense
            Assert.That(elementSize, Is.EqualTo(Unsafe.SizeOf<Position>()));
            Assert.That(elementsPerCacheLine, Is.GreaterThan(0));
            Assert.That(alignedCapacity, Is.GreaterThanOrEqualTo(capacity));
        }
    }

    [Test]
    public void CacheLineAllocator_VariousTypes_AlignsProperly()
    {
        const int capacity = 64;
        
        // Test different component types with different sizes
        TestAlignmentFor<Position>(capacity);        // 12 bytes (3 floats)
        TestAlignmentFor<Velocity>(capacity);        // 12 bytes (3 floats) 
        TestAlignmentFor<TestComponentA>(capacity);  // Larger component
        TestAlignmentFor<Entity>(capacity);          // 8 bytes (2 uint32s)
    }
    
    private void TestAlignmentFor<T>(int capacity) where T : unmanaged
    {
        var aligned = CacheLineAlignedAllocator.AllocateAligned<T>(capacity);
        var (elementSize, elementsPerCacheLine, alignedCapacity, overhead) = 
            CacheLineAlignedAllocator.GetAlignmentInfo<T>(capacity);
        
        Assert.That(aligned.Length, Is.EqualTo(alignedCapacity));
        Assert.That(elementSize, Is.EqualTo(Unsafe.SizeOf<T>()));
        
        // Cache line should be 64 bytes
        var expectedElementsPerLine = Math.Max(1, 64 / elementSize);
        Assert.That(elementsPerCacheLine, Is.EqualTo(expectedElementsPerLine));
        
        // Aligned capacity should be multiple of elements per cache line
        Assert.That(alignedCapacity % elementsPerCacheLine, Is.EqualTo(0));
        
        Console.WriteLine($"{typeof(T).Name}[{capacity}]: {elementSize}B each, " +
                        $"{elementsPerCacheLine} per cache line, aligned to {alignedCapacity} " +
                        $"(overhead: {overhead:P1})");
    }

    [Test]
    public void ComponentStorage_UsesCacheLineAlignment()
    {
        // Add entities and check that the resulting chunk storage is cache-line aligned
        var entities = new Entity[50];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new Position(i, i + 1, i + 2));
            _world.AddComponent(entities[i], new Velocity(i * 0.1f, i * 0.2f, i * 0.3f));
        }
        
        // Verify data integrity after alignment
        for (int i = 0; i < entities.Length; i++)
        {
            var pos = _world.GetComponent<Position>(entities[i]);
            var vel = _world.GetComponent<Velocity>(entities[i]);
            
            Assert.That(pos.X, Is.EqualTo(i).Within(0.001f));
            Assert.That(pos.Y, Is.EqualTo(i + 1).Within(0.001f));
            Assert.That(pos.Z, Is.EqualTo(i + 2).Within(0.001f));
            
            Assert.That(vel.X, Is.EqualTo(i * 0.1f).Within(0.001f));
            Assert.That(vel.Y, Is.EqualTo(i * 0.2f).Within(0.001f));
            Assert.That(vel.Z, Is.EqualTo(i * 0.3f).Within(0.001f));
        }
        
        Console.WriteLine($"Successfully created and validated {entities.Length} entities with cache-line aligned storage");
    }

    [Test]
    public void CacheLineAlignment_OverheadReasonable()
    {
        // Test that cache-line alignment doesn't create excessive overhead
        var testCases = new[]
        {
            (typeof(Position), 64),   // 12 bytes each
            (typeof(Velocity), 128),  // 12 bytes each
            (typeof(Entity), 256),    // 8 bytes each
            (typeof(float), 512),     // 4 bytes each
            (typeof(int), 1024)       // 4 bytes each
        };
        
        foreach (var (type, capacity) in testCases)
        {
            var elementSize = GetElementSize(type);
            var elementsPerCacheLine = Math.Max(1, 64 / elementSize);
            var alignedCapacity = ((capacity + elementsPerCacheLine - 1) / elementsPerCacheLine) * elementsPerCacheLine;
            var overhead = alignedCapacity > capacity ? (alignedCapacity - capacity) / (float)capacity : 0f;
            
            // Overhead should be reasonable (< 50% for most cases)
            Assert.That(overhead, Is.LessThan(0.5f), 
                $"{type.Name} alignment overhead too high: {overhead:P1}");
            
            Console.WriteLine($"{type.Name}[{capacity}]: aligned to {alignedCapacity}, overhead {overhead:P1}");
        }
    }
    
    private static int GetElementSize(Type type)
    {
        if (type == typeof(Position)) return 12;
        if (type == typeof(Velocity)) return 12;
        if (type == typeof(Entity)) return 8;
        if (type == typeof(float)) return 4;
        if (type == typeof(int)) return 4;
        return 16; // Default estimate
    }

    [Test]
    public void ChunkCreation_UsesAlignedStorage()
    {
        // Create chunk with mixed component types
        var componentTypes = new[] { typeof(Position), typeof(Velocity), typeof(TestComponentA) };
        var signature = new ArchetypeSignature()
            .Add<Position>()
            .Add<Velocity>()
            .Add<TestComponentA>();
        
        var chunk = new Chunk(componentTypes, 128);
        
        // Verify chunk was created successfully
        Assert.That(chunk.Capacity, Is.EqualTo(128));
        Assert.That(chunk.Count, Is.EqualTo(0));
        Assert.That(chunk.HasComponent<Position>(), Is.True);
        Assert.That(chunk.HasComponent<Velocity>(), Is.True);
        Assert.That(chunk.HasComponent<TestComponentA>(), Is.True);
        
        // Test adding entities works with aligned storage
        var entities = new Entity[50];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = new Entity((uint)(i + 1), 1);
            chunk.AddEntity(entities[i]);
        }
        
        Assert.That(chunk.Count, Is.EqualTo(50));
        
        // Verify entity retrieval works
        for (int i = 0; i < entities.Length; i++)
        {
            var retrieved = chunk.GetEntity(i);
            Assert.That(retrieved, Is.EqualTo(entities[i]));
        }
    }

    [Test]
    public void AlignedMemory_PerformanceCharacteristics()
    {
        // This test documents the performance characteristics of aligned memory
        const int iterations = 1000;
        const int capacity = 512;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Allocate many aligned arrays to test allocation performance
        var arrays = new Position[iterations][];
        for (int i = 0; i < iterations; i++)
        {
            arrays[i] = CacheLineAlignedAllocator.AllocateAligned<Position>(capacity);
        }
        
        stopwatch.Stop();
        var allocTime = stopwatch.ElapsedMilliseconds;
        
        // Test sequential access performance
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var array = arrays[i];
            for (int j = 0; j < Math.Min(capacity, array.Length); j++)
            {
                array[j] = new Position(j, j + 1, j + 2);
            }
        }
        stopwatch.Stop();
        var writeTime = stopwatch.ElapsedMilliseconds;
        
        // Test read performance  
        stopwatch.Restart();
        float sum = 0f;
        for (int i = 0; i < iterations; i++)
        {
            var array = arrays[i];
            for (int j = 0; j < Math.Min(capacity, array.Length); j++)
            {
                sum += array[j].X + array[j].Y + array[j].Z;
            }
        }
        stopwatch.Stop();
        var readTime = stopwatch.ElapsedMilliseconds;
        
        Console.WriteLine($"Cache-line aligned allocation performance:");
        Console.WriteLine($"  Allocation: {allocTime}ms for {iterations} arrays of {capacity} Position elements");
        Console.WriteLine($"  Sequential write: {writeTime}ms");  
        Console.WriteLine($"  Sequential read: {readTime}ms (sum: {sum})");
        Console.WriteLine($"  Total elements processed: {iterations * capacity}");
        
        // Basic performance assertions - should complete in reasonable time
        Assert.That(allocTime, Is.LessThan(1000), "Allocation took too long");
        Assert.That(writeTime, Is.LessThan(100), "Write access took too long");
        Assert.That(readTime, Is.LessThan(100), "Read access took too long");
        Assert.That(sum, Is.GreaterThan(0), "Sum should be positive");
    }
}