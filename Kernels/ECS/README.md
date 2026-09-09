# UltraSim ECS 3.4.3

UltraSim ECS is a bounded, owner-controlled, archetype/SoA kernel for large
deterministic simulations. It combines compact generational entity identity,
sealed schemas, packed chunk storage, generated contiguous traversal, deferred
structural mutation, a bounded scheduler, change/lifecycle observation, compiled
content, and transactional full-world persistence.

It should be understood as one current machine, not as the chronology of the
optimization checkpoints that produced it.

## Mental model

```text
EcsWorld
    EntityPool             identity, liveness, physical location, durable IDs
    ArchetypeRegistry      sealed signatures and transition routes
    ArchetypeChunkStore    packed SoA columns, enable/change state
    Prepared execution     generated chunk jobs, filters, sparse lookups
    SystemScheduler        bounded graph, workers, cadence, structural lanes
    Structural playback    deterministic barrier and physical planning
    LifecycleHistory       independent structural observation
    PrefabCatalog          compiled content identity/defaults
    Persistence            staged durable snapshot/restore
```

Normal public use cannot fabricate world-local handles, bypass ownership, retain
callback spans safely, or publish half a structural/persistence operation.

## Documentation

- [Execution and API selection](EXECUTION.md)
- [Scheduler](SCHEDULER.md)
- [Structural mutation](STRUCTURAL.md)
- [Compiled content](CONTENT.md)
- [Lifecycle publication](LIFECYCLE.md)
- [Persistence](PERSISTENCE.md)
- [Whole-engine documentation map](../../Docs/README.md)

## Core contracts

### Schema and components

`ComponentSchema` is a generated/closed mapping from component type and stable
canonical key to dense world-local `ComponentId`. Components are unmanaged data;
storage-free tags participate in topology/filters without pretending to be value
columns. Archetype signatures are canonical and fixed after world sealing.

### Entity identity

`Entity` contains world-local index and generation. Stale, wrong-world, and dead
values reject. Numeric index alone is not full identity. Durable identity is a
separate persistence contract.

### Storage

Each archetype owns packed chunks with structure-of-arrays component columns,
enable bitplanes, entity indices, and change metadata. Chunk handles and physical
row order are internal and may be reused. Query-visible storage is dense.

### Execution

Prepared generated jobs traverse matching bound columns contiguously. Range jobs
serve external/preselected work. Prepared lookups serve sparse references. These
are intentionally different cost/ownership shapes; see [EXECUTION.md](EXECUTION.md).

### Mutation and observation

Value writes publish dense/exact change state according to their API. Entity
shape/existence changes cross the structural barrier. Lifecycle cursors observe
successful topology history independently from change cursors.

Exact changed-row history belongs to the logical occupants present when a write
is published. Append, migration, compaction, and row reuse do not inherit an old
dense write merely because they later occupy the same physical chunk. Coarse
changed-chunk observation remains deliberately conservative.

### Scheduler

The optional scheduler seals a deterministic stage/wave graph, partitions typed
jobs over persistent workers, and merges bounded structural lanes in logical
order. When disabled it reserves no scheduler runtime. See
[SCHEDULER.md](SCHEDULER.md).

### Content and persistence

Cold prefab authoring compiles to ordinary archetypes/default payloads and stable
provenance. Full-world persistence stores stable identities/schema/content keys,
allocates all identities before reference fixup, and publishes only a completely
validated candidate.

## World lifecycle

The public lifecycle is deliberate: register schema-dependent topology/plans and
systems, `Seal`, `Initialize`, `BeginRun`, execute/tick, then dispose. APIs state
which phases they admit. The owner thread coordinates structural, lifecycle,
publication, and disposal boundaries; scheduler workers receive narrow expiring
capabilities instead of the world facade.

Lifecycle mutation is admitted before teardown begins. A rejected `Dispose`
from a direct ECS callback or persistence-capture callback leaves the scheduler,
world state, and next ordinary tick intact.

## Capacity and memory

`EcsWorldOptions` fixes entity/archetype/plan/chunk, structural, change,
lifecycle, scheduler, prefab, and persistence-related capacities. Native/Pinned
backends share semantics. Reserved-payload snapshots report known storage; they
are not process working-set claims.

Exact row tracking is conditionally installed per watched physical component.
When installed, its predictable reservation is approximately
`8 * maximumChunks * (rowCapacity + 1)` bytes: one stamp per configured row plus
one dense stamp per chunk, regardless of live population. "Sparse" describes
write publication, not allocation density.

Warmed admitted hot paths are managed-allocation-free where tests declare that
contract. Cold schema/content/planning/persistence work may allocate within
reported and bounded policies. Restore planning resolves the largest expanded
runtime column before admitting its current whole-column decoder scratch; that
known-buffer estimate is not a claim about complete CLR/process working set or
arbitrary allocations inside application migration callbacks.

## Version and evidence

The active internal engineering identity is `3.4.3`. It hardens exact change
history across occupancy changes, lifecycle/capture rejection atomicity,
expanded persistence scratch admission, and staging-factory ownership. It
identifies benchmark and persistence evidence; it is not SemVer or a broad
public compatibility promise.
Format/schema versions are owned separately by their persisted/protocol contracts.

Accepted machine-readable evidence is under
[`_Benchmarks/Baselines`](../../_Benchmarks/Baselines/README.md). Historical
acceptance reports and completed rebuild plans remain available through git.
