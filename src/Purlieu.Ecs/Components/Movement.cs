namespace PurlieuEcs.Components;

/// <summary>
/// Position component for entities.
/// </summary>
public struct Position
{
    public int X;
    public int Y;
    
    public Position(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Movement intent component.
/// </summary>
[OneFrame]
public struct MoveIntent
{
    public int DX;
    public int DY;
    
    public MoveIntent(int dx, int dy)
    {
        DX = dx;
        DY = dy;
    }
}

/// <summary>
/// Tag component for stunned entities.
/// </summary>
[Tag]
public struct Stunned
{
}