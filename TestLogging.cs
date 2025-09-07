// Simple test to verify logging compilation
using System;
using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Logging;

class TestLogging
{
    static void Main()
    {
        // Test that logging compiles and works
        var logger = new ConsoleEcsLogger(LogLevel.Debug);
        var world = new World(logger: logger);
        
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Position(1, 2, 3));
        var pos = world.GetComponent<Position>(entity);
        
        logger.LogPerformanceMetric("test_metric", 42, "ms");
        
        world.Dispose();
        Console.WriteLine("Logging integration test completed successfully!");
    }
}