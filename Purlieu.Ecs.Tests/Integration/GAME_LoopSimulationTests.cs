using System.Diagnostics;
using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Events;
using PurlieuEcs.Logging;
using PurlieuEcs.Monitoring;

namespace Purlieu.Ecs.Tests.Integration;

/// <summary>
/// Integration tests covering full game loop simulation scenarios.
/// Validates the ECS in realistic game-like conditions.
/// Part of Phase 12: Hardening & v0 Release.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GAME_LoopSimulationTests
{
    private GameSimulation _simulation = null!;

    [SetUp]
    public void Setup()
    {
        _simulation = new GameSimulation();
    }

    [TearDown]
    public void TearDown()
    {
        _simulation?.Dispose();
    }

    [Test]
    public void FullGameLoop_60FPS_MaintainsStablePerformance()
    {
        // Arrange
        const int targetFps = 60;
        const float targetFrameTime = 1000f / targetFps;
        const int frameCount = 300; // 5 seconds at 60 FPS
        
        _simulation.Initialize(playerCount: 4, enemyCount: 100, itemCount: 50);
        
        var frameTimes = new List<double>();
        var frameStopwatch = new Stopwatch();

        // Act - Simulate game loop
        for (int frame = 0; frame < frameCount; frame++)
        {
            frameStopwatch.Restart();
            
            _simulation.Update(1f / targetFps);
            
            frameStopwatch.Stop();
            frameTimes.Add(frameStopwatch.Elapsed.TotalMilliseconds);
            
            // Simulate frame rate limiting
            if (frameStopwatch.Elapsed.TotalMilliseconds < targetFrameTime)
            {
                Thread.Sleep((int)(targetFrameTime - frameStopwatch.Elapsed.TotalMilliseconds));
            }
        }

        // Assert
        var avgFrameTime = frameTimes.Average();
        var maxFrameTime = frameTimes.Max();
        var frameTimeStdDev = CalculateStandardDeviation(frameTimes);
        
        TestContext.WriteLine($"Avg frame time: {avgFrameTime:F2}ms");
        TestContext.WriteLine($"Max frame time: {maxFrameTime:F2}ms");
        TestContext.WriteLine($"Frame time StdDev: {frameTimeStdDev:F2}ms");
        TestContext.WriteLine($"Entity count: {_simulation.EntityCount}");
        
        Assert.That(avgFrameTime, Is.LessThan(targetFrameTime * 1.1), "Average frame time should be close to target");
        Assert.That(maxFrameTime, Is.LessThan(targetFrameTime * 2), "No severe frame spikes");
        Assert.That(frameTimeStdDev, Is.LessThan(5), "Frame times should be consistent");
    }

    [Test]
    public void GameSystems_ProcessInCorrectOrder()
    {
        // Arrange
        _simulation.Initialize(playerCount: 2, enemyCount: 10, itemCount: 5);
        _simulation.EnableEventTracking();

        // Act - Single frame update
        _simulation.Update(0.016f);

        // Assert - Verify system execution order
        var events = _simulation.GetTrackedEvents();
        
        // Input should process before movement
        var inputIndex = events.FindIndex(e => e.Contains("Input"));
        var movementIndex = events.FindIndex(e => e.Contains("Movement"));
        Assert.That(inputIndex, Is.LessThan(movementIndex), "Input should process before movement");
        
        // Movement should process before collision
        var collisionIndex = events.FindIndex(e => e.Contains("Collision"));
        Assert.That(movementIndex, Is.LessThan(collisionIndex), "Movement should process before collision");
        
        // Collision should process before damage
        var damageIndex = events.FindIndex(e => e.Contains("Damage"));
        Assert.That(collisionIndex, Is.LessThan(damageIndex), "Collision should process before damage");
    }

    [Test]
    public void PlayerActions_TriggerCorrectEvents()
    {
        // Arrange
        _simulation.Initialize(playerCount: 1, enemyCount: 5, itemCount: 3);
        var playerId = _simulation.GetPlayerEntities()[0];

        // Act - Simulate player actions
        _simulation.SimulatePlayerInput(playerId, new PlayerInput
        {
            MoveDirection = new Vector3(1, 0, 0),
            Attack = true,
            UseItem = false
        });
        
        _simulation.Update(0.016f);

        // Assert
        Assert.That(_simulation.GetEntityPosition(playerId).X, Is.GreaterThan(0), "Player should have moved");
        Assert.That(_simulation.GetAttackEvents().Count, Is.GreaterThan(0), "Attack event should be triggered");
    }

    [Test]
    public void EnemyAI_RespondsToPlayerProximity()
    {
        // Arrange
        _simulation.Initialize(playerCount: 1, enemyCount: 10, itemCount: 0);
        var playerId = _simulation.GetPlayerEntities()[0];
        var enemyIds = _simulation.GetEnemyEntities();
        
        // Place player near enemies
        _simulation.SetEntityPosition(playerId, new Vector3(50, 50, 0));
        
        // Record initial enemy positions
        var initialPositions = enemyIds.ToDictionary(
            id => id,
            id => _simulation.GetEntityPosition(id)
        );

        // Act - Update several frames for AI to respond
        for (int i = 0; i < 10; i++)
        {
            _simulation.Update(0.016f);
        }

        // Assert - Enemies should move towards player
        int enemiesMovedTowardsPlayer = 0;
        foreach (var enemyId in enemyIds)
        {
            var initialPos = initialPositions[enemyId];
            var currentPos = _simulation.GetEntityPosition(enemyId);
            var playerPos = _simulation.GetEntityPosition(playerId);
            
            var initialDistance = Vector3.Distance(initialPos, playerPos);
            var currentDistance = Vector3.Distance(currentPos, playerPos);
            
            if (currentDistance < initialDistance)
            {
                enemiesMovedTowardsPlayer++;
            }
        }
        
        Assert.That(enemiesMovedTowardsPlayer, Is.GreaterThan(enemyIds.Length / 2), 
                   "Most enemies should move towards player");
    }

    [Test]
    public void ItemPickup_ModifiesPlayerStats()
    {
        // Arrange
        _simulation.Initialize(playerCount: 1, enemyCount: 0, itemCount: 5);
        var playerId = _simulation.GetPlayerEntities()[0];
        var itemIds = _simulation.GetItemEntities();
        
        // Place player at origin
        _simulation.SetEntityPosition(playerId, new Vector3(0, 0, 0));
        
        // Place item near player
        _simulation.SetEntityPosition(itemIds[0], new Vector3(1, 0, 0));
        
        var initialHealth = _simulation.GetEntityHealth(playerId);

        // Act - Move player to item
        _simulation.SimulatePlayerInput(playerId, new PlayerInput
        {
            MoveDirection = new Vector3(1, 0, 0),
            Attack = false,
            UseItem = false
        });
        
        _simulation.Update(0.016f);
        _simulation.Update(0.016f); // Extra frame for pickup processing

        // Assert
        var finalHealth = _simulation.GetEntityHealth(playerId);
        Assert.That(finalHealth, Is.GreaterThan(initialHealth), "Health should increase after pickup");
        Assert.That(_simulation.EntityExists(itemIds[0]), Is.False, "Item should be consumed");
    }

    [Test]
    public void CombatSystem_DamageCalculation()
    {
        // Arrange
        _simulation.Initialize(playerCount: 1, enemyCount: 1, itemCount: 0);
        var playerId = _simulation.GetPlayerEntities()[0];
        var enemyId = _simulation.GetEnemyEntities()[0];
        
        // Position entities close for combat
        _simulation.SetEntityPosition(playerId, new Vector3(0, 0, 0));
        _simulation.SetEntityPosition(enemyId, new Vector3(1, 0, 0));
        
        var initialEnemyHealth = _simulation.GetEntityHealth(enemyId);

        // Act - Player attacks
        _simulation.SimulatePlayerInput(playerId, new PlayerInput
        {
            MoveDirection = Vector3.Zero,
            Attack = true,
            UseItem = false
        });
        
        _simulation.Update(0.016f);

        // Assert
        var finalEnemyHealth = _simulation.GetEntityHealth(enemyId);
        Assert.That(finalEnemyHealth, Is.LessThan(initialEnemyHealth), "Enemy should take damage");
    }

    [Test]
    public void EntityLifecycle_SpawnAndDespawn()
    {
        // Arrange
        _simulation.Initialize(playerCount: 1, enemyCount: 50, itemCount: 20);
        var initialEntityCount = _simulation.EntityCount;

        // Act - Simulate combat causing entity deaths and respawns
        for (int frame = 0; frame < 100; frame++)
        {
            if (frame % 10 == 0)
            {
                _simulation.SpawnWave(enemyCount: 5);
            }
            
            if (frame % 20 == 0)
            {
                _simulation.DespawnDeadEntities();
            }
            
            _simulation.Update(0.016f);
        }

        // Assert
        var finalEntityCount = _simulation.EntityCount;
        Assert.That(finalEntityCount, Is.GreaterThan(initialEntityCount), "Entities should spawn over time");
        Assert.That(_simulation.GetDeadEntityCount(), Is.LessThan(10), "Dead entities should be cleaned up");
    }

    [Test]
    public void SaveLoadCycle_PreservesGameState()
    {
        // Arrange
        _simulation.Initialize(playerCount: 2, enemyCount: 20, itemCount: 10);
        
        // Play for a bit
        for (int i = 0; i < 50; i++)
        {
            _simulation.Update(0.016f);
        }
        
        // Save state
        var saveData = _simulation.SaveGame();
        var preEntityCount = _simulation.EntityCount;
        var prePlayerHealth = _simulation.GetEntityHealth(_simulation.GetPlayerEntities()[0]);

        // Act - Create new simulation and load
        var newSimulation = new GameSimulation();
        newSimulation.LoadGame(saveData);

        // Assert
        Assert.That(newSimulation.EntityCount, Is.EqualTo(preEntityCount), "Entity count should match");
        Assert.That(newSimulation.GetEntityHealth(newSimulation.GetPlayerEntities()[0]), 
                   Is.EqualTo(prePlayerHealth), "Player health should match");
        
        // Verify simulation continues to work
        Assert.DoesNotThrow(() => newSimulation.Update(0.016f), "Loaded simulation should be functional");
        
        newSimulation.Dispose();
    }

    [Test]
    public void MemoryStability_LongRunningSimulation()
    {
        // Arrange
        _simulation.Initialize(playerCount: 4, enemyCount: 100, itemCount: 50);
        
        var initialMemory = GC.GetTotalMemory(true);
        var memorySnapshots = new List<long>();

        // Act - Run simulation for extended period
        for (int minute = 0; minute < 5; minute++)
        {
            for (int frame = 0; frame < 60 * 60; frame++) // 60 seconds at 60 FPS
            {
                _simulation.Update(0.016f);
                
                // Periodic spawning and cleanup
                if (frame % 300 == 0) // Every 5 seconds
                {
                    _simulation.SpawnWave(enemyCount: 10);
                    _simulation.DespawnDeadEntities();
                }
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            memorySnapshots.Add(GC.GetTotalMemory(false));
        }

        var finalMemory = GC.GetTotalMemory(true);

        // Assert
        var memoryGrowth = (finalMemory - initialMemory) / (1024.0 * 1024.0);
        TestContext.WriteLine($"Memory growth over 5 minutes: {memoryGrowth:F2} MB");
        
        Assert.That(memoryGrowth, Is.LessThan(50), "Memory growth should be limited");
        
        // Check for memory stability
        if (memorySnapshots.Count > 2)
        {
            var trend = memorySnapshots.Last() - memorySnapshots[memorySnapshots.Count / 2];
            Assert.That(trend / (1024.0 * 1024.0), Is.LessThan(10), "Memory should stabilize");
        }
    }

    private double CalculateStandardDeviation(List<double> values)
    {
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }
}

/// <summary>
/// Complete game simulation for integration testing.
/// </summary>
internal class GameSimulation : IDisposable
{
    private World _world = null!;
    private SystemScheduler _scheduler = null!;
    private readonly List<Entity> _players = new();
    private readonly List<Entity> _enemies = new();
    private readonly List<Entity> _items = new();
    private readonly List<Entity> _deadEntities = new();
    private readonly List<string> _trackedEvents = new();
    private bool _trackEvents;

    public int EntityCount => _players.Count + _enemies.Count + _items.Count;

    public void Initialize(int playerCount, int enemyCount, int itemCount)
    {
        _world = new World(logger: NullEcsLogger.Instance, healthMonitor: NullEcsHealthMonitor.Instance);
        _scheduler = _world.SystemScheduler;
        
        // Register game systems in correct order
        _scheduler.RegisterSystem(new InputSystem());
        _scheduler.RegisterSystem(new MovementSystem());
        _scheduler.RegisterSystem(new AISystem());
        _scheduler.RegisterSystem(new CollisionSystem());
        _scheduler.RegisterSystem(new CombatSystem());
        _scheduler.RegisterSystem(new PickupSystem());
        _scheduler.RegisterSystem(new HealthSystem());
        _scheduler.RegisterSystem(new RespawnSystem());
        
        // Spawn initial entities
        SpawnPlayers(playerCount);
        SpawnEnemies(enemyCount);
        SpawnItems(itemCount);
    }

    public void Update(float deltaTime)
    {
        if (_trackEvents) _trackedEvents.Add($"Frame start: {DateTime.UtcNow:HH:mm:ss.fff}");
        
        _scheduler.ExecuteAllPhases(_world, deltaTime);
        
        if (_trackEvents) _trackedEvents.Add($"Frame end: {DateTime.UtcNow:HH:mm:ss.fff}");
    }

    public void SpawnWave(int enemyCount)
    {
        SpawnEnemies(enemyCount);
    }

    public void DespawnDeadEntities()
    {
        foreach (var entity in _deadEntities)
        {
            if (_world.IsAlive(entity))
            {
                _world.DestroyEntity(entity);
            }
        }
        _deadEntities.Clear();
    }

    public void SimulatePlayerInput(Entity player, PlayerInput input)
    {
        _world.SetComponent(player, input);
    }

    public Vector3 GetEntityPosition(Entity entity)
    {
        return _world.GetComponent<Transform>(entity).Position;
    }

    public void SetEntityPosition(Entity entity, Vector3 position)
    {
        var transform = _world.GetComponent<Transform>(entity);
        transform.Position = position;
        _world.SetComponent(entity, transform);
    }

    public int GetEntityHealth(Entity entity)
    {
        return _world.GetComponent<Health>(entity).Current;
    }

    public bool EntityExists(Entity entity)
    {
        return _world.IsAlive(entity);
    }

    public Entity[] GetPlayerEntities() => _players.ToArray();
    public Entity[] GetEnemyEntities() => _enemies.ToArray();
    public Entity[] GetItemEntities() => _items.ToArray();
    public int GetDeadEntityCount() => _deadEntities.Count;
    public List<AttackEvent> GetAttackEvents() => _world.Events<AttackEvent>().ConsumeAll().ToList();

    public void EnableEventTracking() => _trackEvents = true;
    public List<string> GetTrackedEvents() => new(_trackedEvents);

    public GameSaveData SaveGame()
    {
        return new GameSaveData
        {
            Snapshot = _world.CreateSnapshot(),
            PlayerIds = _players.Select(p => p.Id).ToArray(),
            EnemyIds = _enemies.Select(e => e.Id).ToArray(),
            ItemIds = _items.Select(i => i.Id).ToArray()
        };
    }

    public void LoadGame(GameSaveData saveData)
    {
        _world = new World();
        _world.RestoreSnapshot(saveData.Snapshot);
        
        _players.Clear();
        _enemies.Clear();
        _items.Clear();
        
        foreach (var id in saveData.PlayerIds)
            _players.Add(new Entity(id, 0));
        foreach (var id in saveData.EnemyIds)
            _enemies.Add(new Entity(id, 0));
        foreach (var id in saveData.ItemIds)
            _items.Add(new Entity(id, 0));
            
        Initialize(0, 0, 0); // Just register systems
    }

    private void SpawnPlayers(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var player = _world.CreateEntity();
            _world.AddComponent(player, new Transform { Position = new Vector3(i * 10, 0, 0) });
            _world.AddComponent(player, new Health { Current = 100, Max = 100 });
            _world.AddComponent(player, new PlayerTag());
            _world.AddComponent(player, new PlayerInput());
            _world.AddComponent(player, new CombatStats { Damage = 10, Defense = 5 });
            _players.Add(player);
        }
    }

    private void SpawnEnemies(int count)
    {
        var random = new Random();
        for (int i = 0; i < count; i++)
        {
            var enemy = _world.CreateEntity();
            _world.AddComponent(enemy, new Transform 
            { 
                Position = new Vector3(random.Next(-100, 100), random.Next(-100, 100), 0) 
            });
            _world.AddComponent(enemy, new Health { Current = 50, Max = 50 });
            _world.AddComponent(enemy, new EnemyTag());
            _world.AddComponent(enemy, new AIState { Target = Entity.Null, State = AIStateType.Idle });
            _world.AddComponent(enemy, new CombatStats { Damage = 5, Defense = 2 });
            _enemies.Add(enemy);
        }
    }

    private void SpawnItems(int count)
    {
        var random = new Random();
        for (int i = 0; i < count; i++)
        {
            var item = _world.CreateEntity();
            _world.AddComponent(item, new Transform 
            { 
                Position = new Vector3(random.Next(-100, 100), random.Next(-100, 100), 0) 
            });
            _world.AddComponent(item, new ItemTag());
            _world.AddComponent(item, new ItemEffect { HealthBonus = 20 });
            _items.Add(item);
        }
    }

    public void Dispose()
    {
        _world?.Dispose();
    }
}

