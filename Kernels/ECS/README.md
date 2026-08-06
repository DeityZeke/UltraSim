# UltraSim ECS Next

This is the new engine-agnostic ECS described by the root
[`ECS_NEXT_DESIGN.md`](../../ECS_NEXT_DESIGN.md). The V2 Current and Legacy
implementations remain isolated under `_V2/` for differential tests and
benchmarks.

## Current implementation slices

The first Phase 1 slice establishes runtime identity and bounded entity-slot
allocation:

- `WorldId` is a transient process-lifetime identifier. A monotonic atomic source
  prevents two live worlds from receiving the same ID and permanently refuses to
  wrap. It is a cold identity counter, not a component/schema registry.
- `Entity` is the initial 12-byte candidate containing world ID, slot index, and
  generation. Tests lock its actual runtime size. Benchmarks must compare it with
  viable 8-byte packing before chunk layout is accepted.
- `EntityPool` preallocates all generation, state, and free-index storage. It has
  explicit `Try` APIs, never resizes, rejects stale/foreign handles, and retires a
  slot rather than wrapping its generation.
- `ArchetypeId` is a four-byte dense world-local route key. `EntityLocation`
  combines it with the existing eight-byte `ChunkRow` without duplicating entity
  generation, for 12 bytes of preallocated location metadata per entity slot.
- `EntityPool` now owns that location table. Live entities bind exactly once,
  expected-location unbinds reject stale structural work, located entities cannot
  be freed, and trusted row-store swap relocations must match the donor's recorded
  archetype and source row before updating it.
- `ArchetypeRegistry<TArena>` preallocates a bounded open-address lookup and dense
  ID table. Cold registration copies and validates canonical sorted signatures,
  plans their physical layouts, and takes exact ownership of one fixed arena/store.
  Sealing drops the factory and permits concurrent immutable signature lookup;
  hot ID-to-store routing is a direct indexed read.
- `ArchetypeTransitionPlan<TArena>` is prepared during the cold registration
  phase. It merge-walks canonical source/destination signatures once, validates
  retained storage compatibility, and prebinds exact physical source/destination
  columns; hot migration performs no component lookup or managed allocation.
- `ArchetypeMigrator` reserves and default-initializes the destination row before
  changing live state, copies retained data and enable bits, publishes the
  replacement entity location, removes the source row, and applies any source
  swap-donor relocation. Destination-capacity and stale-plan failures leave the
  source unchanged; guarded rollback paths protect later transaction failures. Added
  components retain store-defined zero/enabled defaults and removed components
  are discarded.
- `EcsWorld<TArena>` is the first internal composition root over a process-unique
  world ID, fixed entity pool, owned archetype registry/stores, and prepared
  transitions. Its explicit `Created -> Sealed -> Disposed` lifecycle closes
  registration and transition-plan construction before runtime mutation.
- Immediate default creation, transition, and destruction are checked at one
  boundary. Full-archetype rejection does not allocate or burn an entity
  generation, creation rollback removes a just-added row, destruction repairs a
  packed swap donor before invalidating the stale handle, and foreign-world plans
  are rejected by O(1) registry/store identity checks even when dense IDs collide.
- `ArchetypeComponentBinding<T>` is a 16-byte, cold-built route from a generated
  component ID to one exact world/archetype/physical column. Binding validates
  schema type, flags, size, membership, and layout once before sealing; hot access
  performs no component scan, metadata lookup, boxing, or managed allocation.
- Bound world access copies values in/out rather than returning refs, rejects
  stale/foreign/wrong-archetype entities, supports enableable data and tags, and
  rejects data access for storage-free tags. Single-value initialized creation
  writes through the same checked binding without exposing raw storage.
- `VisitReadChunks` and `VisitWriteChunks` are the first system-facing execution
  path. They reuse an exact binding, walk only occupied partial/full chunk lists,
  and invoke constrained struct visitors with stack-only component/entity-index
  views. Enable words stay encapsulated behind checked row operations.
- The synchronous chunk barrier rejects nested visits, structural operations,
  scalar writes, enable writes, and world disposal until the visitor returns.
  A `finally` releases it on exceptions. Safe managed code cannot retain the
  view or its spans beyond the callback; this is not yet the parallel scheduler
  or the final generated multi-component query surface.
- `ArchetypeReadWriteQueryPlan<TArena,TRead,TWrite>` is the first multi-archetype
  typed plan. Its descriptor occupies an explicitly bounded world-owned slot,
  and `Seal` compiles all matching archetypes—including registrations made after
  descriptor creation—into direct storage and two-column routes.
- Read/write aliasing, storage-free tags, generated-ID/type mismatch, foreign
  ownership, and plan-capacity overflow fail before hot execution. A matched
  chunk resolves its entity prefix, read span, write span, and enable state from
  one arena lookup; the constrained visitor retains the existing barrier rules.
  World disposal invalidates plans and clears their routes so a retained plan
  object cannot retain all matched storage objects.
