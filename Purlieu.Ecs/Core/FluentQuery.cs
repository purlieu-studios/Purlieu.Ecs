using System.Runtime.CompilerServices;
using PurlieuEcs.Query;

namespace PurlieuEcs.Core;

/// <summary>
/// Fluent query builder that provides a more intuitive API for complex queries
/// </summary>
public struct FluentQuery
{
    private readonly World _world;
    private readonly WorldQuery _query;
    
    internal FluentQuery(World world)
    {
        _world = world;
        _query = world.Query();
    }
    
    /// <summary>
    /// Add a required component to the query
    /// Usage: world.Select().With<Position>().With<Velocity>()
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FluentQuery With<T>() where T : unmanaged
    {
        _query.With<T>();
        return this;
    }
    
    /// <summary>
    /// Add an excluded component to the query
    /// Usage: world.Select().With<Position>().Without<Player>()
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FluentQuery Without<T>() where T : unmanaged
    {
        _query.Without<T>();
        return this;
    }
    
    /// <summary>
    /// Execute the query with a lambda for single component
    /// Usage: world.Select().With<Position>().ForEach((entity, ref pos) => pos.X += 1);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ForEach<T>(QueryAction<T> action) where T : unmanaged
    {
        foreach (var chunk in _query.ChunksStack())
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
    /// Execute the query with a lambda for two components
    /// Usage: world.Select().With<Position>().With<Velocity>().ForEach((entity, ref pos, ref vel) => {
    ///     pos.X += vel.X;
    ///     pos.Y += vel.Y;
    /// });
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ForEach<T1, T2>(QueryAction<T1, T2> action) 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        foreach (var chunk in _query.ChunksStack())
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
    /// Execute the query with a lambda for three components
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ForEach<T1, T2, T3>(QueryAction<T1, T2, T3> action) 
        where T1 : unmanaged 
        where T2 : unmanaged 
        where T3 : unmanaged
    {
        foreach (var chunk in _query.ChunksStack())
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
    /// Count matching entities
    /// Usage: int count = world.Select().With<Position>().Count();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Count()
    {
        int count = 0;
        foreach (var chunk in _query.ChunksStack())
        {
            count += chunk.Count;
        }
        return count;
    }
    
    /// <summary>
    /// Check if any entities match
    /// Usage: bool hasEnemies = world.Select().With<Enemy>().Any();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Any()
    {
        foreach (var chunk in _query.ChunksStack())
        {
            if (chunk.Count > 0) return true;
        }
        return false;
    }
    
    /// <summary>
    /// Get first matching entity
    /// Usage: var player = world.Select().With<Player>().First();
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity First()
    {
        foreach (var chunk in _query.ChunksStack())
        {
            if (chunk.Count > 0)
            {
                var entities = chunk.GetEntities();
                return entities[0];
            }
        }
        return default;
    }
    
    /// <summary>
    /// Get all matching entities (allocates array)
    /// Usage: var enemies = world.Select().With<Enemy>().ToArray();
    /// </summary>
    public Entity[] ToArray()
    {
        var entities = new List<Entity>();
        
        foreach (var chunk in _query.ChunksStack())
        {
            var chunkEntities = chunk.GetEntities();
            for (int i = 0; i < chunkEntities.Length; i++)
            {
                entities.Add(chunkEntities[i]);
            }
        }
        
        return entities.ToArray();
    }
    
    /// <summary>
    /// Get the underlying WorldQuery for advanced usage
    /// Usage: world.Select().With<Position>().AsQuery().ChunksStack()
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldQuery AsQuery() => _query;
}

/// <summary>
/// Extension method to start fluent query building
/// </summary>
public static class FluentQueryExtensions
{
    /// <summary>
    /// Start building a fluent query
    /// Usage: world.Select().With<Position>().With<Velocity>().ForEach(...);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FluentQuery Select(this World world)
    {
        return new FluentQuery(world);
    }
}