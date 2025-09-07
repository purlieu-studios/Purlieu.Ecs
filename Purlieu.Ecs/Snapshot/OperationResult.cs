using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PurlieuEcs.Snapshot;

/// <summary>
/// Result type for operations that can fail without throwing exceptions.
/// Maintains deterministic behavior in ECS operations.
/// </summary>
public readonly struct OperationResult<T>
{
    private readonly T _value;
    private readonly string _error;
    
    public bool Success { get; }
    
    /// <summary>
    /// Gets the result value. Only valid when Success is true.
    /// </summary>
    public T Value => Success ? _value : throw new InvalidOperationException($"Cannot access Value when operation failed: {_error}");
    
    /// <summary>
    /// Gets the error message. Only valid when Success is false.
    /// </summary>
    public string Error => Success ? string.Empty : _error;
    
    private OperationResult(bool success, T value, string error)
    {
        Success = success;
        _value = value;
        _error = error;
    }
    
    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperationResult<T> Ok(T value) => new(true, value, string.Empty);
    
    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperationResult<T> Fail(string error) => new(false, default!, error);
    
    /// <summary>
    /// Implicitly converts a value to a successful result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator OperationResult<T>(T value) => Ok(value);
    
    /// <summary>
    /// Tries to get the value, returning true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = Success ? _value : default;
        return Success;
    }
    
    /// <summary>
    /// Gets the value or returns the specified default value if the operation failed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValueOrDefault(T defaultValue = default!) => Success ? _value : defaultValue;
    
    public override string ToString() => Success ? $"Ok({_value})" : $"Fail({_error})";
}

/// <summary>
/// Result type for operations that don't return a value but can fail.
/// </summary>
public readonly struct OperationResult
{
    public bool Success { get; }
    public string Error { get; }
    
    private OperationResult(bool success, string error)
    {
        Success = success;
        Error = error;
    }
    
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperationResult Ok() => new(true, string.Empty);
    
    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperationResult Fail(string error) => new(false, error);
    
    public override string ToString() => Success ? "Ok" : $"Fail({Error})";
}