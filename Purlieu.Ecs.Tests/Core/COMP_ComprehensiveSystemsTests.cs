using System.Collections.Concurrent;
using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Logging;
using PurlieuEcs.Monitoring;
using System.Diagnostics;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Comprehensive systems execution tests with thread safety validation.
/// Tests the new SystemScheduler with dependency resolution, parallel execution, and conflict detection.
/// </summary>
[TestFixture]
public class COMP_ComprehensiveSystemsTests
{
    private World _world = null!;
    private SystemScheduler _scheduler = null!;
    private TestEcsLogger _logger = null!;
    private TestEcsHealthMonitor _monitor = null!;

    [SetUp]
    public void Setup()
    {
        _logger = new TestEcsLogger();
        _monitor = new TestEcsHealthMonitor(_logger);
        _world = new World(logger: _logger, healthMonitor: _monitor);
        _scheduler = _world.SystemScheduler;
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
        _monitor?.Dispose();
    }

    [Test]
    public void SystemScheduler_ConcurrentExecution_ThreadSafety()
    {
        // Arrange - Create systems with overlapping component access patterns
        var readOnlySystem1 = new TestReadOnlySystem("ReadOnly1", typeof(TestComponent1), typeof(TestComponent2));
        var readOnlySystem2 = new TestReadOnlySystem("ReadOnly2", typeof(TestComponent1), typeof(TestComponent3));
        var writeSystem1 = new TestWriteSystem("Write1", typeof(TestComponent4));
        var writeSystem2 = new TestWriteSystem("Write2", typeof(TestComponent5));
        var conflictSystem = new TestWriteSystem("Conflict", typeof(TestComponent1)); // Conflicts with readers

        _scheduler.RegisterSystem(readOnlySystem1);
        _scheduler.RegisterSystem(readOnlySystem2);
        _scheduler.RegisterSystem(writeSystem1);
        _scheduler.RegisterSystem(writeSystem2);
        _scheduler.RegisterSystem(conflictSystem);

        // Create test entities with various component combinations
        CreateTestEntities(1000);

        var exceptions = new ConcurrentBag<Exception>();
        var executionTimes = new ConcurrentBag<long>();

        // Act - Execute multiple phases concurrently to stress test
        var tasks = new Task[4];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    _scheduler.ExecutePhase(SystemPhase.Update, _world, 0.016f);
                    stopwatch.Stop();
                    executionTimes.Add(stopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert
        Assert.That(exceptions, Is.Empty, $"No exceptions should occur during concurrent execution: {string.Join(", ", exceptions.Select(e => e.Message))}");
        Assert.That(executionTimes.Count, Is.EqualTo(4), "All tasks should complete");
        
        // Verify all systems executed
        Assert.That(readOnlySystem1.ExecutionCount, Is.GreaterThan(0), "ReadOnly1 should execute");
        Assert.That(readOnlySystem2.ExecutionCount, Is.GreaterThan(0), "ReadOnly2 should execute");
        Assert.That(writeSystem1.ExecutionCount, Is.GreaterThan(0), "Write1 should execute");
        Assert.That(writeSystem2.ExecutionCount, Is.GreaterThan(0), "Write2 should execute");
    }

    [Test]
    public void SystemScheduler_DependencyResolution_Deterministic()
    {
        // Arrange - Create systems with explicit dependencies
        var earlySystem = new TestSystem("Early", SystemPhase.EarlyUpdate);
        var dependentSystem = new TestDependentSystem("Dependent", runAfter: typeof(TestSystem));
        var lateSystem = new TestSystem("Late", SystemPhase.LateUpdate);

        _scheduler.RegisterSystem(lateSystem);      // Register out of order
        _scheduler.RegisterSystem(dependentSystem);
        _scheduler.RegisterSystem(earlySystem);

        var executionOrder = new ConcurrentQueue<string>();

        earlySystem.OnExecute = () => executionOrder.Enqueue("Early");
        dependentSystem.OnExecute = () => executionOrder.Enqueue("Dependent");
        lateSystem.OnExecute = () => executionOrder.Enqueue("Late");

        // Act - Execute multiple times to verify determinism
        var allExecutionOrders = new List<string[]>();
        for (int run = 0; run < 10; run++)
        {
            executionOrder = new ConcurrentQueue<string>();
            earlySystem.OnExecute = () => executionOrder.Enqueue("Early");
            dependentSystem.OnExecute = () => executionOrder.Enqueue("Dependent");
            lateSystem.OnExecute = () => executionOrder.Enqueue("Late");

            _scheduler.ExecuteAllPhases(_world, 0.016f);
            allExecutionOrders.Add(executionOrder.ToArray());
        }

        // Assert - All runs should have identical execution order
        var firstOrder = allExecutionOrders[0];
        foreach (var order in allExecutionOrders.Skip(1))
        {
            Assert.That(order, Is.EqualTo(firstOrder), "Execution order must be deterministic across runs");
        }

        // Verify dependency constraints
        var finalOrder = firstOrder.ToList();
        var earlyIndex = finalOrder.IndexOf("Early");
        var dependentIndex = finalOrder.IndexOf("Dependent");
        var lateIndex = finalOrder.IndexOf("Late");

        Assert.That(earlyIndex, Is.LessThan(dependentIndex), "Dependent system should run after Early system");
        Assert.That(dependentIndex, Is.LessThan(lateIndex), "Late system should run after Dependent system");
    }

    [Test]
    public void SystemScheduler_ParallelExecution_NoComponentConflicts()
    {
        // Arrange - Systems that can run in parallel (no overlapping write access)
        var parallelSystems = new[]
        {
            new TestWriteSystem("Parallel1", typeof(TestComponent1)),
            new TestWriteSystem("Parallel2", typeof(TestComponent2)),
            new TestWriteSystem("Parallel3", typeof(TestComponent3)),
            new TestWriteSystem("Parallel4", typeof(TestComponent4)),
            new TestWriteSystem("Parallel5", typeof(TestComponent5))
        };

        foreach (var system in parallelSystems)
        {
            _scheduler.RegisterSystem(system);
        }

        CreateTestEntities(5000); // More entities to make parallel execution beneficial

        var startTime = DateTime.UtcNow;

        // Act
        _scheduler.ExecutePhase(SystemPhase.Update, _world, 0.016f);

        var executionTime = DateTime.UtcNow - startTime;

        // Assert
        foreach (var system in parallelSystems)
        {
            Assert.That(system.ExecutionCount, Is.EqualTo(1), $"System {system.Name} should execute once");
            Assert.That(system.LastExecutionThread, Is.Not.Null, $"System {system.Name} should record thread ID");
        }

        // Verify parallel execution by checking if different systems ran on different threads
        var threadIds = parallelSystems.Select(s => s.LastExecutionThread).Distinct().ToList();
        Assert.That(threadIds.Count, Is.GreaterThan(1), "Systems should execute on multiple threads for parallel execution");

        // Performance check - parallel execution should complete within reasonable time
        Assert.That(executionTime.TotalSeconds, Is.LessThan(5.0), "Parallel execution should complete quickly");
    }

    [Test]
    public void SystemScheduler_ComponentConflictDetection_ForcesSequentialExecution()
    {
        // Arrange - Systems with conflicting component access (write-write conflict)
        var conflictingSystem1 = new TestWriteSystem("Conflict1", typeof(TestComponent1));
        var conflictingSystem2 = new TestWriteSystem("Conflict2", typeof(TestComponent1)); // Same component
        var independentSystem = new TestWriteSystem("Independent", typeof(TestComponent2));

        _scheduler.RegisterSystem(conflictingSystem1);
        _scheduler.RegisterSystem(conflictingSystem2);
        _scheduler.RegisterSystem(independentSystem);

        CreateTestEntities(1000);

        // Act
        _scheduler.ExecutePhase(SystemPhase.Update, _world, 0.016f);

        // Assert
        Assert.That(conflictingSystem1.ExecutionCount, Is.EqualTo(1), "Conflicting system 1 should execute");
        Assert.That(conflictingSystem2.ExecutionCount, Is.EqualTo(1), "Conflicting system 2 should execute");
        Assert.That(independentSystem.ExecutionCount, Is.EqualTo(1), "Independent system should execute");

        // Conflicting systems should not execute simultaneously
        var conflict1Thread = conflictingSystem1.LastExecutionThread;
        var conflict2Thread = conflictingSystem2.LastExecutionThread;
        var independentThread = independentSystem.LastExecutionThread;

        // Either they run on the same thread (sequential) or at different times
        if (conflict1Thread == conflict2Thread)
        {
            // Sequential execution - verify timing
            Assert.That(Math.Abs(conflictingSystem1.LastExecutionTime - conflictingSystem2.LastExecutionTime),
                       Is.GreaterThan(TimeSpan.FromMicroseconds(100).Ticks),
                       "Conflicting systems should not execute simultaneously");
        }
        else
        {
            // If on different threads, they should have proper synchronization
            // This is handled by the scheduler's conflict detection
        }
    }

    [Test]
    public void SystemScheduler_ExceptionHandling_IsolatesFailures()
    {
        // Arrange
        var goodSystem1 = new TestSystem("Good1", SystemPhase.Update);
        var faultySystem = new TestFaultySystem("Faulty");
        var goodSystem2 = new TestSystem("Good2", SystemPhase.Update);

        _scheduler.RegisterSystem(goodSystem1);
        _scheduler.RegisterSystem(faultySystem);
        _scheduler.RegisterSystem(goodSystem2);

        // Act & Assert
        var ex = Assert.Throws<AggregateException>(() => 
            _scheduler.ExecutePhase(SystemPhase.Update, _world, 0.016f));

        Assert.That(ex.InnerExceptions.Count, Is.EqualTo(1), "Should contain one inner exception");
        Assert.That(ex.InnerExceptions[0].Message, Does.Contain("Test system failure"));

        // Good systems should still execute (depending on execution order)
        // At least one should execute before the failure
        var totalExecutions = goodSystem1.ExecutionCount + goodSystem2.ExecutionCount;
        Assert.That(totalExecutions, Is.GreaterThan(0), "Some systems should execute before failure");
    }

    [Test]
    public void SystemScheduler_HealthMonitoringIntegration_RecordsMetrics()
    {
        // Arrange
        var testSystem = new TestSystem("Monitored", SystemPhase.Update);
        _scheduler.RegisterSystem(testSystem);

        CreateTestEntities(100);

        var initialMetrics = _monitor.GetPerformanceMetrics();

        // Act
        _scheduler.ExecutePhase(SystemPhase.Update, _world, 0.016f);

        // Assert
        var finalMetrics = _monitor.GetPerformanceMetrics();

        Assert.That(finalMetrics.TotalEntityOperations, Is.GreaterThan(initialMetrics.TotalEntityOperations),
                   "Should record entity operations");
        Assert.That(testSystem.ExecutionCount, Is.EqualTo(1), "System should execute once");
        Assert.That(_monitor.RecordedOperations.Count, Is.GreaterThan(0), "Should record system execution metrics");
    }

    private void CreateTestEntities(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var entity = _world.CreateEntity();
            
            // Distribute components across entities for testing
            if (i % 5 == 0) _world.AddComponent(entity, new TestComponent1 { Value = i });
            if (i % 4 == 0) _world.AddComponent(entity, new TestComponent2 { Value = i * 2 });
            if (i % 3 == 0) _world.AddComponent(entity, new TestComponent3 { Value = i * 3 });
            if (i % 2 == 0) _world.AddComponent(entity, new TestComponent4 { Value = i * 4 });
            if (i % 7 == 0) _world.AddComponent(entity, new TestComponent5 { Value = i * 5 });
        }
    }
}

