#r "Purlieu.Ecs.Tests\bin\Debug\net8.0\Purlieu.Ecs.dll"

using PurlieuEcs.Core;

Console.WriteLine("Testing archetype transition component preservation...");

struct TestComp
{
    public int Value;
}

struct TestComp2  
{
    public int Value;
}

var world = new World();
var entity = world.CreateEntity();

Console.WriteLine($"1. Created entity: {entity}");

// Add first component
world.AddComponent(entity, new TestComp { Value = 42 });
Console.WriteLine($"2. Added TestComp with value 42");

// Read it back immediately
var comp1 = world.GetComponent<TestComp>(entity);
Console.WriteLine($"3. Read TestComp back: {comp1.Value} (should be 42)");

// Add second component (this triggers archetype transition)
Console.WriteLine($"4. Adding TestComp2 (this will trigger archetype transition)...");
world.AddComponent(entity, new TestComp2 { Value = 84 });

// Try to read the first component again
var comp1After = world.GetComponent<TestComp>(entity);
Console.WriteLine($"5. After transition, TestComp value: {comp1After.Value} (should still be 42)");

// Read the second component
var comp2 = world.GetComponent<TestComp2>(entity);
Console.WriteLine($"6. TestComp2 value: {comp2.Value} (should be 84)");

Console.WriteLine("Done!");