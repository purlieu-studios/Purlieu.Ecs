using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using PurlieuEcs.Query;

namespace PurlieuEcs.Core;

/// <summary>
/// Centralized memory management for the ECS framework.
/// Handles pool cleanup, memory pressure monitoring, and chunk defragmentation.
/// </summary>
public sealed class MemoryManager : IDisposable
{
    private readonly World _world;
    private readonly Timer _cleanupTimer;
    private readonly object _cleanupLock = new();
    private bool _disposed;
    
    // Memory pressure thresholds
    private const long HighMemoryThresholdBytes = 500_000_000; // 500MB
    private const long CriticalMemoryThresholdBytes = 1_000_000_000; // 1GB
    
    // Cleanup intervals
    private readonly TimeSpan _normalCleanupInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _aggressiveCleanupInterval = TimeSpan.FromMinutes(1);
    
    // Statistics
    private long _lastCleanupTicks;
    private int _cleanupCount;
    private long _totalMemoryReclaimed;
    
    public MemoryManager(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        
        // Start periodic cleanup timer
        _cleanupTimer = new Timer(
            PeriodicCleanup, 
            null, 
            _normalCleanupInterval, 
            _normalCleanupInterval);
        
        // Register for Gen2 GC notifications for memory pressure detection
        GC.RegisterForFullGCNotification(10, 10);
    }
    
