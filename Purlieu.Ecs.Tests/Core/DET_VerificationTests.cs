using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Snapshot;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Determinism verification tests with snapshot comparison.
/// Ensures identical operations produce byte-identical snapshots across runs.
/// Part of Phase 12: Hardening & v0 Release.
/// </summary>
[TestFixture]
[Category("Determinism")]
public class DET_VerificationTests
{
    private World _world = null!;

    [SetUp]
    public void Setup()
    {
        _world = new World();
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void IdenticalOperations_ProduceIdenticalSnapshots()
    {
        // Arrange & Act - Run identical operations multiple times
        const int runCount = 5;
        var snapshots = new List<byte[]>();
        var hashes = new List<string>();

        for (int run = 0; run < runCount; run++)
        {
            var world = new World();
            
            // Perform identical operations
            PerformDeterministicOperations(world, seed: 42);
            
            // Capture snapshot and hash
            var snapshotResult = WorldSnapshot.Save(world);
            var snapshot = snapshotResult.Value;
            snapshots.Add(snapshot);
            hashes.Add(ComputeSnapshotHash(snapshot));
            
            world.Dispose();
        }

        // Assert - All snapshots should be identical
        var firstHash = hashes[0];
        for (int i = 1; i < hashes.Count; i++)
        {
            Assert.That(hashes[i], Is.EqualTo(firstHash), 
                       $"Run {i + 1} produced different snapshot (hash mismatch)");
        }

        // Verify snapshot contents are identical
        var firstSnapshot = snapshots[0];
        for (int i = 1; i < snapshots.Count; i++)
        {
            AssertSnapshotsEqual(firstSnapshot, snapshots[i], $"Run {i + 1}");
        }
    }

    [Test]
    public void DifferentOperationOrder_ProducesDifferentSnapshots()
    {
        // Arrange
        var world1 = new World();
        var world2 = new World();

        // Act - Same operations, different order
        // World 1: Create entities then add components
        var e1a = world1.CreateEntity();
        var e1b = world1.CreateEntity();
        world1.AddComponent(e1a, new DetTestComponent1 { Value = 100 });
        world1.AddComponent(e1b, new DetTestComponent2 { Value = 200 });

        // World 2: Interleave entity creation and component addition
        var e2a = world2.CreateEntity();
        world2.AddComponent(e2a, new DetTestComponent1 { Value = 100 });
        var e2b = world2.CreateEntity();
        world2.AddComponent(e2b, new DetTestComponent2 { Value = 200 });

        var snapshot1 = WorldSnapshot.Save(world1).Value;
        var snapshot2 = WorldSnapshot.Save(world2).Value;
        
        var hash1 = ComputeSnapshotHash(snapshot1);
        var hash2 = ComputeSnapshotHash(snapshot2);

        // Assert - Different operation order should produce different snapshots
        Assert.That(hash2, Is.Not.EqualTo(hash1), 
                   "Different operation order should produce different snapshots");

        // Cleanup
        world1.Dispose();
        world2.Dispose();
    }

    [Test]
    public void SnapshotRestore_ProducesIdenticalWorld()
    {
        // Arrange - Create world with complex state
        PerformDeterministicOperations(_world, seed: 12345);
        
        // Act - Create snapshot and restore to new world
        var originalSnapshot = WorldSnapshot.Save(_world).Value;
        var originalHash = ComputeSnapshotHash(originalSnapshot);
        
        var restoredWorld = new World();
        WorldSnapshot.Load(restoredWorld, originalSnapshot);
        
        // Verify restored world produces identical snapshot
        var restoredSnapshot = WorldSnapshot.Save(restoredWorld).Value;
        var restoredHash = ComputeSnapshotHash(restoredSnapshot);

        // Assert
        Assert.That(restoredHash, Is.EqualTo(originalHash), 
                   "Restored world should produce identical snapshot");
        
        AssertSnapshotsEqual(originalSnapshot, restoredSnapshot, "Restored");
        
        // Verify world functionality after restore
        var query = restoredWorld.Query().With<DetTestComponent1>();
        Assert.That(() => query.Count(), Throws.Nothing, 
                   "Restored world should be fully functional");

        // Cleanup
        restoredWorld.Dispose();
    }

    [Test]
    public void ConcurrentOperations_WithSynchronization_RemainDeterministic()
    {
        // Arrange
        const int threadCount = 4;
        const int operationsPerThread = 100;
        var barrier = new Barrier(threadCount);
        
        // Run test multiple times to verify determinism
        var hashes = new List<string>();
        
        for (int run = 0; run < 3; run++)
        {
            var world = new World();
            var entities = new Entity[threadCount * operationsPerThread];
            var entityIndex = 0;
            var entityLock = new object();

            var tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    barrier.SignalAndWait(); // Synchronize start
                    
                    for (int op = 0; op < operationsPerThread; op++)
                    {
                        Entity entity;
                        int index;
                        
                        lock (entityLock)
                        {
                            entity = world.CreateEntity();
                            index = entityIndex++;
                            entities[index] = entity;
                        }
                        
                        // Deterministic component addition based on index
                        if (index % 2 == 0)
                            world.AddComponent(entity, new DetTestComponent1 { Value = index });
                        if (index % 3 == 0)
                            world.AddComponent(entity, new DetTestComponent2 { Value = index * 2 });
                        if (index % 5 == 0)
                            world.AddComponent(entity, new DetTestComponent3 { Value = index * 3 });
                    }
                });
            }

