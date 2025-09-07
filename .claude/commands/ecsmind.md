/project:ecs-mind

Goal
Answer any ECS architecture or implementation question using a small panel of specialist roles that debate in rounds, attack the ideas, and finish with a single decision plus concrete deliverables. Be concise and actionable.

Params
- question: required, quoted string with the decision you want.
- rounds: optional integer, default 3.
- web: optional on|off, default off. If on, consult 2–4 reputable sources and cite briefly.
- scope: optional folder for local scan, default src/PurlieuEcs.
- weights: optional priorities like determinism=3,testability=3,performance=3,delivery=2,complexity=1,dx=2.

Baseline (read CLAUDE.md if present)
- Systems are engine-agnostic and stateless; all logic runs without external services or DI.
- Components are `struct`s only (no engine refs, no heap-only collections).
- Storage is archetype+chunk SoA; queries are zero-alloc once constructed.
- No reflection in hot paths; codegen or init-time caching only.
- Events/Intents are one-frame by default and are cleared after processing.
- Visual/engine bridges live outside the ECS assembly.

Roles
- Core Architect: correctness, invariants, entity lifecycle, archetype transitions, determinism.
- API Designer: surface simplicity, generics, zero-boxing contracts, backwards compatibility.
- Data & Performance: layout, chunk capacity, cache behavior, iterator pooling, GC pressure.
- Query Engineer: signature bitsets, With/Without semantics, chunk-first iteration guarantees.
- Test Lead: unit+integration surface, fixtures, determinism, failure modes, reproducibility.
- Tooling & DX: codegen hooks, analyzers, debug probes, profiling and developer ergonomics.
- Release Manager: delivery risk, versioning, migration strategy, docs, adoption.
- Integration Engineer: external engine/app bridges, boundary contracts, no leakage of engine types.
- Red Team: adversarial. Find boxing, allocations, hidden reflection, stateful systems, engine leaks, nondeterminism.

Workflow
Round 0. Local scan
- Enumerate C# files under {scope}. Identify Core (World, Entity), Storage (Archetype/Chunk), Query, Systems, Events, Codegen, Snapshot, and any Bridge/Debug code.
- Detect types ending with Intent/Event and whether they are cleared after consumption.
- Check public APIs for boxing risks and reflection usage in hot paths.
- Confirm systems are stateless and components are `struct`s without engine refs.
- Summarize findings in ≤5 bullets.

Round 1. Options
- Propose exactly 2 candidate answers to {question}. Each under 3 sentences.
- If web=on, add one short source note per candidate.

Round 2..N. Debate
- Each role contributes one short note per round: trade-offs, risks, concrete code impacts.
- Red Team attacks both candidates using the baseline and priorities (use weights if provided).

Finalization
- Pick a single winner. If neither meets the baseline without risky surgery, output BLOCKED with exact files and lines to fix.
- Compute a Local Fit Score 0–10 using the weights.
- Produce deliverables.

Deliverables
- Decision: one paragraph.
- Why: 3 bullets.
- Checklist: 6 steps scoped to this week.
- Tests: 5 test names that lock the decision.
- Patches: 1–3 small unified diffs if changes are obvious.
- Risks table: High | Medium | Low with mitigations.

Output format
Decision:
Why:
Local fit score: N/10 (show weight breakdown)
Checklist:
1.
2.
3.
4.
5.
6.
Tests:
- ...
- ...
- ...
- ...
- ...
Patches:
```patch
*** PATCH: path/to/file.cs
@@
- old
+ new
