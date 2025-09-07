// Health Monitoring Demo - Demonstrates real-time ECS performance tracking
using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Logging;
using PurlieuEcs.Monitoring;

Console.WriteLine("=== Purlieu ECS Health Monitoring Demo ===\n");

// Create world with full monitoring enabled
var logger = new ConsoleEcsLogger(LogLevel.Information, useColors: true);
var healthMonitor = new EcsHealthMonitor(logger);
var world = new World(logger: logger, healthMonitor: healthMonitor);

Console.WriteLine("1. Creating entities with health monitoring...");

// Simulate frame-based game loop
for (int frame = 0; frame < 10; frame++)
{
    healthMonitor.StartFrame();
    
    // Create entities (triggers monitoring)
    for (int i = 0; i < 100; i++)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Position(i, i, i));
        world.AddComponent(entity, new Velocity(i % 10, i % 5, 0));
        
        // Occasionally add health component (triggers archetype transitions)
        if (i % 10 == 0)
        {
            world.AddComponent(entity, new Health(100, 100));
        }
    }
    
    healthMonitor.EndFrame();
    Thread.Sleep(16); // Simulate 60 FPS target
}

Console.WriteLine("\n2. Performance Metrics:");
var performanceMetrics = healthMonitor.GetPerformanceMetrics();

Console.WriteLine($"   Total Entity Operations: {performanceMetrics.TotalEntityOperations:N0}");
Console.WriteLine($"   Average Entity Operation: {performanceMetrics.AverageEntityOperationMicros:F2} μs");
Console.WriteLine($"   Total Archetype Transitions: {performanceMetrics.TotalArchetypeTransitions:N0}");
Console.WriteLine($"   Average Transition Time: {performanceMetrics.AverageArchetypeTransitionMicros:F2} μs");
Console.WriteLine($"   Current FPS: {performanceMetrics.CurrentFPS:F1}");
Console.WriteLine($"   Frame Count: {performanceMetrics.FrameCount:N0}");

Console.WriteLine("\n3. Memory Metrics:");
var memoryMetrics = healthMonitor.GetMemoryMetrics();

Console.WriteLine($"   Total Managed Memory: {memoryMetrics.TotalManagedBytes / 1024:N0} KB");
Console.WriteLine($"   Chunk Memory: {memoryMetrics.ChunkMemoryBytes / 1024:N0} KB");
Console.WriteLine($"   Active Chunks: {memoryMetrics.ActiveChunks:N0}");
Console.WriteLine($"   Active Archetypes: {memoryMetrics.ActiveArchetypes:N0}");
Console.WriteLine($"   GC Collections: Gen0={memoryMetrics.Generation0Collections} Gen1={memoryMetrics.Generation1Collections} Gen2={memoryMetrics.Generation2Collections}");

Console.WriteLine("\n4. Health Status:");
var healthStatus = healthMonitor.GetHealthStatus();
var statusColor = healthStatus switch
{
    HealthStatus.Healthy => ConsoleColor.Green,
    HealthStatus.Warning => ConsoleColor.Yellow,
    HealthStatus.Critical => ConsoleColor.Red,
    HealthStatus.Failure => ConsoleColor.DarkRed,
    _ => ConsoleColor.White
};

Console.ForegroundColor = statusColor;
Console.WriteLine($"   Overall Health: {healthStatus}");
Console.ResetColor();

Console.WriteLine("\n5. Stress Testing...");

// Stress test to trigger performance warnings
Console.WriteLine("   Creating 10,000 entities rapidly...");
var stopwatch = System.Diagnostics.Stopwatch.StartNew();

healthMonitor.StartFrame();
for (int i = 0; i < 10000; i++)
{
    var entity = world.CreateEntity();
    world.AddComponent(entity, new Position(i, i, i));
    world.AddComponent(entity, new Velocity(1, 1, 1));
    world.AddComponent(entity, new Health(100 + i, 100 + i));
}
healthMonitor.EndFrame();

stopwatch.Stop();

Console.WriteLine($"   Created 10,000 entities in {stopwatch.ElapsedMilliseconds} ms");

// Check health status after stress test
var stressHealthStatus = healthMonitor.GetHealthStatus();
var stressMetrics = healthMonitor.GetPerformanceMetrics();

Console.WriteLine($"   Health Status After Stress Test: {stressHealthStatus}");
Console.WriteLine($"   Total Operations: {stressMetrics.TotalEntityOperations:N0}");
Console.WriteLine($"   Average Operation Time: {stressMetrics.AverageEntityOperationMicros:F2} μs");

Console.WriteLine("\n6. Production vs Development Monitoring:");

// Compare with null monitor (production setup)
Console.WriteLine("   Benchmarking null monitor (production mode)...");
var nullWorld = new World(healthMonitor: NullEcsHealthMonitor.Instance);

var nullStopwatch = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < 1000; i++)
{
    var entity = nullWorld.CreateEntity();
    nullWorld.AddComponent(entity, new Position(i, i, i));
}
nullStopwatch.Stop();

Console.WriteLine($"   Null monitor (production): {nullStopwatch.ElapsedMilliseconds} ms for 1,000 operations");

// Compare with full monitoring
var monitorStopwatch = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < 1000; i++)
{
    var entity = world.CreateEntity();
    world.AddComponent(entity, new Position(i + 10000, i + 10000, i + 10000));
}
monitorStopwatch.Stop();

Console.WriteLine($"   Full monitoring (debug): {monitorStopwatch.ElapsedMilliseconds} ms for 1,000 operations");
Console.WriteLine($"   Monitoring overhead: {((double)monitorStopwatch.ElapsedMilliseconds / nullStopwatch.ElapsedMilliseconds):F2}x");

// Cleanup
world.Dispose();
nullWorld.Dispose();
healthMonitor.Dispose();

Console.WriteLine("\n=== Demo completed successfully! ===");
Console.WriteLine("\nKey health monitoring features demonstrated:");
Console.WriteLine("✓ Real-time performance metrics (entity operations, queries, transitions)");
Console.WriteLine("✓ Memory usage tracking (managed/unmanaged, chunks, archetypes)");
Console.WriteLine("✓ Frame timing and FPS calculation");
Console.WriteLine("✓ Health status assessment (healthy/warning/critical/failure)");
Console.WriteLine("✓ Production-ready zero-overhead null monitoring");
Console.WriteLine("✓ Thread-safe lock-free performance counters");
Console.WriteLine("✓ Automatic integration with World operations");