namespace PurlieuEcs.Components;

/// <summary>
/// Marks a component as a tag component with no data.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class TagAttribute : Attribute
{
}

/// <summary>
/// Marks a component or event as one-frame only, automatically cleared at end of frame.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class OneFrameAttribute : Attribute
{
}