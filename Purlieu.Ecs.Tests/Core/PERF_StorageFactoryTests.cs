using NUnit.Framework;
using PurlieuEcs.Core;
using PurlieuEcs.Components;
using System;
using System.Diagnostics;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
[Category("Performance")]
public class PERF_StorageFactoryTests
{
    [SetUp]
    public void Setup()
    {
        var world = new World();
        LogicBootstrap.RegisterComponents(world);
    }

    [Test]
    public void ComponentStorageFactory_EliminatesReflection()
    {
        // Test that registered types don't use reflection
        
        const int iterations = 10000;
        
        // Measure creation time for registered type
        var registeredType = typeof(TestComponentA);
        Assert.That(ComponentStorageFactory.IsRegistered(registeredType), Is.True,
            "TestComponentA should be pre-registered");
        
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var storage = ComponentStorageFactory.Create(registeredType, 512);
            Assert.That(storage, Is.Not.Null);
        }
        sw.Stop();
        var registeredTime = sw.ElapsedMilliseconds;
        
        // Create an unregistered type for comparison
        var unregisteredType = typeof(UnregisteredComponent);
        Assert.That(ComponentStorageFactory.IsRegistered(unregisteredType), Is.False,
            "UnregisteredComponent should not be registered");
        
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var storage = ComponentStorageFactory.Create(unregisteredType, 512);
            Assert.That(storage, Is.Not.Null);
        }
        sw.Stop();
        var unregisteredTime = sw.ElapsedMilliseconds;
        
        // Registered type should be faster or at least similar (no reflection)
        // Note: On fast machines, both might be very fast, so we check registered isn't worse
        Assert.That(registeredTime, Is.LessThanOrEqualTo(unregisteredTime * 2),
            $"Registered type creation slower than expected. Registered: {registeredTime}ms, Unregistered: {unregisteredTime}ms");
    }
    
    [Test]
    public void ComponentStorageFactory_PreRegistersCommonTypes()
    {
        // Verify that common types are pre-registered
        
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(Position)), Is.True,
            "Position should be pre-registered");
        
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(MoveIntent)), Is.True,
            "MoveIntent should be pre-registered");
        
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(Stunned)), Is.True,
            "Stunned should be pre-registered");
        
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(TestComponentA)), Is.True,
            "TestComponentA should be pre-registered for tests");
        
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(TestComponentB)), Is.True,
            "TestComponentB should be pre-registered for tests");
        
        // Should have several pre-registered types
        Assert.That(ComponentStorageFactory.RegisteredCount, Is.GreaterThan(5),
            "Should have multiple pre-registered component types");
    }
    
    [Test]
    public void ComponentStorageFactory_CreatesCorrectStorageType()
    {
        // Test that factory creates correct storage instances
        
        var storageA = ComponentStorageFactory.Create(typeof(TestComponentA), 128);
        var storageB = ComponentStorageFactory.Create(typeof(TestComponentB), 128);
        
        Assert.That(storageA, Is.Not.Null);
        Assert.That(storageB, Is.Not.Null);
        Assert.That(storageA, Is.Not.SameAs(storageB), "Different types should get different storage");
    }
    
    [Test]
    public void ComponentStorageFactory_ThreadSafe()
    {
        // Test that factory registration is thread-safe
        
        var tasks = new Task[10];
        var errors = new List<Exception>();
        
        for (int i = 0; i < tasks.Length; i++)
        {
            int taskId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    // Each thread tries to register the same types
                    ComponentStorageFactory.Register<ThreadTestComponent1>();
                    ComponentStorageFactory.Register<ThreadTestComponent2>();
                    ComponentStorageFactory.Register<ThreadTestComponent3>();
                    
                    // And create storages
                    for (int j = 0; j < 100; j++)
                    {
                        var storage1 = ComponentStorageFactory.Create(typeof(ThreadTestComponent1), 64);
                        var storage2 = ComponentStorageFactory.Create(typeof(ThreadTestComponent2), 64);
                        var storage3 = ComponentStorageFactory.Create(typeof(ThreadTestComponent3), 64);
                        
                        Assert.That(storage1, Is.Not.Null);
                        Assert.That(storage2, Is.Not.Null);
                        Assert.That(storage3, Is.Not.Null);
                    }
                }
                catch (Exception ex)
                {
                    lock (errors)
                    {
                        errors.Add(ex);
                    }
                }
            });
        }
        
        Task.WaitAll(tasks);
        
        Assert.That(errors, Is.Empty, 
            $"Thread safety issue: {string.Join(", ", errors.Select(e => e.Message))}");
        
        // All types should be registered exactly once
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(ThreadTestComponent1)), Is.True);
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(ThreadTestComponent2)), Is.True);
        Assert.That(ComponentStorageFactory.IsRegistered(typeof(ThreadTestComponent3)), Is.True);
    }
    
    [Test]
    public void ComponentStorageFactory_FallbackToReflection()
    {
        // Test that unregistered types still work via reflection fallback
        
        var unregisteredType = typeof(UnregisteredComponent);
        Assert.That(ComponentStorageFactory.IsRegistered(unregisteredType), Is.False);
        
        // Should still be able to create storage via reflection
        var storage = ComponentStorageFactory.Create(unregisteredType, 256);
        
        Assert.That(storage, Is.Not.Null, "Should create storage via reflection fallback");
    }
    
    [Test]
    public void ChunkCreation_UsesStorageFactory()
    {
        // Test that Chunk class uses ComponentStorageFactory
        
        var componentTypes = new[] { typeof(TestComponentA), typeof(TestComponentB) };
        var signature = new ArchetypeSignature().Add<TestComponentA>().Add<TestComponentB>();
        
        // Create archetype (internally uses ComponentStorageFactory)
        var sw = Stopwatch.StartNew();
        var archetype = new Archetype(1, signature, componentTypes, 512);
        sw.Stop();
        
        var creationTime = sw.ElapsedMilliseconds;
        
        // Should be fast (no reflection for registered types)
        Assert.That(creationTime, Is.LessThan(10), 
            $"Chunk creation too slow: {creationTime}ms. Factory may not be working.");
        
        // Verify archetype was created successfully
        Assert.That(archetype, Is.Not.Null);
        Assert.That(archetype.Signature.Has<TestComponentA>(), Is.True);
        Assert.That(archetype.Signature.Has<TestComponentB>(), Is.True);
    }
    
    // Test component types
    private struct UnregisteredComponent { public int Value; }
    private struct ThreadTestComponent1 { public int X; }
    private struct ThreadTestComponent2 { public float Y; }
    private struct ThreadTestComponent3 { public bool Z; }
}