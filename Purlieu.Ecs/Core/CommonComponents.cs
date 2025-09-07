namespace PurlieuEcs.Common;

/// <summary>
/// Common component types for easy ECS usage
/// </summary>
public struct Position
{
    public float X, Y, Z;
    
    public Position(float x, float y, float z = 0)
    {
        X = x; Y = y; Z = z;
    }
}

public struct Velocity
{
    public float X, Y, Z;
    
    public Velocity(float x, float y, float z = 0)
    {
        X = x; Y = y; Z = z;
    }
}

public struct Acceleration
{
    public float X, Y, Z;
    
    public Acceleration(float x, float y, float z = 0)
    {
        X = x; Y = y; Z = z;
    }
}

public struct Health
{
    public float Current, Maximum;
    
    public Health(float current, float maximum)
    {
        Current = current; Maximum = maximum;
    }
}

public struct DamageOverTime
{
    public float DamagePerSecond;
    public float Duration;
    
    public DamageOverTime(float damagePerSecond, float duration)
    {
        DamagePerSecond = damagePerSecond;
        Duration = duration;
    }
}