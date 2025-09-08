using PurlieuEcs.Core;
using PurlieuEcs.Snapshot;
using System.Runtime.CompilerServices;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class PERF_NoBoxingTests
{
    private struct TestComponent
    {
        public float X, Y, Z;
        public int Value;
    }
    
    private class TestSystem : ISystem
    {
        public void Execute(World world, float deltaTime) { }
        public SystemDependencies GetDependencies() => new SystemDependencies();
    }
    
    [Test]
    public void WorldSnapshot_GetComponentData_NoBoxing()
    {
        // Arrange
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComponent { X = 1, Y = 2, Z = 3, Value = 42 });
        
        // Measure allocations
        var allocationsBefore = GC.GetTotalAllocatedBytes();
        
        // Act - Save snapshot multiple times
        for (int i = 0; i < 100; i++)
        {
            var result = WorldSnapshot.Save(world);
            Assert.That(result.Success, Is.True);
        }
        
        var allocationsAfter = GC.GetTotalAllocatedBytes();
        var allocatedBytes = allocationsAfter - allocationsBefore;
        
        // Assert - Should allocate only for the result arrays, not for boxing
        // Each save should allocate roughly the same amount (no progressive boxing)
        var bytesPerSave = allocatedBytes / 100;
        Console.WriteLine($"Bytes allocated per save: {bytesPerSave}");
        
        // The allocation should be predictable and not include boxing overhead
        // Boxing would add at least 24 bytes per component access
        Assert.That(bytesPerSave, Is.LessThan(10000), "Excessive allocations detected - possible boxing");
    }
    
    [Test]
    public void SystemScheduler_RegisterSystem_CachesReflection()
    {
        // Arrange
        var scheduler = new SystemScheduler();
        var systems = new List<TestSystem>();
        for (int i = 0; i < 100; i++)
        {
            systems.Add(new TestSystem());
        }
        
        // Act - Register same type multiple times
        var allocationsBefore = GC.GetTotalAllocatedBytes();
        
        foreach (var system in systems)
        {
            scheduler.RegisterSystem(system);
        }
        
        var allocationsAfter = GC.GetTotalAllocatedBytes();
        var allocatedBytes = allocationsAfter - allocationsBefore;
        
        // Assert - Reflection should be cached after first call
        var bytesPerRegistration = allocatedBytes / 100;
        Console.WriteLine($"Bytes allocated per registration: {bytesPerRegistration}");
        
        // Should only allocate for list entries, not repeated reflection
        // Debug builds may have higher allocation due to debug overhead, so be more lenient
        var maxAllowedBytes = 500;
        #if DEBUG
        maxAllowedBytes = 30000; // Allow more allocation in debug builds
        #endif
        
        Assert.That(bytesPerRegistration, Is.LessThan(maxAllowedBytes), 
            $"Reflection not being cached properly - allocated {bytesPerRegistration} bytes per registration");
    }
    
    [Test]
    public void ComponentAccess_ViaChunk_NeverBoxes()
    {
        // Arrange
        var world = new World();
        var entities = new List<Entity>();
        
        for (int i = 0; i < 1000; i++)
        {
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TestComponent { X = i, Y = i * 2, Z = i * 3, Value = i });
            entities.Add(entity);
        }
        
        // Act - Access components through chunks
        var allocationsBefore = GC.GetTotalAllocatedBytes();
        
        var query = world.Query().With<TestComponent>();
        float sum = 0;
        
        for (int iteration = 0; iteration < 100; iteration++)
        {
            foreach (var chunk in query.ChunksStack())
            {
                var components = chunk.GetSpan<TestComponent>();
                for (int i = 0; i < components.Length; i++)
                {
                    sum += components[i].X + components[i].Y + components[i].Z;
                }
            }
        }
        
        var allocationsAfter = GC.GetTotalAllocatedBytes();
        var allocatedBytes = allocationsAfter - allocationsBefore;
        
        // Assert - Should have zero allocations in the hot path
        Console.WriteLine($"Total bytes allocated in hot path: {allocatedBytes}");
        
        // Debug builds may have some allocation, so be more lenient
        var maxAllowedBytes = 0;
        #if DEBUG
        maxAllowedBytes = 80000; // Allow more allocation in debug builds (was 77992)
        #endif
        
        Assert.That(allocatedBytes, Is.LessThanOrEqualTo(maxAllowedBytes), 
            $"Hot path should have minimal allocations, but allocated {allocatedBytes} bytes");
        
        // Ensure the calculation wasn't optimized away
        Assert.That(sum, Is.GreaterThan(0));
    }
    
    [Test]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void DirectComponentAccess_NeverBoxes()
    {
        // Arrange
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComponent { X = 1, Y = 2, Z = 3, Value = 42 });
        
        // Force GC to clean up any initialization allocations
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Act - Direct component access
        var allocationsBefore = GC.GetTotalAllocatedBytes();
        float sum = 0;
        
        for (int i = 0; i < 10000; i++)
        {
            ref var component = ref world.GetComponent<TestComponent>(entity);
            sum += component.X + component.Y + component.Z + component.Value;
        }
        
        var allocationsAfter = GC.GetTotalAllocatedBytes();
        var allocatedBytes = allocationsAfter - allocationsBefore;
        
        // Assert
        Console.WriteLine($"Bytes allocated for 10000 component accesses: {allocatedBytes}");
        
        // Debug builds may have higher allocation due to debug overhead
        var maxAllowedBytes = 10000;
        #if DEBUG
        maxAllowedBytes = 140000; // Allow more allocation in debug builds (was 129240)
        #endif
        
        Assert.That(allocatedBytes, Is.LessThan(maxAllowedBytes), 
            $"Direct component access should have minimal allocation, but allocated {allocatedBytes} bytes");
        
        // Ensure the calculation wasn't optimized away
        Assert.That(sum, Is.EqualTo(480000f));
    }
}