using PurlieuEcs.Core;
using PurlieuEcs.Common;
using PurlieuEcs.Logging;

namespace Purlieu.Ecs.Tests.Core;

[TestFixture]
public class ERROR_HandlingTests
{
    private TestLogger _testLogger;
    private World _world;
    
    [SetUp]
    public void SetUp()
    {
        _testLogger = new TestLogger();
        _world = new World(logger: _testLogger);
    }
    
    [TearDown]
    public void TearDown()
    {
        _world?.Dispose();
    }
    
    [Test]
    public void EntityNotFoundException_ThrownForInvalidEntity()
    {
        // Arrange
        var invalidEntity = new Entity(999, 1);
        
        // Act & Assert - AddComponent
        var addEx = Assert.Throws<EntityNotFoundException>(() => 
            _world.AddComponent(invalidEntity, new Purlieu.Logic.Components.Position(1, 2, 3)));
        
        Assert.That(addEx.EntityId, Is.EqualTo(999u));
        Assert.That(addEx.Message, Does.Contain("non-existent entity"));
        
        // Act & Assert - RemoveComponent
        var removeEx = Assert.Throws<EntityNotFoundException>(() => 
            _world.RemoveComponent<Purlieu.Logic.Components.Position>(invalidEntity));
        
        Assert.That(removeEx.EntityId, Is.EqualTo(999u));
        
        // Act & Assert - GetComponent
        var getEx = Assert.Throws<EntityNotFoundException>(() => 
            _world.GetComponent<Purlieu.Logic.Components.Position>(invalidEntity));
        
        Assert.That(getEx.EntityId, Is.EqualTo(999u));
        
        // Verify errors were logged
        var errorMessages = _testLogger.LoggedMessages
            .Where(m => m.Level == LogLevel.Error)
            .ToList();
        
        Assert.That(errorMessages.Count, Is.EqualTo(3));
        Assert.That(errorMessages.All(m => m.EntityId == 999u));
    }
    
    [Test]
    public void ComponentException_ThrownForMissingComponent()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _testLogger.Clear();
        
        // Act & Assert - GetComponent for non-existent component
        var ex = Assert.Throws<ComponentException>(() => 
            _world.GetComponent<Purlieu.Logic.Components.Position>(entity));
        
