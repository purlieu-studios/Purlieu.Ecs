using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using PurlieuEcs.Core;

namespace PurlieuEcs.Query;

/// <summary>
/// Extension methods for fluent query execution with inline delegates.
/// Provides Arch ECS-style ForEach methods with our performance optimizations.
/// </summary>
public static class QueryExtensions
{
    #region Single Component Queries
    
    /// <summary>
    /// Executes an action for each entity with the specified component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<T1>(this WorldQuery query, ForEachDelegate<T1> action)
        where T1 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var entities = chunk.GetEntitySpan();
            var components1 = chunk.GetSpan<T1>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(entities[i], ref components1[i]);
            }
        }
    }
    
    /// <summary>
    /// Executes an action for each entity with the specified component (without entity parameter).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<T1>(this WorldQuery query, ForEachRefDelegate<T1> action)
        where T1 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var components1 = chunk.GetSpan<T1>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(ref components1[i]);
            }
        }
    }
    
    #endregion
    
    #region Two Component Queries
    
    /// <summary>
    /// Executes an action for each entity with the specified components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<T1, T2>(this WorldQuery query, ForEachDelegate<T1, T2> action)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var entities = chunk.GetEntitySpan();
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(entities[i], ref components1[i], ref components2[i]);
            }
        }
    }
    
    /// <summary>
    /// Executes an action for each entity with the specified components (without entity parameter).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<T1, T2>(this WorldQuery query, ForEachRefDelegate<T1, T2> action)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(ref components1[i], ref components2[i]);
            }
        }
    }
    
    #endregion
    
    #region Three Component Queries
    
    /// <summary>
    /// Executes an action for each entity with the specified components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<T1, T2, T3>(this WorldQuery query, ForEachDelegate<T1, T2, T3> action)
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var entities = chunk.GetEntitySpan();
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            var components3 = chunk.GetSpan<T3>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(entities[i], ref components1[i], ref components2[i], ref components3[i]);
            }
        }
    }
    
    /// <summary>
    /// Executes an action for each entity with the specified components (without entity parameter).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<T1, T2, T3>(this WorldQuery query, ForEachRefDelegate<T1, T2, T3> action)
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            var components3 = chunk.GetSpan<T3>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(ref components1[i], ref components2[i], ref components3[i]);
            }
        }
    }
    
    #endregion
    
    #region Four Component Queries
    
    /// <summary>
    /// Executes an action for each entity with the specified components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<T1, T2, T3, T4>(this WorldQuery query, ForEachDelegate<T1, T2, T3, T4> action)
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var entities = chunk.GetEntitySpan();
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            var components3 = chunk.GetSpan<T3>();
            var components4 = chunk.GetSpan<T4>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(entities[i], ref components1[i], ref components2[i], ref components3[i], ref components4[i]);
            }
        }
    }
    
    #endregion
    
    #region SIMD-Aware Queries
    
    /// <summary>
    /// Executes SIMD-optimized or scalar processing based on chunk capabilities.
    /// </summary>
    public static void ForEachSimd<T1, T2>(
        this WorldQuery query,
        SimdProcessDelegate<T1, T2> simdProcessor,
        ForEachRefDelegate<T1, T2> scalarFallback)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.IsSimdAligned() && Vector.IsHardwareAccelerated)
            {
                simdProcessor(chunk);
            }
            else
            {
                var components1 = chunk.GetSpan<T1>();
                var components2 = chunk.GetSpan<T2>();
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    scalarFallback(ref components1[i], ref components2[i]);
                }
            }
        }
    }
    
    /// <summary>
    /// Executes vectorized operations on numeric component data.
    /// </summary>
    public static unsafe void ForEachVectorized<T>(
        this WorldQuery query,
        VectorProcessDelegate<T> processor)
        where T : unmanaged
    {
        if (!Vector.IsHardwareAccelerated)
        {
            throw new NotSupportedException("Hardware SIMD acceleration is not available");
        }
        
        foreach (var chunk in query.ChunksStack())
        {
            if (!chunk.IsSimdAligned())
                continue;
                
            var span = chunk.GetSpan<T>();
            if (span.Length == 0)
                continue;
                
            fixed (T* ptr = span)
            {
                int vectorSize = Vector<float>.Count;
                int vectorIterations = span.Length / vectorSize;
                
                for (int i = 0; i < vectorIterations; i++)
                {
                    processor(ptr + (i * vectorSize), vectorSize);
                }
                
                // Process remaining elements
                int remaining = span.Length % vectorSize;
                if (remaining > 0)
                {
                    processor(ptr + (vectorIterations * vectorSize), remaining);
                }
            }
        }
    }
    
    #endregion
    
    #region Parallel Queries
    
    /// <summary>
    /// Executes an action in parallel for each matching chunk.
    /// </summary>
    public static void ParallelForEach<T1, T2>(
        this WorldQuery query,
        ForEachRefDelegate<T1, T2> action)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        var chunks = query.Chunks();
        
        Parallel.ForEach((IEnumerable<Chunk>)chunks, chunk =>
        {
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(ref components1[i], ref components2[i]);
            }
        });
    }
    
    /// <summary>
    /// Executes an action in parallel with configurable options.
    /// </summary>
    public static void ParallelForEach<T1, T2>(
        this WorldQuery query,
        ParallelOptions options,
        ForEachRefDelegate<T1, T2> action)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        var chunks = query.Chunks();
        
        Parallel.ForEach((IEnumerable<Chunk>)chunks, options, chunk =>
        {
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(ref components1[i], ref components2[i]);
            }
        });
    }
    
    #endregion
    
    #region Indexed Queries
    
    /// <summary>
    /// Executes an action with chunk and local index information.
    /// </summary>
    public static void ForEachIndexed<T1>(
        this WorldQuery query,
        ForEachIndexedDelegate<T1> action)
        where T1 : unmanaged
    {
        int globalIndex = 0;
        foreach (var chunk in query.ChunksStack())
        {
            var entities = chunk.GetEntitySpan();
            var components1 = chunk.GetSpan<T1>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                action(entities[i], ref components1[i], globalIndex++, i);
            }
        }
    }
    
    #endregion
    
    #region Bulk Operations
    
    /// <summary>
    /// Processes all matching entities in bulk using a single operation.
    /// </summary>
    public static void BulkUpdate<T>(
        this WorldQuery query,
        BulkUpdateDelegate<T> operation)
        where T : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var span = chunk.GetSpan<T>();
            operation(span);
        }
    }
    
    /// <summary>
    /// Sets all matching components to a specific value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetAll<T>(this WorldQuery query, T value)
        where T : unmanaged
    {
        foreach (var chunk in query.ChunksStack())
        {
            var span = chunk.GetSpan<T>();
            span.Fill(value);
        }
    }
    
    #endregion
}