// Game Components
internal struct Transform
{
    public Vector3 Position;
    public Vector3 Rotation;
    public float Scale;
}

internal struct Vector3
{
    public float X, Y, Z;
    
    public Vector3(float x, float y, float z)
    {
        X = x; Y = y; Z = z;
    }
    
    public static Vector3 Zero => new(0, 0, 0);
    
    public static float Distance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

internal struct Health
{
    public int Current;
    public int Max;
}

internal struct PlayerInput
{
    public Vector3 MoveDirection;
    public bool Attack;
    public bool UseItem;
}

internal struct CombatStats
{
    public int Damage;
    public int Defense;
}

internal struct AIState
{
    public Entity Target;
    public AIStateType State;
}

internal enum AIStateType : byte
{
    Idle, Patrol, Chase, Attack, Flee
}

internal struct ItemEffect
{
    public int HealthBonus;
}

// Tags
internal struct PlayerTag { }
internal struct EnemyTag { }
internal struct ItemTag { }

// Events
internal struct AttackEvent : IEvent
{
    public Entity Attacker;
    public Entity Target;
    public int Damage;
}

// Game Systems (simplified)
internal class InputSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        // Process player input
        if (world.Events<string>().Count() > 0)
            world.Events<string>().ConsumeAll(e => { if (e.Contains("Input")) { } });
    }
    
    public SystemDependencies GetDependencies() => SystemDependencies.WriteOnly(typeof(PlayerInput));
}

