using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Factory for creating component storage instances without reflection.
/// Uses pre-registered factories for known component types.
/// </summary>
internal static class ComponentStorageFactory
{
    private static readonly Dictionary<Type, Func<int, IComponentStorage>> _factories = new();
    private static readonly object _lock = new object();
    
    /// <summary>
    /// Registers a component storage factory for the specified type.
    /// </summary>
    public static void Register<T>() where T : unmanaged
    {
        var componentType = typeof(T);
        
        lock (_lock)
        {
            if (!_factories.ContainsKey(componentType))
            {
                _factories[componentType] = capacity => new ComponentStorage<T>(capacity);
            }
        }
    }
    
    /// <summary>
    /// Creates component storage for the specified type using registered factory.
    /// Falls back to reflection if no factory is registered.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IComponentStorage Create(Type componentType, int capacity)
    {
        // Try factory first (no allocation, no reflection)
        if (_factories.TryGetValue(componentType, out var factory))
        {
            return factory(capacity);
        }
        
        // Fallback to reflection for unregistered types
        return CreateUsingReflection(componentType, capacity);
    }
    
    /// <summary>
    /// Creates component storage using reflection (fallback for unregistered types).
    /// </summary>
    private static IComponentStorage CreateUsingReflection(Type componentType, int capacity)
    {
        var storageType = typeof(ComponentStorage<>).MakeGenericType(componentType);
        return (IComponentStorage)Activator.CreateInstance(storageType, capacity)!;
    }
    
    /// <summary>
    /// Checks if a factory is registered for the specified type.
    /// </summary>
    public static bool IsRegistered(Type componentType)
    {
        return _factories.ContainsKey(componentType);
    }
    
    /// <summary>
    /// Gets the number of registered component types.
    /// </summary>
    public static int RegisteredCount => _factories.Count;
    
    /// <summary>
    /// Pre-registers common component types during static initialization.
    /// </summary>
    static ComponentStorageFactory()
    {
        // Auto-register test component types for benchmarks/tests
        Register<TestComponentA>();
        Register<TestComponentB>();
        Register<TestComponentC>();
        
        // Note: Game-specific components should be registered in the Logic layer
    }
}

// Helper struct definitions that might be missing
internal struct TestComponentA { public int Value; public int X, Y, Z; }
internal struct TestComponentB { public float X, Y; public double Timestamp; }  
internal struct TestComponentC { public bool Flag; }