using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Stateless system interface for ECS logic processing.
/// Systems must be pure functions that operate only on World state.
/// </summary>
public interface ISystem
{
    /// <summary>
    /// Execute the system logic for the current frame.
    /// Must be stateless - all state should be stored in components or passed parameters.
    /// </summary>
    /// <param name="world">The ECS world to operate on</param>
    /// <param name="deltaTime">Time elapsed since last frame in seconds</param>
    void Execute(World world, float deltaTime);
    
    /// <summary>
    /// Get system dependencies for scheduling order.
    /// Used to determine execution order and parallelization opportunities.
    /// </summary>
    SystemDependencies GetDependencies();
}

/// <summary>
/// System dependencies specification for scheduling and parallel execution.
/// Enables the scheduler to determine execution order and parallelization.
/// </summary>
public readonly struct SystemDependencies
{
    /// <summary>
    /// Component types this system reads from (allows parallel execution with other readers)
    /// </summary>
    public readonly Type[] ReadComponents;
    
    /// <summary>
    /// Component types this system writes to (requires exclusive access)
    /// </summary>
    public readonly Type[] WriteComponents;
    
    /// <summary>
    /// Systems that must execute before this system
    /// </summary>
    public readonly Type[] RunAfter;
    
    /// <summary>
    /// Systems that must execute after this system
    /// </summary>
    public readonly Type[] RunBefore;
    
    /// <summary>
    /// Whether this system can run in parallel with other compatible systems
    /// </summary>
    public readonly bool AllowParallelExecution;
    
    public SystemDependencies(
        Type[]? readComponents = null,
        Type[]? writeComponents = null,
        Type[]? runAfter = null,
        Type[]? runBefore = null,
        bool allowParallelExecution = true)
    {
        ReadComponents = readComponents ?? Array.Empty<Type>();
        WriteComponents = writeComponents ?? Array.Empty<Type>();
        RunAfter = runAfter ?? Array.Empty<Type>();
        RunBefore = runBefore ?? Array.Empty<Type>();
        AllowParallelExecution = allowParallelExecution;
    }
    
    /// <summary>
    /// Create dependencies with only read components (safe for parallel execution)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemDependencies ReadOnly(params Type[] components)
    {
        return new SystemDependencies(readComponents: components, allowParallelExecution: true);
    }
    
    /// <summary>
    /// Create dependencies with only write components (requires exclusive access)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemDependencies WriteOnly(params Type[] components)
    {
        return new SystemDependencies(writeComponents: components, allowParallelExecution: false);
    }
    
    /// <summary>
    /// Create dependencies with both read and write components
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemDependencies ReadWrite(Type[] readComponents, Type[] writeComponents)
    {
        return new SystemDependencies(readComponents: readComponents, writeComponents: writeComponents, allowParallelExecution: false);
    }
}

/// <summary>
/// Execution phase for systems to control when they run in the frame
/// </summary>
public enum SystemPhase : byte
{
    /// <summary>
    /// Early update phase - input processing, AI decisions
    /// </summary>
    EarlyUpdate = 0,
    
    /// <summary>
    /// Main update phase - game logic, physics simulation
    /// </summary>
    Update = 1,
    
    /// <summary>
    /// Late update phase - animations, camera updates, cleanup
    /// </summary>
    LateUpdate = 2,
    
    /// <summary>
    /// Render phase - rendering, UI updates (usually single-threaded)
    /// </summary>
    Render = 3
}

/// <summary>
/// Attribute to specify system execution phase and priority
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class SystemExecutionAttribute : Attribute
{
    /// <summary>
    /// Phase when this system should execute
    /// </summary>
    public SystemPhase Phase { get; }
    
    /// <summary>
    /// Priority within the phase (lower values execute first)
    /// </summary>
    public int Priority { get; }
    
    public SystemExecutionAttribute(SystemPhase phase = SystemPhase.Update, int priority = 0)
    {
        Phase = phase;
        Priority = priority;
    }
}

/// <summary>
/// Delegate for simple function-based systems without classes
/// </summary>
public delegate void SystemFunction(World world, float deltaTime);

/// <summary>
/// Simple wrapper to convert function delegates into ISystem instances
/// </summary>
internal sealed class FunctionSystem : ISystem
{
    private readonly SystemFunction _function;
    private readonly SystemDependencies _dependencies;
    
    public FunctionSystem(SystemFunction function, SystemDependencies dependencies)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
        _dependencies = dependencies;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute(World world, float deltaTime)
    {
        _function(world, deltaTime);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemDependencies GetDependencies()
    {
        return _dependencies;
    }
}