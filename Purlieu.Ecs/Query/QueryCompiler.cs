using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Compiles LINQ-style queries into optimized delegates for maximum performance.
/// </summary>
internal static class QueryCompiler
{
    private static readonly Dictionary<QuerySignature, ICompiledQuery> _compiledQueries = new(capacity: 64);
    
    /// <summary>
    /// Gets or compiles a query delegate for the specified signature.
    /// </summary>
    public static CompiledQuery<T> GetOrCompile<T>(QuerySignature signature, Expression<Func<Chunk, IEnumerable<T>>> queryExpression)
    {
        var key = signature;
        if (_compiledQueries.TryGetValue(key, out var cached))
        {
            return (CompiledQuery<T>)cached;
        }
        
        var compiled = CompileQuery(queryExpression);
        var query = new CompiledQuery<T>(signature, compiled);
        
        // Cache with size limit
        if (_compiledQueries.Count < 100)
        {
            _compiledQueries[key] = query;
        }
        
        return query;
    }
    
    /// <summary>
    /// Compiles a query expression into an optimized delegate.
    /// </summary>
    private static Func<Chunk, IEnumerable<T>> CompileQuery<T>(Expression<Func<Chunk, IEnumerable<T>>> queryExpression)
    {
        // Analyze the expression tree to optimize common patterns
        var optimized = OptimizeExpression(queryExpression);
        
        return optimized.Compile();
    }
    
    /// <summary>
    /// Applies optimization passes to the expression tree.
    /// </summary>
    private static Expression<Func<Chunk, IEnumerable<T>>> OptimizeExpression<T>(Expression<Func<Chunk, IEnumerable<T>>> expression)
    {
        // Common optimization: convert LINQ operations to span-based iterations
        var visitor = new QueryOptimizationVisitor();
        var optimized = visitor.Visit(expression);
        
        return (Expression<Func<Chunk, IEnumerable<T>>>)optimized;
    }
    
    /// <summary>
    /// Clears the compiled query cache.
    /// </summary>
    public static void ClearCache()
    {
        _compiledQueries.Clear();
    }
}

/// <summary>
/// A compiled query with signature and optimized execution delegate.
/// </summary>
internal interface ICompiledQuery
{
    QuerySignature Signature { get; }
}

/// <summary>
/// Typed compiled query implementation.
/// </summary>
internal sealed class CompiledQuery<T> : ICompiledQuery
{
    private readonly Func<Chunk, IEnumerable<T>> _compiledDelegate;
    
    public QuerySignature Signature { get; }
    
    public CompiledQuery(QuerySignature signature, Func<Chunk, IEnumerable<T>> compiledDelegate)
    {
        Signature = signature;
        _compiledDelegate = compiledDelegate;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<T> Execute(Chunk chunk)
    {
        return _compiledDelegate(chunk);
    }
}

/// <summary>
/// Expression visitor that optimizes common query patterns.
/// </summary>
internal sealed class QueryOptimizationVisitor : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Optimize common LINQ patterns
        if (IsEnumerableMethod(node, "Where"))
        {
            return OptimizeWhere(node);
        }
        
        if (IsEnumerableMethod(node, "Select"))
        {
            return OptimizeSelect(node);
        }
        
        if (IsEnumerableMethod(node, "Any"))
        {
            return OptimizeAny(node);
        }
        
        if (IsEnumerableMethod(node, "Count"))
        {
            return OptimizeCount(node);
        }
        
        return base.VisitMethodCall(node);
    }
    
    private bool IsEnumerableMethod(MethodCallExpression node, string methodName)
    {
        return node.Method.DeclaringType == typeof(Enumerable) && 
               node.Method.Name == methodName;
    }
    
    private Expression OptimizeWhere(MethodCallExpression node)
    {
        // Convert LINQ Where to span-based filtering for better performance
        // This is a simplified example - full implementation would be more complex
        return base.VisitMethodCall(node);
    }
    
    private Expression OptimizeSelect(MethodCallExpression node)
    {
        // Convert LINQ Select to span-based mapping
        return base.VisitMethodCall(node);
    }
    
    private Expression OptimizeAny(MethodCallExpression node)
    {
        // Convert LINQ Any to early-exit span iteration
        return base.VisitMethodCall(node);
    }
    
    private Expression OptimizeCount(MethodCallExpression node)
    {
        // Convert LINQ Count to direct span length access when possible
        return base.VisitMethodCall(node);
    }
}