#region Delegate Definitions

// Single component delegates
public delegate void ForEachDelegate<T1>(Entity entity, ref T1 c1) where T1 : unmanaged;
public delegate void ForEachRefDelegate<T1>(ref T1 c1) where T1 : unmanaged;

// Two component delegates
public delegate void ForEachDelegate<T1, T2>(Entity entity, ref T1 c1, ref T2 c2) 
    where T1 : unmanaged where T2 : unmanaged;
public delegate void ForEachRefDelegate<T1, T2>(ref T1 c1, ref T2 c2)
    where T1 : unmanaged where T2 : unmanaged;

// Three component delegates
public delegate void ForEachDelegate<T1, T2, T3>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3)
    where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged;
public delegate void ForEachRefDelegate<T1, T2, T3>(ref T1 c1, ref T2 c2, ref T3 c3)
    where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged;

// Four component delegates
public delegate void ForEachDelegate<T1, T2, T3, T4>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4)
    where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged;

// SIMD delegates
public delegate void SimdProcessDelegate<T1, T2>(Chunk chunk)
    where T1 : unmanaged where T2 : unmanaged;

public unsafe delegate void VectorProcessDelegate<T>(T* data, int count)
    where T : unmanaged;

// Indexed delegate
public delegate void ForEachIndexedDelegate<T1>(Entity entity, ref T1 c1, int globalIndex, int localIndex)
    where T1 : unmanaged;

// Bulk operation delegate
public delegate void BulkUpdateDelegate<T>(Span<T> components)
    where T : unmanaged;

#endregion