using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Integration;

/// <summary>
/// Integration tests simulating realistic game scenarios to ensure ECS performs well
/// in actual usage patterns rather than synthetic benchmarks.
/// </summary>
[TestFixture]
[Category("GameScenarios")]
public class GAME_ScenarioTests
{
    private World _world = null!;

    [SetUp] 
    public void Setup()
    {
        _world = new World();
        LogicBootstrap.RegisterComponents(_world);
    }

    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }

    [Test]
    public void RPGCombatScenario_ManyEntitiesWithVariedComponents()
    {
        // Simulate RPG combat: 200 players, 500 monsters, 100 projectiles
        var players = CreateEntitiesWithComponents(200, "Player", 
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(i * 10, 0, 0));
                _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(0, 0, 0));
                // Players have more complex component combinations
            });

        var monsters = CreateEntitiesWithComponents(500, "Monster",
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(i * 5, 100, 0));
                _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(1, 0, 0));
                if (i % 3 == 0) _world.AddComponent(entity, new Stunned());
            });

        var projectiles = CreateEntitiesWithComponents(100, "Projectile", 
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(i, i, 0));
                _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(5, 0, 0));
            });

        // Test queries that would be common in combat system
        TestScenarioQueries("RPG Combat");
        
        // Verify entity counts
        Assert.That(players.Length, Is.EqualTo(200));
        Assert.That(monsters.Length, Is.EqualTo(500));
        Assert.That(projectiles.Length, Is.EqualTo(100));
    }

    [Test]
    public void RealTimeStrategyScenario_LargeEntityCounts()
    {
        // Simulate RTS: 1000 units, 500 buildings, 200 resources
        var units = CreateEntitiesWithComponents(1000, "Unit",
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(i % 100, i / 100, 0));
                _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(
                    (i % 3) - 1, // -1, 0, or 1
                    (i % 5) - 2, // -2, -1, 0, 1, 2  
                    0));
            });

        var buildings = CreateEntitiesWithComponents(500, "Building",
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(i % 50 * 20, i / 50 * 20, 0));
                // Buildings don't move
            });

        var resources = CreateEntitiesWithComponents(200, "Resource",
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(
                    Random.Shared.Next(2000), 
                    Random.Shared.Next(2000), 
                    0));
            });

        // Test common RTS queries
        TestScenarioQueries("RTS");

        // Performance test: should handle large entity counts efficiently
        var movingUnitsQuery = _world.Query().With<Position>().With<Velocity>();
        int movingUnitCount = movingUnitsQuery.Count();
        Assert.That(movingUnitCount, Is.EqualTo(1000), "All units should be moving");
    }

    [Test]
    public void PlatformerGameScenario_FrequentComponentChanges()
    {
        // Simulate platformer: player + enemies with frequent state changes
        var player = _world.CreateEntity();
        _world.AddComponent(player, new Purlieu.Logic.Components.Position(0, 0, 0));
        _world.AddComponent(player, new Purlieu.Logic.Components.Velocity(0, 0, 0));

        var enemies = CreateEntitiesWithComponents(50, "Enemy",
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(i * 32, 100, 0));
                _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(1, 0, 0));
            });

        // Simulate frequent state changes (stunning, movement changes)
        for (int frame = 0; frame < 100; frame++)
        {
            // Every 10 frames, stun/unstun random enemies
            if (frame % 10 == 0)
            {
                for (int i = 0; i < 10; i++)
                {
                    var enemy = enemies[Random.Shared.Next(enemies.Length)];
                    if (_world.HasComponent<Stunned>(enemy))
                    {
                        _world.RemoveComponent<Stunned>(enemy);
                    }
                    else
                    {
                        _world.AddComponent(enemy, new Stunned());
                    }
                }
            }

            // Test queries each frame
            var activeEnemies = _world.Query().With<Purlieu.Logic.Components.Position>().With<Purlieu.Logic.Components.Velocity>().Without<Stunned>().Count();
            var stunnedEnemies = _world.Query().With<Stunned>().Count();
            
            Assert.That(activeEnemies + stunnedEnemies, Is.LessThanOrEqualTo(50),
                $"Frame {frame}: Total enemies should not exceed 50");
        }

        TestScenarioQueries("Platformer");
    }

    [Test] 
    public void MMOScenario_HighEntityCountWithSpatialQueries()
    {
        // Simulate MMO zone: 5000 entities spread across large area
        var entities = CreateEntitiesWithComponents(5000, "MMOEntity",
            (entity, i) => {
                _world.AddComponent(entity, new Purlieu.Logic.Components.Position(
                    Random.Shared.Next(10000),  // Large world: 10k x 10k
                    Random.Shared.Next(10000),
                    Random.Shared.Next(1000)));
                    
                if (i % 3 == 0) // 1/3 are moving
                {
                    _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(
                        Random.Shared.Next(-5, 6),
                        Random.Shared.Next(-5, 6),
                        0));
                }
            });

        // Test spatial-like queries (though we don't have spatial indexing yet)
        TestScenarioQueries("MMO");

        // Performance test for large entity counts
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetTotalMemory(false);
        
        // Simulate multiple system queries per frame
        var allEntities = _world.Query().With<Purlieu.Logic.Components.Position>().Count();
        var movingEntities = _world.Query().With<Purlieu.Logic.Components.Position>().With<Purlieu.Logic.Components.Velocity>().Count();
        var staticEntities = _world.Query().With<Purlieu.Logic.Components.Position>().Without<Purlieu.Logic.Components.Velocity>().Count();
        
        long after = GC.GetTotalMemory(false);
        var allocated = after - before;

        Assert.That(allEntities, Is.EqualTo(5000));
        Assert.That(movingEntities + staticEntities, Is.EqualTo(allEntities));
        Assert.That(allocated, Is.LessThanOrEqualTo(10 * 1024), 
            $"MMO queries should allocate minimally, but allocated {allocated} bytes");
    }

    [Test]
    public void StressTest_ManyArchetypes()
    {
        // Create many different archetype combinations to stress archetype matching
        var archetypePatterns = new[]
        {
            new System.Type[] { typeof(Purlieu.Logic.Components.Position) },
            new System.Type[] { typeof(Purlieu.Logic.Components.Position), typeof(Purlieu.Logic.Components.Velocity) },
            new System.Type[] { typeof(Purlieu.Logic.Components.Position), typeof(Stunned) },
            new System.Type[] { typeof(Purlieu.Logic.Components.Position), typeof(Purlieu.Logic.Components.Velocity), typeof(Stunned) },
            new System.Type[] { typeof(Purlieu.Logic.Components.Velocity) },
            new System.Type[] { typeof(Purlieu.Logic.Components.Velocity), typeof(Stunned) },
            new System.Type[] { typeof(Stunned) }
        };

        // Create entities for each archetype pattern
        foreach (var pattern in archetypePatterns)
        {
            for (int i = 0; i < 100; i++)
            {
                var entity = _world.CreateEntity();
                foreach (var componentType in pattern)
                {
                    if (componentType == typeof(Purlieu.Logic.Components.Position))
                        _world.AddComponent(entity, new Purlieu.Logic.Components.Position(i, i, i));
                    else if (componentType == typeof(Purlieu.Logic.Components.Velocity))
                        _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(1, 1, 1));
                    else if (componentType == typeof(Stunned))
                        _world.AddComponent(entity, new Stunned());
                }
            }
        }

        // Test that all archetype combinations work correctly
        TestScenarioQueries("Many Archetypes");

        // Verify we created the expected archetype diversity
        var allEntities = _world.Query().Count(); // This should find all entities
        Assert.That(allEntities, Is.EqualTo(archetypePatterns.Length * 100),
            $"Should have {archetypePatterns.Length * 100} entities total");
    }

    private Entity[] CreateEntitiesWithComponents(int count, string category, 
        System.Action<Entity, int> componentSetup)
    {
        var entities = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            entities[i] = _world.CreateEntity();
            componentSetup(entities[i], i);
        }
        return entities;
    }

    private void TestScenarioQueries(string scenario)
    {
        // Common queries that should work in all scenarios
        var queries = new[]
        {
            ("All entities with Position", _world.Query().With<Purlieu.Logic.Components.Position>()),
            ("Moving entities", _world.Query().With<Purlieu.Logic.Components.Position>().With<Purlieu.Logic.Components.Velocity>()),
            ("Static entities", _world.Query().With<Purlieu.Logic.Components.Position>().Without<Purlieu.Logic.Components.Velocity>()),
            ("Stunned entities", _world.Query().With<Stunned>()),
            ("Active moving entities", _world.Query().With<Purlieu.Logic.Components.Position>().With<Purlieu.Logic.Components.Velocity>().Without<Stunned>())
        };

        foreach (var (description, query) in queries)
        {
            var count = query.Count();
            Assert.That(count, Is.GreaterThanOrEqualTo(0), 
                $"{scenario} - {description}: Should return non-negative count");
                
            // Verify enumeration works
            int enumeratedCount = 0;
            foreach (var chunk in query.ChunksStack())
            {
                enumeratedCount += chunk.Count;
            }
            
            Assert.That(enumeratedCount, Is.EqualTo(count),
                $"{scenario} - {description}: Enumerated count should match query count");
        }
    }
}