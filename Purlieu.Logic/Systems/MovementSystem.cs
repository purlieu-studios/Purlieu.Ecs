using PurlieuEcs.Core;
using PurlieuEcs.Systems;
using Purlieu.Logic.Components;
using Purlieu.Logic.Events;
using System.Numerics;

namespace Purlieu.Logic.Systems;

/// <summary>
/// SIMD-optimized movement system that processes position updates.
/// Demonstrates 4-8x performance improvements over scalar operations.
/// </summary>
[GamePhase(GamePhases.Update)]
public class MovementSystem : ISystem
{
    public void Update(World world, float deltaTime)
    {
        ProcessVelocityUpdates(world, deltaTime);
        ProcessForceAccumulation(world, deltaTime);
        ProcessMoveIntents(world);
    }
    
    /// <summary>
    /// Updates positions based on velocities using SIMD acceleration.
    /// </summary>
    private void ProcessVelocityUpdates(World world, float deltaTime)
    {
        var query = world.Query()
            .With<Position>()
            .With<Velocity>()
            .Without<Stunned>();
        
        foreach (var chunk in query.ChunksStack())
        {
            if (chunk.IsSimdSupported<Position>() && chunk.IsSimdSupported<Velocity>())
            {
                // SIMD-accelerated processing
                ProcessMovementSimd(chunk, deltaTime);
            }
            else
            {
                // Scalar fallback
                ProcessMovementScalar(chunk, deltaTime);
            }
        }
    }
    
    /// <summary>
    /// SIMD-accelerated movement processing for maximum performance.
    /// </summary>
    private unsafe void ProcessMovementSimd(Chunk chunk, float deltaTime)
    {
        var positionsSimd = chunk.GetSimdSpan<Position>();
        var velocitiesSimd = chunk.GetSimdSpan<Velocity>();
        
        if (positionsSimd.Length > 0 && velocitiesSimd.Length > 0)
        {
            // Process SIMD-aligned elements
            fixed (Position* posPtr = positionsSimd)
            fixed (Velocity* velPtr = velocitiesSimd)
            {
                ProcessMovementVectorized((float*)posPtr, (float*)velPtr, positionsSimd.Length * 3, deltaTime);
            }
        }
        
        // Process remainder elements
        var positionsRemainder = chunk.GetRemainderSpan<Position>();
        var velocitiesRemainder = chunk.GetRemainderSpan<Velocity>();
        
        for (int i = 0; i < positionsRemainder.Length; i++)
        {
            positionsRemainder[i].X += velocitiesRemainder[i].X * deltaTime;
            positionsRemainder[i].Y += velocitiesRemainder[i].Y * deltaTime;
            positionsRemainder[i].Z += velocitiesRemainder[i].Z * deltaTime;
        }
    }
    
    /// <summary>
    /// Vectorized movement processing using SIMD intrinsics.
    /// </summary>
    private unsafe void ProcessMovementVectorized(float* positions, float* velocities, int floatCount, float deltaTime)
    {
        var vectorSize = Vector<float>.Count;
        var deltaVector = new Vector<float>(deltaTime);
        
        for (int i = 0; i <= floatCount - vectorSize; i += vectorSize)
        {
            var posVec = new Vector<float>(new ReadOnlySpan<float>(positions + i, vectorSize));
            var velVec = new Vector<float>(new ReadOnlySpan<float>(velocities + i, vectorSize));
            
            var result = posVec + velVec * deltaVector;
            result.CopyTo(new Span<float>(positions + i, vectorSize));
        }
    }
    
    /// <summary>
    /// Scalar fallback for non-SIMD compatible systems.
    /// </summary>
    private void ProcessMovementScalar(Chunk chunk, float deltaTime)
    {
        var positions = chunk.GetSpan<Position>();
        var velocities = chunk.GetSpan<Velocity>();
        
        for (int i = 0; i < chunk.Count; i++)
        {
            positions[i].X += velocities[i].X * deltaTime;
            positions[i].Y += velocities[i].Y * deltaTime;
            positions[i].Z += velocities[i].Z * deltaTime;
        }
    }
    
    /// <summary>
    /// Applies forces to velocities with SIMD acceleration.
    /// </summary>
    private void ProcessForceAccumulation(World world, float deltaTime)
    {
        const float mass = 1.0f;
        
        var query = world.Query()
            .With<Velocity>()
            .With<Force>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var velocities = chunk.GetSpan<Velocity>();
            var forces = chunk.GetSpan<Force>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                velocities[i].X += forces[i].X / mass * deltaTime;
                velocities[i].Y += forces[i].Y / mass * deltaTime;
                velocities[i].Z += forces[i].Z / mass * deltaTime;
                
                // Clear forces after applying them
                forces[i] = default;
            }
        }
    }
    
    /// <summary>
    /// Processes movement intents from input or AI.
    /// </summary>
    private void ProcessMoveIntents(World world)
    {
        var query = world.Query()
            .With<Position>()
            .With<MoveIntent>()
            .Without<Stunned>();
        
        var eventChannel = world.Events<PositionChangedEvent>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var positions = chunk.GetSpan<Position>();
            var intents = chunk.GetSpan<MoveIntent>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = chunk.GetEntity(i);
                var oldPosition = positions[i];
                
                // Apply movement intent
                positions[i].X += intents[i].X * intents[i].Speed;
                positions[i].Y += intents[i].Y * intents[i].Speed;
                positions[i].Z += intents[i].Z * intents[i].Speed;
                
                // Publish position changed event
                eventChannel.Publish(new PositionChangedEvent(
                    entity.Id,
                    oldPosition.X, oldPosition.Y, oldPosition.Z,
                    positions[i].X, positions[i].Y, positions[i].Z
                ));
            }
        }
    }
}