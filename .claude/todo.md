Phase 0 — Skeleton

Scaffold solution/projects/folders (Core, Query, Systems, Scheduler, Events, Blueprints, Snapshot, Tests, Benchmarks).

Add empty types: Entity, World, Archetype, Chunk, Query, ISystem, Scheduler, EventChannel, EntityBlueprint, SnapshotManager. Commit.

Phase 1 — Entity Lifecycle (foundation)

Entity: define 64-bit (id, version) + equality/hash/Null.

World + EntityAllocator: CreateEntity(), Destroy(Entity), IsAlive(Entity), freelist + version bump.

Tests (Gate 1): stale handle returns false; version increments on reuse.

Bench (Gate 1): entity create/destroy throughput (100/10k/100k).

Phase 2 — Signatures & Archetype Shell

ArchetypeSignature: bitset type with With<T>()/Without<T>() ops and hashing.

Archetype registry: map ArchetypeSignature -> Archetype and assign stable IDs.

Archetype (shell): holds signature, chunk list; no component storage yet.

Tests (Gate 2): signature equality/hash; registry returns same instance for same signature.

Phase 3 — Chunked Storage (SoA)

ChunkStorage<T>: contiguous SoA arrays via Span<T>; capacity = 512; add/remove by slot.

Archetype integrate storage: per-component array dictionary keyed by component type id.

Entity location map: Entity -> (Archetype, ChunkIndex, Row) for O(1) access/migration.

World component APIs: Add<T>, Get<T>, Has<T>, Remove<T> (no boxing).

Tests (Gate 3): add/get/remove correctness; dense packing after removals.

Phase 4 — Migration & Builder

Migration path: on Add<T>/Remove<T>, move entity to new archetype/chunk; update location map.

EntityBuilder: chain .With<T>(in T) then Build() to spawn multi-component entities fast.

Tests (Gate 4): add/remove causes correct archetype transition; data copied correctly.

Phase 5 — Queries (zero-alloc)

QueryDescription/Query: fluent .With<T>()/.Without<T>(); resolve to matching archetypes; iterate chunks first.

Chunk view: Chunk.Count, Chunk.GetSpan<T>(), Chunk.GetEntity(i).

Iterator pooling: reuse per frame; no GC in tight loops.

Tests (Gate 5): filters correct; spans aligned; no allocations in hot loop (assert via GC collection count).

Phase 6 — Systems & Scheduler

ISystem and [GamePhase(PreUpdate|Update|PostUpdate|Presentation, order)].

Scheduler: deterministic run by phase+order; hooks for profiling each system.

Profiler: per-system current, rolling avg (30), peak; ResetPeaks().

Tests (Gate 6): order determinism; profiler numbers captured; reset works.

Phase 7 — Events & One-Frame

EventChannel<T>: ring buffer; Publish(in T), ConsumeAll(Action<in T>).

One-frame policy: auto-clear at frame end; attribute [OneFrame] for components/tags.

Tests (Gate 7): publish/consume order; overflow policy (drop or overwrite as chosen); auto-clear verified.

Phase 8 — Backend–Visual Intent Pattern (BVIP)

Define VisualIntent structs (e.g., PositionChangedIntent).

Change-only rule: utility to compare previous vs current; publish intent only on change.

GodotBridge stub (outside ECS): subscribes to intents → emits signals/tweens.

Demo system: MovementSystem consumes MoveIntent, updates Position, emits PositionChangedIntent.

Tests (Gate 8): intent only when value changes; MoveIntent consumed.

Phase 9 — Blueprints (prefabs)

EntityBlueprint: declarative list of components/values.

Instantiate: World.Instantiate(blueprint) fast-path into correct archetype (batch add).

Tests (Gate 9): blueprint spawns correct components; no per-component boxing; many entities spawn dense.

Phase 10 — Snapshot (save/load)

SnapshotManager: versioned header, entity table, archetype table, chunk payloads.

Compression: LZ4 block; restore bulk copy into chunks.

Tests (Gate 10): round-trip world equality (entities/components/archetypes); large world restore time budget.

Phase 11 — Debug & Bench Essentials

Probe API (read-only): archetype list, chunk fill %, total entities, per-system timings, event counts.

Benchmarks:

CreateEntity throughput (100/10k/100k)

Query(2 components) system update across scales

Add/Remove component migration cost

(Optional) Godot Debug Overlay v1: simple panels for timings + chunk usage (don’t block release).

Phase 12 — Hardening & v0 Release

API audit: no boxing on public hot path (where T : struct everywhere).

Guardrails: analyzers or tests for “no engine types in ECS assemblies”.

Docs: update CLAUDE.md with commands/tasks; include movement demo usage.

Tag v0.0.1: publish package locally or as a git submodule for your game.