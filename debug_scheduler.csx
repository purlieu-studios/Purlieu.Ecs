#!/usr/bin/env dotnet-script
#r "Purlieu.Ecs.dll"
#r "Purlieu.Logic.dll"

using PurlieuEcs.Core;
using PurlieuEcs.Systems;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

// Test classes from the test file
internal class TestSystem : ISystem
{
    public string Name { get; }
    public SystemPhase Phase { get; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecute { get; set; }

    public TestSystem(string name, SystemPhase phase = SystemPhase.Update)
    {
        Name = name;
        Phase = phase;
    }

    public void Execute(World world, float deltaTime)
    {
        ExecutionCount++;
        OnExecute?.Invoke();
    }

    public SystemDependencies GetDependencies()
    {
        return SystemDependencies.ReadOnly();
    }
}

internal class TestDependentSystem : ISystem
{
    public string Name { get; }
    public Type[] RunAfterTypes { get; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecute { get; set; }

    public TestDependentSystem(string name, params Type[] runAfter)
    {
        Name = name;
        RunAfterTypes = runAfter;
    }

    public void Execute(World world, float deltaTime)
    {
        ExecutionCount++;
        OnExecute?.Invoke();
    }

    public SystemDependencies GetDependencies()
    {
        return new SystemDependencies(runAfter: RunAfterTypes);
    }
}

// Create scheduler and systems
var scheduler = new SystemScheduler();
var world = new World();

var earlySystem = new TestSystem("Early", SystemPhase.EarlyUpdate);
var dependentSystem = new TestDependentSystem("Dependent", typeof(TestSystem));
var lateSystem = new TestSystem("Late", SystemPhase.LateUpdate);

scheduler.RegisterSystem(lateSystem);
scheduler.RegisterSystem(dependentSystem); 
scheduler.RegisterSystem(earlySystem);

var executionOrder = new ConcurrentQueue<string>();

earlySystem.OnExecute = () => executionOrder.Enqueue("Early");
dependentSystem.OnExecute = () => executionOrder.Enqueue("Dependent");
lateSystem.OnExecute = () => executionOrder.Enqueue("Late");

// Run multiple times
Console.WriteLine("Testing system execution order:");
for (int run = 0; run < 5; run++)
{
    executionOrder.Clear();
    scheduler.ExecuteAllPhases(world, 0.016f);
    
    var order = executionOrder.ToArray();
    Console.WriteLine($"Run {run + 1}: [{string.Join(", ", order)}]");
}