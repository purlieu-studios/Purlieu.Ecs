using PurlieuEcs.Core;
using System.Numerics;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests to ensure SIMD operations don't introduce heap allocations.
/// </summary>
[TestFixture]
public class SIMD_AllocationTests
{
    private World _world = null!;

    [SetUp]
    public void SetUp()
    {
        _world = new World();
        _world.RegisterComponent<SimdTestComponent>();
        _world.RegisterComponent<NonSimdTestComponent>();
        
        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Test]
    public void SIMD_ComponentStorageCreation_NoExtraAllocations()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        long initialGen1 = GC.CollectionCount(1);
        long initialGen2 = GC.CollectionCount(2);
        
        // Create entities with SIMD-compatible components
        for (int i = 0; i < 1000; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimdTestComponent { Value = i * 1.5f });
        }
        
        long finalGen0 = GC.CollectionCount(0);
        long finalGen1 = GC.CollectionCount(1);
        long finalGen2 = GC.CollectionCount(2);
        
        // Allow some allocations for archetype creation but not excessive
        Assert.That(finalGen0 - initialGen0, Is.LessThanOrEqualTo(2), "Gen0 collections should be minimal");
        Assert.That(finalGen1 - initialGen1, Is.EqualTo(0), "No Gen1 collections should occur");
        Assert.That(finalGen2 - initialGen2, Is.EqualTo(0), "No Gen2 collections should occur");
    }

    [Test]
    public void SIMD_SpanOperations_NoHeapAllocation()
    {
        // Create test entities
        var entities = new Entity[500];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = _world.CreateEntity();
            _world.AddComponent(entities[i], new SimdTestComponent { Value = i * 2.0f });
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        
        var query = _world.Query().With<SimdTestComponent>();
        
        // Test SIMD span operations
        for (int iteration = 0; iteration < 100; iteration++)
        {
            foreach (var chunk in query.ChunksStack())
            {
                if (chunk.IsSimdSupported<SimdTestComponent>())
                {
                    var simdSpan = chunk.GetSimdSpan<SimdTestComponent>();
                    var remainderSpan = chunk.GetRemainderSpan<SimdTestComponent>();
                    
                    // Ensure spans are actually used
                    if (simdSpan.Length > 0)
                    {
                        var firstElement = simdSpan[0];
                    }
                    
                    if (remainderSpan.Length > 0)
                    {
                        var firstRemainder = remainderSpan[0];
                    }
                }
            }
        }
        
        long finalGen0 = GC.CollectionCount(0);
        
        Assert.That(finalGen0 - initialGen0, Is.EqualTo(0), "SIMD span operations should not allocate");
    }

    [Test]
    public void SIMD_VectorizedProcessing_NoHeapAllocation()
    {
        if (!Vector.IsHardwareAccelerated)
        {
            Assert.Inconclusive("SIMD not supported on this hardware");
            return;
        }
        
        // Create entities with SIMD components
        for (int i = 0; i < 1000; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimdTestComponent { Value = i * 0.5f });
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        
        var query = _world.Query().With<SimdTestComponent>();
        var processor = new TestVectorProcessor();
        
        // Test vectorized processing
        foreach (var chunk in query.ChunksStack())
        {
            chunk.ProcessVectorized(processor);
        }
        
        long finalGen0 = GC.CollectionCount(0);
        
        Assert.That(finalGen0 - initialGen0, Is.EqualTo(0), "Vectorized processing should not allocate");
    }

    [Test]
    public void SIMD_TransformVectorized_NoHeapAllocation()
    {
        if (!Vector.IsHardwareAccelerated)
        {
            Assert.Inconclusive("SIMD not supported on this hardware");
            return;
        }
        
        // Create entities
        for (int i = 0; i < 500; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimdTestComponent { Value = i * 1.0f });
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        long initialGen0 = GC.CollectionCount(0);
        
        var query = _world.Query().With<SimdTestComponent>();
        var multiplier = new Vector<float>(2.0f);
        
        // Test transform operations
        foreach (var chunk in query.ChunksStack())
        {
            chunk.TransformVectorized<float>(vector => vector * multiplier);
        }
        
        long finalGen0 = GC.CollectionCount(0);
        
        Assert.That(finalGen0 - initialGen0, Is.EqualTo(0), "Vector transforms should not allocate");
    }

    [Test]
    public void SIMD_FallbackPath_WorksCorrectly()
    {
        // Test with non-SIMD compatible component
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new NonSimdTestComponent { Flag = true });
        
        var query = _world.Query().With<NonSimdTestComponent>();
        
        foreach (var chunk in query.ChunksStack())
        {
            Assert.That(chunk.IsSimdSupported<NonSimdTestComponent>(), Is.False, 
                "Non-SIMD component should not report SIMD support");
            
            var span = chunk.GetSpan<NonSimdTestComponent>();
            var simdSpan = chunk.GetSimdSpan<NonSimdTestComponent>();
            var remainderSpan = chunk.GetRemainderSpan<NonSimdTestComponent>();
            
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(simdSpan.Length, Is.EqualTo(1), "Should fallback to regular span");
            Assert.That(remainderSpan.Length, Is.EqualTo(0), "No remainder for non-SIMD");
        }
    }
}

/// <summary>
/// SIMD-compatible test component (float is vectorizable).
/// </summary>
public struct SimdTestComponent
{
    public float Value;
}

/// <summary>
/// Non-SIMD compatible test component.
/// </summary>
public struct NonSimdTestComponent
{
    public bool Flag;
}

/// <summary>
/// Test implementation of VectorProcessor.
/// </summary>
public struct TestVectorProcessor : VectorProcessor<SimdTestComponent>
{
    public void ProcessSimd(Span<SimdTestComponent> simdSpan)
    {
        // Simple processing to ensure the span is used
        if (simdSpan.Length > 0)
        {
            var first = simdSpan[0];
            // Modify in place to ensure memory access
            for (int i = 0; i < simdSpan.Length; i++)
            {
                simdSpan[i].Value *= 1.1f;
            }
        }
    }
    
    public void ProcessScalar(Span<SimdTestComponent> scalarSpan)
    {
        // Scalar fallback processing
        for (int i = 0; i < scalarSpan.Length; i++)
        {
            scalarSpan[i].Value *= 1.1f;
        }
    }
}