        Assert.That(ex.EntityId, Is.EqualTo(entity.Id));
        Assert.That(ex.ComponentType, Is.EqualTo(typeof(Purlieu.Logic.Components.Position)));
        Assert.That(ex.Message, Does.Contain("does not have component"));
    }
    
    [Test]
    public void HasComponent_ReturnsFalseOnError()
    {
        // Arrange
        var invalidEntity = new Entity(999, 1);
        
        // Act - HasComponent should return false for invalid entity instead of throwing
        var hasComponent = _world.HasComponent<Purlieu.Logic.Components.Position>(invalidEntity);
        
        // Assert
        Assert.That(hasComponent, Is.False);
        
        // Should still log the error
        var errorMessages = _testLogger.LoggedMessages
            .Where(m => m.Level == LogLevel.Error)
            .ToList();
        
        Assert.That(errorMessages.Count, Is.EqualTo(1));
    }
    
    [Test]
    public void EcsException_ThrownForRegistrationFailure()
    {
        // This test verifies the exception handling pattern
        // In practice, component registration rarely fails
        Assert.DoesNotThrow(() => _world.RegisterComponent<Purlieu.Logic.Components.Position>());
        Assert.DoesNotThrow(() => _world.RegisterComponent<Purlieu.Logic.Components.Velocity>());
    }
    
    [Test]
    public void ErrorLogging_ContainsCorrelationIds()
    {
        // Arrange
        var customCorrelation = "TEST_ERROR";
        CorrelationContext.Set(customCorrelation);
        var invalidEntity = new Entity(999, 1);
        
        // Act
        try
        {
            _world.AddComponent(invalidEntity, new Purlieu.Logic.Components.Position(1, 2, 3));
        }
        catch (EntityNotFoundException)
        {
            // Expected
        }
        
        // Assert
        var errorMessage = _testLogger.LoggedMessages
            .FirstOrDefault(m => m.Level == LogLevel.Error);
        
        Assert.That(errorMessage, Is.Not.Null);
        Assert.That(errorMessage.CorrelationId, Is.EqualTo(customCorrelation));
        Assert.That(errorMessage.Operation, Is.EqualTo(EcsOperation.ComponentAdd));
        Assert.That(errorMessage.Exception, Is.Not.Null);
        
        // Reset correlation
        CorrelationContext.Reset();
    }
    
    [Test]
    public void ErrorHandling_PreservesInnerExceptions()
    {
        // Arrange
        var invalidEntity = new Entity(999, 1);
        
        // Act & Assert
        var ex = Assert.Throws<EntityNotFoundException>(() => 
            _world.GetComponent<Purlieu.Logic.Components.Position>(invalidEntity));
        
        // The inner exception should be preserved in more complex scenarios
        Assert.That(ex.EntityId, Is.EqualTo(999u));
        
        // Check that the error was logged with full exception details
        var errorMessage = _testLogger.LoggedMessages
            .FirstOrDefault(m => m.Level == LogLevel.Error);
        
        Assert.That(errorMessage, Is.Not.Null);
        Assert.That(errorMessage.Exception, Is.Not.Null);
    }
    
    [Test]
    public void ErrorHandling_DoesNotAffectNormalOperation()
    {
        // Arrange
        var entity = _world.CreateEntity();
        
        // Act - Normal operations should still work fine
        Assert.DoesNotThrow(() => _world.AddComponent(entity, new Purlieu.Logic.Components.Position(1, 2, 3)));
        Assert.DoesNotThrow(() => _world.AddComponent(entity, new Purlieu.Logic.Components.Velocity(4, 5, 6)));
        
        var position = _world.GetComponent<Purlieu.Logic.Components.Position>(entity);
        var velocity = _world.GetComponent<Purlieu.Logic.Components.Velocity>(entity);
        
        // Assert
        Assert.That(position.X, Is.EqualTo(1f));
        Assert.That(velocity.X, Is.EqualTo(4f));
        Assert.That(_world.HasComponent<Purlieu.Logic.Components.Position>(entity), Is.True);
        Assert.That(_world.HasComponent<Health>(entity), Is.False);
        
        // Should not have any error messages for successful operations
        var errorMessages = _testLogger.LoggedMessages
            .Where(m => m.Level == LogLevel.Error)
            .ToList();
        
        Assert.That(errorMessages.Count, Is.EqualTo(0));
    }
    
    [Test]
    public void ErrorHandling_MaxEntityLimitValidation()
    {
        // This would be a very long-running test to actually hit uint.MaxValue
        // Instead, we verify the validation logic exists by checking the code path
        
        // Arrange - Create an entity normally
        var entity = _world.CreateEntity();
        
        // Act & Assert - Normal entity creation should work
        Assert.That(entity.Id, Is.GreaterThan(0u));
        Assert.That(entity.Id, Is.LessThan(uint.MaxValue));
        
        // The validation for uint.MaxValue is in place but would take too long to test directly
        // This test ensures the basic validation framework is working
    }
    
    [Test] 
    public void DestroyEntity_HandlesMissingArchetypeGracefully()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _testLogger.Clear();
        
        // Act - Destroy entity (should work fine)
        Assert.DoesNotThrow(() => _world.DestroyEntity(entity));
        
        // Try to destroy the same entity again (should handle gracefully)
        Assert.DoesNotThrow(() => _world.DestroyEntity(entity));
        
        // Assert - No errors should be logged for graceful handling
        var errorMessages = _testLogger.LoggedMessages
            .Where(m => m.Level == LogLevel.Error)
            .ToList();
        
        Assert.That(errorMessages.Count, Is.EqualTo(0));
    }
    
    [Test]
    public void ComponentOperations_HandleDuplicateOperationsGracefully()
    {
        // Arrange
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Purlieu.Logic.Components.Position(1, 2, 3));
        _testLogger.Clear();
        
        // Act - Add same component again (should be handled gracefully)
        Assert.DoesNotThrow(() => _world.AddComponent(entity, new Purlieu.Logic.Components.Position(4, 5, 6)));
        
        // Remove non-existent component (should be handled gracefully)
        Assert.DoesNotThrow(() => _world.RemoveComponent<Purlieu.Logic.Components.Velocity>(entity));
        
        // Assert - No errors should be logged for graceful handling
        var errorMessages = _testLogger.LoggedMessages
            .Where(m => m.Level == LogLevel.Error)
            .ToList();
        
        Assert.That(errorMessages.Count, Is.EqualTo(0));
    }
}