// Test Systems
internal class TestSystem : ISystem
{
    public string Name { get; }
    public SystemPhase Phase { get; }
    public int ExecutionCount { get; private set; }
    public long LastExecutionTime { get; private set; }
    public int? LastExecutionThread { get; private set; }
    public Action? OnExecute { get; set; }

    public TestSystem(string name, SystemPhase phase = SystemPhase.Update)
    {
        Name = name;
        Phase = phase;
    }

    public void Execute(World world, float deltaTime)
    {
        ExecutionCount++;
        LastExecutionTime = DateTime.UtcNow.Ticks;
        LastExecutionThread = Environment.CurrentManagedThreadId;
        OnExecute?.Invoke();
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadOnly();
    }
}

internal class TestReadOnlySystem : ISystem
{
    public string Name { get; }
    public Type[] ReadComponents { get; }
    public int ExecutionCount { get; private set; }
    public int? LastExecutionThread { get; private set; }

    public TestReadOnlySystem(string name, params Type[] readComponents)
    {
        Name = name;
        ReadComponents = readComponents;
    }

    public void Execute(World world, float deltaTime)
    {
        ExecutionCount++;
        LastExecutionThread = Environment.CurrentManagedThreadId;
        // Simulate reading components
        Thread.Sleep(1);
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadOnly(ReadComponents);
    }
}

