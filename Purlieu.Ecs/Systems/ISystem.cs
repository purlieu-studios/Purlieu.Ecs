using PurlieuEcs.Core;

namespace PurlieuEcs.Systems;

/// <summary>
/// Base interface for all ECS systems.
/// Systems must be stateless and deterministic.
/// </summary>
public interface ISystem
{
    /// <summary>
    /// Updates the system for one frame.
    /// </summary>
    /// <param name="world">The ECS world containing entities and components</param>
    /// <param name="deltaTime">Time elapsed since last frame in seconds</param>
    void Update(World world, float deltaTime);
}