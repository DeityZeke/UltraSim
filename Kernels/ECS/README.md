# ECS Kernel Guidance

This document is the ECS-specific engineering guide for `Kernels/ECS`.

It is derived from the old `_OLD/AGENTS.md`, but adapted for this new kernel port.

## Mission

Build an engine-agnostic ECS kernel that is:

- correct,
- explicit,
- testable,
- portable,
- scalable where it matters.

## Priority Order

Do not violate this order without a clear reason:

1. Correctness
2. Security and safety
3. Simplicity
4. Maintainability
5. Performance

Performance matters in the ECS, but only after correctness and clarity are secure.

## Kernel Rules

- Keep the kernel engine-agnostic.
- Do not couple ECS core to UI, Godot nodes, file formats, or transport details.
- Do not use process-global mutable registries in the kernel.
- Prefer sealed schema/runtime objects over ambient static state.
- Keep public mutation APIs explicit.
- Do not expose public write paths that can bypass dirty tracking.
- If ordering is required, use `Stage` or explicit dependencies, not hidden heuristics.
- Do not carry dead API forward.

## Architecture Guidance

- Prefer explicit bootstrap and ownership.
- Keep hot-path structures array-based and allocation-aware.
- Avoid speculative abstractions.
- Minimize public surface area.
- Keep persistence and prefabs outside the core until needed, but preserve clean seams.
- Keep framework and IO concerns at the edges.

## Port-Specific Rules

- Do not blindly copy `_OLD/UltraSim/ECS`.
- Preserve proven runtime behavior where it is still sound.
- Intentionally change architecture where the old design had debt:
  - global static registries,
  - dead scheduler priority,
  - implicit mutable write paths,
  - tightly coupled second-wave subsystems.
- Make structural progress in small testable slices.

## Performance Guidance

- Default to clear code first.
- Treat schema lookup, archetype iteration, structural apply, dirty tracking, and scheduler execution as likely hot paths.
- Do not introduce heavy immutable collection libraries on the hot path.
- Use immutable snapshots built from arrays and tables, not allocation-heavy immutable containers.
- When a low-level optimization makes code harder to read, justify it with actual need.

## Testing Guidance

- Add executable coverage as the kernel grows.
- Test isolation explicitly: schemas and worlds must not leak state across instances.
- Test serial correctness first, then parallel parity.
- Every new public mutation surface should have a test proving dirty tracking cannot be bypassed.

## Scope Guidance

- Make the smallest useful kernel change that keeps the port moving.
- Do not drag prefabs or persistence into the core early.
- Add extension seams before subsystem imports.
- Keep scaffolding honest: placeholders are acceptable, fake completeness is not.

## Current Port Position

The kernel has completed Phase 1E validation and hardening of the port plan in `_TMP/ECS_PORT_IMPLEMENTATION_PLAN.md`.

Implemented so far:

