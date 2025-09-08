using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using PurlieuEcs.Logging;
using PurlieuEcs.Monitoring;

namespace PurlieuEcs.Core;

/// <summary>
/// High-performance system scheduler with dependency resolution and parallel execution.
/// Provides deterministic execution order with thread-safe parallel execution where possible.
/// </summary>
public sealed class SystemScheduler
{
    private readonly Dictionary<SystemPhase, List<SystemExecutionGroup>> _phaseGroups;
    private readonly Dictionary<object, ISystem> _systemInstances; // Use object key to support multiple instances
    private readonly IEcsLogger _logger;
    private readonly IEcsHealthMonitor _healthMonitor;
    
    /// <summary>
    /// System execution group for parallel or sequential execution
    /// </summary>
    private readonly struct SystemExecutionGroup
    {
        public readonly ISystem[] Systems;
        public readonly bool CanRunInParallel;
        public readonly string GroupName;
        
        public SystemExecutionGroup(ISystem[] systems, bool canRunInParallel, string groupName)
        {
            Systems = systems;
            CanRunInParallel = canRunInParallel;
            GroupName = groupName;
        }
    }
    
    public SystemScheduler(IEcsLogger? logger = null, IEcsHealthMonitor? healthMonitor = null)
    {
        _phaseGroups = new Dictionary<SystemPhase, List<SystemExecutionGroup>>();
        _systemInstances = new Dictionary<object, ISystem>();
        _logger = logger ?? NullEcsLogger.Instance;
        _healthMonitor = healthMonitor ?? NullEcsHealthMonitor.Instance;
        
        // Initialize phase groups
        foreach (SystemPhase phase in Enum.GetValues<SystemPhase>())
        {
            _phaseGroups[phase] = new List<SystemExecutionGroup>();
        }
    }
    
    /// <summary>
    /// Register a system for execution
    /// </summary>
    public void RegisterSystem<T>(T system) where T : class, ISystem
    {
        ArgumentNullException.ThrowIfNull(system);
        
        // Use the system instance itself as the key to support multiple instances of the same type
        if (_systemInstances.ContainsKey(system))
        {
            _logger.LogEntityOperation(LogLevel.Warning, EcsOperation.SystemExecute, 0, details: $"System instance already registered");
            return;
        }
        
        _systemInstances[system] = system;
        
        _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.SystemExecute, 0, details: $"Registered system: {system.GetType().Name}");
        
