namespace Purlieu.Ecs.Benchmarks;

public struct TestComponentA
{
    public int X, Y, Z;
    public float Value;
}

public struct TestComponentB  
{
    public float X, Y;
    public double Timestamp;
}

public struct TestComponentC
{
    public bool IsActive;
    public byte Flags;
    public short Priority;
}

public struct TestComponentD
{
    public long Id;
    public uint Hash;
}

public struct TestComponentE
{
    public float X, Y, Z, W;
    public int R, G, B, A;
}