namespace PurlieuEcs.Systems;

/// <summary>
/// Attribute to specify system execution phase and order.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GamePhaseAttribute : Attribute
{
    public string Phase { get; }
    public int Order { get; }
    
    public GamePhaseAttribute(string phase, int order = 0)
    {
        Phase = phase;
        Order = order;
    }
}

/// <summary>
/// Standard game phases for system execution.
/// </summary>
public static class GamePhases
{
    public const string Update = "Update";
    public const string PostUpdate = "PostUpdate";
    public const string Presentation = "Presentation";
}