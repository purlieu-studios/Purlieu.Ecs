using System.Runtime.CompilerServices;

namespace PurlieuEcs.Logging;

/// <summary>
/// Zero-allocation logging interface for ECS operations
/// Designed to avoid boxing and string allocations in hot paths
/// </summary>
public interface IEcsLogger
{
    /// <summary>
    /// Log level enumeration
    /// </summary>
    LogLevel MinimumLevel { get; }
    
    /// <summary>
    /// Zero-allocation entity operation logging
    /// </summary>
    void LogEntityOperation(LogLevel level, EcsOperation operation, uint entityId, string? componentType = null, string? details = null);
    
    /// <summary>
    /// Zero-allocation error logging with correlation tracking
    /// </summary>
    void LogError(Exception exception, EcsOperation operation, uint entityId, string correlationId);
    
    /// <summary>
    /// Zero-allocation performance counter logging
    /// </summary>
    void LogPerformanceMetric(string metricName, long value, string? unit = null);
    
    /// <summary>
    /// Zero-allocation archetype operation logging
    /// </summary>
    void LogArchetypeOperation(LogLevel level, string operation, string signature, int entityCount);
    
    /// <summary>
    /// Check if logging level is enabled to avoid expensive operations
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool IsEnabled(LogLevel level);
}

/// <summary>
/// ECS-specific operation types for structured logging
/// </summary>
public enum EcsOperation : byte
{
    EntityCreate = 0,
    EntityDestroy = 1,
    ComponentAdd = 2,
    ComponentRemove = 3,
    ComponentGet = 4,
    ComponentSet = 5,
    QueryExecute = 6,
    ArchetypeTransition = 7,
    SystemExecute = 8,
    MemoryCleanup = 9,
    SnapshotSave = 10,
    SnapshotLoad = 11
}

/// <summary>
/// Log severity levels
/// </summary>
public enum LogLevel : byte
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

/// <summary>
/// Correlation context for tracking operations across ECS calls
/// Uses ThreadLocal for zero-allocation correlation tracking
/// </summary>
public static class CorrelationContext
{
    private static readonly ThreadLocal<string> _correlationId = new(() => Guid.NewGuid().ToString("N")[..8]);
    
    /// <summary>
    /// Get current correlation ID (8-character hex string for compactness)
    /// </summary>
    public static string Current => _correlationId.Value!;
    
    /// <summary>
    /// Set correlation ID for current thread
    /// </summary>
    public static void Set(string correlationId)
    {
        _correlationId.Value = correlationId;
    }
    
    /// <summary>
    /// Generate new correlation ID for current thread
    /// </summary>
    public static string NewCorrelation()
    {
        var newId = Guid.NewGuid().ToString("N")[..8];
        _correlationId.Value = newId;
        return newId;
    }
    
    /// <summary>
    /// Reset correlation ID to auto-generated value
    /// </summary>
    public static void Reset()
    {
        _correlationId.Value = Guid.NewGuid().ToString("N")[..8];
    }
}

/// <summary>
/// High-performance console logger with structured output
/// Optimized for minimal allocations and fast writes
/// </summary>
public sealed class ConsoleEcsLogger : IEcsLogger
{
    private readonly LogLevel _minimumLevel;
    private readonly bool _useColors;
    private readonly object _lock = new();
    
    // Pre-allocated string builders for hot path logging
    private static readonly ThreadLocal<System.Text.StringBuilder> _stringBuilder = 
        new(() => new System.Text.StringBuilder(256));
    
    public LogLevel MinimumLevel => _minimumLevel;
    
    public ConsoleEcsLogger(LogLevel minimumLevel = LogLevel.Information, bool useColors = true)
    {
        _minimumLevel = minimumLevel;
        _useColors = useColors;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel level) => level >= _minimumLevel;
    
    public void LogEntityOperation(LogLevel level, EcsOperation operation, uint entityId, string? componentType = null, string? details = null)
    {
        if (!IsEnabled(level)) return;
        
        var sb = _stringBuilder.Value!;
        sb.Clear();
        
        sb.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff"));
        sb.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
        sb.Append(CorrelationContext.Current).Append(" | ");
        sb.Append(operation.ToString()).Append(" Entity=").Append(entityId);
        
        if (componentType != null)
            sb.Append(" Component=").Append(componentType);
        
        if (details != null)
            sb.Append(" Details=").Append(details);
        
        WriteToConsole(level, sb.ToString());
    }
    
    public void LogError(Exception exception, EcsOperation operation, uint entityId, string correlationId)
    {
        var sb = _stringBuilder.Value!;
        sb.Clear();
        
        sb.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff"));
        sb.Append(" [ERROR] ");
        sb.Append(correlationId).Append(" | ");
        sb.Append(operation.ToString()).Append(" Entity=").Append(entityId);
        sb.Append(" Exception=").Append(exception.GetType().Name);
        sb.Append(" Message=\"").Append(exception.Message).Append("\"");
        
        WriteToConsole(LogLevel.Error, sb.ToString());
    }
    
    public void LogPerformanceMetric(string metricName, long value, string? unit = null)
    {
        if (!IsEnabled(LogLevel.Information)) return;
        
        var sb = _stringBuilder.Value!;
        sb.Clear();
        
        sb.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff"));
        sb.Append(" [PERF] ");
        sb.Append(CorrelationContext.Current).Append(" | ");
        sb.Append("Metric=").Append(metricName);
        sb.Append(" Value=").Append(value);
        
        if (unit != null)
            sb.Append(unit);
        
        WriteToConsole(LogLevel.Information, sb.ToString());
    }
    
    public void LogArchetypeOperation(LogLevel level, string operation, string signature, int entityCount)
    {
        if (!IsEnabled(level)) return;
        
        var sb = _stringBuilder.Value!;
        sb.Clear();
        
        sb.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff"));
        sb.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
        sb.Append(CorrelationContext.Current).Append(" | ");
        sb.Append("Archetype ").Append(operation);
        sb.Append(" Signature=").Append(signature);
        sb.Append(" EntityCount=").Append(entityCount);
        
        WriteToConsole(level, sb.ToString());
    }
    
    private void WriteToConsole(LogLevel level, string message)
    {
        if (_useColors)
        {
            lock (_lock)
            {
                Console.ForegroundColor = GetLevelColor(level);
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }
        else
        {
            Console.WriteLine(message);
        }
    }
    
    private static ConsoleColor GetLevelColor(LogLevel level) => level switch
    {
        LogLevel.Trace => ConsoleColor.Gray,
        LogLevel.Debug => ConsoleColor.Blue,
        LogLevel.Information => ConsoleColor.White,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Critical => ConsoleColor.Magenta,
        _ => ConsoleColor.White
    };
}

/// <summary>
/// Null logger for production environments where logging is disabled
/// All methods are no-ops with aggressive inlining for zero overhead
/// </summary>
public sealed class NullEcsLogger : IEcsLogger
{
    public static readonly NullEcsLogger Instance = new();
    
    public LogLevel MinimumLevel => LogLevel.Critical;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel level) => false;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogEntityOperation(LogLevel level, EcsOperation operation, uint entityId, string? componentType = null, string? details = null) { }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogError(Exception exception, EcsOperation operation, uint entityId, string correlationId) { }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogPerformanceMetric(string metricName, long value, string? unit = null) { }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogArchetypeOperation(LogLevel level, string operation, string signature, int entityCount) { }
}