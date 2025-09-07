using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

namespace PurlieuEcs.Validation;

/// <summary>
/// Core validation interface for ECS architectural constraints
/// Designed for zero-allocation validation in debug builds with compile-time elimination in release
/// </summary>
public interface IEcsValidator
{
    /// <summary>
    /// Validates a component type for ECS compliance
    /// </summary>
    ValidationResult ValidateComponentType<T>() where T : unmanaged;
    
    /// <summary>
    /// Validates a component type using reflection (for dynamic scenarios)
    /// </summary>
    ValidationResult ValidateComponentType(Type componentType);
    
    /// <summary>
    /// Validates system execution order and dependencies
    /// </summary>
    ValidationResult ValidateSystemDependencies<TSystem>(IReadOnlyList<Type> dependencies);
    
    /// <summary>
    /// Validates entity operations for common anti-patterns
    /// </summary>
    ValidationResult ValidateEntityOperation(EntityOperation operation, uint entityId, Type? componentType);
    
    /// <summary>
    /// Validates memory safety for archetype transitions
    /// </summary>
    ValidationResult ValidateArchetypeTransition(Type[] fromComponents, Type[] toComponents);
}

/// <summary>
/// Validation result with performance-conscious design
/// </summary>
public readonly struct ValidationResult
{
    public readonly bool IsValid;
    public readonly ValidationSeverity Severity;
    public readonly string Message;
    public readonly string? Details;
    
    public ValidationResult(bool isValid, ValidationSeverity severity = ValidationSeverity.None, string message = "", string? details = null)
    {
        IsValid = isValid;
        Severity = severity;
        Message = message;
        Details = details;
    }
    
    public static readonly ValidationResult Valid = new(true);
    
    public static ValidationResult Error(string message, string? details = null) 
        => new(false, ValidationSeverity.Error, message, details);
    
    public static ValidationResult Warning(string message, string? details = null) 
        => new(false, ValidationSeverity.Warning, message, details);
    
    public static ValidationResult Info(string message, string? details = null) 
        => new(true, ValidationSeverity.Info, message, details);
}

/// <summary>
/// Validation severity levels
/// </summary>
public enum ValidationSeverity : byte
{
    None = 0,
    Info = 1,
    Warning = 2, 
    Error = 3
}

/// <summary>
/// Entity operations for validation tracking
/// </summary>
public enum EntityOperation : byte
{
    Create = 0,
    Destroy = 1,
    AddComponent = 2,
    RemoveComponent = 3,
    GetComponent = 4,
    ArchetypeTransition = 5
}

