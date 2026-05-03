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
- opt-in per-system runtime snapshots with live timing data when profiling is enabled,
- minimal typed system control-descriptor seam for future operator/control-plane integration without coupling UI into the kernel,
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
- prefab startup slice now in place through typed prefab contracts, `PrefabDefinition`, `PrefabCatalog`, immediate world prefab spawn helpers, and deferred prefab structural queue integration,
- persistence startup slice now in place through stable persistence ids, instance-owned type registry, canonical persistence signatures, and in-memory entity snapshot capture/restore,
- executable incremental tests.

Current practical status:

- current comparable benchmark snapshot is `70 / 73` wins against the old kernel,
- movement-oriented hot paths are materially ahead of the older-old and old ECS benchmarks,
- dirty-aware write-heavy scheduler paths are strong,
- read-only scheduler and hot-query paths are now decisively competitive after the dedicated hot read path,
- current soak runs show bounded retained memory and stable tick latency in the heaviest active movement scenarios,
- profiling snapshots currently measure as effectively negligible overhead in scheduler-heavy runs,
- per-system live timing data is now consumable through runtime snapshots for future performance-panel views without reviving the old control-panel UI directly inside the kernel,
- Phase 2 now has both initial prefab and persistence runtime slices rather than only placeholder seams,
- execution-mode crossover is now clearer:
  - `ParallelBatches` is the best fit for the heavier oscillating movement workloads,
  - `Serial` plus intra-system parallelism is the best fit for the current `v1` pulse movement shape,
- automatic cadence spreading is now enabled by default for non-critical stages, with a default cap of `10` distinct offsets per period bucket,
- automatic cadence spreading is still intentionally not applied to `Movement` and `Combat`, because current measurements show that benefit is workload-dependent rather than universally positive there,
- prefab work is now partially landed but still intentionally narrow:
  - no static global prefab registry,
  - no file-backed/data-driven loader yet,
  - no richer prefab import/catalog pipeline yet,
- persistence work is now partially landed but still intentionally narrow:
  - in-memory snapshot capture/restore exists,
  - a narrow binary world save/load adapter exists,
  - server-level save-set manifest/recovery work exists above the kernel in
    `Server/Persistence`,
  - richer component type-database/versioning work is still deferred,
- the main remaining competitive gaps are now concentrated in a narrow destroy/dirty-drain path set rather than in core architecture.

Accepted current tradeoffs:

- `DeferredDestroy [10000]` and `DeferredDestroy_SparseVictims [10000]` are still slightly behind the old kernel in a narrow structural case,
- `DirtyDrain_Position [10000]` remains a small measured loss in the current summary,
- current soak validation is strong enough to rule out the previous silent retention concern, but it is not yet a substitute for longer mixed-workload runtime validation once host wrappers exist.

Still intentionally deferred:

- broader prefab runtime work beyond the current startup slice,
- richer component persistence versioning and generated serializer tooling beyond
  the current binary adapter,
- broader integration helpers and higher-level tooling.

## Phase 2 Prefabs

The first prefab slice is now implemented.

Current prefab pieces:

- `IPrefab<TSelf>`
- `ITemplatePrefab<TSelf>`
- `IResolvedPrefab<TSelf>`
- `PrefabDefinition`
- `PrefabCatalog`
- immediate `World.CreatePrefab(...)` helpers
- resolver-backed `World.CreatePrefab(string key)` / `TryCreatePrefab(...)`
- deferred `EnqueueCreatePrefab(...)` queue support for definitions, typed prefabs, and resolver-backed keys

Current prefab policy:

- prefabs remain instance-owned and engine-agnostic,
- there is no process-global prefab registry,
- both immediate and deferred structural prefab spawn paths now exist,
- spawned prefab entities mark their applied components dirty so future persistence/replication layers can observe them consistently.

Still deferred for later Phase 2 slices:

- file-backed/data-driven prefab loading,
- richer prefab catalogs/import pipelines,
- deeper spawn-path optimization if real workloads justify it.

## Phase 2 Persistence

The first persistence slice is now implemented.

Current persistence pieces:

- `PersistenceIdAttribute`
- `PersistenceKind`
- `PersistenceTypeRecord`
- `PersistenceTypeRegistry`
- `PersistenceComponentDescriptor`
- `PersistenceSignature`
- `PersistenceSignatureBuilder`
- `EntityPersistenceSnapshot`
- `BinaryDataWriter`
- `BinaryDataReader`
- `BinaryEntityPersistenceSerializer`
- `BinaryWorldPersistenceAdapter`
- `WorldPersistenceView`
- `PersistenceArchetypeView`
- `World.CapturePersistenceSnapshot(...)`
- `World.RestorePersistenceSnapshot(...)`

Current persistence policy:

- persistence types are instance-owned and explicit rather than process-global,
- only components that implement `ISerializableComponent<T>` and carry a stable `PersistenceId` participate in the snapshot layer,
- the current slice now includes both:
  - an in-memory grouped entity snapshot/restore path,
  - a narrow single-stream binary save/load adapter built on that snapshot shape,
- server-level save-set manifests, backup rotation, participant ordering, and
  recovery policy now live in `Server/Persistence` above this extension,
- restore currently appends compatible persistent entities into a target world and marks restored components dirty.

Still deferred for later Phase 2 slices:

- richer component type/version database work,
- deeper versioned component serialization beyond the current in-memory shape,
- source-generated component serializer/migration scaffolding for annotated ECS
  components,
- broader system-owned persistence tooling.

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

## Control Seams

The old older-old client control panel mixed three concerns together:

- system inspection,
- live performance display,
- operator-editable settings UI.

The new kernel intentionally does not reproduce that whole stack inside `Kernels/ECS`.

Current kernel policy is:

- keep `SystemSettings` as a small low-level schedule/config hook,
- do not turn `SystemSettings` into a generic UI/settings framework,
- expose typed inspection data from the kernel,
- leave richer operator tuning, permissions, persistence of overrides, and UI/schema generation to higher layers later.

What exists now:

- `SystemSettings` can still adjust schedule-level concerns such as cadence, offset, and stage,
- `SystemRuntimeSnapshot` exposes system name, schedule metadata, enabled/manual state, live timing data, and optional control descriptors,
- `SystemBase.ControlDescriptors` is an empty-by-default seam for future control-plane integration,
- timing values populate when `ProfilingEnabled` is on, so future performance panels can consume live per-system metrics without forcing a full settings subsystem now.

What is intentionally deferred:

- generic editable settings persistence,
- permissions/authority for changing tunables,
- network transport for remote operator control,
- UI schema generation for editable controls,
- host-specific control panel implementations beyond thin shells.
