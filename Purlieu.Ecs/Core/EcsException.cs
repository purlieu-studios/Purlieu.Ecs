using System.Runtime.Serialization;

namespace PurlieuEcs.Core;

/// <summary>
/// Exception thrown by ECS operations when critical errors occur
/// </summary>
[Serializable]
public class EcsException : Exception
{
    public EcsException() { }
    
    public EcsException(string message) : base(message) { }
    
    public EcsException(string message, Exception innerException) : base(message, innerException) { }
    
    protected EcsException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

/// <summary>
/// Exception thrown when an entity operation is attempted on an invalid or destroyed entity
/// </summary>
[Serializable]
public class EntityNotFoundException : EcsException
{
    public uint EntityId { get; }
    
    public EntityNotFoundException(uint entityId) : base($"Entity {entityId} not found or has been destroyed")
    {
        EntityId = entityId;
    }
    
    public EntityNotFoundException(uint entityId, string message) : base(message)
    {
        EntityId = entityId;
    }
    
    public EntityNotFoundException(uint entityId, string message, Exception innerException) : base(message, innerException)
    {
        EntityId = entityId;
    }
    
    protected EntityNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        EntityId = info.GetUInt32(nameof(EntityId));
    }
    
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(EntityId), EntityId);
    }
}

/// <summary>
/// Exception thrown when a component operation fails
/// </summary>
[Serializable]
public class ComponentException : EcsException
{
    public Type? ComponentType { get; }
    public uint EntityId { get; }
    
    public ComponentException(uint entityId, Type? componentType, string message) : base(message)
    {
        EntityId = entityId;
        ComponentType = componentType;
    }
    
    public ComponentException(uint entityId, Type? componentType, string message, Exception innerException) : base(message, innerException)
    {
        EntityId = entityId;
        ComponentType = componentType;
    }
    
    protected ComponentException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        EntityId = info.GetUInt32(nameof(EntityId));
        var typeName = info.GetString(nameof(ComponentType));
        ComponentType = typeName != null ? Type.GetType(typeName) : null;
    }
    
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(EntityId), EntityId);
        info.AddValue(nameof(ComponentType), ComponentType?.AssemblyQualifiedName);
    }
}