- `ArchetypeQueryFilter` is a cold-built, world-owned descriptor over normalized
  required, excluded, enabled, and disabled component sets. Enabled/disabled
  components become structural requirements; contradictions, foreign descriptors,
  non-enableable row filters, invalid IDs, and access/exclusion conflicts fail
  before sealing.
- Seal compilation merge-matches structural filters against canonical signatures
  and reduces row filters to direct enable-bit offsets per route. Runtime combines
  those words and exposes only contiguous matching slices, with a physical
  `RowOffset` preserving enable access. Visitors cannot write unmatched rows, the
  unfiltered route remains branch-minimal, and warmed dense, fragmented, native,
  and pinned tests allocate zero managed bytes.
- The kernel now opts into a data-driven source-generated access-family catalog.
  Its first five specifications cover 2R0W, 2R1W, 1R2W, 3R1W, and 2R2W access.
  The generated descriptors, seal-compiled
  physical routes, trusted store binding, stack-only view, filter range dispatch,
  visitor contract, and world entry points all encode access without per-row
  lookup, delegate dispatch, boxing, or entity-handle materialization. Consumer
  schema assemblies do not receive duplicate runtime families; generation is
  gated by an assembly marker. One emitter derives naming, generic constraints,
  distinct-access checks, span mutability, enable APIs, plan compilation, and hot
  execution for all five access shapes. The 2R0W view exposes only read-only
  component spans and enable-state reads; it has no generated writable span or
  enable setter.
- Unsigned one-branch range checks now use explicit `unchecked` normalization.
  This preserves negative-index rejection as `ArgumentOutOfRangeException` under
  the kernel's project-wide checked arithmetic instead of paying for or surfacing
  a checked signed-to-unsigned overflow. Entity/chunk slot snapshots, storage
  indices, handwritten views, generated views, routes, and plan ownership share
  regression coverage for that boundary.
- Runtime handles are not durable IDs and will not be serialized. Persistence
  restore will construct a temporary old-index-to-new-entity remap during a
  bounded transactional load.
- `IComponent<TSelf>`, `[EcsComponent]`, and `[EcsSchema]` define a closed,
  generated component set without runtime reflection or a mutable global
  registry.
- Generated component IDs are dense and sorted by canonical key. Generated
  128-bit schema fingerprints include component identity, version, traits,
  persistence identity, type, and field signature.
- `ComponentSchema` owns immutable instance tables. Metadata-by-ID and generated
  generic lookup are direct; type, canonical-key, stable-hash, and persistence-ID
  lookup allocate nothing. Schemas with more than eight components use bounded
  open-address indexes; small schemas retain the lower-overhead linear path.
- `ComponentMetadata` is exactly 16 bytes. Tag components consume no column
  bytes, while runtime size and alignment are captured once during schema
  construction for ordinary components.
- `ChunkLayoutPlanner` converts a canonical signature into deterministic SoA
  column offsets using caller-owned spans. It reserves a local entity-index
  column, omits tag data, word-rounds enable masks, searches for the maximum
  fitting row count, and reports every padding/tail byte with checked arithmetic.
- `ArchetypeCapacityPlan` projects a population onto that layout, while
  `WorldCapacityPlanner` combines archetype chunks with fixed entity metadata
  and explicit command/change/scratch arena budgets. These planners allocate
  no memory.
- `NativeChunkArena` and `PinnedChunkArena` are internal, fixed-capacity storage
  candidates over the same 12-byte generational `ChunkHandle` lifecycle. Both
  reserve one contiguous 64-byte-aligned slab, reject stale/foreign handles,
  retire generation-exhausted slots, clear a whole chunk on acquisition, and
  provide allocation-free typed SoA/entity-index/enable-word spans.
- Native storage uses `NativeMemory.AlignedAlloc` and explicit aligned release.
  The managed comparison uses one pinned object-heap byte array plus at most
  `alignment - 1` bytes of alignment slack. Both expose cold payload, release,
  slot, retirement, and high-water accounting; explicit `Dispose` is required.
- Arena structure is single-owner. Disjoint live chunks may be processed in
  parallel after a barrier, but no chunk span may survive release or arena
  disposal. The arenas remain internal until generated query bindings can prevent
  chunk-level callers from requesting a same-sized but semantically wrong type.
- `ArchetypeChunkStore<TArena>` now makes both candidates executable row stores.
  It owns a fresh fixed arena; keeps compact intrusive full, partial, and empty
  chunk lists; acquires lazily; and retains empty chunks for immediate reuse.
- `ChunkRow` is the current eight-byte entity-location candidate: chunk index
  plus row, with an all-zero invalid value. The store remains densely packed,
  with every occupied chunk full except at most one partial chunk.