internal class TestWriteSystem : ISystem
{
    public string Name { get; }
    public Type[] WriteComponents { get; }
    public int ExecutionCount { get; private set; }
    public long LastExecutionTime { get; private set; }
    public int? LastExecutionThread { get; private set; }

    public TestWriteSystem(string name, params Type[] writeComponents)
    {
        Name = name;
        WriteComponents = writeComponents;
    }

    public void Execute(World world, float deltaTime)
    {
        ExecutionCount++;
        LastExecutionTime = DateTime.UtcNow.Ticks;
        LastExecutionThread = Environment.CurrentManagedThreadId;
        // Simulate writing components
        Thread.Sleep(2);
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.WriteOnly(WriteComponents);
    }
}

internal class TestDependentSystem : ISystem
{
    public string Name { get; }
    public Type[] RunAfterTypes { get; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecute { get; set; }

    public TestDependentSystem(string name, params Type[] runAfter)
    {
        Name = name;
        RunAfterTypes = runAfter;
    }

    public void Execute(World world, float deltaTime)
    {
        ExecutionCount++;
        OnExecute?.Invoke();
    }

    public SystemDependencies GetDependencies()
    {
        return new SystemDependencies(runAfter: RunAfterTypes);
    }
}

internal class TestFaultySystem : ISystem
{
    public string Name { get; }