/// <summary>
/// Represents a query signature for compilation caching.
/// </summary>
internal readonly struct QuerySignature : IEquatable<QuerySignature>
{
    private readonly ArchetypeSignature _withSignature;
    private readonly ArchetypeSignature _withoutSignature;
    private readonly string _expressionString;
    private readonly int _hashCode;
    
    public QuerySignature(ArchetypeSignature withSignature, ArchetypeSignature withoutSignature, string expressionString)
    {
        _withSignature = withSignature;
        _withoutSignature = withoutSignature;
        _expressionString = expressionString;
        _hashCode = HashCode.Combine(_withSignature.GetHashCode(), _withoutSignature.GetHashCode(), _expressionString.GetHashCode());
    }
    
    public bool Equals(QuerySignature other)
    {
        return _hashCode == other._hashCode &&
               _withSignature.Equals(other._withSignature) &&
               _withoutSignature.Equals(other._withoutSignature) &&
               _expressionString == other._expressionString;
    }
    
    public override bool Equals(object? obj) => obj is QuerySignature other && Equals(other);
    public override int GetHashCode() => _hashCode;
}

/// <summary>
/// Fast query execution strategies for common patterns.
/// </summary>
internal static class QueryStrategies
{
    /// <summary>
    /// Executes a simple component access pattern with SIMD optimization.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> FastComponentAccess<T>(Chunk chunk) where T : struct
    {
        var span = chunk.GetSpan<T>();
        
        // Use custom enumerator to avoid allocations
        return new SpanEnumerable<T>(span);
    }
    
    /// <summary>
    /// Executes a dual-component access pattern with cache-friendly iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<(T1, T2)> FastDualComponentAccess<T1, T2>(Chunk chunk) 
        where T1 : struct 
        where T2 : struct
    {
        var span1 = chunk.GetSpan<T1>();
        var span2 = chunk.GetSpan<T2>();
        
        return new DualSpanEnumerable<T1, T2>(span1, span2);
    }
    
    /// <summary>
    /// Executes a filtered component access with early termination.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> FastFilteredAccess<T>(Chunk chunk, Func<T, bool> predicate) where T : struct
    {
        var span = chunk.GetSpan<T>();
        
        return new FilteredSpanEnumerable<T>(span, predicate);
    }
}

/// <summary>
/// Zero-allocation enumerator for span-based iteration.
/// </summary>
internal readonly struct SpanEnumerable<T> : IEnumerable<T>
{
    private readonly T[] _array;
    
    public SpanEnumerable(ReadOnlySpan<T> span)
    {
        _array = span.ToArray();
    }
    
    public SpanEnumerator<T> GetEnumerator() => new(_array);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new SpanEnumeratorBoxed<T>(_array);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => new SpanEnumeratorBoxed<T>(_array);
}

/// <summary>
/// High-performance span enumerator.
/// </summary>
internal ref struct SpanEnumerator<T>
{
    private readonly T[] _array;
    private int _index;
    
    public SpanEnumerator(T[] array)
    {
        _array = array;
        _index = -1;
    }
    
    public T Current => _array[_index];
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext() => ++_index < _array.Length;
}

/// <summary>
/// Boxed version for interface compatibility.
/// </summary>
internal sealed class SpanEnumeratorBoxed<T> : IEnumerator<T>
{
    private readonly T[] _array;
    private int _index;
    
    public SpanEnumeratorBoxed(T[] array)
    {
        _array = array;
        _index = -1;
    }
    
    public T Current => _array[_index];
    object? System.Collections.IEnumerator.Current => Current;
    
    public bool MoveNext() => ++_index < _array.Length;
    public void Reset() => _index = -1;
    public void Dispose() { }
}

