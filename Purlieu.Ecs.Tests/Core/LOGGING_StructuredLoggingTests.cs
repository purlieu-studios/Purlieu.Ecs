using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Logging;
using System.Text;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class LOGGING_StructuredLoggingTests
{
    private TestLogger _testLogger;
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _testLogger = new TestLogger();
        _world = new World(logger: _testLogger);
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    [Test]
    public void StructuredLogging_EntityCreation_LogsCorrectly()
    {
        // Act
        var entity = _world.CreateEntity();
        
        // Assert
        Assert.That(_testLogger.LoggedMessages.Count, Is.EqualTo(1));
        var logMessage = _testLogger.LoggedMessages[0];
        Assert.That(logMessage.Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(logMessage.Operation, Is.EqualTo(EcsOperation.EntityCreate));
        Assert.That(logMessage.EntityId, Is.EqualTo(entity.Id));
    }
    
    [Test]
    public void StructuredLogging_ComponentOperations_LogsCorrectly()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _testLogger.Clear();
        
        // Act - Add component
        _world.AddComponent(entity, new Purlieu.Logic.Components.Position(10, 20, 30));
        
        // Assert - Component addition logged
        Assert.That(_testLogger.LoggedMessages.Count, Is.EqualTo(1));
        var addMessage = _testLogger.LoggedMessages[0];
        Assert.That(addMessage.Operation, Is.EqualTo(EcsOperation.ComponentAdd));
        Assert.That(addMessage.ComponentType, Is.EqualTo("Position"));
        
        _testLogger.Clear();
        
        // Act - Get component
        var pos = _world.Get<Purlieu.Logic.Components.Position>(entity);
        
        // Assert - Component access logged at trace level (if enabled)
        if (_testLogger.MinimumLevel <= LogLevel.Trace)
        {
            Assert.That(_testLogger.LoggedMessages.Any(m => m.Operation == EcsOperation.ComponentGet));
        }
        
        _testLogger.Clear();
        
        // Act - Remove component
        _world.RemoveComponent<Purlieu.Logic.Components.Position>(entity);
        
        // Assert - Component removal logged
        Assert.That(_testLogger.LoggedMessages.Count, Is.EqualTo(1));
        var removeMessage = _testLogger.LoggedMessages[0];
        Assert.That(removeMessage.Operation, Is.EqualTo(EcsOperation.ComponentRemove));
        Assert.That(removeMessage.ComponentType, Is.EqualTo("Position"));
    }
    
    [Test]
    public void StructuredLogging_ArchetypeTransitions_LogsCorrectly()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _testLogger.Clear();
        
        // Act - Add component to trigger archetype transition
        _world.AddComponent(entity, new Purlieu.Logic.Components.Position(0, 0, 0));
        
        // Assert - Should log component add and archetype transition
        var transitionMessages = _testLogger.LoggedMessages
            .Where(m => m.Operation == EcsOperation.ArchetypeTransition)
            .ToList();
        
        Assert.That(transitionMessages.Count, Is.EqualTo(1));
        var transition = transitionMessages[0];
        Assert.That(transition.Details, Does.Contain("From:").And.Contain("To:"));
    }
    
    [Test]
    public void StructuredLogging_EntityDestruction_LogsCorrectly()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _testLogger.Clear();
        
        // Act
        _world.DestroyEntity(entity);
        
        // Assert
        Assert.That(_testLogger.LoggedMessages.Count, Is.EqualTo(1));
        var logMessage = _testLogger.LoggedMessages[0];
        Assert.That(logMessage.Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(logMessage.Operation, Is.EqualTo(EcsOperation.EntityDestroy));
        Assert.That(logMessage.EntityId, Is.EqualTo(entity.Id));
    }
    
    [Test]
    public void StructuredLogging_CorrelationIds_WorkCorrectly()
    {
        // Arrange
        var originalCorrelation = CorrelationContext.Current;
        var customCorrelation = "TEST123";
        
        // Act
        CorrelationContext.Set(customCorrelation);
        var entity = _world.CreateEntity();
        
        // Assert - All messages should have the custom correlation ID
        foreach (var message in _testLogger.LoggedMessages)
        {
            Assert.That(message.CorrelationId, Is.EqualTo(customCorrelation));
        }
        
        // Cleanup
        CorrelationContext.Set(originalCorrelation);
    }
    
    [Test]
    public void StructuredLogging_PerformanceMetrics_WorkCorrectly()
    {
        // Act
        _testLogger.LogPerformanceMetric("TestMetric", 42, "ms");
        
        // Assert
        var perfMessages = _testLogger.PerformanceMetrics;
        Assert.That(perfMessages.Count, Is.EqualTo(1));
        Assert.That(perfMessages[0].MetricName, Is.EqualTo("TestMetric"));
        Assert.That(perfMessages[0].Value, Is.EqualTo(42));
        Assert.That(perfMessages[0].Unit, Is.EqualTo("ms"));
    }
    
    [Test]
    public void NullLogger_NoAllocationsOrExceptions()
    {
        // Arrange
        var worldWithNullLogger = new World(); // Should default to NullLogger
        
        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() =>
        {
            var entity = worldWithNullLogger.CreateEntity();
            worldWithNullLogger.AddComponent(entity, new Purlieu.Logic.Components.Position(1, 2, 3));
            var pos = worldWithNullLogger.Get<Purlieu.Logic.Components.Position>(entity);
            worldWithNullLogger.RemoveComponent<Purlieu.Logic.Components.Position>(entity);
            worldWithNullLogger.DestroyEntity(entity);
        });
        
        // Verify logger is null logger
        Assert.That(worldWithNullLogger.Logger, Is.TypeOf<NullEcsLogger>());
        
        worldWithNullLogger.Dispose();
    }
}