- sealed schema/bootstrap flow with no process-global registries,
- entity/archetype storage bound to schema-owned column factories,
- explicit dirty-aware mutation paths through `World` and the structural queue,
- explicit patch/update mutation path without public mutable refs,
- explicit chunk-level read helpers for hot read-only systems without exposing mutable refs,
- chunk-level bulk patch APIs for hot-path systems without public mutable refs,
- reusable prepared chunk-plan caches for hot-path systems that revisit the same query shape every tick,
- `SystemContext` parallel chunk patch helpers for hot-path systems,
- sparse chunk patch APIs for conditionally-mutating systems so dirty tracking can mark only touched rows,
- hot movement benchmarks now exercise the parallel chunk patch path,
- explicit world lifecycle with seal/initialize/run/shutdown state,
- batched structural enqueue APIs for create-default, destroy, add, and remove,
- mixed structural barriers keep deterministic phase order: destroy, create-default, remove, add,
- scheduler primitives with `Requires`, stage batching, manual systems, execution modes, and phase staggering,
- shared deferred structural command capture is safe under parallel batch execution,
- read-only query and chunk iteration surfaces,
- explicit in-query patch surfaces for dirty-aware write-heavy systems without public mutable refs,
- read-only archetype traversal and persistence adapter hooks,
- lightweight persistence snapshot views in `Extensions/Persistence`,
- serial vs parallel parity coverage for explicit writes and deferred structural commands,
- randomized model-based mutation fuzz coverage,
- entity slot reuse/version invalidation coverage,
- swap-remove relocation stress coverage,
- sparse-victim batch destroy regression coverage,
- heavy multi-hop archetype migration coverage,
- create/destroy churn coverage with stale-handle checks,
- multi-tick serial vs parallel determinism soak coverage,
- opt-in tick profiling snapshots for pre-systems, systems, and post-systems phases,
- recent correctness hardening on archetype enumeration, due-tick wrap behavior, sparse dirty-index validation, manual worker-pool reentrancy, and component-column copier type checks,
- versioned query-plan caching for read-only query, patch-query, chunk, and archetype traversal entry points,
- bound transition copy steps to reduce per-entity structural copy dispatch,
- caller-participating parallel worker execution to reduce small-batch scheduler overhead,
- touched-component tracking in local dirty capture state to reduce merge scanning,
- column-bound query plans for `Query`, `PatchQuery`, and `QueryChunks`,
- column-bound chunk patch paths for `PatchChunks`,
- block-partitioned parallel chunk patch execution through `SystemContext`,
- ordered same-source destroy fast path for swap-remove-safe batch destroy workloads,
- targeted benchmark coverage against the old ECS, including read-only vs write-heavy scheduler scenarios,
- comparative oscillating sine-wave movement tick benchmarks for end-to-end scheduler cost,
- comparative older-old `v1`-style pulse-to-velocity-to-movement scheduler benchmarks,
- comparative activity-shaped `35% moving / reshuffle every 10 ticks` movement benchmarks for both oscillating and v1-style pulse pipelines,
- comparative entity-heavy benchmarks for reuse churn, sparse-victim destroy, and migration cycles,
- dedicated hot read struct-visitor path for ultra-tight read-only per-entity loops,
- sustained GC soak coverage for heavy serial and parallel scheduler scenarios without forced per-scenario collections,
- dedicated scheduler feature report coverage for profiling overhead, execution-mode crossover, and cadence spreading,
- worker-count retention diagnostics for dirty-capture and thread-local scratch state,
- worker-local dirty capture redesigned around bounded append-only entity buffers instead of large per-worker index dedupe tables,
- prefab/persistence seams held for second-wave import,
- executable incremental tests.

Current practical status:

- current comparable benchmark snapshot is `70 / 73` wins against the old kernel,
- movement-oriented hot paths are materially ahead of the older-old and old ECS benchmarks,
- dirty-aware write-heavy scheduler paths are strong,
- read-only scheduler and hot-query paths are now decisively competitive after the dedicated hot read path,
- current soak runs show bounded retained memory and stable tick latency in the heaviest active movement scenarios,
- profiling snapshots currently measure as effectively negligible overhead in scheduler-heavy runs,
- execution-mode crossover is now clearer:
  - `ParallelBatches` is the best fit for the heavier oscillating movement workloads,
  - `Serial` plus intra-system parallelism is the best fit for the current `v1` pulse movement shape,
- automatic cadence spreading is now enabled by default for non-critical stages, with a default cap of `10` distinct offsets per period bucket,
- automatic cadence spreading is still intentionally not applied to `Movement` and `Combat`, because current measurements show that benefit is workload-dependent rather than universally positive there,
- prefabs and persistence remain intentionally deferred behind working seams,
- the main remaining competitive gaps are now concentrated in a narrow destroy/dirty-drain path set rather than in core architecture.

Accepted current tradeoffs:

- `DeferredDestroy [10000]` and `DeferredDestroy_SparseVictims [10000]` are still slightly behind the old kernel in a narrow structural case,
- `DirtyDrain_Position [10000]` remains a small measured loss in the current summary,
- current soak validation is strong enough to rule out the previous silent retention concern, but it is not yet a substitute for longer mixed-workload runtime validation once host wrappers exist.

Still intentionally deferred:

- prefab runtime port,
- persistence/runtime save-load port,
- broader integration helpers and higher-level tooling.

## Scheduler Guidance

Current scheduler policy is:

- keep automatic phase staggering enabled by default for non-critical stages,
- keep the default non-critical stagger cap at `10`,
- keep the cap configurable through `ConfigurePhaseStaggering(enabled, maxNonCriticalStagger)`,
- keep automatic staggering off for `Movement` and `Combat` unless a concrete workload proves it should be widened.

Why `10` remains the right default for now:

- it is large enough to smooth the common low-frequency cadence groups without exploding phase fragmentation,
- it bounds offset growth for very long cadence systems where spreading every possible phase adds little practical value,
- it keeps scheduler behavior predictable and close to the older runtime unless a caller explicitly asks for more.

Current measured guidance:

- profiling overhead is small enough that it is not a practical scheduler concern right now,
- scheduler mode choice matters more than the profiling toggle,
- movement/combat cadence spreading should stay opt-in or manually offset for now rather than becoming a broader automatic rule.
