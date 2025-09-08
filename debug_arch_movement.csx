#r "Purlieu.Ecs.Tests\bin\Debug\net8.0\Purlieu.Ecs.dll"

using PurlieuEcs.Core;
using PurlieuEcs.Common;

Console.WriteLine("Testing Arch-style movement...");

var world = new World();

// Create one entity
Console.WriteLine("Creating entity...");
var entity = world.Create(
    new Position { X = 0, Y = 0, Z = 0 },
    new Velocity { X = 1, Y = 2, Z = 3 }
);

Console.WriteLine($"Entity created: {entity}");

// Check initial state
var initialPos = world.Get<Position>(entity);
var vel = world.Get<Velocity>(entity);

Console.WriteLine($"Initial position: X={initialPos.X}, Y={initialPos.Y}, Z={initialPos.Z}");
Console.WriteLine($"Velocity: X={vel.X}, Y={vel.Y}, Z={vel.Z}");

// Update movement
float deltaTime = 0.016f;
Console.WriteLine($"Updating movement with deltaTime={deltaTime}...");

world.UpdateMovement(deltaTime);

// Check final state
var finalPos = world.Get<Position>(entity);
Console.WriteLine($"Final position: X={finalPos.X}, Y={finalPos.Y}, Z={finalPos.Z}");

Console.WriteLine("Expected position:");
Console.WriteLine($"X: {0 + (1 * 0.016f)} (actual: {finalPos.X})");
Console.WriteLine($"Y: {0 + (2 * 0.016f)} (actual: {finalPos.Y})");
Console.WriteLine($"Z: {0 + (3 * 0.016f)} (actual: {finalPos.Z})");