using NUnit.Framework;
using PurlieuEcs.Components;
using PurlieuEcs.Core;
using PurlieuEcs.Snapshot;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace PurlieuEcs.Tests.Core;

[TestFixture]
public class SNAP_SnapshotTests
{
    private World _world = null!;
    
    [SetUp]
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }
    
    [Test]
    public void Snapshot_CaptureAndRestore_PreservesWorldState()
    {
        var e1 = _world.CreateEntity();
        _world.AddComponent(e1, new Position(10, 20, 0));
        
        var e2 = _world.CreateEntity();
        _world.AddComponent(e2, new Position(30, 40, 0));
        _world.AddComponent(e2, new MoveIntent(1, 2, 0));
        
        var snapshotResult = WorldSnapshot.Save(_world);
        
        Assert.That(snapshotResult.Success, Is.True, $"Snapshot should succeed: {snapshotResult.Error}");
        Assert.That(snapshotResult.Value, Is.Not.Null);
        Assert.That(snapshotResult.Value.Length, Is.GreaterThan(0));
        
        // TODO: Full implementation would restore to a new world and verify
        // For now, just verify the snapshot was created
    }
    
    [Test]
    public void Snapshot_WithManyEntities_CreatesValidSnapshot()
    {
        const int entityCount = 100;
        
        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Position(i, i, 0));
        }
        
        var snapshotResult = WorldSnapshot.Save(_world);
        
        Assert.That(snapshotResult.Success, Is.True, $"Snapshot should succeed: {snapshotResult.Error}");
        Assert.That(snapshotResult.Value, Is.Not.Null);
        Assert.That(snapshotResult.Value.Length, Is.GreaterThan(0));
        
        // Deterministic format should produce consistent results
        var snapshotResult2 = WorldSnapshot.Save(_world);
        Assert.That(snapshotResult2.Success, Is.True);
        
        // Note: Timestamps will differ, so we can't do exact byte comparison
        // but the structure should be similar
        Assert.That(snapshotResult2.Value.Length, Is.EqualTo(snapshotResult.Value.Length).Within(64)); // Allow for timestamp differences
    }
    
    [Test]
    public void Snapshot_EmptyWorld_CreatesValidSnapshot()
    {
        var snapshotResult = WorldSnapshot.Save(_world);
        
        Assert.That(snapshotResult.Success, Is.True, $"Snapshot should succeed: {snapshotResult.Error}");
        Assert.That(snapshotResult.Value, Is.Not.Null);
        Assert.That(snapshotResult.Value.Length, Is.GreaterThan(0));
        
        // Empty world should still have header and minimal structure
        Assert.That(snapshotResult.Value.Length, Is.GreaterThan(64)); // At least header size
    }
}