- Scalar and bulk adds explicitly zero every data row. Enableable components and
  tags start enabled. Swap removal works within or across chunks, preserves all
  data and enable states, clears the donor row, rejects an expected-entity
  mismatch, and returns the donor's relocation for the entity-location table.
- Executable layouts are revalidated once before use. Hot access can bind a
  physical column index once, avoiding repeated component metadata scans.

`EntityPool` is intentionally single-owner. Parallel systems will submit
structural work through bounded per-worker command arenas; they will not mutate
the live pool concurrently.

The current runtime and primitive suite has 137 passing tests, including
randomized chunk-slot lifecycle parity, alignment/range/type rejection,
alignment extremes and concurrent arena-ID uniqueness, clear-on-reuse,
disposal/accounting, terminal generation retirement, and warmed zero-allocation
lifecycle and typed-access loops. New row tests cover every occupancy transition,
retained-empty reuse, bulk/scalar and native/pinned byte parity, default-enabled
bit ranges, within/cross-chunk swap relocation, stale locations, two randomized
reference models, owned disposal, and allocation-free churn. Entity-location tests
also cover stale/foreign binding, overwrite and bound-free rejection, checked donor
relocation, a 30,000-operation randomized integrated pool/store model, and warmed
100,000-cycle allocation-free located churn. Registry tests cover empty and dense
signatures, duplicate geometry, bounded exhaustion, forced open-address collisions,
factory cleanup, exact arena capacity, native/pinned isolation and disposal,
concurrent sealed reads, randomized lookup, and one million warmed allocation-free
lookups. Migration tests cover retained data and enable state, added-component
defaults, removed components, source donor relocation, stale-plan and bounded
destination rejection, 30,000 randomized bidirectional moves, native/pinned byte
equivalence, and 100,000 warmed allocation-free migration cycles. The generator
suite has 19 passing tests. Composed-world tests cover lifecycle ordering,
owned disposal, runtime-before-seal rejection, row/entity capacity atomicity,
generation reuse, donor repair, cross-world plan and entity isolation, 30,000
randomized create/transition/destroy operations, native/pinned parity, and 100,000
warmed allocation-free full lifecycle cycles. Bound-access tests lock the 16-byte
layout, generated-ID/type/membership validation, value and enable semantics,
tag/non-enableable rejection, initialized creation, exact-archetype behavior over
migration, stale/foreign isolation, a 30,000-operation randomized model,
native/pinned parity, and one million warmed allocation-free access rounds.
Chunk-visitor tests add empty and multi-chunk traversal, exact metadata and local
indices, data/enable writes, tag/foreign/non-enableable rejection, synchronous
mutation and nesting rejection, exception cleanup, randomized reference parity,
native/pinned equivalence, and warmed zero-allocation scans.
Query-plan tests add seal-complete matching, archetypes registered after descriptor
creation, multiple layouts, explicit zero/bounded plan capacity, alias/tag/type/
foreign rejection, barrier and exception cleanup, randomized transition parity,
native/pinned equivalence, and ten million warmed allocation-free row visits.
Filter tests add normalization/deduplication, contradictory and foreign constraint
rejection, required/excluded seal matching, enableable data/tag row predicates,
noncontiguous slices crossing enable words and chunks, randomized reference parity,
native/pinned equivalence, and warmed allocation-free filtered visits.
Generated-family tests add deterministic and explicitly opt-in source emission;
read-only versus writable span contracts; seal-complete 2R0W, 2R1W, 1R2W, 3R1W,
and 2R2W routing; alias/tag/type/access-exclusion/capacity/foreign-plan
rejection; full and filtered multi-archetype traversal; physical row offsets;
non-enableable-write and row-boundary rejection; barrier and exception cleanup;
randomized state parity; native/pinned equivalence; and ten million warmed
allocation-free row visits across all five shapes. The wider-family tests exercise
four distinct component slots, two matching layouts, a structurally excluded
layout, filtered execution, randomized state, and a joint ten-million-row warmed
allocation gate.

This is now a minimal internal component/archetype world, not yet the public
scheduler/host facade. The current generators emit the core manifest and a
systematic catalog currently containing 2R0W, 2R1W, 1R2W, 3R1W, and 2R2W
families. Further arities should now be added only when real system signatures
require them. The next natural query work is generated per-chunk tracked-write
versions and change versions before
writable views become public.
Optional/change filters, bounded deferred structural commands, and field/
serializer/prefab descriptors remain later work.

Run both executable test suites from the repository root:

```bash
dotnet run --project _Tests/UltraSim.ECS.Tests/UltraSim.ECS.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.ECS.Generator.Tests/UltraSim.ECS.Generator.Tests.csproj -c Release
```

## Local independent review

Focused changes can be sent to the local Qwen critic with:

```bash
_Tools/LocalAI/qwen-review.sh ecs \
  Kernels/ECS/Entities/Entity.cs \
  Kernels/ECS/Entities/EntityPool.cs
```

Treat its output as hypotheses until verified by source, tests, or measurements.
