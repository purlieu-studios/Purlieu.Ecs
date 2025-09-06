using PurlieuEcs.Components;

namespace Purlieu.Logic.Components;

/// <summary>
/// Basic position component for 3D space.
/// SIMD-compatible for optimal performance.
/// </summary>
public struct Position
{
    public float X, Y, Z;
    
    public Position(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>
/// Velocity component for movement calculations.
/// SIMD-compatible for optimal performance.
/// </summary>
public struct Velocity
{
    public float X, Y, Z;
    
    public Velocity(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>
/// Force accumulator for physics calculations.
/// SIMD-compatible for optimal performance.
/// </summary>
public struct Force
{
    public float X, Y, Z;
    
    public Force(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>
/// Movement intent from input or AI.
/// One-frame component that gets cleared after processing.
/// </summary>
[OneFrame]
public struct MoveIntent
{
    public float X, Y, Z;
    public float Speed;
    
    public MoveIntent(float x, float y, float z, float speed = 1.0f)
    {
        X = x;
        Y = y;
        Z = z;
        Speed = speed;
    }
}

/// <summary>
/// Prevents entity from moving temporarily.
/// </summary>
public struct Stunned
{
    public float Duration;
    
    public Stunned(float duration)
    {
        Duration = duration;
    }
}