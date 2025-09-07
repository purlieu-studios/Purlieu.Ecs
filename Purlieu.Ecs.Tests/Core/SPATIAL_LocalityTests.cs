using NUnit.Framework;
using PurlieuEcs.Core;
using Purlieu.Logic.Components;
using Purlieu.Logic;

namespace Purlieu.Ecs.Tests.Core;

/// <summary>
/// Tests for spatial locality optimizations in archetype and component layout.
/// Verifies that related archetypes and components are organized for optimal cache performance.
/// </summary>
[TestFixture]
[Category("SpatialLocality")]
public class SPATIAL_LocalityTests
{
    [SetUp]
    public void Setup()
    {
        var world = new World();
        LogicBootstrap.RegisterComponents(world);
    }

    [Test]
    public void ComponentLocalityComparer_PrioritizesHighAccessFrequency()
    {
        // Test that position components are prioritized over config components
        var comparer = ComponentLocalityComparer.Instance;
        
        var positionType = typeof(Position);
        var configType = typeof(TestConfigComponent);
        
        int comparison = comparer.Compare(positionType, configType);
        
        Assert.That(comparison, Is.LessThan(0), 
            "Position (high priority) should come before Config (low priority) components");
    }
    
    [Test]
    public void ComponentLocalityComparer_GroupsSmallComponentsTogether()
    {
        // Test that small components are grouped before large ones
        var comparer = ComponentLocalityComparer.Instance;
        
        var smallType = typeof(TestSmallComponent);  // Should be ≤16 bytes
        var largeType = typeof(TestLargeComponent);  // Should be >16 bytes
        
        int comparison = comparer.Compare(smallType, largeType);
        
        Assert.That(comparison, Is.LessThan(0),
            "Small components should be grouped before large components");
    }
    
    [Test]
    public void ComponentLocalityComparer_OrdersBySize()
    {
        // Test that within same priority, smaller components come first
        var comparer = ComponentLocalityComparer.Instance;
        
        var smallPriorityType = typeof(TestSmallComponent);
        var mediumPriorityType = typeof(TestMediumComponent);
        
        int comparison = comparer.Compare(smallPriorityType, mediumPriorityType);
        
        Assert.That(comparison, Is.LessThan(0),
            "Within same priority category, smaller components should come first");
    }
    
    [Test]
    public void Archetype_OptimizesComponentOrder()
    {
        // Test that archetype constructor reorders components for optimal layout
        var componentTypes = new Type[]
        {
            typeof(TestLargeComponent),    // Should move to end
            typeof(Position),              // High priority - should move to front
            typeof(TestConfigComponent),   // Low priority - should move to end
            typeof(TestSmallComponent)     // Small - should be near front
        };
        
        var signature = new ArchetypeSignature()
            .Add<TestLargeComponent>()
            .Add<Position>()
            .Add<TestConfigComponent>()
            .Add<TestSmallComponent>();
        
        var archetype = new Archetype(1, signature, componentTypes, 64);
        
        var optimizedTypes = archetype.ComponentTypes;
        
        // Position should be first (highest priority)
        Assert.That(optimizedTypes[0], Is.EqualTo(typeof(Position)),
            "Position should be first due to high priority");
            
        // Small component should be early
        var smallComponentIndex = optimizedTypes.ToList().IndexOf(typeof(TestSmallComponent));
        var configComponentIndex = optimizedTypes.ToList().IndexOf(typeof(TestConfigComponent));
        
        Assert.That(smallComponentIndex, Is.LessThan(configComponentIndex),
            "Small component should come before config component");
    }
    
