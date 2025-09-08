using System;
using PurlieuEcs.Core;
using PurlieuEcs.Common;

class TestArchetypeIssue
{
    static void Main()
    {
        Console.WriteLine("=== Testing Archetype Issue ===");
        
        var world = new World();
        world.RegisterComponent<Position>();
        world.RegisterComponent<Velocity>();
        
        Console.WriteLine("\n1. Creating entity with both components using Create<T1,T2>:");
        var entity = world.Create(
            new Position { X = 0, Y = 0, Z = 0 },
            new Velocity { X = 1, Y = 2, Z = 3 }
        );
        
        Console.WriteLine($"   Entity created: {entity}");
        Console.WriteLine($"   Has Position: {world.HasComponent<Position>(entity)}");
        Console.WriteLine($"   Has Velocity: {world.HasComponent<Velocity>(entity)}");
        
        Console.WriteLine("\n2. Querying for entities with both Position AND Velocity:");
        var query = world.Query().With<Position>().With<Velocity>();
        
        int count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
            Console.WriteLine($"   Found chunk with {chunk.Count} entities");
        }
        Console.WriteLine($"   Total entities found: {count}");
        
        Console.WriteLine("\n3. Querying for entities with just Position:");
        var posQuery = world.Query().With<Position>();
        int posCount = 0;
        foreach (var chunk in posQuery.ChunksStack())
        {
            posCount += chunk.Count;
        }
        Console.WriteLine($"   Found {posCount} entities with Position");
        
        Console.WriteLine("\n4. Querying for entities with just Velocity:");
        var velQuery = world.Query().With<Velocity>();
        int velCount = 0;
        foreach (var chunk in velQuery.ChunksStack())
        {
            velCount += chunk.Count;
        }
        Console.WriteLine($"   Found {velCount} entities with Velocity");
        
        Console.WriteLine("\n5. Creating entity by adding components separately:");
        var entity2 = world.CreateEntity();
        Console.WriteLine($"   Created empty entity: {entity2}");
        world.AddComponent(entity2, new Position { X = 10, Y = 10, Z = 10 });
        Console.WriteLine($"   Added Position - Has Position: {world.HasComponent<Position>(entity2)}");
        world.AddComponent(entity2, new Velocity { X = 5, Y = 5, Z = 5 });
        Console.WriteLine($"   Added Velocity - Has Velocity: {world.HasComponent<Velocity>(entity2)}");
        
        Console.WriteLine("\n6. Querying again for entities with both components:");
        count = 0;
        foreach (var chunk in query.ChunksStack())
        {
            count += chunk.Count;
            Console.WriteLine($"   Found chunk with {chunk.Count} entities");
        }
        Console.WriteLine($"   Total entities found: {count}");
        
        Console.WriteLine("\n=== Test Complete ===");
    }
}