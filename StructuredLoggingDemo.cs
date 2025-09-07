using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Logging;

// Demonstrate structured logging with correlation IDs and zero-allocation patterns

Console.WriteLine("=== Purlieu ECS Structured Logging Demo ===\n");

// 1. Create world with console logger at Debug level
var logger = new ConsoleEcsLogger(LogLevel.Debug, useColors: true);
var world = new World(logger: logger);

Console.WriteLine("1. Creating entities with logging...");

// Set a custom correlation ID for this operation batch
CorrelationContext.Set("DEMO001");

// Create entities - these operations will be logged
var playerEntity = world.Create(new Position(10, 20, 30));
var enemyEntity = world.Create(new Position(100, 200, 300), new Velocity(1, 0, 0));

Console.WriteLine("\n2. Component operations with correlation tracking...");

// New correlation for next batch of operations
CorrelationContext.NewCorrelation();

// Add components - creates archetype transitions
world.Add(playerEntity, new Velocity(5, 10, 15));
world.Add(playerEntity, new Health(100, 100));

Console.WriteLine("\n3. Accessing components (trace level - may not show unless trace enabled)...");

// Component access - logged at trace level
var playerPos = world.Get<Position>(playerEntity);
var playerVel = world.Get<Velocity>(playerEntity);

Console.WriteLine($"Player position: ({playerPos.X}, {playerPos.Y}, {playerPos.Z})");
Console.WriteLine($"Player velocity: ({playerVel.X}, {playerVel.Y}, {playerVel.Z})");

Console.WriteLine("\n4. Performance metrics logging...");

// Log some performance metrics
logger.LogPerformanceMetric("entity_count", world.Count<Position>());
logger.LogPerformanceMetric("archetype_count", world.GetQueryCacheStatistics().CacheSize);

Console.WriteLine("\n5. System operations with automatic optimization...");

CorrelationContext.Set("PHYSICS");

// Use the clean Arch-style API that automatically chooses optimal path
world.UpdateMovement(0.016f);

Console.WriteLine($"Updated entity positions with deltaTime=0.016f");

Console.WriteLine("\n6. Entity destruction...");

// Destroy entities
world.DestroyEntity(playerEntity);
world.DestroyEntity(enemyEntity);

Console.WriteLine("\n7. Memory and performance statistics...");

var stats = world.GetQueryCacheStatistics();
logger.LogPerformanceMetric("cache_hits", stats.Hits);
logger.LogPerformanceMetric("cache_misses", stats.Misses);
logger.LogPerformanceMetric("cache_hit_ratio", (long)(stats.HitRatio * 100), "%");

world.Dispose();

Console.WriteLine("\n=== Demo completed successfully! ===");
Console.WriteLine("\nKey features demonstrated:");
Console.WriteLine("✓ Zero-allocation structured logging with IEcsLogger");
Console.WriteLine("✓ Correlation ID tracking across operations");
Console.WriteLine("✓ Automatic archetype transition logging");
Console.WriteLine("✓ Performance metrics integration");
Console.WriteLine("✓ Clean console output with timestamps and colors");
Console.WriteLine("✓ Production-ready NullLogger fallback");