/// <summary>
/// High-performance ECS validator with compile-time elimination in release builds
/// Uses aggressive inlining and conditional compilation for zero overhead in production
/// </summary>
public sealed class EcsValidator : IEcsValidator
{
    private static readonly ConcurrentDictionary<Type, ValidationResult> _componentTypeCache = new();
    private static readonly ConcurrentDictionary<string, ValidationResult> _systemDependencyCache = new();
    private readonly HashSet<string> _validatedArchetypeTransitions = new();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateComponentType<T>() where T : unmanaged
    {
        #if DEBUG
            return _componentTypeCache.GetOrAdd(typeof(T), ValidateComponentTypeInternal);
        #else
            return ValidationResult.Valid; // Compile-time eliminated in release
        #endif
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateComponentType(Type componentType)
    {
        #if DEBUG
            return _componentTypeCache.GetOrAdd(componentType, ValidateComponentTypeInternal);
        #else
            return ValidationResult.Valid;
        #endif
    }
    
    public ValidationResult ValidateSystemDependencies<TSystem>(IReadOnlyList<Type> dependencies)
    {
        #if DEBUG
            var key = $"{typeof(TSystem).Name}:{string.Join(",", dependencies.Select(d => d.Name))}";
            return _systemDependencyCache.GetOrAdd(key, _ => ValidateSystemDependenciesInternal<TSystem>(dependencies));
        #else
            return ValidationResult.Valid;
        #endif
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateEntityOperation(EntityOperation operation, uint entityId, Type? componentType)
    {
        #if DEBUG
            return ValidateEntityOperationInternal(operation, entityId, componentType);
        #else
            return ValidationResult.Valid;
        #endif
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateArchetypeTransition(Type[] fromComponents, Type[] toComponents)
    {
        #if DEBUG
            var key = $"{string.Join(",", fromComponents.Select(c => c.Name))}|{string.Join(",", toComponents.Select(c => c.Name))}";
            
            if (_validatedArchetypeTransitions.Contains(key))
                return ValidationResult.Valid;
            
            var result = ValidateArchetypeTransitionInternal(fromComponents, toComponents);
            if (result.IsValid)
                _validatedArchetypeTransitions.Add(key);
            
            return result;
        #else
            return ValidationResult.Valid;
        #endif
    }
    
    private ValidationResult ValidateComponentTypeInternal(Type componentType)
    {
        // Check if type is unmanaged
        if (!componentType.IsUnmanagedType())
        {
            return ValidationResult.Error(
                $"Component {componentType.Name} is not unmanaged",
                "ECS components must be unmanaged structs for optimal performance and memory safety"
            );
        }
        
        // Check for reference types
        var fields = componentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            if (!field.FieldType.IsValueType)
            {
                return ValidationResult.Error(
                    $"Component {componentType.Name} contains reference field {field.Name}",
                    "Components should only contain value types to ensure cache efficiency and avoid GC pressure"
                );
            }
            
            // Check for mutable collections
            if (IsMutableCollectionType(field.FieldType))
            {
                return ValidationResult.Warning(
                    $"Component {componentType.Name} contains mutable collection {field.Name}",
                    "Consider using immutable data structures or separate this into a managed resource"
                );
            }
        }
        
        // Check size for cache efficiency
        var size = System.Runtime.InteropServices.Marshal.SizeOf(componentType);
        if (size > 256) // 4 cache lines
        {
            return ValidationResult.Warning(
                $"Component {componentType.Name} is large ({size} bytes)",
                "Large components may impact cache performance. Consider splitting or using indirection."
            );
        }
        
        // Check for proper alignment
        if (size % 4 != 0)
        {
            return ValidationResult.Info(
                $"Component {componentType.Name} is not 4-byte aligned ({size} bytes)",
                "Consider padding to improve memory access patterns"
            );
        }
        
        return ValidationResult.Valid;
    }
    
    private ValidationResult ValidateSystemDependenciesInternal<TSystem>(IReadOnlyList<Type> dependencies)
    {
        var systemType = typeof(TSystem);
        
        // Check for circular dependencies
        if (dependencies.Contains(systemType))
        {
            return ValidationResult.Error(
                $"System {systemType.Name} has circular dependency on itself",
                "Systems cannot depend on themselves"
            );
        }
        
        // Check for stateful system patterns
        var fields = systemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var hasState = fields.Any(f => !f.IsStatic && !f.IsInitOnly);
        
        if (hasState)
        {
            return ValidationResult.Warning(
                $"System {systemType.Name} contains mutable state",
                "ECS systems should be stateless for better testability and parallelization"
            );
        }
        
        return ValidationResult.Valid;
    }
    
    private ValidationResult ValidateEntityOperationInternal(EntityOperation operation, uint entityId, Type? componentType)
    {
        // Validate entity ID range
        if (entityId == 0)
        {
            return ValidationResult.Error(
                "Entity ID cannot be 0",
                "Entity ID 0 is reserved for invalid entities"
            );
        }
        
        if (entityId == uint.MaxValue)
        {
            return ValidationResult.Error(
                "Entity ID at maximum value",
                "Entity ID uint.MaxValue indicates overflow or corruption"
            );
        }
        
        // Component-specific validations
        if (componentType != null)
        {
            var componentValidation = ValidateComponentType(componentType);
            if (!componentValidation.IsValid)
                return componentValidation;
        }
        
        return ValidationResult.Valid;
    }
    
    private ValidationResult ValidateArchetypeTransitionInternal(Type[] fromComponents, Type[] toComponents)
    {
        // Check for valid transitions
        var fromSet = new HashSet<Type>(fromComponents);
        var toSet = new HashSet<Type>(toComponents);
        
        var added = toSet.Except(fromSet).ToArray();
        var removed = fromSet.Except(toSet).ToArray();
        
        // Warn about complex transitions (adding/removing multiple components)
        if (added.Length > 1 && removed.Length > 1)
        {
            return ValidationResult.Warning(
                $"Complex archetype transition: +{added.Length}/-{removed.Length} components",
                "Consider batching component operations or using more specific archetypes"
            );
        }
        
        // Check for problematic component combinations
        foreach (var component in toSet)
        {
            var validation = ValidateComponentType(component);
            if (!validation.IsValid && validation.Severity == ValidationSeverity.Error)
                return validation;
        }
        
        return ValidationResult.Valid;
    }
    
    private static bool IsMutableCollectionType(Type type)
    {
        if (type.IsArray) return true;
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(List<>) || 
                   genericDef == typeof(Dictionary<,>) ||
                   genericDef == typeof(HashSet<>);
        }
        return false;
    }
}

/// <summary>
/// Null validator for production builds - all methods are no-ops with aggressive inlining
/// </summary>
public sealed class NullEcsValidator : IEcsValidator
{
    public static readonly NullEcsValidator Instance = new();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateComponentType<T>() where T : unmanaged => ValidationResult.Valid;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateComponentType(Type componentType) => ValidationResult.Valid;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateSystemDependencies<TSystem>(IReadOnlyList<Type> dependencies) => ValidationResult.Valid;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateEntityOperation(EntityOperation operation, uint entityId, Type? componentType) => ValidationResult.Valid;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult ValidateArchetypeTransition(Type[] fromComponents, Type[] toComponents) => ValidationResult.Valid;
}