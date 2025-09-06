using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic.Events;

namespace Purlieu.Logic;

/// <summary>
/// Bootstrap class for registering Logic layer components with the ECS framework.
/// Call this during application startup to ensure optimal performance.
/// </summary>
public static class LogicBootstrap
{
    /// <summary>
    /// Registers all Logic layer components and events for optimal performance.
    /// Call this once during application initialization.
    /// </summary>
    public static void RegisterComponents(World world)
    {
        // Register SIMD-compatible movement components
        world.RegisterComponent<Position>();
        world.RegisterComponent<Velocity>();
        world.RegisterComponent<Force>();
        
        // Register one-frame components
        world.RegisterComponent<MoveIntent>();
        world.RegisterComponent<Stunned>();
        
        // Register events
        world.RegisterComponent<PositionChangedEvent>();
        world.RegisterComponent<PositionChangedIntent>();
    }
}