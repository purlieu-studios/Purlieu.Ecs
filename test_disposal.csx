#r "Purlieu.Ecs.Tests\bin\Debug\net8.0\Purlieu.Ecs.dll"

using PurlieuEcs.Core;

Console.WriteLine("Testing disposal...");

var world = new World();
var entity = world.CreateEntity();

Console.WriteLine("Created entity, disposing world...");
world.Dispose();

try 
{
    world.CreateEntity();
    Console.WriteLine("ERROR: Should have thrown ObjectDisposedException");
}
catch (ObjectDisposedException)
{
    Console.WriteLine("✅ CreateEntity correctly threw ObjectDisposedException");
}

try 
{
    var query = world.Query();
    query.Count();
    Console.WriteLine("ERROR: Should have thrown ObjectDisposedException for query");
}
catch (ObjectDisposedException)
{
    Console.WriteLine("✅ Query.Count() correctly threw ObjectDisposedException");
}

Console.WriteLine("Disposal test completed successfully!");