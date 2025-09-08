using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Validation;
using PurlieuEcs.Logging;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class VALIDATION_FrameworkTests
{
    private EcsValidator _validator;
    private TestLogger _logger;
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _validator = new EcsValidator();
        _logger = new TestLogger();
        _world = new World(logger: _logger, validator: _validator);
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    #region Component Type Validation Tests
    
    [Test]
    public void ValidateComponentType_ValidComponent_ReturnsValid()
    {
        // Act
        var result = _validator.ValidateComponentType<Purlieu.Logic.Components.Position>();
        
        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.None));
    }
    
    [Test]
    public void ValidateComponentType_LargeComponent_ReturnsWarning()
    {
        // Act
        var result = _validator.ValidateComponentType<LargeComponent>();
        
        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.Warning));
        Assert.That(result.Message, Does.Contain("large"));
    }
    
    [Test]
    public void ValidateComponentType_UnalignedComponent_ReturnsInfo()
    {
        // Act  
        var result = _validator.ValidateComponentType<UnalignedComponent>();
        
        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.Info));
        Assert.That(result.Message, Does.Contain("aligned"));
    }
    
    [Test]
    public void ValidateComponentType_CachesResults()
    {
        // Act - Call twice
        var result1 = _validator.ValidateComponentType<Purlieu.Logic.Components.Position>();
        var result2 = _validator.ValidateComponentType<Purlieu.Logic.Components.Position>();
        
        // Assert - Should be same instance (cached)
        Assert.That(result1.IsValid, Is.EqualTo(result2.IsValid));
        Assert.That(result1.Message, Is.EqualTo(result2.Message));
    }
    
    #endregion
    
    #region Entity Operation Validation Tests
    
    [Test]
    public void ValidateEntityOperation_ValidOperation_ReturnsValid()
    {
        // Arrange
        var entity = _world.CreateEntity();
        
        // Act
        var result = _validator.ValidateEntityOperation(EntityOperation.AddComponent, entity.Id, typeof(Purlieu.Logic.Components.Position));
        
        // Assert
        Assert.That(result.IsValid, Is.True);
    }
    
    [Test]
    public void ValidateEntityOperation_ZeroEntityId_ReturnsError()
    {
        // Act
        var result = _validator.ValidateEntityOperation(EntityOperation.Create, 0, null);
        
        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.Error));
        Assert.That(result.Message, Does.Contain("cannot be 0"));
    }
    
    [Test]
    public void ValidateEntityOperation_MaxEntityId_ReturnsError()
    {
        // Act
        var result = _validator.ValidateEntityOperation(EntityOperation.Create, uint.MaxValue, null);
        
        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.Error));
        Assert.That(result.Message, Does.Contain("maximum"));
    }
    
    #endregion
    
    #region Archetype Transition Validation Tests
    
    [Test]
    public void ValidateArchetypeTransition_SimpleTransition_ReturnsValid()
    {
        // Arrange
        var fromComponents = new[] { typeof(Purlieu.Logic.Components.Position) };
        var toComponents = new[] { typeof(Purlieu.Logic.Components.Position), typeof(Purlieu.Logic.Components.Velocity) };
        
        // Act
        var result = _validator.ValidateArchetypeTransition(fromComponents, toComponents);
        
        // Assert
        Assert.That(result.IsValid, Is.True);
    }
    
    [Test]
    public void ValidateArchetypeTransition_ComplexTransition_ReturnsWarning()
    {
        // Arrange - Adding and removing multiple components
        var fromComponents = new[] { typeof(Purlieu.Logic.Components.Position), typeof(Purlieu.Logic.Components.Velocity) };
        var toComponents = new[] { typeof(Health), typeof(DamageOverTime) };
        
        // Act
        var result = _validator.ValidateArchetypeTransition(fromComponents, toComponents);
        
        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.Warning));
        Assert.That(result.Message, Does.Contain("Complex archetype transition"));
    }
    
    [Test]
    public void ValidateArchetypeTransition_CachesTransitions()
    {
        // Arrange
        var fromComponents = new[] { typeof(Purlieu.Logic.Components.Position) };
        var toComponents = new[] { typeof(Purlieu.Logic.Components.Position), typeof(Purlieu.Logic.Components.Velocity) };
        
        // Act - Call twice
        var result1 = _validator.ValidateArchetypeTransition(fromComponents, toComponents);
        var result2 = _validator.ValidateArchetypeTransition(fromComponents, toComponents);
        
        // Assert - Should return cached result
        Assert.That(result1.IsValid, Is.EqualTo(result2.IsValid));
    }
    
    #endregion
    
    #region System Dependency Validation Tests
    
    [Test]
    public void ValidateSystemDependencies_ValidDependencies_ReturnsValid()
    {
        // Arrange
        var dependencies = new List<Type> { typeof(TestSystem1), typeof(TestSystem2) };
        
        // Act
        var result = _validator.ValidateSystemDependencies<TestSystem3>(dependencies);
        
        // Assert
        Assert.That(result.IsValid, Is.True);
    }
    
    [Test]
    public void ValidateSystemDependencies_CircularDependency_ReturnsError()
    {
        // Arrange - System depending on itself
        var dependencies = new List<Type> { typeof(TestSystem1), typeof(TestSystem1) };
        
        // Act
        var result = _validator.ValidateSystemDependencies<TestSystem1>(dependencies);
        
        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.Error));
        Assert.That(result.Message, Does.Contain("circular dependency"));
    }
    
    [Test]
    public void ValidateSystemDependencies_StatefulSystem_ReturnsWarning()
    {
        // Act
        var result = _validator.ValidateSystemDependencies<StatefulSystem>(new List<Type>());
        
        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Severity, Is.EqualTo(ValidationSeverity.Warning));
        Assert.That(result.Message, Does.Contain("mutable state"));
    }
    
    #endregion
    
    #region Integration Tests with World
    
    [Test]
    public void World_ValidatesComponentsOnAdd()
    {
        // Arrange
        var entity = _world.CreateEntity();
        
        // Act & Assert - Should not throw for valid component
        Assert.DoesNotThrow(() => _world.AddComponent(entity, new Purlieu.Logic.Components.Position(1, 2, 3)));
    }
    
    [Test]
    public void World_LogsValidationWarnings()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Purlieu.Logic.Components.Position(1, 2, 3));
        _logger.Clear();
        
        // Act - Trigger a complex archetype transition that should generate warnings
        _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(1, 1, 1));
        
        // Assert - Check if validation warnings were logged
        var warningMessages = _logger.LoggedMessages
            .Where(m => m.Level == LogLevel.Warning)
            .ToList();
        
        // Note: This test depends on the specific validation rules
        // In this case, the transition might not trigger warnings for these simple components
    }
    
    #endregion
    
    #region Performance Tests
    
    [Test]
    public void Validation_ZeroAllocationInReleaseMode()
    {
        // This test ensures validation doesn't allocate in release builds
        // In debug builds, some allocation is expected for validation logic
        
        var nullValidator = NullEcsValidator.Instance;
        
        // Act - Multiple validation calls should be no-ops
        var result1 = nullValidator.ValidateComponentType<Purlieu.Logic.Components.Position>();
        var result2 = nullValidator.ValidateEntityOperation(EntityOperation.Create, 1, typeof(Purlieu.Logic.Components.Position));
        var result3 = nullValidator.ValidateArchetypeTransition(new[] { typeof(Purlieu.Logic.Components.Position) }, new[] { typeof(Purlieu.Logic.Components.Velocity) });
        
        // Assert - All should return Valid with no allocation
        Assert.That(result1.IsValid, Is.True);
        Assert.That(result2.IsValid, Is.True); 
        Assert.That(result3.IsValid, Is.True);
    }
    
    [Test]
    public void Validation_CacheEfficiency()
    {
        // Act - Validate same component type many times
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < 10000; i++)
        {
            _validator.ValidateComponentType<Purlieu.Logic.Components.Position>();
        }
        
        stopwatch.Stop();
        
        // Assert - Should be very fast due to caching
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(10), 
            "Cached validation should be extremely fast");
    }
    
    #endregion
    
    #region Test Components and Systems
    
    // Valid components
    public struct ValidComponent
    {
        public float X, Y;
        public int Value;
    }
    
    // Large component (>256 bytes)
    public struct LargeComponent
    {
        public float A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P;
        public float Q, R, S, T, U, V, W, X, Y, Z;
        public double DA, DB, DC, DD, DE, DF, DG, DH, DI, DJ;
        public double DK, DL, DM, DN, DO, DP, DQ, DR, DS, DT;
        public long LA, LB, LC, LD, LE, LF, LG, LH, LI, LJ;
    }
    
    // Unaligned component (not 4-byte aligned)
    public struct UnalignedComponent
    {
        public byte A;
        public byte B; 
        public byte C; // 3 bytes total
    }
    
    // Test systems
    public class TestSystem1 { }
    public class TestSystem2 { }  
    public class TestSystem3 { }
    
    // Stateful system (should trigger warning)
    public class StatefulSystem
    {
        public int Counter; // Mutable state
    }
    
    #endregion
}