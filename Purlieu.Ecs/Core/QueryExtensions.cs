using System.Runtime.CompilerServices;
using PurlieuEcs.Query;

namespace PurlieuEcs.Core;

/// <summary>
/// Delegate types for query processing
/// </summary>
public delegate void QueryAction<T>(Entity entity, ref T component) where T : unmanaged;
public delegate void QueryAction<T1, T2>(Entity entity, ref T1 component1, ref T2 component2) where T1 : unmanaged where T2 : unmanaged;
public delegate void QueryAction<T1, T2, T3>(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged;
public delegate void QueryAction<T1, T2, T3, T4>(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged;

/// <summary>
/// Extension methods for easy query processing with lambda expressions
/// </summary>
public static class QueryExtensions
{
    /// <summary>
    /// Process entities with a single component using a lambda
    /// Usage: world.Query<Position>((entity, ref pos) => pos.X += 1.0f);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Query<T>(this World world, QueryAction<T> action) where T : unmanaged
    {
        var query = world.Query().With<T>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var components = chunk.GetSpan<T>();
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < components.Length; i++)
            {
                action(entities[i], ref components[i]);
            }
        }
    }
    
    /// <summary>
    /// Process entities with two components using a lambda
    /// Usage: world.Query<Position, Velocity>((entity, ref pos, ref vel) => {
    ///     pos.X += vel.X;
    ///     pos.Y += vel.Y;
    /// });
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Query<T1, T2>(this World world, QueryAction<T1, T2> action) 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        var query = world.Query().With<T1>().With<T2>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < components1.Length; i++)
            {
                action(entities[i], ref components1[i], ref components2[i]);
            }
        }
    }
    
    /// <summary>
    /// Process entities with three components using a lambda
    /// Usage: world.Query<Position, Velocity, Health>((entity, ref pos, ref vel, ref health) => {
    ///     pos.X += vel.X;
    ///     pos.Y += vel.Y;
    ///     health.Current -= 1;
    /// });
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Query<T1, T2, T3>(this World world, QueryAction<T1, T2, T3> action) 
        where T1 : unmanaged 
        where T2 : unmanaged 
        where T3 : unmanaged
    {
        var query = world.Query().With<T1>().With<T2>().With<T3>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            var components3 = chunk.GetSpan<T3>();
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < components1.Length; i++)
            {
                action(entities[i], ref components1[i], ref components2[i], ref components3[i]);
            }
        }
    }
    
    /// <summary>
    /// Process entities with four components using a lambda
    /// Usage: world.Query<Position, Velocity, Health, Player>((entity, ref pos, ref vel, ref health, ref player) => {
    ///     pos.X += vel.X * player.SpeedMultiplier;
    ///     pos.Y += vel.Y * player.SpeedMultiplier;
    ///     health.Current -= 1;
    ///     player.Experience += 1;
    /// });
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Query<T1, T2, T3, T4>(this World world, QueryAction<T1, T2, T3, T4> action) 
        where T1 : unmanaged 
        where T2 : unmanaged 
        where T3 : unmanaged 
        where T4 : unmanaged
    {
        var query = world.Query().With<T1>().With<T2>().With<T3>().With<T4>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var components1 = chunk.GetSpan<T1>();
            var components2 = chunk.GetSpan<T2>();
            var components3 = chunk.GetSpan<T3>();
            var components4 = chunk.GetSpan<T4>();
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < components1.Length; i++)
            {
                action(entities[i], ref components1[i], ref components2[i], ref components3[i], ref components4[i]);
            }
        }
    }
    
    /// <summary>
    /// Count entities matching the query
    /// Usage: int count = world.Count<Position>();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count<T>(this World world) where T : unmanaged
    {
        var query = world.Query().With<T>();
        int count = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        
        return count;
    }
    
    /// <summary>
    /// Count entities matching the two-component query
    /// Usage: int count = world.Count<Position, Velocity>();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count<T1, T2>(this World world) 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        var query = world.Query().With<T1>().With<T2>();
        int count = 0;
        
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
        }
        
        return count;
    }
    
    /// <summary>
    /// Check if any entities match the query
    /// Usage: bool hasMovingEntities = world.Any<Position, Velocity>();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Any<T>(this World world) where T : unmanaged
    {
        var query = world.Query().With<T>();
        
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.Count > 0) return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if any entities match the two-component query
    /// Usage: bool hasMovingEntities = world.Any<Position, Velocity>();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Any<T1, T2>(this World world) 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        var query = world.Query().With<T1>().With<T2>();
        
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.Count > 0) return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get the first entity matching the query (or default if none)
    /// Usage: var firstPlayer = world.First<Player>();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entity First<T>(this World world) where T : unmanaged
    {
        var query = world.Query().With<T>();
        
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.Count > 0)
            {
                var entities = chunk.GetEntities();
                return entities[0];
            }
        }
        
        return default;
    }
}