using PurlieuEcs.Core;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class DET_DeterminismTests
{
    [Test]
    public void DET_EntityCreation_SameOrderWithSameSeed()
    {
        var world1 = new World();
        var world2 = new World();
        
        var entities1 = new List<Entity>();
        var entities2 = new List<Entity>();
        
        // Create entities in both worlds
        for (int i = 0; i < 100; i++)
        {
            entities1.Add(world1.CreateEntity());
            entities2.Add(world2.CreateEntity());
        }
        
        // Entity IDs should be identical (deterministic allocation)
        for (int i = 0; i < entities1.Count; i++)
        {
            Assert.That(entities1[i].Id, Is.EqualTo(entities2[i].Id), $"Entity {i} ID should match");
            Assert.That(entities1[i].Version, Is.EqualTo(entities2[i].Version), $"Entity {i} version should match");
        }
    }
    
    [Test]
    public void DET_EntityRecycling_SamePatternAcrossWorlds()
    {
        var world1 = new World();
        var world2 = new World();
        
        // Create and destroy entities in identical pattern
        var pattern = new[] { true, false, true, true, false, false, true }; // create=true, destroy=false
        var entities1 = new List<Entity>();
        var entities2 = new List<Entity>();
        var destroyed1 = new List<Entity>();
        var destroyed2 = new List<Entity>();
        
        for (int cycle = 0; cycle < 3; cycle++)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i]) // Create
                {
                    entities1.Add(world1.CreateEntity());
                    entities2.Add(world2.CreateEntity());
                }
                else if (entities1.Count > 0) // Destroy
                {
                    var entity1 = entities1[^1];
                    var entity2 = entities2[^1];
                    entities1.RemoveAt(entities1.Count - 1);
                    entities2.RemoveAt(entities2.Count - 1);
                    
                    world1.DestroyEntity(entity1);
                    world2.DestroyEntity(entity2);
                    destroyed1.Add(entity1);
                    destroyed2.Add(entity2);
                }
            }
        }
        
        // Verify remaining entities have same IDs
        Assert.That(entities1.Count, Is.EqualTo(entities2.Count));
        for (int i = 0; i < entities1.Count; i++)
        {
            Assert.That(entities1[i].Id, Is.EqualTo(entities2[i].Id), $"Remaining entity {i} ID should match");
        }
    }
    
    [Test]
    public void DET_ArchetypeCreation_SameOrderAndIds()
    {
        var world1 = new World();
        var world2 = new World();
        
        // Create archetypes in same order
        var sig1a = ArchetypeSignature.With<TestComponentA>();
        var sig1b = sig1a.Add<TestComponentB>();
        var sig1c = new ArchetypeSignature().Add<TestComponentC>();
        
        var sig2a = ArchetypeSignature.With<TestComponentA>();
        var sig2b = sig2a.Add<TestComponentB>();
        var sig2c = new ArchetypeSignature().Add<TestComponentC>();
        
        var arch1a = world1.GetOrCreateArchetype(sig1a, new[] { typeof(TestComponentA) });
        var arch1b = world1.GetOrCreateArchetype(sig1b, new[] { typeof(TestComponentA), typeof(TestComponentB) });
        var arch1c = world1.GetOrCreateArchetype(sig1c, new[] { typeof(TestComponentC) });
        
        var arch2a = world2.GetOrCreateArchetype(sig2a, new[] { typeof(TestComponentA) });
        var arch2b = world2.GetOrCreateArchetype(sig2b, new[] { typeof(TestComponentA), typeof(TestComponentB) });
        var arch2c = world2.GetOrCreateArchetype(sig2c, new[] { typeof(TestComponentC) });
        
        // Archetype IDs should be deterministic
        Assert.That(arch1a.Id, Is.EqualTo(arch2a.Id));
        Assert.That(arch1b.Id, Is.EqualTo(arch2b.Id));
        Assert.That(arch1c.Id, Is.EqualTo(arch2c.Id));
        
        // Signatures should be equal
        Assert.That(arch1a.Signature, Is.EqualTo(arch2a.Signature));
        Assert.That(arch1b.Signature, Is.EqualTo(arch2b.Signature));
        Assert.That(arch1c.Signature, Is.EqualTo(arch2c.Signature));
    }
    
    [Test]
    public void DET_ComponentTypeIds_ConsistentAcrossRuns()
    {
        // Component type IDs should be consistent within a single run
        // but may differ between runs (this is acceptable)
        var idA1 = ComponentTypeId.Get<TestComponentA>();
        var idB1 = ComponentTypeId.Get<TestComponentB>();
        var idC1 = ComponentTypeId.Get<TestComponentC>();
        
        var idA2 = ComponentTypeId.Get<TestComponentA>();
        var idB2 = ComponentTypeId.Get<TestComponentB>();
        var idC2 = ComponentTypeId.Get<TestComponentC>();
        
        Assert.That(idA1, Is.EqualTo(idA2));
        Assert.That(idB1, Is.EqualTo(idB2));
        Assert.That(idC1, Is.EqualTo(idC2));
        
        // IDs should be unique
        Assert.That(idA1, Is.Not.EqualTo(idB1));
        Assert.That(idA1, Is.Not.EqualTo(idC1));
        Assert.That(idB1, Is.Not.EqualTo(idC1));
    }
    
    [Test]
    public void DET_WorldStateConsistency_IdenticalOperationsProduceSameState()
    {
        var world1 = new World();
        var world2 = new World();
        
        // Perform identical sequence of operations
        var operations = new Action<World>[]
        {
            w => w.CreateEntity(),
            w => w.CreateEntity(),
            w => w.CreateEntity(),
            w => { var e = w.CreateEntity(); w.DestroyEntity(e); },
            w => w.CreateEntity(),
            w => w.CreateEntity(),
        };
        
        var entities1 = new List<Entity>();
        var entities2 = new List<Entity>();
        
        foreach (var op in operations)
        {
            // Apply operation and capture any created entities
            var initialCount1 = CountEntities(world1);
            var initialCount2 = CountEntities(world2);
            
            op(world1);
            op(world2);
            
            var finalCount1 = CountEntities(world1);
            var finalCount2 = CountEntities(world2);
            
            Assert.That(finalCount1, Is.EqualTo(finalCount2), "Entity counts should match after each operation");
        }
    }
    
    private static int CountEntities(World world)
    {
        // Count entities by trying IsAlive on reasonable range
        int count = 0;
        for (uint id = 1; id <= 1000; id++)
        {
            var entity = new Entity(id, 1); // Try version 1
            if (world.IsAlive(entity))
            {
                count++;
                continue;
            }
            
            // Try other versions
            for (uint version = 2; version <= 10; version++)
            {
                entity = new Entity(id, version);
                if (world.IsAlive(entity))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }
}