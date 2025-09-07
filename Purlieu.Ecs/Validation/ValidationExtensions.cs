using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

namespace PurlieuEcs.Validation;

/// <summary>
/// Extension methods for validation operations with zero-allocation design
/// </summary>
internal static class ValidationExtensions
{
    /// <summary>
    /// High-performance check if a type is unmanaged
    /// Uses compile-time generic constraints where possible
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsUnmanagedType(this Type type)
    {
        // Fast path for primitives
        if (type.IsPrimitive) return true;
        
        // Fast path for known unmanaged types
        if (type == typeof(DateTime) || 
            type == typeof(TimeSpan) || 
            type == typeof(Guid) ||
            type == typeof(IntPtr) ||
            type == typeof(UIntPtr))
            return true;
        
        // Check if it's an enum
        if (type.IsEnum) return true;
        
        // Check if it's a pointer type
        if (type.IsPointer) return true;
        
        // For value types, check if all fields are unmanaged
        if (type.IsValueType && !type.IsGenericType)
        {
            return CheckUnmanagedRecursive(type, new HashSet<Type>());
        }
        
        return false;
    }
    
    private static bool CheckUnmanagedRecursive(Type type, HashSet<Type> visited)
    {
        // Prevent infinite recursion
        if (visited.Contains(type)) return true;
        visited.Add(type);
        
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        
        foreach (var field in fields)
        {
            var fieldType = field.FieldType;
            
            // Reference types are not unmanaged
            if (!fieldType.IsValueType) return false;
            
            // Recursively check field types
            if (!fieldType.IsPrimitive && 
                !fieldType.IsEnum && 
                !fieldType.IsPointer &&
                fieldType != typeof(DateTime) &&
                fieldType != typeof(TimeSpan) &&
                fieldType != typeof(Guid) &&
                fieldType != typeof(IntPtr) &&
                fieldType != typeof(UIntPtr))
            {
                if (!CheckUnmanagedRecursive(fieldType, visited))
                    return false;
            }
        }
        
        return true;
    }
}

/// <summary>
/// Validation context for tracking validation state across operations
/// Uses object pooling to minimize allocations
/// </summary>
internal class ValidationContext
{
    private static readonly ConcurrentQueue<ValidationContext> _pool = new();
    private readonly List<ValidationResult> _results = new();
    
    public IReadOnlyList<ValidationResult> Results => _results;
    public bool HasErrors => _results.Any(r => !r.IsValid && r.Severity == ValidationSeverity.Error);
    public bool HasWarnings => _results.Any(r => r.Severity == ValidationSeverity.Warning);
    
    public static ValidationContext Rent()
    {
        if (_pool.TryDequeue(out var context))
        {
            return context;
        }
        return new ValidationContext();
    }
    
    public static void Return(ValidationContext context)
    {
        context._results.Clear();
        _pool.Enqueue(context);
    }
    
    public void AddResult(ValidationResult result)
    {
        _results.Add(result);
    }
    
    public ValidationResult GetCombinedResult()
    {
        if (_results.Count == 0) return ValidationResult.Valid;
        
        var hasErrors = HasErrors;
        var hasWarnings = HasWarnings;
        
        if (hasErrors)
        {
            var errorCount = _results.Count(r => !r.IsValid && r.Severity == ValidationSeverity.Error);
            return ValidationResult.Error($"Validation failed with {errorCount} error(s)");
        }
        
        if (hasWarnings)
        {
            var warningCount = _results.Count(r => r.Severity == ValidationSeverity.Warning);
            return ValidationResult.Warning($"Validation completed with {warningCount} warning(s)");
        }
        
        return ValidationResult.Valid;
    }
}