    /// <summary>
    /// Performs periodic memory cleanup based on current memory pressure.
    /// </summary>
    private void PeriodicCleanup(object? state)
    {
        if (_disposed) return;
        
        lock (_cleanupLock)
        {
            if (_disposed) return;
            
            var stopwatch = Stopwatch.StartNew();
            var beforeMemory = GC.GetTotalMemory(false);
            
            // Determine cleanup level based on memory pressure
            var currentMemory = GC.GetTotalMemory(false);
            var cleanupLevel = currentMemory switch
            {
                > CriticalMemoryThresholdBytes => CleanupLevel.Aggressive,
                > HighMemoryThresholdBytes => CleanupLevel.Normal,
                _ => CleanupLevel.Light
            };
            
            // Perform cleanup
            PerformCleanup(cleanupLevel);
            
            // Force GC if memory pressure is high
            if (cleanupLevel >= CleanupLevel.Normal)
            {
                GC.Collect(2, GCCollectionMode.Optimized);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Optimized);
            }
            
            var afterMemory = GC.GetTotalMemory(false);
            var memoryReclaimed = beforeMemory - afterMemory;
            
            stopwatch.Stop();
            
            // Update statistics
            _lastCleanupTicks = DateTime.UtcNow.Ticks;
            _cleanupCount++;
            _totalMemoryReclaimed += Math.Max(0, memoryReclaimed);
            
            // Adjust cleanup interval based on memory pressure
            if (cleanupLevel == CleanupLevel.Aggressive)
            {
                _cleanupTimer.Change(_aggressiveCleanupInterval, _aggressiveCleanupInterval);
            }
            else
            {
                _cleanupTimer.Change(_normalCleanupInterval, _normalCleanupInterval);
            }
            
            // Log cleanup results (would integrate with production logging system)
            Debug.WriteLine($"Memory cleanup completed: Level={cleanupLevel}, Time={stopwatch.ElapsedMilliseconds}ms, Reclaimed={memoryReclaimed:N0} bytes");
        }
    }
    
    /// <summary>
    /// Performs cleanup operations based on the specified level.
    /// </summary>
    private void PerformCleanup(CleanupLevel level)
    {
        // Clear pooled arrays that haven't been used recently
        ClearUnusedPools();
        
        if (level >= CleanupLevel.Normal)
        {
            // Defragment chunks with low occupancy
            DefragmentChunks();
            
            // Clear query cache entries that haven't been accessed recently
            ClearStaleQueryCache();
        }
        
        if (level == CleanupLevel.Aggressive)
        {
            // Compact archetypes with very few entities
            CompactSparseArchetypes();
            
            // Trim excess capacity from internal collections
            TrimExcessCapacity();
        }
    }
    
    /// <summary>
    /// Clears pooled arrays that haven't been used recently.
    /// </summary>
    private void ClearUnusedPools()
    {
        // SignatureArrayPool uses ThreadStatic, so we can't clear it directly
        // But we can trigger cleanup by forcing allocations to cycle through
        
        // Clear chunk pools
        ChunkPool.ClearUnusedPools();
        
        // Clear list pools
        ListPool<int>.ClearIfUnused(TimeSpan.FromMinutes(5));
        
        // Clear small archetype array pools
        SmallArchetypeArrayPool.ClearUnusedPools();
    }
    
    /// <summary>
    /// Defragments chunks with low occupancy to improve memory locality.
    /// </summary>
    private void DefragmentChunks()
    {
        const float MinOccupancyThreshold = 0.25f; // Defragment chunks less than 25% full
        
        foreach (var archetype in _world._allArchetypes)
        {
            var chunks = archetype.GetChunks();
            if (chunks.Count <= 1) continue;
            
            // Find chunks with low occupancy
            var sparseChunks = new List<Chunk>();
            foreach (var chunk in chunks)
            {
                float occupancy = (float)chunk.Count / chunk.Capacity;
                if (occupancy < MinOccupancyThreshold && chunk.Count > 0)
                {
                    sparseChunks.Add(chunk);
                }
            }
            
            if (sparseChunks.Count <= 1) continue;
            
            // Merge sparse chunks
            MergeSparseChunks(archetype, sparseChunks);
        }
    }
    
    /// <summary>
    /// Merges sparse chunks to reduce fragmentation.
    /// </summary>
    private void MergeSparseChunks(Archetype archetype, List<Chunk> sparseChunks)
    {
        // Sort by entity count to merge smallest first
        sparseChunks.Sort((a, b) => a.Count.CompareTo(b.Count));
        
        int writeChunkIndex = 0;
        var writeChunk = sparseChunks[writeChunkIndex];
        
        for (int readIndex = 1; readIndex < sparseChunks.Count; readIndex++)
        {
            var readChunk = sparseChunks[readIndex];
            
            while (readChunk.Count > 0)
            {
                // Check if write chunk has space
                if (writeChunk.Count >= writeChunk.Capacity)
                {
                    writeChunkIndex++;
                    if (writeChunkIndex >= sparseChunks.Count) break;
                    writeChunk = sparseChunks[writeChunkIndex];
                    if (writeChunk == readChunk) continue;
                }
                
                // Move one entity from read chunk to write chunk
                var entity = readChunk.GetEntity(readChunk.Count - 1);
                var oldRow = readChunk.Count - 1;
                
                // This would require more complex logic to actually move the entity
                // For now, this is a placeholder for the defragmentation strategy
                // Real implementation would need to update entity records and move component data
            }
        }
    }
    
    /// <summary>
    /// Clears stale entries from the query cache.
    /// </summary>
    private void ClearStaleQueryCache()
    {
        // The ArchetypeIndex would need to track access times for cache entries
        // For now, we can clear the entire cache periodically
        var stats = _world._archetypeIndex.GetCacheStatistics();
        
        // Clear cache if hit rate is too low (indicating stale entries)
        if (stats.HitRate < 50.0)
        {
            _world._archetypeIndex.ClearCache();
        }
    }
    
    /// <summary>
    /// Compacts archetypes with very few entities.
    /// </summary>
    private void CompactSparseArchetypes()
    {
        const int MinEntityThreshold = 10;
        
        foreach (var archetype in _world._allArchetypes)
        {
            if (archetype.EntityCount < MinEntityThreshold && archetype.EntityCount > 0)
            {
                // Trim excess chunk capacity
                var chunks = archetype.GetChunks();
                
                // Keep only chunks with entities
                for (int i = chunks.Count - 1; i >= 0; i--)
                {
                    if (chunks[i].Count == 0)
                    {
                        chunks.RemoveAt(i);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Trims excess capacity from internal collections.
    /// </summary>
    private void TrimExcessCapacity()
    {
        // Trim archetype lists if they have significant excess capacity
        foreach (var archetype in _world._allArchetypes)
        {
            var chunks = archetype.GetChunks();
            if (chunks.Capacity > chunks.Count * 2)
            {
                chunks.TrimExcess();
            }
        }
        
        // Trim world's archetype list
        if (_world._allArchetypes.Capacity > _world._allArchetypes.Count * 2)
        {
            _world._allArchetypes.TrimExcess();
        }
    }
    
    /// <summary>
    /// Forces an immediate memory cleanup.
    /// </summary>
    public void ForceCleanup(CleanupLevel level = CleanupLevel.Normal)
    {
        lock (_cleanupLock)
        {
            if (!_disposed)
            {
                PerformCleanup(level);
            }
        }
    }
    
    /// <summary>
    /// Gets memory management statistics.
    /// </summary>
    public MemoryStatistics GetStatistics()
    {
        return new MemoryStatistics
        {
            CleanupCount = _cleanupCount,
            LastCleanupTime = new DateTime(_lastCleanupTicks, DateTimeKind.Utc),
            TotalMemoryReclaimed = _totalMemoryReclaimed,
            CurrentMemoryUsage = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_cleanupLock)
        {
            if (_disposed) return;
            
            _disposed = true;
            _cleanupTimer?.Dispose();
            
            // Perform final cleanup
            PerformCleanup(CleanupLevel.Aggressive);
        }
    }
}

/// <summary>
/// Cleanup level for memory management operations.
/// </summary>
public enum CleanupLevel
{
    /// <summary>
    /// Light cleanup - only clear obviously unused resources.
    /// </summary>
    Light,
    
    /// <summary>
    /// Normal cleanup - defragment and clear stale caches.
    /// </summary>
    Normal,
    
    /// <summary>
    /// Aggressive cleanup - compact everything and trim excess capacity.
    /// </summary>
    Aggressive
}

/// <summary>
/// Memory management statistics.
/// </summary>
public readonly struct MemoryStatistics
{
    public int CleanupCount { get; init; }
    public DateTime LastCleanupTime { get; init; }
    public long TotalMemoryReclaimed { get; init; }
    public long CurrentMemoryUsage { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    
    public override string ToString()
    {
        return $"Memory Stats: Current={CurrentMemoryUsage:N0}, Reclaimed={TotalMemoryReclaimed:N0}, " +
               $"Cleanups={CleanupCount}, GC=[{Gen0Collections}/{Gen1Collections}/{Gen2Collections}]";
    }
}