        // Rebuild execution groups when systems are added
        RebuildExecutionGroups();
    }
    
    /// <summary>
    /// Register a function as a system
    /// </summary>
    public void RegisterSystem(SystemFunction function, SystemDependencies dependencies, 
        SystemPhase phase = SystemPhase.Update, int priority = 0, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        
        var functionSystem = new FunctionSystem(function, dependencies);
        var systemName = name ?? $"Function_{_systemInstances.Count}";
        
        // Use the function system instance as the key
        _systemInstances[functionSystem] = functionSystem;
        
        _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.SystemExecute, 0, details: $"Registered function system: {systemName}");
        
        RebuildExecutionGroups();
    }
    
    /// <summary>
    /// Unregister all systems of a given type
    /// </summary>
    public void UnregisterSystem<T>() where T : class, ISystem
    {
        var systemType = typeof(T);
        var systemsToRemove = _systemInstances.Where(kvp => kvp.Value.GetType() == systemType).Select(kvp => kvp.Key).ToList();
        
        foreach (var key in systemsToRemove)
        {
            _systemInstances.Remove(key);
        }
        
        if (systemsToRemove.Count > 0)
        {
            _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.SystemExecute, 0, details: $"Unregistered {systemsToRemove.Count} instance(s) of system: {systemType.Name}");
            RebuildExecutionGroups();
        }
    }
    
    /// <summary>
    /// Execute all systems for the specified phase
    /// </summary>
    public void ExecutePhase(SystemPhase phase, World world, float deltaTime)
    {
        if (!_phaseGroups.TryGetValue(phase, out var groups))
            return;
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            foreach (var group in groups)
            {
                ExecuteGroup(group, world, deltaTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EcsOperation.SystemExecute, 0, CorrelationContext.Current);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _healthMonitor.RecordEntityOperation(EcsOperation.SystemExecute, stopwatch.ElapsedTicks);
        }
    }
    
    /// <summary>
    /// Execute all phases in order
    /// </summary>
    public void ExecuteAllPhases(World world, float deltaTime)
    {
        ExecutePhase(SystemPhase.EarlyUpdate, world, deltaTime);
        ExecutePhase(SystemPhase.Update, world, deltaTime);
        ExecutePhase(SystemPhase.LateUpdate, world, deltaTime);
        ExecutePhase(SystemPhase.Render, world, deltaTime);
    }
    
    /// <summary>
    /// Execute a group of systems either in parallel or sequentially
    /// </summary>
    private void ExecuteGroup(SystemExecutionGroup group, World world, float deltaTime)
    {
        if (group.Systems.Length == 0)
            return;
            
        if (group.CanRunInParallel && group.Systems.Length > 1)
        {
            ExecuteGroupInParallel(group, world, deltaTime);
        }
        else
        {
            ExecuteGroupSequentially(group, world, deltaTime);
        }
    }
    
    /// <summary>
    /// Execute systems in parallel using Tasks
    /// </summary>
    private void ExecuteGroupInParallel(SystemExecutionGroup group, World world, float deltaTime)
    {
        var exceptions = new ConcurrentBag<Exception>();
        
        Parallel.ForEach(group.Systems, system =>
        {
            try
            {
                var systemStopwatch = Stopwatch.StartNew();
                system.Execute(world, deltaTime);
                systemStopwatch.Stop();
                
                _healthMonitor.RecordEntityOperation(EcsOperation.SystemExecute, systemStopwatch.ElapsedTicks);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });
        
        if (!exceptions.IsEmpty)
        {
            throw new AggregateException($"Errors in parallel system group '{group.GroupName}'", exceptions);
        }
    }
    
    /// <summary>
    /// Execute systems sequentially
    /// </summary>
    private void ExecuteGroupSequentially(SystemExecutionGroup group, World world, float deltaTime)
    {
        foreach (var system in group.Systems)
        {
            var systemStopwatch = Stopwatch.StartNew();
            
            try
            {
                system.Execute(world, deltaTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, EcsOperation.SystemExecute, 0, CorrelationContext.Current);
                throw;
            }
            finally
            {
                systemStopwatch.Stop();
                _healthMonitor.RecordEntityOperation(EcsOperation.SystemExecute, systemStopwatch.ElapsedTicks);
            }
        }
    }
    
    /// <summary>
    /// Rebuild execution groups based on dependencies and phases
    /// </summary>
    private void RebuildExecutionGroups()
    {
        // Clear existing groups
        foreach (var phaseList in _phaseGroups.Values)
        {
            phaseList.Clear();
        }
        
        // Group systems by phase and resolve dependencies
        var systemsByPhase = new Dictionary<SystemPhase, List<(ISystem System, Type SystemType, SystemExecutionAttribute? Attribute)>>();
        
        foreach (SystemPhase phase in Enum.GetValues<SystemPhase>())
        {
            systemsByPhase[phase] = new List<(ISystem, Type, SystemExecutionAttribute?)>();
        }
        
        // Categorize systems by phase
        foreach (var (_, system) in _systemInstances)
        {
            var systemType = system.GetType();
            var executionAttribute = systemType.GetCustomAttribute<SystemExecutionAttribute>();
            var phase = executionAttribute?.Phase ?? SystemPhase.Update;
            
            systemsByPhase[phase].Add((System: system, SystemType: systemType, Attribute: executionAttribute));
        }
        
        // Process each phase
        foreach (var (phase, systems) in systemsByPhase)
        {
            if (systems.Count == 0)
                continue;
            
            // Sort by priority within phase
            systems.Sort((a, b) => (a.Attribute?.Priority ?? 0).CompareTo(b.Attribute?.Priority ?? 0));
            
            // Build execution groups based on dependencies
            var executionGroups = BuildExecutionGroups(systems);
            _phaseGroups[phase].AddRange(executionGroups);
        }
        
        _logger.LogEntityOperation(LogLevel.Debug, EcsOperation.SystemExecute, 0, details: $"Rebuilt system execution groups. Total systems: {_systemInstances.Count}");
    }
    
    /// <summary>
    /// Build execution groups based on system dependencies
    /// </summary>
    private List<SystemExecutionGroup> BuildExecutionGroups(List<(ISystem System, Type SystemType, SystemExecutionAttribute? Attribute)> systems)
    {
        var groups = new List<SystemExecutionGroup>();
        var processedSystems = new HashSet<ISystem>(); // Track by instance, not type
        var systemDependencies = new Dictionary<ISystem, SystemDependencies>(); // Map instance to dependencies
        
        // Cache dependencies
        foreach (var (system, systemType, _) in systems)
        {
            systemDependencies[system] = system.GetDependencies();
        }
        
        // Process systems in dependency order
        while (processedSystems.Count < systems.Count)
        {
            var currentGroup = new List<ISystem>();
            var canRunInParallel = true;
            
            foreach (var (system, systemType, _) in systems)
            {
                if (processedSystems.Contains(system))
                    continue;
                
                var dependencies = systemDependencies[system];
                
                // Check if all dependencies are satisfied
                if (AreSystemDependenciesSatisfied(system, systemType, dependencies, processedSystems, systems))
                {
                    // Check for component conflicts with current group
                    if (CanAddToGroup(system, dependencies, currentGroup, systemDependencies))
                    {
                        currentGroup.Add(system);
                        processedSystems.Add(system);
                        
                        // If any system in the group doesn't allow parallel execution, the whole group is sequential
                        if (!dependencies.AllowParallelExecution || dependencies.WriteComponents.Length > 0)
                        {
                            canRunInParallel = false;
                        }
                    }
                }
            }
            
            if (currentGroup.Count > 0)
            {
                var groupName = $"Group_{groups.Count}_{(canRunInParallel ? "Parallel" : "Sequential")}";
                groups.Add(new SystemExecutionGroup(currentGroup.ToArray(), canRunInParallel, groupName));
            }
            else
            {
                // Break infinite loop if no progress can be made
                break;
            }
        }
        
        return groups;
    }
    
    /// <summary>
    /// Check if all system dependencies are satisfied
    /// </summary>
    private bool AreSystemDependenciesSatisfied(ISystem system, Type systemType, SystemDependencies dependencies, 
        HashSet<ISystem> processedSystems, List<(ISystem System, Type SystemType, SystemExecutionAttribute? Attribute)> allSystems)
    {
        // Check RunAfter dependencies
        if (dependencies.RunAfter != null)
        {
            foreach (var dependencyType in dependencies.RunAfter)
            {
                // Check if at least one system of the required type has been processed
                var dependencySatisfied = allSystems
                    .Where(s => s.SystemType == dependencyType && processedSystems.Contains(s.System))
                    .Any();
                    
                if (!dependencySatisfied)
                    return false;
            }
        }
        
        // Check RunBefore dependencies (systems that must run after this one shouldn't be processed yet)
        if (dependencies.RunBefore != null)
        {
            foreach (var dependentType in dependencies.RunBefore)
            {
                // Check if any system of the dependent type has been processed
                var dependentProcessed = allSystems
                    .Where(s => s.SystemType == dependentType && processedSystems.Contains(s.System))
                    .Any();
                    
                if (dependentProcessed)
                    return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Check if a system can be added to the current execution group without conflicts
    /// </summary>
    private bool CanAddToGroup(ISystem system, SystemDependencies dependencies, List<ISystem> currentGroup, 
        Dictionary<ISystem, SystemDependencies> systemDependencies)
    {
        if (currentGroup.Count == 0)
            return true;
        
        // Check for component access conflicts
        var writeComponents = new HashSet<Type>(dependencies.WriteComponents);
        var readComponents = new HashSet<Type>(dependencies.ReadComponents);
        
        foreach (var existingSystem in currentGroup)
        {
            if (!systemDependencies.TryGetValue(existingSystem, out var existingDeps))
                continue;
            
            // Check for write-write conflicts
            foreach (var writeComponent in existingDeps.WriteComponents)
            {
                if (writeComponents.Contains(writeComponent))
                    return false; // Write-write conflict
            }
            
            // Check for read-write conflicts
            foreach (var writeComponent in writeComponents)
            {
                if (existingDeps.ReadComponents.Contains(writeComponent))
                    return false; // Read-write conflict
            }
            
            foreach (var existingWriteComponent in existingDeps.WriteComponents)
            {
                if (readComponents.Contains(existingWriteComponent))
                    return false; // Write-read conflict
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Get execution statistics
    /// </summary>
    public SystemExecutionStatistics GetStatistics()
    {
        var totalSystems = _systemInstances.Count;
        var totalGroups = _phaseGroups.Values.Sum(groups => groups.Count);
        var parallelGroups = _phaseGroups.Values.SelectMany(groups => groups).Count(g => g.CanRunInParallel);
        
        return new SystemExecutionStatistics(totalSystems, totalGroups, parallelGroups);
    }
}

/// <summary>
/// Statistics about system execution scheduling
/// </summary>
public readonly struct SystemExecutionStatistics
{
    public readonly int TotalSystems;
    public readonly int TotalExecutionGroups;
    public readonly int ParallelGroups;
    
    public SystemExecutionStatistics(int totalSystems, int totalGroups, int parallelGroups)
    {
        TotalSystems = totalSystems;
        TotalExecutionGroups = totalGroups;
        ParallelGroups = parallelGroups;
    }
}