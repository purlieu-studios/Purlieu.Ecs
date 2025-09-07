using PurlieuEcs.Core;

namespace PurlieuEcs.Systems;

/// <summary>
/// Legacy base interface for all ECS systems - deprecated, use PurlieuEcs.Core.ISystem instead.
/// Systems must be stateless and deterministic.
/// </summary>
[Obsolete("Use PurlieuEcs.Core.ISystem instead")]
public interface ILegacySystem
{
    /// <summary>
    /// Updates the system for one frame.
    /// </summary>
    /// <param name="world">The ECS world containing entities and components</param>
    /// <param name="deltaTime">Time elapsed since last frame in seconds</param>
    void Update(World world, float deltaTime);
}