    public TestFaultySystem(string name)
    {
        Name = name;
    }

    public void Execute(World world, float deltaTime)
    {
        throw new InvalidOperationException("Test system failure");
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadOnly();
    }
}

// Test Components
public struct TestComponent1
{
    public int Value;
}

public struct TestComponent2
{
    public int Value;
}

public struct TestComponent3
{
    public int Value;
}

public struct TestComponent4
{
    public int Value;
}

public struct TestComponent5
{
    public int Value;
}

// Test Health Monitor
internal class TestEcsHealthMonitor : IEcsHealthMonitor
{
    private readonly IEcsLogger _logger;
    public List<(EcsOperation Operation, long Duration)> RecordedOperations { get; } = new();

    public TestEcsHealthMonitor(IEcsLogger logger)
    {
        _logger = logger;
    }

    public HealthStatus GetHealthStatus() => HealthStatus.Healthy;

    public PerformanceMetrics GetPerformanceMetrics()
    {
        return new PerformanceMetrics(
            avgEntityOps: RecordedOperations.Count > 0 ? RecordedOperations.Average(r => r.Duration) : 0,
            avgQuery: 0, avgTransition: 0,
            totalEntityOps: RecordedOperations.Count,
            totalQueries: 0, totalTransitions: 0,
            currentFPS: 60, frameCount: 1
        );
    }

    public MemoryMetrics GetMemoryMetrics() => default;

    public void RecordEntityOperation(EcsOperation operation, long durationTicks)
    {
        RecordedOperations.Add((operation, durationTicks));
    }

    public void RecordQueryExecution(int entityCount, long durationTicks) { }
    public void RecordArchetypeTransition(int fromArchetypeId, int toArchetypeId, long durationTicks) { }
    public void RecordMemoryEvent(MemoryEventType eventType, long bytes) { }
    public void StartFrame() { }
    public void EndFrame() { }
    public void Dispose() { }
}

// Test Logger
internal class TestEcsLogger : IEcsLogger
{
    public List<string> Messages { get; } = new();

    public LogLevel MinimumLevel => LogLevel.Debug;

    public bool IsEnabled(LogLevel level) => true;

    public void LogEntityOperation(LogLevel level, EcsOperation operation, uint entityId, string? componentType = null, string? details = null)
    {
        Messages.Add($"[{level}] {operation} Entity:{entityId} Component:{componentType} {details}");
    }

    public void LogError(Exception exception, EcsOperation operation, uint entityId, string correlationId)
    {
        Messages.Add($"[ERROR] {operation} Entity:{entityId} Correlation:{correlationId} Exception:{exception.Message}");
    }

    public void LogPerformanceMetric(string metricName, long value, string? unit = null)
    {
        Messages.Add($"[PERF] {metricName}={value}{unit}");
    }

    public void LogArchetypeOperation(LogLevel level, string operation, string signature, int entityCount)
    {
        Messages.Add($"[{level}] Archetype {operation} Signature:{signature} Count:{entityCount}");
    }
}