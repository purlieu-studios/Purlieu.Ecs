namespace PurlieuEcs.Core;

/// <summary>
/// Entity represented as packed ID and version in a ulong.
/// Prevents stale references by tracking entity generations.
/// </summary>
public readonly struct Entity : IEquatable<Entity>
{
    private readonly ulong _packed;
    
    // 32 bits for ID, 32 bits for version
    private const int IdBits = 32;
    private const ulong IdMask = (1UL << IdBits) - 1;
    private const int VersionShift = IdBits;
    
    public uint Id => (uint)(_packed & IdMask);
    public uint Version => (uint)(_packed >> VersionShift);
    
    public Entity(uint id, uint version)
    {
        _packed = ((ulong)version << VersionShift) | id;
    }
    
    public static Entity FromPacked(ulong packed) => new() { _packed = packed };
    public ulong ToPacked() => _packed;
    
    public bool IsValid => _packed != 0;
    public static readonly Entity Invalid = default;
    
    public bool Equals(Entity other) => _packed == other._packed;
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);
    public override int GetHashCode() => _packed.GetHashCode();
    public override string ToString() => $"Entity({Id}v{Version})";
    
    public static bool operator ==(Entity left, Entity right) => left._packed == right._packed;
    public static bool operator !=(Entity left, Entity right) => left._packed != right._packed;
}