internal class MovementSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        var query = world.Query().With<Transform>().With<PlayerInput>();
        foreach (var chunk in query.Chunks())
        {
            var transforms = chunk.GetSpan<Transform>();
            var inputs = chunk.GetSpan<PlayerInput>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                transforms[i].Position.X += inputs[i].MoveDirection.X * deltaTime * 10;
                transforms[i].Position.Y += inputs[i].MoveDirection.Y * deltaTime * 10;
                transforms[i].Position.Z += inputs[i].MoveDirection.Z * deltaTime * 10;
            }
        }
    }
    
    public SystemDependencies GetDependencies() => 
        SystemDependencies.ReadWrite(new[] { typeof(PlayerInput) }, new[] { typeof(Transform) });
}

internal class AISystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        // Simple AI behavior
    }
    
    public SystemDependencies GetDependencies() => 
        SystemDependencies.ReadWrite(new[] { typeof(AIState) }, new[] { typeof(Transform) });
}

internal class CollisionSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        // Collision detection
    }
    
    public SystemDependencies GetDependencies() => SystemDependencies.ReadOnly(typeof(Transform));
}

internal class CombatSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        // Combat processing
        var query = world.Query().With<PlayerInput>().With<CombatStats>().With<Transform>();
        var events = world.Events<AttackEvent>();
        
        foreach (var chunk in query.Chunks())
        {
            var inputs = chunk.GetSpan<PlayerInput>();
            var stats = chunk.GetSpan<CombatStats>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                if (inputs[i].Attack)
                {
                    events.Publish(new AttackEvent 
                    { 
                        Attacker = chunk.GetEntity(i),
                        Damage = stats[i].Damage 
                    });
                }
            }
        }
    }
    
    public SystemDependencies GetDependencies() => 
        SystemDependencies.ReadWrite(new[] { typeof(PlayerInput), typeof(CombatStats) }, new[] { typeof(Health) });
}

internal class PickupSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        // Item pickup logic
    }
    
    public SystemDependencies GetDependencies() => 
        SystemDependencies.ReadWrite(new[] { typeof(Transform), typeof(ItemEffect) }, new[] { typeof(Health) });
}

internal class HealthSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        // Health regeneration and death
    }
    
    public SystemDependencies GetDependencies() => SystemDependencies.WriteOnly(typeof(Health));
}

internal class RespawnSystem : ISystem
{
    public void Execute(World world, float deltaTime)
    {
        // Respawn dead entities
    }
    
    public SystemDependencies GetDependencies() => SystemDependencies.ReadOnly();
}

// Save data
internal class GameSaveData
{
    public WorldSnapshot Snapshot { get; set; } = null!;
    public uint[] PlayerIds { get; set; } = null!;
    public uint[] EnemyIds { get; set; } = null!;
    public uint[] ItemIds { get; set; } = null!;
}