using PurlieuEcs.Core;

namespace PurlieuEcs.Events;

/// <summary>
/// Intent event emitted when an entity's position changes.
/// Used for Backend-Visual Intent Pattern (BVIP).
/// </summary>
public readonly struct PositionChangedIntent
{
    public readonly Entity Entity;
    public readonly int X;
    public readonly int Y;
    
    public PositionChangedIntent(Entity entity, int x, int y)
    {
        Entity = entity;
        X = x;
        Y = y;
    }
}