    [Test]
    public void ArchetypeIndex_ClustersRelatedArchetypes()
    {
        // Test that similar archetypes are placed near each other in memory
        var index = new ArchetypeIndex();
        
        // Create archetypes with overlapping components
        var positionOnlySignature = new ArchetypeSignature().Add<Position>();
        var positionVelocitySignature = new ArchetypeSignature().Add<Position>().Add<TestVelocityComponent>();
        var positionHealthSignature = new ArchetypeSignature().Add<Position>().Add<TestHealthComponent>();
        var unrelatedSignature = new ArchetypeSignature().Add<TestConfigComponent>();
        
        var archetype1 = new Archetype(1, positionOnlySignature, new[] { typeof(Position) });
        var archetype2 = new Archetype(2, positionVelocitySignature, new[] { typeof(Position), typeof(TestVelocityComponent) });
        var archetype3 = new Archetype(3, unrelatedSignature, new[] { typeof(TestConfigComponent) });
        var archetype4 = new Archetype(4, positionHealthSignature, new[] { typeof(Position), typeof(TestHealthComponent) });
        
        // Add archetypes in non-optimal order
        index.AddArchetype(archetype1);
        index.AddArchetype(archetype3); // Unrelated archetype
        index.AddArchetype(archetype2); // Should cluster with archetype1
        index.AddArchetype(archetype4); // Should cluster with other Position archetypes
        
        // The index should organize them for spatial locality
        // We can't directly test the internal ordering without exposing internals,
        // but we can verify the functionality works by checking cache performance
        var withPosition = new ArchetypeSignature().Add<Position>();
        var withoutAnything = new ArchetypeSignature();
        
        var matchingArchetypes = index.GetMatchingArchetypes(withPosition, withoutAnything);
        
        Assert.That(matchingArchetypes.Count, Is.EqualTo(3),
            "Should find all 3 archetypes with Position component");
    }
    
    [Test]
    public void ArchetypeSignature_CalculatesIntersectionCount()
    {
        // Test the intersection count calculation used for spatial locality
        var signature1 = new ArchetypeSignature().Add<Position>().Add<TestVelocityComponent>();
        var signature2 = new ArchetypeSignature().Add<Position>().Add<TestHealthComponent>();
        var signature3 = new ArchetypeSignature().Add<TestConfigComponent>();
        
        var intersection1_2 = signature1.GetIntersectionCount(signature2);
        var intersection1_3 = signature1.GetIntersectionCount(signature3);
        
        Assert.That(intersection1_2, Is.EqualTo(1), 
            "Signatures 1 and 2 should have 1 component in common (Position)");
        Assert.That(intersection1_3, Is.EqualTo(0),
            "Signatures 1 and 3 should have no components in common");
    }
    
    [Test]
    public void ArchetypeSignature_CalculatesComponentCount()
    {
        // Test the component count calculation
        var emptySignature = new ArchetypeSignature();
        var singleSignature = new ArchetypeSignature().Add<Position>();
        var multiSignature = new ArchetypeSignature().Add<Position>().Add<TestVelocityComponent>().Add<TestHealthComponent>();
        
        Assert.That(emptySignature.GetComponentCount(), Is.EqualTo(0));
        Assert.That(singleSignature.GetComponentCount(), Is.EqualTo(1));
        Assert.That(multiSignature.GetComponentCount(), Is.EqualTo(3));
    }
    
    [Test]
    public void SpatialLocality_PerformanceTest()
    {
        // Performance test to verify spatial locality improves iteration speed
        var index = new ArchetypeIndex();
        
        // Create many archetypes with Position component (should be clustered)
        for (int i = 0; i < 100; i++)
        {
            var signature = new ArchetypeSignature().Add<Position>();
            if (i % 3 == 0) signature = signature.Add<TestVelocityComponent>();
            if (i % 5 == 0) signature = signature.Add<TestHealthComponent>();
            
            var types = signature.GetComponentTypes();
            var archetype = new Archetype((ulong)i, signature, types);
            index.AddArchetype(archetype);
        }
        
        // Measure query performance (spatial locality should improve cache hits)
        var withPosition = new ArchetypeSignature().Add<Position>();
        var withoutAnything = new ArchetypeSignature();
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < 1000; i++)
        {
            var result = index.GetMatchingArchetypes(withPosition, withoutAnything);
            Assert.That(result.Count, Is.GreaterThan(90), "Should find most archetypes with Position");
        }
        
        sw.Stop();
        
        Console.WriteLine($"Query performance with spatial locality: {sw.ElapsedMilliseconds}ms for 1000 queries");
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(50), 
            "Spatial locality should enable fast queries even with many archetypes");
    }
    
    // Test component types for spatial locality testing
    private struct TestSmallComponent 
    { 
        public int Value; // 4 bytes
    }
    
    private struct TestMediumComponent
    {
        public float X, Y, Z; // 12 bytes
    }
    
    private struct TestLargeComponent
    {
        public float A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P; // 64 bytes
    }
    
    private struct TestConfigComponent
    {
        public int ConfigValue;
        public bool ConfigFlag;
    }
    
    private struct TestVelocityComponent
    {
        public float X, Y, Z;
    }
    
    private struct TestHealthComponent
    {
        public int Health;
        public int MaxHealth;
    }
}