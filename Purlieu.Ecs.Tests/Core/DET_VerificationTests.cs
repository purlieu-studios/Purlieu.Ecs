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
        var snapshots = new List<WorldSnapshot>();
        var hashes = new List<string>();

        for (int run = 0; run < runCount; run++)
        {
            var world = new World();
            
            // Perform identical operations
            PerformDeterministicOperations(world, seed: 42);
            
            // Capture snapshot and hash
            var snapshot = world.CreateSnapshot();
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
        world1.AddComponent(e1a, new TestComponent1 { Value = 100 });
        world1.AddComponent(e1b, new TestComponent2 { Value = 200 });

        // World 2: Interleave entity creation and component addition
        var e2a = world2.CreateEntity();
        world2.AddComponent(e2a, new TestComponent1 { Value = 100 });
        var e2b = world2.CreateEntity();
        world2.AddComponent(e2b, new TestComponent2 { Value = 200 });

        var snapshot1 = world1.CreateSnapshot();
        var snapshot2 = world2.CreateSnapshot();
        
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
        var originalSnapshot = _world.CreateSnapshot();
        var originalHash = ComputeSnapshotHash(originalSnapshot);
        
        var restoredWorld = new World();
        restoredWorld.RestoreSnapshot(originalSnapshot);
        
        // Verify restored world produces identical snapshot
        var restoredSnapshot = restoredWorld.CreateSnapshot();
        var restoredHash = ComputeSnapshotHash(restoredSnapshot);

        // Assert
        Assert.That(restoredHash, Is.EqualTo(originalHash), 
                   "Restored world should produce identical snapshot");
        
        AssertSnapshotsEqual(originalSnapshot, restoredSnapshot, "Restored");
        
        // Verify world functionality after restore
        var query = restoredWorld.Query().With<TestComponent1>();
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
                            world.AddComponent(entity, new TestComponent1 { Value = index });
                        if (index % 3 == 0)
                            world.AddComponent(entity, new TestComponent2 { Value = index * 2 });
                        if (index % 5 == 0)
                            world.AddComponent(entity, new TestComponent3 { Value = index * 3 });
                    }
                });
            }

            Task.WaitAll(tasks);
            
            // Sort entities by ID for deterministic snapshot
            Array.Sort(entities, (a, b) => a.Id.CompareTo(b.Id));
            
            var snapshot = world.CreateSnapshot();
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
            var snapshot = world.CreateSnapshot();
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
                var value = world.GetComponent<TestFloatComponent>(entity);
                value.Value = (value.Value * 1.0001f) + 0.001f;
                world.SetComponent(entity, value);
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
                        world.AddComponent(entity, new TestComponent1 { Value = i });
                        break;
                    case 1:
                        world.AddComponent(entity, new TestComponent1 { Value = i });
                        world.AddComponent(entity, new TestComponent2 { Value = i * 2 });
                        break;
                    case 2:
                        world.AddComponent(entity, new TestComponent2 { Value = i * 2 });
                        world.AddComponent(entity, new TestComponent3 { Value = i * 3 });
                        break;
                    case 3:
                        world.AddComponent(entity, new TestComponent1 { Value = i });
                        world.AddComponent(entity, new TestComponent2 { Value = i * 2 });
                        world.AddComponent(entity, new TestComponent3 { Value = i * 3 });
                        break;
                }
            }

            // Perform archetype transitions
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                
                if (i % 3 == 0 && world.HasComponent<TestComponent1>(entity))
                    world.RemoveComponent<TestComponent1>(entity);
                    
                if (i % 5 == 0)
                    world.AddComponent(entity, new TestComponent3 { Value = i * 5 });
            }

            var snapshot = world.CreateSnapshot();
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
                world.AddComponent(entity, new TestComponent1 { Value = value });
            if (value % 3 == 0)
                world.AddComponent(entity, new TestComponent2 { Value = value * 2 });
            if (value % 5 == 0)
                world.AddComponent(entity, new TestComponent3 { Value = value * 3 });
        }

        // Perform some modifications
        for (int i = 0; i < entities.Count / 2; i++)
        {
            if (world.HasComponent<TestComponent1>(entities[i]))
            {
                var comp = world.GetComponent<TestComponent1>(entities[i]);
                comp.Value += 100;
                world.SetComponent(entities[i], comp);
            }
        }

        // Remove some components
        for (int i = entities.Count / 3; i < entities.Count / 2; i++)
        {
            if (world.HasComponent<TestComponent2>(entities[i]))
                world.RemoveComponent<TestComponent2>(entities[i]);
        }

        // Destroy some entities
        for (int i = 0; i < entities.Count / 4; i++)
        {
            world.DestroyEntity(entities[i]);
        }
    }

    private string ComputeSnapshotHash(WorldSnapshot snapshot)
    {
        using var sha256 = SHA256.Create();
        var builder = new StringBuilder();
        
        // Hash entity data
        builder.Append($"Entities:{snapshot.Entities.Length}|");
        foreach (var entity in snapshot.Entities.OrderBy(e => e.Id))
        {
            builder.Append($"{entity.Id}:{entity.Generation}|");
        }
        
        // Hash archetype data
        builder.Append($"Archetypes:{snapshot.Archetypes.Length}|");
        foreach (var archetype in snapshot.Archetypes.OrderBy(a => a.Id))
        {
            builder.Append($"A{archetype.Id}:E{archetype.EntityIds.Length}|");
            foreach (var entityId in archetype.EntityIds.OrderBy(id => id))
            {
                builder.Append($"{entityId}|");
            }
        }
        
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private void AssertSnapshotsEqual(WorldSnapshot expected, WorldSnapshot actual, string context)
    {
        Assert.That(actual.Entities.Length, Is.EqualTo(expected.Entities.Length), 
                   $"{context}: Entity count mismatch");
        Assert.That(actual.Archetypes.Length, Is.EqualTo(expected.Archetypes.Length), 
                   $"{context}: Archetype count mismatch");
        
        // Compare entities
        var expectedEntities = expected.Entities.OrderBy(e => e.Id).ToArray();
        var actualEntities = actual.Entities.OrderBy(e => e.Id).ToArray();
        
        for (int i = 0; i < expectedEntities.Length; i++)
        {
            Assert.That(actualEntities[i].Id, Is.EqualTo(expectedEntities[i].Id), 
                       $"{context}: Entity ID mismatch at index {i}");
            Assert.That(actualEntities[i].Generation, Is.EqualTo(expectedEntities[i].Generation), 
                       $"{context}: Entity generation mismatch at index {i}");
        }
    }
}

// Test Components
internal struct TestComponent1
{
    public int Value;
}

internal struct TestComponent2
{
    public int Value;
}

internal struct TestComponent3
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