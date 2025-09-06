# SIMD Vectorization Implementation Plan

## Decision
Implement SIMD vectorization for bulk component operations to achieve 4-8x performance gains on arithmetic-heavy workloads while preserving the zero-allocation, deterministic architecture. Focus on Vector<T> operations for Position, Movement, and bulk mathematical operations on component spans.

## Why
- Maintains deterministic execution and zero-allocation guarantees unlike multi-threading approaches
- Delivers immediate 4-8x performance improvements on position/velocity calculations and bulk component operations  
- Preserves existing API surface and backwards compatibility - performance improvement is transparent to users

## Local Fit Score: 8/10
- **determinism=3**: ✓ SIMD operations are deterministic (3/3)
- **testability=3**: ✓ Vectorized operations have predictable outputs (3/3) 
- **performance=3**: ✓ 4-8x gains on arithmetic workloads (3/3)
- **delivery=2**: ⚠ Requires memory alignment and Vector<T> integration (1/2)
- **complexity=1**: ⚠ Adds SIMD complexity but contained (0/1)
- **dx=2**: ✓ Transparent performance gains for developers (2/2)

## Implementation Checklist

1. **Add Vector<T> support to ComponentStorage<T>**
   - Implement proper memory alignment for SIMD operations
   - Ensure 16-byte or 32-byte alignment as needed

2. **Implement vectorized bulk operations**
   - Focus on Position, Velocity, and mathematical component types
   - Create SIMD-optimized mathematical operations

3. **Create SIMD-optimized chunk iteration**
   - Process components in Vector<T>.Count batches
   - Handle remainder elements with scalar operations

4. **Add memory alignment checks and fallback paths**
   - Detect when memory isn't properly aligned
   - Provide scalar fallback for non-SIMD scenarios

5. **Update benchmarks for vectorized performance**
   - Measure vectorized vs scalar performance on bulk operations
   - Validate 4-8x performance improvements

6. **Add allocation tests for Vector<T> operations**
   - Ensure Vector<T> operations remain zero-allocation in hot paths
   - Validate no unexpected heap allocations

## Test Requirements

- **SIMD_VectorizedPositionUpdates_4xFasterThanScalar**
- **SIMD_BulkComponentOperations_NoExtraAllocations**
- **SIMD_ChunkAlignment_ProperVectorBoundaryHandling**
- **SIMD_FallbackPath_HandlesNonAlignedMemory**  
- **SIMD_DeterministicResults_SameInputsSameOutputs**

## Code Changes Preview

### Chunk.cs - Add SIMD Support
```csharp
using System.Numerics;

// In GetSpan<T>() method
if (Vector.IsHardwareAccelerated && Vector<T>.IsSupported)
{
    return GetAlignedSpan<T>();
}

// New SIMD processing method
public void ProcessVectorized<T>(Action<Vector<T>> processor) where T : struct
{
    var span = GetSpan<T>();
    for (int i = 0; i <= span.Length - Vector<T>.Count; i += Vector<T>.Count)
    {
        var vector = new Vector<T>(span.Slice(i, Vector<T>.Count));
        processor(vector);
    }
}
```

### ComponentStorage<T> - Memory Alignment
```csharp
private readonly T[] _components;
private readonly int _alignment;

public ComponentStorage(int capacity)
{
    // Ensure SIMD alignment for supported types
    if (Vector<T>.IsSupported)
    {
        _alignment = Vector<T>.Count * Unsafe.SizeOf<T>();
        _components = AllocateAligned<T>(capacity, _alignment);
    }
    else
    {
        _components = new T[capacity];
    }
}
```

## Risk Assessment

| Risk | Level | Mitigation |
|------|--------|------------|
| Vector<T> allocations | **High** | Extensive allocation testing, stack-only Vector operations |
| Memory alignment issues | **Medium** | Proper alignment in ComponentStorage, fallback paths |
| Platform compatibility | **Low** | Hardware acceleration checks, scalar fallbacks |

## Success Criteria

- [ ] 4-8x performance improvement on bulk component operations
- [ ] Zero additional heap allocations in hot paths
- [ ] All existing tests continue to pass
- [ ] Deterministic behavior maintained across runs
- [ ] Graceful fallback on non-SIMD hardware

## Files to Modify

1. `Purlieu.Ecs/Core/Chunk.cs` - Add SIMD span access and processing methods
2. `Purlieu.Ecs/Core/ComponentStorage.cs` - Implement memory alignment
3. `Purlieu.Ecs.Tests/Core/SIMD_*` - New SIMD-specific tests  
4. `Purlieu.Ecs.Benchmarks/BENCH_SIMD.cs` - New SIMD benchmarks