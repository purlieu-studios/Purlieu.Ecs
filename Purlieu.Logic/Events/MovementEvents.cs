using PurlieuEcs.Components;

namespace Purlieu.Logic.Events;

/// <summary>
/// Event fired when an entity's position changes.
/// One-frame event that gets cleared after processing.
/// </summary>
[OneFrame]
public struct PositionChangedEvent
{
    public uint EntityId;
    public float OldX, OldY, OldZ;
    public float NewX, NewY, NewZ;
    
    public PositionChangedEvent(uint entityId, float oldX, float oldY, float oldZ, float newX, float newY, float newZ)
    {
        EntityId = entityId;
        OldX = oldX;
        OldY = oldY;
        OldZ = oldZ;
        NewX = newX;
        NewY = newY;
        NewZ = newZ;
    }
}

/// <summary>
/// Intent to change an entity's position.
/// One-frame intent that gets cleared after processing.
/// </summary>
[OneFrame]
public struct PositionChangedIntent
{
    public uint EntityId;
    public float X, Y, Z;
    
    public PositionChangedIntent(uint entityId, float x, float y, float z)
    {
        EntityId = entityId;
        X = x;
        Y = y;
        Z = z;
    }
}