using System.Diagnostics;
using System.Reflection;
using System.Collections.Concurrent;
using PurlieuEcs.Core;

namespace PurlieuEcs.Systems;

/// <summary>
/// Legacy system scheduler - deprecated, use PurlieuEcs.Core.SystemScheduler instead.
/// Manages system execution order and performance tracking.
/// </summary>
[Obsolete("Use PurlieuEcs.Core.SystemScheduler instead")]
public sealed class LegacySystemScheduler
{
    private readonly List<SystemEntry> _systems;
    private readonly Dictionary<string, List<SystemEntry>> _phaseToSystems;
    private readonly SystemProfiler _profiler;
    
    // Cache for system metadata to avoid repeated reflection
    private static readonly ConcurrentDictionary<Type, SystemMetadata> _metadataCache = new();
    
    public LegacySystemScheduler()
    {
        _systems = new List<SystemEntry>();
        _phaseToSystems = new Dictionary<string, List<SystemEntry>>();
        _profiler = new SystemProfiler();
    }
    
    /// <summary>
    /// Registers a system for execution.
    /// </summary>
    public void RegisterSystem(ILegacySystem system)
    {
        var systemType = system.GetType();
        
        // Get cached metadata to avoid reflection on every registration
        var metadata = _metadataCache.GetOrAdd(systemType, type =>
        {
            var phaseAttr = type.GetCustomAttribute<GamePhaseAttribute>();
            return new SystemMetadata
            {
                Phase = phaseAttr?.Phase ?? GamePhases.Update,
                Order = phaseAttr?.Order ?? 0,
                Name = type.Name
            };
        });
        
        var entry = new SystemEntry(system, metadata.Phase, metadata.Order, metadata.Name);
        _systems.Add(entry);
        
        if (!_phaseToSystems.TryGetValue(metadata.Phase, out var phaseList))
        {
            phaseList = new List<SystemEntry>();
            _phaseToSystems[metadata.Phase] = phaseList;
        }
        
        phaseList.Add(entry);
        phaseList.Sort((a, b) => a.Order.CompareTo(b.Order));
    }
    
    /// <summary>
    /// Executes all systems in the specified phase.
    /// </summary>
    public void UpdatePhase(World world, float deltaTime, string phase = GamePhases.Update)
    {
        if (!_phaseToSystems.TryGetValue(phase, out var systems))
            return;
            
        foreach (var systemEntry in systems)
        {
            var stopwatch = Stopwatch.StartNew();
            systemEntry.System.Update(world, deltaTime);
            stopwatch.Stop();
            
            _profiler.RecordSystemTime(systemEntry.Name, stopwatch.Elapsed.TotalMilliseconds);
        }
    }
    
    /// <summary>
    /// Gets performance statistics for all systems.
    /// </summary>
    public SystemStats GetSystemStats(string systemName)
    {
        return _profiler.GetStats(systemName);
    }
    
    /// <summary>
    /// Resets peak performance counters.
    /// </summary>
    public void ResetPeaks()
    {
        _profiler.ResetPeaks();
    }
    
    private sealed class SystemEntry
    {
        public ILegacySystem System { get; }
        public string Phase { get; }
        public int Order { get; }
        public string Name { get; }
        
        public SystemEntry(ILegacySystem system, string phase, int order, string name)
        {
            System = system;
            Phase = phase;
            Order = order;
            Name = name;
        }
    }
    
    /// <summary>
    /// Cached system metadata to avoid repeated reflection.
    /// </summary>
    private sealed class SystemMetadata
    {
        public required string Phase { get; init; }
        public int Order { get; init; }
        public required string Name { get; init; }
    }
}

/// <summary>
/// Tracks system performance metrics.
/// </summary>
public sealed class SystemProfiler
{
    private readonly Dictionary<string, SystemMetrics> _metrics = new();
    private const int SampleWindow = 30;
    
    public void RecordSystemTime(string systemName, double milliseconds)
    {
        if (!_metrics.TryGetValue(systemName, out var metrics))
        {
            metrics = new SystemMetrics(SampleWindow);
            _metrics[systemName] = metrics;
        }
        
        metrics.RecordTime(milliseconds);
    }
    
    public SystemStats GetStats(string systemName)
    {
        return _metrics.TryGetValue(systemName, out var metrics) 
            ? metrics.GetStats() 
            : new SystemStats(0, 0, 0);
    }
    
    public void ResetPeaks()
    {
        foreach (var metrics in _metrics.Values)
        {
            metrics.ResetPeak();
        }
    }
    
    private sealed class SystemMetrics
    {
        private readonly Queue<double> _samples;
        private readonly int _maxSamples;
        private double _peak;
        
        public SystemMetrics(int maxSamples)
        {
            _samples = new Queue<double>();
            _maxSamples = maxSamples;
        }
        
        public void RecordTime(double milliseconds)
        {
            _samples.Enqueue(milliseconds);
            if (_samples.Count > _maxSamples)
                _samples.Dequeue();
                
            if (milliseconds > _peak)
                _peak = milliseconds;
        }
        
        public SystemStats GetStats()
        {
            if (_samples.Count == 0)
                return new SystemStats(0, 0, 0);
                
            var current = _samples.LastOrDefault();
            var average = _samples.Average();
            
            return new SystemStats(current, average, _peak);
        }
        
        public void ResetPeak()
        {
            _peak = _samples.Count > 0 ? _samples.Max() : 0;
        }
    }
}

/// <summary>
/// Performance statistics for a system.
/// </summary>
public readonly struct SystemStats
{
    public double Current { get; }
    public double Average { get; }
    public double Peak { get; }
    
    public SystemStats(double current, double average, double peak)
    {
        Current = current;
        Average = average;
        Peak = peak;
    }
    
    public override string ToString()
    {
        return $"Current: {Current:F2}ms, Avg: {Average:F2}ms, Peak: {Peak:F2}ms";
    }
}