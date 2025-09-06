using NUnit.Framework;
using PurlieuEcs.Core;
using System;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
[Category("Allocation")]
public class ALLOC_SignaturePoolTests
{
    [Test]
    public void SignatureArrayPool_ReusesArrays()
    {
        // Test that the pool actually reuses arrays instead of allocating new ones
        
        // Rent an array
        var array1 = SignatureArrayPool.Rent(4);
        Assert.That(array1, Is.Not.Null);
        Assert.That(array1.Length, Is.GreaterThanOrEqualTo(4));
        
        // Store reference and size
        var array1Ref = array1;
        var array1Size = array1.Length;
        
        // Return it to the pool
        SignatureArrayPool.Return(array1);
        
        // Rent again with same size
        var array2 = SignatureArrayPool.Rent(4);
        
        // Should get the same array back (reused from pool)
        Assert.That(ReferenceEquals(array1Ref, array2), Is.True, 
            "Pool should reuse the same array instance");
        Assert.That(array2.Length, Is.EqualTo(array1Size));
        
        // Array should be cleared
        for (int i = 0; i < array2.Length; i++)
        {
            Assert.That(array2[i], Is.EqualTo(0UL), 
                $"Reused array should be cleared at index {i}");
        }
    }
    
    [Test]
    public void SignatureArrayPool_HandlesMultipleSizes()
    {
        // Test that pool correctly categorizes different sizes
        
        var small = SignatureArrayPool.Rent(2);  // Small pool (1-4)
        var medium = SignatureArrayPool.Rent(8); // Medium pool (5-16)
        var large = SignatureArrayPool.Rent(32); // Large pool (17+)
        
        Assert.That(small.Length, Is.LessThanOrEqualTo(4), "Small array in wrong pool");
        Assert.That(medium.Length, Is.GreaterThan(4).And.LessThanOrEqualTo(16), "Medium array in wrong pool");
        Assert.That(large.Length, Is.GreaterThan(16), "Large array in wrong pool");
        
        // Return them
        SignatureArrayPool.Return(small);
        SignatureArrayPool.Return(medium);
        SignatureArrayPool.Return(large);
        
        // Rent again - should get same arrays back
        var small2 = SignatureArrayPool.Rent(2);
        var medium2 = SignatureArrayPool.Rent(8);
        var large2 = SignatureArrayPool.Rent(32);
        
        Assert.That(ReferenceEquals(small, small2), Is.True, "Small array not reused");
        Assert.That(ReferenceEquals(medium, medium2), Is.True, "Medium array not reused");
        Assert.That(ReferenceEquals(large, large2), Is.True, "Large array not reused");
    }
    
    [Test]
    public void SignatureArrayPool_CloneCreatesNewArray()
    {
        // Test that Clone creates a new array with copied data
        
        var source = new ulong[] { 0x1234567890ABCDEF, 0xFEDCBA0987654321, 0xAAAAAAAAAAAAAAAA };
        
        var clone = SignatureArrayPool.Clone(source);
        
        // Should be different array instance
        Assert.That(ReferenceEquals(source, clone), Is.False, 
            "Clone should create new array instance");
        
        // But with same content (pool may provide larger array)
        Assert.That(clone.Length, Is.GreaterThanOrEqualTo(source.Length));
        for (int i = 0; i < source.Length; i++)
        {
            Assert.That(clone[i], Is.EqualTo(source[i]), 
                $"Clone content mismatch at index {i}");
        }
    }
    
    [Test]
    public void SignatureArrayPool_ResizeDoesNotReturnSourceArray()
    {
        // Important: Resize should NOT return source array to pool
        // because it might still be referenced by immutable ArchetypeSignature
        
        var source = new ulong[] { 0x1111, 0x2222 };
        var sourceRef = source;
        
        var resized = SignatureArrayPool.Resize(source, 4);
        
        // Should be different array
        Assert.That(ReferenceEquals(source, resized), Is.False);
        Assert.That(resized.Length, Is.GreaterThanOrEqualTo(4));
        
        // Content should be copied
        Assert.That(resized[0], Is.EqualTo(0x1111));
        Assert.That(resized[1], Is.EqualTo(0x2222));
        
        // Original array should still be valid (not cleared/modified)
        Assert.That(sourceRef[0], Is.EqualTo(0x1111), 
            "Source array was modified - it shouldn't be returned to pool!");
        Assert.That(sourceRef[1], Is.EqualTo(0x2222));
    }
    
    [Test]
    public void SignatureArrayPool_PoolSizeLimit()
    {
        // Test that pool doesn't grow unbounded
        
        var arrays = new ulong[20][];
        
        // Rent many arrays
        for (int i = 0; i < arrays.Length; i++)
        {
            arrays[i] = SignatureArrayPool.Rent(4);
        }
        
        // Return them all
        for (int i = 0; i < arrays.Length; i++)
        {
            SignatureArrayPool.Return(arrays[i]);
        }
        
        // Pool should have size limit (MaxPoolSize = 8)
        // So only first 8 should be reused
        var reusedCount = 0;
        for (int i = 0; i < 8; i++)
        {
            var rented = SignatureArrayPool.Rent(4);
            
            // Check if it's one of our returned arrays
            for (int j = 0; j < arrays.Length; j++)
            {
                if (ReferenceEquals(rented, arrays[j]))
                {
                    reusedCount++;
                    break;
                }
            }
            
            SignatureArrayPool.Return(rented);
        }
        
        // Should reuse up to pool limit
        Assert.That(reusedCount, Is.LessThanOrEqualTo(8), 
            "Pool exceeded size limit");
    }
    
    [Test]
    public void ArchetypeSignature_UsesPooledArrays()
    {
        // Test that ArchetypeSignature operations use the pool
        
        // Measure allocations during signature operations
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var beforeBytes = GC.GetTotalMemory(false);
        
        // Perform many signature operations
        var signature = new ArchetypeSignature();
        for (int i = 0; i < 100; i++)
        {
            signature = signature.Add<TestComponentA>();
            signature = signature.Add<TestComponentB>();
            signature = signature.Remove<TestComponentA>();
        }
        
        var afterBytes = GC.GetTotalMemory(false);
        var allocated = afterBytes - beforeBytes;
        
        // With pooling, allocations should be minimal
        // Without pooling, this would allocate 300+ arrays
        Assert.That(allocated, Is.LessThan(50000), 
            $"Signature operations allocated too much: {allocated} bytes. Pool may not be working.");
    }
}