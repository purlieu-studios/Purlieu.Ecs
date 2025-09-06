using System.Runtime.CompilerServices;
using PurlieuEcs.Core;

namespace PurlieuEcs.Blueprints;

/// <summary>
/// Defines a reusable entity template with components.
/// </summary>
public sealed class EntityBlueprint
{
    private readonly List<IComponentInitializer> _initializers;
    private readonly string _name;
    
    public string Name => _name;
    
    public EntityBlueprint(string name)
    {
        _name = name;
        _initializers = new List<IComponentInitializer>();
    }
    
    /// <summary>
    /// Adds a component to the blueprint.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityBlueprint With<T>(T component) where T : struct
    {
        _initializers.Add(new ComponentInitializer<T>(component));
        return this;
    }
    
    /// <summary>
    /// Instantiates the blueprint into the world.
    /// </summary>
    public Entity Instantiate(World world)
    {
        var entity = world.CreateEntity();
        
        foreach (var initializer in _initializers)
        {
            initializer.Apply(world, entity);
        }
        
        return entity;
    }
    
    /// <summary>
    /// Instantiates multiple copies of the blueprint.
    /// </summary>
    public Entity[] InstantiateMany(World world, int count)
    {
        var entities = new Entity[count];
        
        for (int i = 0; i < count; i++)
        {
            entities[i] = Instantiate(world);
        }
        
        return entities;
    }
    
    private interface IComponentInitializer
    {
        void Apply(World world, Entity entity);
    }
    
    private readonly struct ComponentInitializer<T> : IComponentInitializer where T : struct
    {
        private readonly T _component;
        
        public ComponentInitializer(T component)
        {
            _component = component;
        }
        
        public void Apply(World world, Entity entity)
        {
            world.AddComponent(entity, _component);
        }
    }
}

/// <summary>
/// Registry for managing entity blueprints.
/// </summary>
public sealed class BlueprintRegistry
{
    private readonly Dictionary<string, EntityBlueprint> _blueprints;
    
    public BlueprintRegistry()
    {
        _blueprints = new Dictionary<string, EntityBlueprint>();
    }
    
    /// <summary>
    /// Registers a blueprint.
    /// </summary>
    public void Register(EntityBlueprint blueprint)
    {
        _blueprints[blueprint.Name] = blueprint;
    }
    
    /// <summary>
    /// Gets a blueprint by name.
    /// </summary>
    public EntityBlueprint? Get(string name)
    {
        return _blueprints.TryGetValue(name, out var blueprint) ? blueprint : null;
    }
    
    /// <summary>
    /// Creates and registers a new blueprint.
    /// </summary>
    public EntityBlueprint Create(string name)
    {
        var blueprint = new EntityBlueprint(name);
        Register(blueprint);
        return blueprint;
    }
}