            Task.WaitAll(tasks);
            
            // Sort entities by ID for deterministic snapshot
            Array.Sort(entities, (a, b) => a.Id.CompareTo(b.Id));
            
            var snapshotResult = WorldSnapshot.Save(world);
            var snapshot = snapshotResult.Value;
            hashes.Add(ComputeSnapshotHash(snapshot));
            
            world.Dispose();
        }

        // Assert - All runs should produce identical results
        var firstHash = hashes[0];
        for (int i = 1; i < hashes.Count; i++)
        {
            Assert.That(hashes[i], Is.EqualTo(firstHash), 
                       $"Concurrent run {i + 1} produced different result");
        }
    }

    [Test]
    public void SystemExecution_Deterministic_AcrossRuns()
    {
        // Arrange
        const int entityCount = 1000;
        const int frameCount = 10;
        var hashes = new List<string>();

        for (int run = 0; run < 3; run++)
        {
            var world = new World();
            
            // Create entities
            for (int i = 0; i < entityCount; i++)
            {
                var entity = world.CreateEntity();
                world.AddComponent(entity, new TestPosition { X = i, Y = i });
                
                if (i % 2 == 0)
                    world.AddComponent(entity, new TestVelocity { X = 1, Y = 1 });
            }

            // Register deterministic system
            var system = new DeterministicMovementSystem();
            world.RegisterSystem(system);

            // Execute frames
            for (int frame = 0; frame < frameCount; frame++)
            {
                world.SystemScheduler.ExecutePhase(SystemPhase.Update, world, 0.016f);
            }

            // Capture final state
            var snapshotResult = WorldSnapshot.Save(world);
            var snapshot = snapshotResult.Value;
            hashes.Add(ComputeSnapshotHash(snapshot));
            
            world.Dispose();
        }

        // Assert - All runs should produce identical final state
        var firstHash = hashes[0];
        for (int i = 1; i < hashes.Count; i++)
        {
            Assert.That(hashes[i], Is.EqualTo(firstHash), 
                       $"System execution run {i + 1} produced different final state");
        }
    }

    [Test]
    public void FloatingPointOperations_RemainDeterministic()
    {
        // Arrange
        const int iterations = 10000;
        var results = new List<float>();

        for (int run = 0; run < 3; run++)
        {
            var world = new World();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TestFloatComponent { Value = 0.1f });

            // Perform many floating-point operations
            for (int i = 0; i < iterations; i++)
            {
                ref var value = ref world.GetComponent<TestFloatComponent>(entity);
                value.Value = (value.Value * 1.0001f) + 0.001f;
            }

            var finalValue = world.GetComponent<TestFloatComponent>(entity).Value;
            results.Add(finalValue);
            
            world.Dispose();
        }

        // Assert - Floating-point operations should be deterministic
        var firstResult = results[0];
        for (int i = 1; i < results.Count; i++)
        {
            Assert.That(results[i], Is.EqualTo(firstResult).Within(0.0001f), 
                       $"Floating-point run {i + 1} produced different result");
        }
    }

    [Test]
    public void ArchetypeTransitions_Deterministic()
    {
        // Arrange
        const int transitionCount = 100;
        var hashes = new List<string>();

        for (int run = 0; run < 3; run++)
        {
            var world = new World();
            var entities = new List<Entity>();

            // Create entities with various archetypes
            for (int i = 0; i < transitionCount; i++)
            {
                var entity = world.CreateEntity();
                entities.Add(entity);
                
                // Deterministic archetype transitions based on index
                switch (i % 7)
                {
                    case 0:
                        world.AddComponent(entity, new DetTestComponent1 { Value = i });
                        break;
                    case 1:
                        world.AddComponent(entity, new DetTestComponent1 { Value = i });
                        world.AddComponent(entity, new DetTestComponent2 { Value = i * 2 });
                        break;
                    case 2:
                        world.AddComponent(entity, new DetTestComponent2 { Value = i * 2 });
                        world.AddComponent(entity, new DetTestComponent3 { Value = i * 3 });
                        break;
                    case 3:
                        world.AddComponent(entity, new DetTestComponent1 { Value = i });
                        world.AddComponent(entity, new DetTestComponent2 { Value = i * 2 });
                        world.AddComponent(entity, new DetTestComponent3 { Value = i * 3 });
                        break;
                }
            }

            // Perform archetype transitions
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                
                if (i % 3 == 0 && world.HasComponent<DetTestComponent1>(entity))
                    world.RemoveComponent<DetTestComponent1>(entity);
                    
                if (i % 5 == 0)
                    world.AddComponent(entity, new DetTestComponent3 { Value = i * 5 });
            }

            var snapshotResult = WorldSnapshot.Save(world);
            var snapshot = snapshotResult.Value;
            hashes.Add(ComputeSnapshotHash(snapshot));
            
            world.Dispose();
        }

        // Assert
        var firstHash = hashes[0];
        for (int i = 1; i < hashes.Count; i++)
        {
            Assert.That(hashes[i], Is.EqualTo(firstHash), 
                       $"Archetype transition run {i + 1} produced different result");
        }
    }

    private void PerformDeterministicOperations(World world, int seed)
    {
        var random = new Random(seed);
        var entities = new List<Entity>();

        // Create entities
        for (int i = 0; i < 100; i++)
        {
            entities.Add(world.CreateEntity());
        }

        // Add components deterministically
        foreach (var entity in entities)
        {
            var value = random.Next(1000);
            
            if (value % 2 == 0)
                world.AddComponent(entity, new DetTestComponent1 { Value = value });
            if (value % 3 == 0)
                world.AddComponent(entity, new DetTestComponent2 { Value = value * 2 });
            if (value % 5 == 0)
                world.AddComponent(entity, new DetTestComponent3 { Value = value * 3 });
        }

        // Perform some modifications
        for (int i = 0; i < entities.Count / 2; i++)
        {
            if (world.HasComponent<DetTestComponent1>(entities[i]))
            {
                ref var comp = ref world.GetComponent<DetTestComponent1>(entities[i]);
                comp.Value += 100;
            }
        }

        // Remove some components
        for (int i = entities.Count / 3; i < entities.Count / 2; i++)
        {
            if (world.HasComponent<DetTestComponent2>(entities[i]))
                world.RemoveComponent<DetTestComponent2>(entities[i]);
        }

        // Destroy some entities
        for (int i = 0; i < entities.Count / 4; i++)
        {
            world.DestroyEntity(entities[i]);
        }
    }

    private string ComputeSnapshotHash(byte[] snapshotData)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(snapshotData);
        return Convert.ToBase64String(hash);
    }

    private void AssertSnapshotsEqual(byte[] expected, byte[] actual, string context)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length), $"{context}: Snapshot length mismatch");
        Assert.That(actual, Is.EqualTo(expected), $"{context}: Snapshot content mismatch");
    }
}

// Test Components
internal struct DetTestComponent1
{
    public int Value;
}

internal struct DetTestComponent2
{
    public int Value;
}

internal struct DetTestComponent3
{
    public int Value;
}

internal struct TestPosition
{
    public float X, Y;
}

internal struct TestVelocity
{
    public float X, Y;
}

internal struct TestFloatComponent
{
    public float Value;
}

// Deterministic test system
internal class DeterministicMovementSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        var query = world.Query().With<TestPosition>().With<TestVelocity>();
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<TestPosition>();
            var velocities = chunk.GetSpan<TestVelocity>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                // Use exact floating-point operations for determinism
                positions[i].X = positions[i].X + (velocities[i].X * deltaTime);
                positions[i].Y = positions[i].Y + (velocities[i].Y * deltaTime);
            }
        }
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadWrite(
            readComponents: new[] { typeof(TestVelocity) },
            writeComponents: new[] { typeof(TestPosition) }
        );
    }
}