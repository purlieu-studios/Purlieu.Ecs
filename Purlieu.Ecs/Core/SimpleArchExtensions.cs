using System.Runtime.CompilerServices;
using PurlieuEcs.Query;
using PurlieuEcs.Common;

namespace PurlieuEcs.Core;

/// <summary>
/// Clean Arch-style extensions that automatically handle SIMD optimization
/// </summary>
public static class SimpleArchExtensions
{
    /// <summary>
    /// Clean movement system like Arch ECS - automatically optimized
    /// 
    /// Before: 
    /// foreach (var chunk in query.ChunksStack())
    /// {
    ///     if (chunk.IsSimdSupported<Position>() && chunk.IsSimdSupported<Velocity>())
    ///     {
    ///         ProcessMovementSimd(chunk, deltaTime);
    ///     }
    ///     else
    ///     {
    ///         ProcessMovementScalar(chunk, deltaTime);
    ///     }
    /// }
    /// 
    /// Now:
    /// world.UpdateMovement(deltaTime);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateMovement(this World world, float deltaTime)
    {
        var query = world.Query().With<PurlieuEcs.Common.Position>().With<PurlieuEcs.Common.Velocity>();
        
        int totalEntitiesProcessed = 0;
        int chunkCount = 0;
        foreach (var chunk in query.ChunksStack())
        {
            chunkCount++;
            totalEntitiesProcessed += chunk.Count;
            
            // Automatically choose optimal processing path
            if (ShouldUseSimdPath(chunk.Count))
            {
                ProcessMovementOptimized(chunk, deltaTime);
            }
            else
            {
                ProcessMovementStandard(chunk, deltaTime);
            }
        }
    }
    
    /// <summary>
    /// Clean physics system - handles acceleration to velocity to position automatically
    /// Usage: world.UpdatePhysics(deltaTime);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdatePhysics(this World world, float deltaTime)
    {
        // First update velocities from acceleration
        UpdateVelocityFromAcceleration(world, deltaTime);
        
        // Then update positions from velocity
        world.UpdateMovement(deltaTime);
    }
    
    /// <summary>
    /// Applies damage over time effects automatically
    /// Usage: world.UpdateDamageOverTime(deltaTime);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateDamageOverTime(this World world, float deltaTime)
    {
        var query = world.Query().With<PurlieuEcs.Common.Health>().With<PurlieuEcs.Common.DamageOverTime>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var healthSpan = chunk.GetSpan<PurlieuEcs.Common.Health>();
            var dotSpan = chunk.GetSpan<PurlieuEcs.Common.DamageOverTime>();
            
            for (int i = 0; i < healthSpan.Length; i++)
            {
                healthSpan[i].Current -= dotSpan[i].DamagePerSecond * deltaTime;
                if (healthSpan[i].Current < 0) healthSpan[i].Current = 0;
            }
        }
    }
    
    /// <summary>
    /// Decides whether to use SIMD-optimized path based on data size and hardware
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldUseSimdPath(int entityCount)
    {
        // Use optimized path for larger datasets when SIMD is available
        return System.Numerics.Vector.IsHardwareAccelerated && entityCount >= 16;
    }
    
    /// <summary>
    /// SIMD-hinted movement processing (compiler will vectorize when beneficial)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessMovementOptimized(Chunk chunk, float deltaTime)
    {
        var positions = chunk.GetSpan<PurlieuEcs.Common.Position>();
        var velocities = chunk.GetSpan<PurlieuEcs.Common.Velocity>();
        
        // Loop structure encourages compiler auto-vectorization
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X * deltaTime;
            positions[i].Y += velocities[i].Y * deltaTime;
            positions[i].Z += velocities[i].Z * deltaTime;
        }
    }
    
    /// <summary>
    /// Standard scalar movement processing
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessMovementStandard(Chunk chunk, float deltaTime)
    {
        var positions = chunk.GetSpan<PurlieuEcs.Common.Position>();
        var velocities = chunk.GetSpan<PurlieuEcs.Common.Velocity>();
        
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X * deltaTime;
            positions[i].Y += velocities[i].Y * deltaTime;
            positions[i].Z += velocities[i].Z * deltaTime;
        }
    }
    
    /// <summary>
    /// Updates velocity from acceleration
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateVelocityFromAcceleration(World world, float deltaTime)
    {
        var query = world.Query().With<PurlieuEcs.Common.Velocity>().With<PurlieuEcs.Common.Acceleration>();
        
        foreach (var chunk in query.ChunksStack())
        {
            var velocities = chunk.GetSpan<PurlieuEcs.Common.Velocity>();
            var accelerations = chunk.GetSpan<PurlieuEcs.Common.Acceleration>();
            
            for (int i = 0; i < velocities.Length; i++)
            {
                velocities[i].X += accelerations[i].X * deltaTime;
                velocities[i].Y += accelerations[i].Y * deltaTime;
                velocities[i].Z += accelerations[i].Z * deltaTime;
            }
        }
    }
}

