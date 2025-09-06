using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Interface for type-erased component operations.
/// </summary>
internal interface IComponentOperations
{
    void Copy(Chunk fromChunk, int fromIndex, Chunk toChunk, int toIndex);
    bool HasOneFrameAttribute { get; }
    Type ComponentType { get; }
}

/// <summary>
/// Typed implementation of component operations for zero-boxing access.
/// </summary>
internal sealed class ComponentOperations<T> : IComponentOperations where T : struct
{
    private readonly bool _hasOneFrameAttribute;
    
    public Type ComponentType => typeof(T);
    public bool HasOneFrameAttribute => _hasOneFrameAttribute;
    
    public ComponentOperations()
    {
        // Cache attribute check to avoid repeated reflection
        _hasOneFrameAttribute = Attribute.IsDefined(typeof(T), typeof(Components.OneFrameAttribute));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Copy(Chunk fromChunk, int fromIndex, Chunk toChunk, int toIndex)
    {
        var fromSpan = fromChunk.GetSpan<T>();
        var toSpan = toChunk.GetSpan<T>();
        
        if (fromIndex < fromSpan.Length && toIndex < toSpan.Length)
        {
            toSpan[toIndex] = fromSpan[fromIndex];
        }
    }
}

/// <summary>
/// Registry for component operations, eliminating reflection from hot paths.
/// </summary>
internal static class ComponentRegistry
{
    private static readonly Dictionary<Type, IComponentOperations> _operations = new(capacity: 32);
    private static readonly HashSet<Type> _oneFrameComponents = new(capacity: 16);
    
    /// <summary>
    /// Registers a component type with its operations.
    /// </summary>
    public static void Register<T>() where T : struct
    {
        var type = typeof(T);
        if (!_operations.ContainsKey(type))
        {
            var operations = new ComponentOperations<T>();
            _operations[type] = operations;
            
            if (operations.HasOneFrameAttribute)
            {
                _oneFrameComponents.Add(type);
            }
        }
    }
    
    /// <summary>
    /// Gets operations for a component type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IComponentOperations? Get(Type type)
    {
        return _operations.TryGetValue(type, out var operations) ? operations : null;
    }
    
    /// <summary>
    /// Checks if a component type has the OneFrame attribute.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsOneFrame(Type type)
    {
        return _oneFrameComponents.Contains(type);
    }
    
    /// <summary>
    /// Copies a component between chunks if the type is registered.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCopy(Type componentType, Chunk fromChunk, int fromIndex, Chunk toChunk, int toIndex)
    {
        var operations = Get(componentType);
        if (operations != null)
        {
            operations.Copy(fromChunk, fromIndex, toChunk, toIndex);
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Gets all registered one-frame component types.
    /// </summary>
    public static IEnumerable<Type> GetOneFrameComponents()
    {
        return _oneFrameComponents;
    }
}