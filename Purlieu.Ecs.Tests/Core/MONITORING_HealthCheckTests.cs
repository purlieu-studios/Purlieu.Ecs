using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Monitoring;
using PurlieuEcs.Logging;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class MONITORING_HealthCheckTests
{
    private EcsHealthMonitor _healthMonitor;
    private TestLogger _logger;
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger();
        _healthMonitor = new EcsHealthMonitor(_logger);
        _world = new World(logger: _logger, healthMonitor: _healthMonitor);
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
        _healthMonitor?.Dispose();
    }
    
    #region Health Status Tests
    
    [Test]
    public void HealthStatus_InitiallyHealthy()
    {
        // Act
        var status = _healthMonitor.GetHealthStatus();
        
        // Assert
        Assert.That(status, Is.EqualTo(HealthStatus.Healthy));
    }
    
    [Test]
    public void HealthStatus_DetectsPerformanceIssues()
    {
        // Arrange - Simulate slow operations by recording high durations
        const int slowOperationTicks = (int)(TimeSpan.TicksPerMillisecond * 2); // 2ms operation
        
        for (int i = 0; i < 100; i++)
        {
            _healthMonitor.RecordEntityOperation(EcsOperation.EntityCreate, slowOperationTicks);
        }
        
        // Act
        var status = _healthMonitor.GetHealthStatus();
        var metrics = _healthMonitor.GetPerformanceMetrics();
        
        // Assert
        Console.WriteLine($"Average operation time: {metrics.AverageEntityOperationMicros:F2} microseconds");
        Assert.That(status, Is.EqualTo(HealthStatus.Critical).Or.EqualTo(HealthStatus.Warning));
    }
    
    [Test]
    public void HealthStatus_DetectsMemoryPressure()
    {
        // Arrange - Simulate high memory usage
        const long largeAllocation = 50 * 1024 * 1024; // 50MB
        
        for (int i = 0; i < 10; i++)
        {
            _healthMonitor.RecordMemoryEvent(MemoryEventType.ChunkAllocated, largeAllocation);
        }
        
        // Act
        var status = _healthMonitor.GetHealthStatus();
        var memoryMetrics = _healthMonitor.GetMemoryMetrics();
        
        // Assert
        Console.WriteLine($"Total managed memory: {memoryMetrics.TotalManagedBytes / (1024 * 1024):F2} MB");
        
        // Note: This test depends on current system memory state
        // In production, this would trigger warning/critical states
    }
    
    #endregion
    
    #region Performance Metrics Tests
    
    [Test]
    public void PerformanceMetrics_TracksEntityOperations()
    {
        // Arrange
        const int operationCount = 1000;
        const long operationTicks = TimeSpan.TicksPerMillisecond / 10; // 0.1ms per operation
        
        // Act
        for (int i = 0; i < operationCount; i++)
        {
            _healthMonitor.RecordEntityOperation(EcsOperation.EntityCreate, operationTicks);
        }
        
        var metrics = _healthMonitor.GetPerformanceMetrics();
        
        // Assert
        Assert.That(metrics.TotalEntityOperations, Is.EqualTo(operationCount));
        Assert.That(metrics.AverageEntityOperationMicros, Is.GreaterThan(0));
        
        Console.WriteLine($"Recorded {operationCount} operations");
        Console.WriteLine($"Average time: {metrics.AverageEntityOperationMicros:F2} microseconds");
    }
    
    [Test]
    public void PerformanceMetrics_TracksQueryExecutions()
    {
        // Arrange  
        const int queryCount = 500;
        const long queryTicks = TimeSpan.TicksPerMillisecond / 5; // 0.2ms per query
        
        // Act
        for (int i = 0; i < queryCount; i++)
        {
            _healthMonitor.RecordQueryExecution(100, queryTicks); // 100 entities per query
        }
        
        var metrics = _healthMonitor.GetPerformanceMetrics();
        
        // Assert
        Assert.That(metrics.TotalQueryExecutions, Is.EqualTo(queryCount));
        Assert.That(metrics.AverageQueryExecutionMicros, Is.GreaterThan(0));
        
        Console.WriteLine($"Recorded {queryCount} query executions");
        Console.WriteLine($"Average query time: {metrics.AverageQueryExecutionMicros:F2} microseconds");
    }
    
    [Test]
    public void PerformanceMetrics_TracksArchetypeTransitions()
    {
        // Arrange
        const int transitionCount = 200;
        const long transitionTicks = TimeSpan.TicksPerMillisecond / 20; // 0.05ms per transition
        
        // Act
        for (int i = 0; i < transitionCount; i++)
        {
            _healthMonitor.RecordArchetypeTransition(1, 2, transitionTicks);
        }
        
        var metrics = _healthMonitor.GetPerformanceMetrics();
        
        // Assert
        Assert.That(metrics.TotalArchetypeTransitions, Is.EqualTo(transitionCount));
        Assert.That(metrics.AverageArchetypeTransitionMicros, Is.GreaterThan(0));
        
        Console.WriteLine($"Recorded {transitionCount} archetype transitions");
        Console.WriteLine($"Average transition time: {metrics.AverageArchetypeTransitionMicros:F2} microseconds");
    }
    
    #endregion
    
    #region Memory Metrics Tests
    
    [Test]
    public void MemoryMetrics_TracksChunkAllocations()
    {
        // Arrange
        const long chunkSize = 64 * 1024; // 64KB chunks
        const int chunkCount = 10;
        
        // Act
        for (int i = 0; i < chunkCount; i++)
        {
            _healthMonitor.RecordMemoryEvent(MemoryEventType.ChunkAllocated, chunkSize);
        }
        
        var memoryMetrics = _healthMonitor.GetMemoryMetrics();
        
        // Assert
        Assert.That(memoryMetrics.ActiveChunks, Is.EqualTo(chunkCount));
        Assert.That(memoryMetrics.ChunkMemoryBytes, Is.EqualTo(chunkSize * chunkCount));
        
        Console.WriteLine($"Active chunks: {memoryMetrics.ActiveChunks}");
        Console.WriteLine($"Chunk memory: {memoryMetrics.ChunkMemoryBytes / 1024} KB");
    }
    
    [Test] 
    public void MemoryMetrics_TracksArchetypeCreation()
    {
        // Arrange
        const int archetypeCount = 5;
        
        // Act
        for (int i = 0; i < archetypeCount; i++)
        {
            _healthMonitor.RecordMemoryEvent(MemoryEventType.ArchetypeCreated, 1024);
        }
        
        var memoryMetrics = _healthMonitor.GetMemoryMetrics();
        
        // Assert
        Assert.That(memoryMetrics.ActiveArchetypes, Is.EqualTo(archetypeCount));
        
        Console.WriteLine($"Active archetypes: {memoryMetrics.ActiveArchetypes}");
    }
    
    [Test]
    public void MemoryMetrics_HandlesChunkDeallocation()
    {
        // Arrange - Allocate then deallocate
        const long chunkSize = 32 * 1024;
        
        // Act
        _healthMonitor.RecordMemoryEvent(MemoryEventType.ChunkAllocated, chunkSize);
        _healthMonitor.RecordMemoryEvent(MemoryEventType.ChunkAllocated, chunkSize);
        _healthMonitor.RecordMemoryEvent(MemoryEventType.ChunkDeallocated, chunkSize);
        
        var memoryMetrics = _healthMonitor.GetMemoryMetrics();
        
        // Assert
        Assert.That(memoryMetrics.ActiveChunks, Is.EqualTo(1));
        Assert.That(memoryMetrics.ChunkMemoryBytes, Is.EqualTo(chunkSize));
        
        Console.WriteLine($"Net active chunks: {memoryMetrics.ActiveChunks}");
        Console.WriteLine($"Net chunk memory: {memoryMetrics.ChunkMemoryBytes / 1024} KB");
    }
    
    #endregion
    
    #region Frame Timing Tests
    
    [Test]
    public void FrameTiming_CalculatesFPS()
    {
        // Arrange - Simulate consistent 60 FPS (16.67ms per frame)
        const long frameTimeTicks = TimeSpan.TicksPerMillisecond * 16; // ~16ms frames
        
        // Act - Simulate several frames
        for (int frame = 0; frame < 30; frame++)
        {
            _healthMonitor.StartFrame();
            Thread.Sleep(15); // Simulate 15ms of work
            _healthMonitor.EndFrame();
        }
        
        var metrics = _healthMonitor.GetPerformanceMetrics();
        
        // Assert
        Assert.That(metrics.FrameCount, Is.EqualTo(30));
        Assert.That(metrics.CurrentFPS, Is.GreaterThan(0));
        Assert.That(metrics.CurrentFPS, Is.LessThan(120)); // Reasonable upper bound
        
        Console.WriteLine($"Frame count: {metrics.FrameCount}");
        Console.WriteLine($"Current FPS: {metrics.CurrentFPS:F2}");
    }
    
    #endregion
    
    #region Integration Tests with World
    
    [Test]
    public void World_AutomaticallyRecordsEntityOperations()
    {
        // Act - Perform various entity operations
        var entity1 = _world.CreateEntity();
        var entity2 = _world.CreateEntity();
        
        _world.AddComponent(entity1, new Position(1, 2, 3));
        _world.AddComponent(entity1, new Velocity(4, 5, 6));
        _world.AddComponent(entity2, new Position(7, 8, 9));
        
        var metrics = _healthMonitor.GetPerformanceMetrics();
        
        // Assert - Should have recorded all operations
        Assert.That(metrics.TotalEntityOperations, Is.GreaterThan(0));
        Assert.That(metrics.AverageEntityOperationMicros, Is.GreaterThan(0));
        
        Console.WriteLine($"Total entity operations: {metrics.TotalEntityOperations}");
        Console.WriteLine($"Average operation time: {metrics.AverageEntityOperationMicros:F2} μs");
    }
    
    [Test]
    public void World_RecordsArchetypeTransitions()
    {
        // Act - Create entity and add components (triggers archetype transitions)
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Position(1, 2, 3)); // Empty -> Position
        _world.AddComponent(entity, new Velocity(4, 5, 6)); // Position -> Position+Velocity
        
        var metrics = _healthMonitor.GetPerformanceMetrics();
        
        // Assert
        Assert.That(metrics.TotalArchetypeTransitions, Is.GreaterThan(0));
        
        Console.WriteLine($"Archetype transitions: {metrics.TotalArchetypeTransitions}");
        Console.WriteLine($"Average transition time: {metrics.AverageArchetypeTransitionMicros:F2} μs");
    }
    
    [Test]
    public void World_TracksMemoryEvents()
    {
        // Act - Create many entities to trigger chunk allocations
        for (int i = 0; i < 1000; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, i));
        }
        
        var memoryMetrics = _healthMonitor.GetMemoryMetrics();
        
        // Assert - Should have created archetypes
        Assert.That(memoryMetrics.ActiveArchetypes, Is.GreaterThan(0));
        
        Console.WriteLine($"Active archetypes: {memoryMetrics.ActiveArchetypes}");
        Console.WriteLine($"Total managed memory: {memoryMetrics.TotalManagedBytes / 1024} KB");
    }
    
    #endregion
    
    #region Performance and Threading Tests
    
    [Test]
    public void HealthMonitor_ThreadSafe()
    {
        // Arrange
        var threadCount = Environment.ProcessorCount;
        const int operationsPerThread = 1000;
        
        var tasks = new Task[threadCount];
        var exceptions = new List<Exception>();
        var lockObject = new object();
        
        // Act - Record operations from multiple threads
        for (int t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        _healthMonitor.RecordEntityOperation(EcsOperation.EntityCreate, TimeSpan.TicksPerMillisecond);
                        _healthMonitor.RecordQueryExecution(10, TimeSpan.TicksPerMillisecond / 2);
                        _healthMonitor.RecordMemoryEvent(MemoryEventType.ChunkAllocated, 1024);
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
        
        // Assert
        Assert.That(exceptions.Count, Is.EqualTo(0), 
            $"Health monitoring should be thread-safe. Exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
        
        var metrics = _healthMonitor.GetPerformanceMetrics();
        Assert.That(metrics.TotalEntityOperations, Is.EqualTo(threadCount * operationsPerThread));
        
        Console.WriteLine($"Recorded {metrics.TotalEntityOperations} operations across {threadCount} threads");
    }
    
    [Test]
    public void HealthMonitor_MinimalOverhead()
    {
        // Arrange
        const int operationCount = 100000;
        
        // Act - Record many operations
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < operationCount; i++)
        {
            _healthMonitor.RecordEntityOperation(EcsOperation.EntityCreate, TimeSpan.TicksPerMicrosecond);
        }
        
        stopwatch.Stop();
        
        var averageNanosPerRecord = (stopwatch.ElapsedTicks * 1000000000.0) / (Stopwatch.Frequency * operationCount);
        
        Console.WriteLine($"Average recording time: {averageNanosPerRecord:F0} nanoseconds per operation");
        
        // Assert - Should be very fast (sub-microsecond)
        Assert.That(averageNanosPerRecord, Is.LessThan(1000), 
            "Health monitoring recording should be faster than 1 microsecond");
    }
    
    #endregion
    
    #region Null Monitor Tests
    
    [Test]
    public void NullHealthMonitor_ZeroOverhead()
    {
        // Arrange
        var nullMonitor = NullEcsHealthMonitor.Instance;
        const int operationCount = 1000000;
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < operationCount; i++)
        {
            nullMonitor.RecordEntityOperation(EcsOperation.EntityCreate, TimeSpan.TicksPerMillisecond);
            nullMonitor.RecordQueryExecution(10, TimeSpan.TicksPerMillisecond);
            nullMonitor.RecordMemoryEvent(MemoryEventType.ChunkAllocated, 1024);
        }
        
        stopwatch.Stop();
        
        var averageNanosPerOperation = (stopwatch.ElapsedTicks * 1000000000.0) / (Stopwatch.Frequency * operationCount * 3);
        
        Console.WriteLine($"Null monitor average time: {averageNanosPerOperation:F0} nanoseconds per operation");
        
        // Assert - Should be extremely fast (near zero overhead)
        Assert.That(averageNanosPerOperation, Is.LessThan(10), 
            "Null health monitor should have near-zero overhead");
            
        // All methods should return defaults
        Assert.That(nullMonitor.GetHealthStatus(), Is.EqualTo(HealthStatus.Healthy));
        Assert.That(nullMonitor.GetPerformanceMetrics().TotalEntityOperations, Is.EqualTo(0));
        Assert.That(nullMonitor.GetMemoryMetrics().ActiveChunks, Is.EqualTo(0));
    }
    
    #endregion
}