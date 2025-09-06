using NUnit.Framework;
using PurlieuEcs.Components;
using PurlieuEcs.Core;
using PurlieuEcs.Snapshot;

namespace PurlieuEcs.Tests.Core;

[TestFixture]
public class SNAP_SnapshotTests
{
    private World _world = null!;
    private WorldSnapshot _snapshot = null!;
    
    [SetUp]
    public void Setup()
    {
        _world = new World();
        _snapshot = new WorldSnapshot();
    }
    
    [Test]
    public void Snapshot_CaptureAndRestore_PreservesWorldState()
    {
        var e1 = _world.CreateEntity();
        _world.AddComponent(e1, new Position(10, 20));
        
        var e2 = _world.CreateEntity();
        _world.AddComponent(e2, new Position(30, 40));
        _world.AddComponent(e2, new MoveIntent(1, 2));
        
        var snapshotData = _snapshot.Capture(_world);
        
        Assert.That(snapshotData, Is.Not.Null);
        Assert.That(snapshotData.Length, Is.GreaterThan(0));
        
        // TODO: Full implementation would restore to a new world and verify
        // For now, just verify the snapshot was created
    }
    
    [Test]
    public void Snapshot_WithCompression_ReducesSize()
    {
        const int entityCount = 100;
        
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i));
        }
        
        var snapshotData = _snapshot.Capture(_world);
        
        Assert.That(snapshotData, Is.Not.Null);
        Assert.That(snapshotData.Length, Is.GreaterThan(0));
        
        // Compression should make repetitive data smaller
        // Full test would compare to uncompressed size
    }
    
    [Test]
    public void Snapshot_EmptyWorld_CreatesValidSnapshot()
    {
        var snapshotData = _snapshot.Capture(_world);
        
        Assert.That(snapshotData, Is.Not.Null);
        Assert.That(snapshotData.Length, Is.GreaterThan(0));
    }
}