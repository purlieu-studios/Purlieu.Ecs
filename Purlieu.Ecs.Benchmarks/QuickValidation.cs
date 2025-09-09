using System.Diagnostics;
using PurlieuEcs.Core;

namespace Purlieu.Ecs.Benchmarks;

/// <summary>
/// Quick validation of optimize-allocation-v2 improvements without full BenchmarkDotNet overhead.
/// </summary>
public static class QuickValidation
{
    public static void Run()
    {
        Console.WriteLine("=== Purlieu ECS Optimization Validation ===");
        Console.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        ValidateEntityCreation();
        ValidateArchetypeTransitions();
        ValidateThreadSafety();
        ValidateZeroAllocation();
        ValidateSystemScheduler();
        
        Console.WriteLine("=== Validation Complete ===");
    }

    private static void ValidateEntityCreation()
    {
        Console.WriteLine("1. Entity Creation Performance:");
        
        using var world = new World();
        world.RegisterComponent<TestComponentA>();
        
        var sw = Stopwatch.StartNew();
        var entities = new Entity[10000];
        
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.CreateEntity();
            world.AddComponent(entities[i], new TestComponentA { Value = i, X = i, Y = i, Z = i });
        }
        
        sw.Stop();
        
        var entitiesPerSecond = entities.Length / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"   Created {entities.Length:N0} entities in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Rate: {entitiesPerSecond:N0} entities/sec");
        Console.WriteLine($"   Memory: {GC.GetTotalMemory(false):N0} bytes");
        Console.WriteLine();
    }

    private static void ValidateArchetypeTransitions()
    {
        Console.WriteLine("2. Archetype Transition Performance:");
        
        using var world = new World();
        world.RegisterComponent<TestComponentA>();
        world.RegisterComponent<TestComponentB>();
        
        // Create entities with one component
        var entities = new Entity[1000];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.CreateEntity();
            world.AddComponent(entities[i], new TestComponentA { Value = i });
        }
        
        var sw = Stopwatch.StartNew();
        
        // Add second component (triggers archetype transition)
        for (int i = 0; i < entities.Length; i++)
        {
            world.AddComponent(entities[i], new TestComponentB { X = i, Y = i });
        }
        
        sw.Stop();
        
        var transitionsPerSecond = entities.Length / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"   Performed {entities.Length:N0} archetype transitions in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Rate: {transitionsPerSecond:N0} transitions/sec");
        Console.WriteLine();
    }

    private static void ValidateThreadSafety()
    {
        Console.WriteLine("3. Thread Safety Validation:");
        
        using var world = new World();
        world.RegisterComponent<TestComponentA>();
        
        var exceptions = new List<Exception>();
        var entityCount = 0;
        
        var tasks = new Task[4];
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 250; i++) // 1000 total entities
                    {
                        var entity = world.CreateEntity();
                        world.AddComponent(entity, new TestComponentA { Value = i });
                        Interlocked.Increment(ref entityCount);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        Console.WriteLine($"   Created {entityCount:N0} entities across {tasks.Length} threads");
        Console.WriteLine($"   Exceptions: {exceptions.Count}");
        
        if (exceptions.Count > 0)
        {
            Console.WriteLine($"   First exception: {exceptions[0].Message}");
        }
        
        Console.WriteLine();
    }

    private static void ValidateZeroAllocation()
    {
        Console.WriteLine("4. Zero-Allocation Query Validation:");
        
        using var world = new World();
        world.RegisterComponent<TestComponentA>();
        
        // Create test data
        for (int i = 0; i < 1000; i++)
        {
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TestComponentA { Value = i });
        }
        
        // Warm up
        var query = world.Query().With<TestComponentA>();
        query.Count();
        
        // Measure with forced GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memoryBefore = GC.GetTotalMemory(false);
        
        var sw = Stopwatch.StartNew();
        long sum = 0;
        
        for (int iteration = 0; iteration < 100; iteration++)
        {
            foreach (var chunk in query.ChunksStack())
            {
                var components = chunk.GetSpan<TestComponentA>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    sum += (long)components[i].Value;
                }
            }
        }
        
        sw.Stop();
        
        var memoryAfter = GC.GetTotalMemory(false);
        var allocatedBytes = memoryAfter - memoryBefore;
        
        Console.WriteLine($"   Performed 100 query iterations in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Sum result: {sum:N0}");
        Console.WriteLine($"   Memory allocated: {allocatedBytes:N0} bytes");
        Console.WriteLine($"   Zero allocation: {(allocatedBytes <= 0 ? "✓" : "✗")}");
        Console.WriteLine();
    }

    private static void ValidateSystemScheduler()
    {
        Console.WriteLine("5. System Scheduler Performance:");
        
        using var world = new World();
        var scheduler = world.SystemScheduler;
        
        var systems = new TestSystem[50];
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i] = new TestSystem { Id = i };
        }
        
        var sw = Stopwatch.StartNew();
        
        // Test registration
        foreach (var system in systems)
        {
            scheduler.RegisterSystem(system);
        }
        
        var registrationTime = sw.ElapsedMilliseconds;
        sw.Restart();
        
        // Test execution
        scheduler.ExecuteAllPhases(world, 0.016f);
        
        var executionTime = sw.ElapsedMilliseconds;
        
        Console.WriteLine($"   Registered {systems.Length} systems in {registrationTime}ms");
        Console.WriteLine($"   Executed all systems in {executionTime}ms");
        Console.WriteLine($"   Average execution count: {systems.Average(s => s.ExecutionCount):F1}");
        Console.WriteLine();
    }

    private class TestSystem : ISystem
    {
        public int Id { get; set; }
        public int ExecutionCount { get; private set; }

        public void Execute(World world, float deltaTime)
        {
            ExecutionCount++;
        }

        public SystemDependencies GetDependencies()
        {
            return new SystemDependencies();
        }
    }
}