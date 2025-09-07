using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Validation;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class VALIDATION_PerformanceTests
{
    private World _worldWithValidation;
    private World _worldWithoutValidation; 
    
    [SetUp]
    public void SetUp()
    {
        _worldWithValidation = new World(validator: new EcsValidator());
        _worldWithoutValidation = new World(validator: NullEcsValidator.Instance);
    }
    
    [TearDown]
    public void TearDown()
    {
        _worldWithValidation?.Dispose();
        _worldWithoutValidation?.Dispose();
    }
    
    [Test]
    public void ValidationFramework_ZeroOverheadInRelease()
    {
        const int entityCount = 10000;
        const int iterations = 100;
        
        // Warm up
        BenchmarkEntityOperations(_worldWithValidation, 100);
        BenchmarkEntityOperations(_worldWithoutValidation, 100);
        
        // Measure with validation
        var stopwatchWithValidation = Stopwatch.StartNew();
        BenchmarkEntityOperations(_worldWithValidation, entityCount);
        stopwatchWithValidation.Stop();
        
        // Measure without validation  
        var stopwatchWithoutValidation = Stopwatch.StartNew();
        BenchmarkEntityOperations(_worldWithoutValidation, entityCount);
        stopwatchWithoutValidation.Stop();
        
        var withValidationTime = stopwatchWithValidation.ElapsedMilliseconds;
        var withoutValidationTime = stopwatchWithoutValidation.ElapsedMilliseconds;
        
        Console.WriteLine($"With validation: {withValidationTime}ms");
        Console.WriteLine($"Without validation: {withoutValidationTime}ms");
        
        #if DEBUG
            // In debug builds, validation adds overhead but should be reasonable
            var overhead = (double)withValidationTime / withoutValidationTime;
            Assert.That(overhead, Is.LessThan(3.0), 
                "Validation overhead should be less than 3x in debug builds");
        #else
            // In release builds, validation should compile away to near-zero overhead
            var overhead = (double)withValidationTime / withoutValidationTime;
            Assert.That(overhead, Is.LessThan(1.2), 
                "Validation should have minimal overhead in release builds");
        #endif
    }
    
    [Test]
    public void ValidationCaching_HighPerformance()
    {
        var validator = new EcsValidator();
        const int iterations = 100000;
        
        // First call (cache miss)
        var stopwatchFirstCall = Stopwatch.StartNew();
        validator.ValidateComponentType<Position>();
        stopwatchFirstCall.Stop();
        
        // Subsequent calls (cache hits)
        var stopwatchCachedCalls = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            validator.ValidateComponentType<Position>();
        }
        stopwatchCachedCalls.Stop();
        
        var averageNanosPerCachedCall = (stopwatchCachedCalls.ElapsedTicks * 1000000000.0) / (Stopwatch.Frequency * iterations);
        
        Console.WriteLine($"First call: {stopwatchFirstCall.ElapsedTicks * 1000000000.0 / Stopwatch.Frequency:F0} ns");
        Console.WriteLine($"Cached calls average: {averageNanosPerCachedCall:F0} ns/call");
        
        // Cached calls should be extremely fast (sub-microsecond)
        Assert.That(averageNanosPerCachedCall, Is.LessThan(1000), 
            "Cached validation calls should be faster than 1 microsecond");
    }
    
    [Test]
    public void ValidationMemoryUsage_Minimal()
    {
        // Test that validation doesn't cause excessive allocations
        var validator = new EcsValidator();
        
        // Force garbage collection to get clean baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        
        // Perform many validation operations
        for (int i = 0; i < 10000; i++)
        {
            validator.ValidateComponentType<Position>();
            validator.ValidateComponentType<Velocity>();
            validator.ValidateEntityOperation(EntityOperation.AddComponent, (uint)i, typeof(Position));
            validator.ValidateArchetypeTransition(new[] { typeof(Position) }, new[] { typeof(Position), typeof(Velocity) });
        }
        
        var finalMemory = GC.GetTotalMemory(false);
        var allocatedBytes = finalMemory - initialMemory;
        
        Console.WriteLine($"Memory allocated during validation: {allocatedBytes} bytes");
        
        // Should allocate very little due to caching
        Assert.That(allocatedBytes, Is.LessThan(10000), 
            "Validation should not cause excessive allocations due to caching");
    }
    
    [Test]
    public void ValidationThreadSafety_HighConcurrency()
    {
        var validator = new EcsValidator();
        const int threadCount = Environment.ProcessorCount;
        const int operationsPerThread = 1000;
        
        var tasks = new Task[threadCount];
        var exceptions = new List<Exception>();
        var lockObject = new object();
        
        for (int t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        // Mix of cached and uncached operations
                        validator.ValidateComponentType<Position>();
                        validator.ValidateComponentType<Velocity>();
                        
                        if (i % 10 == 0)
                        {
                            // Occasional archetype transition validation
                            validator.ValidateArchetypeTransition(
                                new[] { typeof(Position) }, 
                                new[] { typeof(Position), typeof(Velocity) }
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObject)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        // Assert no exceptions occurred
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Validation should be thread-safe. Exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        Console.WriteLine($"Successfully validated {threadCount * operationsPerThread} operations across {threadCount} threads");
    }
    
    private void BenchmarkEntityOperations(World world, int entityCount)
    {
        var entities = new Entity[entityCount];
        
        // Create entities
        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = world.CreateEntity();
        }
        
        // Add components
        for (int i = 0; i < entityCount; i++)
        {
            world.AddComponent(entities[i], new Position(i, i, i));
            world.AddComponent(entities[i], new Velocity(i % 10, i % 10, i % 10));
        }
        
        // Access components
        for (int i = 0; i < entityCount; i++)
        {
            var pos = world.GetComponent<Position>(entities[i]);
            var vel = world.GetComponent<Velocity>(entities[i]);
        }
        
        // Clean up
        for (int i = 0; i < entityCount; i++)
        {
            world.DestroyEntity(entities[i]);
        }
    }
}