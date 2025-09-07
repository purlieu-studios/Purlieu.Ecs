using System.Runtime.CompilerServices;

namespace PurlieuEcs.Core;

/// <summary>
/// Extension methods to make World usage more convenient and fluent, similar to Arch ECS
/// </summary>
public static class WorldExtensions
{
    /// <summary>
    /// Creates an entity with a single component in one call
    /// Usage: world.Create(new Position(0, 0))
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entity Create<T>(this World world, T component) where T : unmanaged
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, component);
        return entity;
    }
    
    /// <summary>
    /// Creates an entity with two components in one call
    /// Usage: world.Create(new Position(0, 0), new Velocity(1, 1))
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entity Create<T1, T2>(this World world, T1 component1, T2 component2) 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, component1);
        world.AddComponent(entity, component2);
        return entity;
    }
    
    /// <summary>
    /// Creates an entity with three components in one call
    /// Usage: world.Create(new Position(0, 0), new Velocity(1, 1), new Health(100))
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entity Create<T1, T2, T3>(this World world, T1 component1, T2 component2, T3 component3) 
        where T1 : unmanaged 
        where T2 : unmanaged 
        where T3 : unmanaged
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, component1);
        world.AddComponent(entity, component2);
        world.AddComponent(entity, component3);
        return entity;
    }
    
    /// <summary>
    /// Creates an entity with four components in one call
    /// Usage: world.Create(new Position(0, 0), new Velocity(1, 1), new Health(100), new Player())
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entity Create<T1, T2, T3, T4>(this World world, T1 component1, T2 component2, T3 component3, T4 component4) 
        where T1 : unmanaged 
        where T2 : unmanaged 
        where T3 : unmanaged 
        where T4 : unmanaged
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, component1);
        world.AddComponent(entity, component2);
        world.AddComponent(entity, component3);
        world.AddComponent(entity, component4);
        return entity;
    }
    
    /// <summary>
    /// Fluent API for checking if entity has component
    /// Usage: if (world.Has<Position>(entity)) { ... }
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Has<T>(this World world, Entity entity) where T : unmanaged
    {
        return world.HasComponent<T>(entity);
    }
    
    /// <summary>
    /// Fluent API for getting component reference
    /// Usage: ref var pos = ref world.Get<Position>(entity);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T Get<T>(this World world, Entity entity) where T : unmanaged
    {
        return ref world.GetComponent<T>(entity);
    }
    
    /// <summary>
    /// Fluent API for adding component
    /// Usage: world.Add(entity, new Position(0, 0));
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static World Add<T>(this World world, Entity entity, T component) where T : unmanaged
    {
        world.AddComponent(entity, component);
        return world;
    }
    
    /// <summary>
    /// Fluent API for removing component
    /// Usage: world.Remove<Position>(entity);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static World Remove<T>(this World world, Entity entity) where T : unmanaged
    {
        world.RemoveComponent<T>(entity);
        return world;
    }
    
    /// <summary>
    /// Fluent API for destroying entity
    /// Usage: world.Destroy(entity);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static World Destroy(this World world, Entity entity)
    {
        world.DestroyEntity(entity);
        return world;
    }
    
    /// <summary>
    /// Check if entity is alive with shorter method name
    /// Usage: if (world.IsAlive(entity)) { ... }
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Alive(this World world, Entity entity)
    {
        return world.IsAlive(entity);
    }
}