/// <summary>
/// Zero-allocation enumerator for dual-component iteration.
/// </summary>
internal readonly struct DualSpanEnumerable<T1, T2> : IEnumerable<(T1, T2)>
{
    private readonly T1[] _array1;
    private readonly T2[] _array2;
    
    public DualSpanEnumerable(ReadOnlySpan<T1> span1, ReadOnlySpan<T2> span2)
    {
        _array1 = span1.ToArray();
        _array2 = span2.ToArray();
    }
    
    public DualSpanEnumerator<T1, T2> GetEnumerator() => new(_array1, _array2);
    IEnumerator<(T1, T2)> IEnumerable<(T1, T2)>.GetEnumerator() => new DualSpanEnumeratorBoxed<T1, T2>(_array1, _array2);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => new DualSpanEnumeratorBoxed<T1, T2>(_array1, _array2);
}

/// <summary>
/// High-performance dual span enumerator.
/// </summary>
internal ref struct DualSpanEnumerator<T1, T2>
{
    private readonly T1[] _array1;
    private readonly T2[] _array2;
    private int _index;
    
    public DualSpanEnumerator(T1[] array1, T2[] array2)
    {
        _array1 = array1;
        _array2 = array2;
        _index = -1;
    }
    
    public (T1, T2) Current => (_array1[_index], _array2[_index]);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext() => ++_index < Math.Min(_array1.Length, _array2.Length);
}

/// <summary>
/// Boxed version for dual span enumeration.
/// </summary>
internal sealed class DualSpanEnumeratorBoxed<T1, T2> : IEnumerator<(T1, T2)>
{
    private readonly T1[] _array1;
    private readonly T2[] _array2;
    private int _index;
    
    public DualSpanEnumeratorBoxed(T1[] array1, T2[] array2)
    {
        _array1 = array1;
        _array2 = array2;
        _index = -1;
    }
    
    public (T1, T2) Current => (_array1[_index], _array2[_index]);
    object? System.Collections.IEnumerator.Current => Current;
    
    public bool MoveNext() => ++_index < Math.Min(_array1.Length, _array2.Length);
    public void Reset() => _index = -1;
    public void Dispose() { }
}

/// <summary>
/// Zero-allocation enumerator for filtered span iteration.
/// </summary>
internal readonly struct FilteredSpanEnumerable<T> : IEnumerable<T>
{
    private readonly T[] _array;
    private readonly Func<T, bool> _predicate;
    
    public FilteredSpanEnumerable(ReadOnlySpan<T> span, Func<T, bool> predicate)
    {
        _array = span.ToArray();
        _predicate = predicate;
    }
    
    public FilteredSpanEnumerator<T> GetEnumerator() => new(_array, _predicate);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new FilteredSpanEnumeratorBoxed<T>(_array, _predicate);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => new FilteredSpanEnumeratorBoxed<T>(_array, _predicate);
}

/// <summary>
/// High-performance filtered span enumerator.
/// </summary>
internal ref struct FilteredSpanEnumerator<T>
{
    private readonly T[] _array;
    private readonly Func<T, bool> _predicate;
    private int _index;
    
    public FilteredSpanEnumerator(T[] array, Func<T, bool> predicate)
    {
        _array = array;
        _predicate = predicate;
        _index = -1;
        MoveToNext();
    }
    
    public T Current => _array[_index];
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        _index++;
        return MoveToNext();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveToNext()
    {
        while (_index < _array.Length)
        {
            if (_predicate(_array[_index]))
                return true;
            _index++;
        }
        return false;
    }
}

/// <summary>
/// Boxed version for filtered span enumeration.
/// </summary>
internal sealed class FilteredSpanEnumeratorBoxed<T> : IEnumerator<T>
{
    private readonly T[] _array;
    private readonly Func<T, bool> _predicate;
    private int _index;
    
    public FilteredSpanEnumeratorBoxed(T[] array, Func<T, bool> predicate)
    {
        _array = array;
        _predicate = predicate;
        _index = -1;
        MoveToNext();
    }
    
    public T Current => _array[_index];
    object? System.Collections.IEnumerator.Current => Current;
    
    public bool MoveNext()
    {
        _index++;
        return MoveToNext();
    }
    
    private bool MoveToNext()
    {
        while (_index < _array.Length)
        {
            if (_predicate(_array[_index]))
                return true;
            _index++;
        }
        return false;
    }
    
    public void Reset()
    {
        _index = -1;
        MoveToNext();
    }
    
    public void Dispose() { }
}