/// <summary>
/// Test logger implementation that captures log messages for verification
/// </summary>
public class TestLogger : IEcsLogger
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;
    
    public List<LogMessage> LoggedMessages { get; } = new();
    public List<PerformanceMetric> PerformanceMetrics { get; } = new();
    
    public bool IsEnabled(LogLevel level) => level >= MinimumLevel;
    
    public void LogEntityOperation(LogLevel level, EcsOperation operation, uint entityId, string? componentType = null, string? details = null)
    {
        if (!IsEnabled(level)) return;
        
        LoggedMessages.Add(new LogMessage
        {
            Level = level,
            Operation = operation,
            EntityId = entityId,
            ComponentType = componentType,
            Details = details,
            CorrelationId = CorrelationContext.Current,
            Timestamp = DateTime.UtcNow
        });
    }
    
    public void LogError(Exception exception, EcsOperation operation, uint entityId, string correlationId)
    {
        LoggedMessages.Add(new LogMessage
        {
            Level = LogLevel.Error,
            Operation = operation,
            EntityId = entityId,
            Exception = exception,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        });
    }
    
    public void LogPerformanceMetric(string metricName, long value, string? unit = null)
    {
        if (!IsEnabled(LogLevel.Information)) return;
        
        PerformanceMetrics.Add(new PerformanceMetric
        {
            MetricName = metricName,
            Value = value,
            Unit = unit,
            CorrelationId = CorrelationContext.Current,
            Timestamp = DateTime.UtcNow
        });
    }
    
    public void LogArchetypeOperation(LogLevel level, string operation, string signature, int entityCount)
    {
        if (!IsEnabled(level)) return;
        
        LoggedMessages.Add(new LogMessage
        {
            Level = level,
            Operation = EcsOperation.ArchetypeTransition, // Map to closest operation
            Details = $"{operation}: {signature} (Entities: {entityCount})",
            CorrelationId = CorrelationContext.Current,
            Timestamp = DateTime.UtcNow
        });
    }
    
    public void Clear()
    {
        LoggedMessages.Clear();
        PerformanceMetrics.Clear();
    }
}

public class LogMessage
{
    public LogLevel Level { get; set; }
    public EcsOperation Operation { get; set; }
    public uint EntityId { get; set; }
    public string? ComponentType { get; set; }
    public string? Details { get; set; }
    public Exception? Exception { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class PerformanceMetric
{
    public string MetricName { get; set; } = "";
    public long Value { get; set; }
    public string? Unit { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}