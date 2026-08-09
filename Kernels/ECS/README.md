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
  are discarded. Retained components also carry their effective dense/exact
  change history into the destination with wrap-aware ordering, so migration
  cannot hide a write that an independent cursor has not yet observed.
- `EcsWorld<TArena>` is the internal composition root over a process-unique
  world ID, fixed entity pool, owned archetype registry/stores, and prepared
  transitions. Its lifecycle now supports `Created`, `Sealed`, `Initialized`,
  `Running`, terminal `Faulted`, and `Disposed` states while retaining the
  seal-only runtime compatibility path used by low-level tests.
- The public non-generic `EcsWorld` facade owns the native-backed kernel without
  exposing arenas or raw storage. `EcsWorldOptions` fixes entity, archetype, and
  prepared-plan, public-transition, and structural-command capacity up front;
  explicit sealing compiles routes and closes allocation, initialization opens
  bootstrap mutation, and `BeginRun` publishes the running state. The current
  facade is deliberately engine-neutral and single-owner, rejecting access from
  other threads until scheduler-issued worker tokens exist.
- Public `ArchetypeHandle` values are eight-byte world/archetype pairs, preventing
  dense archetype IDs from colliding across worlds. Public immediate creation,
  initialized creation, transition, destruction, liveness, and current-archetype
  operations preserve the internal stale/entity/transaction checks.
- Public `ArchetypeTransition` is a 16-byte unmanaged numeric catalog handle,
  not a reference to an internal migration plan. Cold preparation validates and
  stores a bounded world-owned route; hot immediate or deferred use validates the
  world and indexes that route without retaining an arena or store after disposal.
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
- `ComponentLookup<T>` is the first public world-wide random-access plan. Sealing
  compiles one `ushort` physical-column route per archetype, using `0xFFFF` as the
  missing sentinel, so it remains valid as entities move between archetypes while
  consuming only two route bytes per registered archetype. Safe calls revalidate
  lifecycle, owner thread, and lookup ownership. `ExecuteAccess` invokes an
  `IComponentAccessJob<T>` inside one exclusive callback boundary and constructs
  a stack-only `ComponentAccessor<T>` that cannot escape through safe managed
  code. The boundary pays facade/thread/plan validation once while each operation
  retains entity generation/location, current-archetype routing, row bounds,
  enable semantics, and exact scalar change stamping. Mutation outside the
  supplied accessor, structural mutation, nested ECS execution, disposal, and
  fault publication are rejected until a `finally` releases the scope. Neither
  public path allocates or returns mutable refs. Retained lookup and
  public-transition values do not retain the facade,
  entity pool, archetype stores, or arenas after world disposal; disposal
  releases the lookup route array.
- `StructuralCommandBuffer` is the first bounded deferred-mutation foothold. It
  preallocates a 24-byte unmanaged command, a 12-byte entity-result slot, and a
  four-byte stable phase link for every configured command, records scalar or
  atomic span batches without managed allocation, and issues 16-byte barrier-local `DeferredEntity` tokens
  so a create can be targeted by later transition/destroy commands in the same
  epoch. Playback is explicitly ordered as live destroy, create, deferred
  destroy, remove transition, then add/replacement transition, while preserving
  record order inside each phase. Expected stale, duplicate, source-mismatch,
  and capacity failures are consumed and counted; unexpected failures fault the
  world and invalidate the buffer. Tokens expire when a new recording epoch
  begins, and resolution reports the creation result rather than promising that
  the entity survived later commands. Exhausting the 32-bit epoch rotates the
  process-unique buffer identity instead of permitting token ABA.
- Recording appends each command to one stable live-destroy/create/deferred-
  destroy/remove/add chain. Playback therefore visits only commands in the active
  phase while preserving exact record order inside it; the retained scalar
  five-scan executor remains an internal differential oracle. World mutations
  underneath those chains are still scalar. This proves bounded storage,
  composition, conflict semantics, lifecycle barriers, and zero warmed
  allocation. Per-worker arenas,
  deterministic worker merge, operation/source/transition grouping, bulk row
  reservation/copy/compaction, typed component payloads, prefab expansion, and
  lifecycle publication remain later structural work and must be measured
  against this baseline rather than hidden behind it.
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
- `ChangeVersion` is an eight-byte world-local sequence with zero reserved for
  "never changed," explicit maximum-to-one wrap behavior, and half-range
  changed-since comparison. Every archetype store preallocates one 64-bit stamp
  per physical component column per possible chunk in column-major order. Empty
  chunk reuse clears every stamp, and cold snapshots report this memory separately
  from the existing 28-byte chunk records.
- Successful scalar value writes, enable-state writes, initialized creation, the
  handwritten writable visitors, and every generated family with at least one
  writable slot now conservatively stamp the affected columns. One invocation
  lazily acquires one version and shares it across its visited chunks and writable
  columns; filtered visits stamp only chunks that expose at least one matching
  row, and stamping happens before the visitor so exceptions cannot hide a
  possibly partial write. Read-only families neither advance nor stamp versions.
  Unfiltered writable chunks publish a dense stamp; scalar and filtered writable
  ranges publish row-precise stamps when that column has an exact-row consumer.
- `ChangeCursor` is a 16-byte world-bound value. A cursor may begin at zero to
  consume all previously stamped chunks or at the world's current version to
  observe only future changes; copying it creates an independent consumer with
  no registry entry or per-consumer heap state. A seal-compiled
  `ArchetypeChangedReadQueryPlan<TArena,TRead>` prebinds the read column and a
  potentially different tracked column for every matching archetype. Runtime
  rejects unchanged chunks before binding component spans, composes with existing
  required/excluded/enabled/disabled filters, and advances the caller's cursor
  only after every visitor succeeds. Row predicates are evaluated against current
  state; a successful scan acknowledges its observation boundary even when no
  rows pass those predicates. Exceptions preserve the old cursor for an
  at-least-once retry without loss; chunks delivered before the exception may be
  delivered again. The initial handwritten plan reads one data component and
  watches one same-or-different component.
- `PrepareChangedRowsQuery` opts a tracked column into exact-row history at world
  sealing. Ordinary dense plans reserve no additional row storage. Each opted-in
  archetype column preallocates eight bytes per possible physical row plus one
  dense version per possible chunk; snapshots expose that cost separately.
  `VisitChangedReadRows` first uses the existing chunk version as a cheap reject,
  then constructs 64-row masks from the bounded row versions. Scalar writes mark
  one row, filtered visitors mark precisely the ranges they were allowed to
  mutate, and full writable chunks remain a one-stamp dense fast path. Multiple
  consumers and arbitrarily delayed consumers retain the existing copied-cursor
  semantics without a destructive shared dirty mask. Exposed writable rows are
  conservatively considered written even if visitor code leaves their bytes
  unchanged. Empty reuse clears old history. Packed donor relocation moves both
  dense-only and exact effective history with the physical row, and
  cross-archetype migration merges retained-component history into the new row
  without falsely marking every destination row. All merges use half-range
  wrap-aware ordering rather than numeric maximum.
- The query-family generator now also emits an initial 2R2C changed family. It
  returns the existing two-read stack-only view while watching two distinct data
  or tag columns that may alias either read. `ChangedMatchMode.Any` ORs watched
  chunk/row histories; `All` requires both chunk stamps or intersects both exact
  row masks. One cursor baseline applies to both columns and advances after every
  successful observation, including an `All` scan with no qualifying route.
  Required/excluded/enabled/disabled filters, exception retry, wrap, backend
  parity, and zero-allocation execution retain the one-watch contracts. Further
  read/watch counts remain workload-driven rather than a promise of arbitrary
  runtime-width mutation.
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

The current runtime and primitive suite has 182 passing tests, including
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
suite has 21 passing tests. Composed-world tests cover lifecycle ordering,
owned disposal, runtime-before-seal rejection, row/entity capacity atomicity,
generation reuse, donor repair, cross-world plan and entity isolation, 30,000
randomized create/transition/destroy operations, native/pinned parity, and 100,000
warmed allocation-free full lifecycle cycles. Bound-access tests lock the 16-byte
layout, generated-ID/type/membership validation, value and enable semantics,
tag/non-enableable rejection, initialized creation, exact-archetype behavior over
migration, stale/foreign isolation, a 30,000-operation randomized model,
native/pinned parity, and one million warmed allocation-free access rounds.
Public-facade tests add explicit created/sealed/initialized/running/faulted/disposed
ordering, eight-byte world-checked archetype handles, fixed option/plan capacity,
single-owner rejection, terminal fault behavior, cross-world/default/stale route
rejection, data/tag/enable semantics, lookup continuity across bidirectional
migration, a 20,000-operation randomized reference model, and a 250,000-round
allocation-free bounded-access-job/migration gate. Adversarial access tests prove
that out-of-scope scalar mutation, structural mutation, destruction, nested execution, disposal, and fault
publication are rejected inside the callback and that exception exit releases
the boundary. Relocation tests cover dense-only packed donors plus exact and dense
history across Native/Pinned cross-archetype migration, including version wrap
and acknowledgement without migration-generated replay.
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
allocation gate. Dense-change tests lock the eight-byte version layout and wrap
ordering, exact bounded metadata size, empty-chunk reset, exact scalar-column
stamping, read-only noninterference, multi-column/multi-chunk generated writes,
filtered unmatched-chunk suppression, stamp-before-exception behavior,
native/pinned parity, and warmed zero-allocation tracked reads and writes. A
generator source-shape assertion confirms that read-only families emit no
tracking work while every writable slot emits a stamp. Changed-read tests cover
the 16-byte cursor layout, maximum-to-one wrap, complete seal matching, a read
column distinct from the tracked column, independent copied consumers, no replay
after acknowledgement, filtered physical row offsets and enable state,
exception-without-acknowledgement retry, foreign/default cursor and plan rejection,
bounded plan capacity, native/pinned parity, and warmed zero-allocation execution.
Exact-row tests add opt-in memory accounting, sparse scalar refinement within a
densely changed chunk, independent consumer progress, filtered handwritten and
generated writable ranges, full-chunk dense fallback, packed-row reset, cursor
wrap and exception retry, backend parity, and 10,000 warmed allocation-free
write/read cycles. Generated multi-change tests add seal-complete two-watch
routing, explicit any/all observation behavior, OR/AND exact masks, current
row-filter intersection, cursor acknowledgement on empty `All` observations,
exception retry, ownership/capacity/alias/mode rejection, wrap, Native/Pinned
parity, and 10,000 warmed allocation-free two-write/exact-read cycles. Deferred
structural tests lock record/handle/result layouts and reserved bytes; strict and
`Try` capacity behavior; atomic batch recording; deterministic phase conflicts;
stale, duplicate, foreign, default, and wrong-thread rejection; create-token
composition, epoch expiry, rollover identity rotation, and mixed-chain clear;
capacity failures; execution-barrier recording versus playback rules;
disposal/fault invalidation; a 64-seed randomized phase-index-versus-scalar
differential model covering logical state and generation/free-list order; and a
250,000-transition warmed zero-allocation gate.

This is now a usable public single-owner component/archetype slice, not yet the
public scheduler/host ECS. The current generators emit the core manifest, a
systematic catalog containing 2R0W, 2R1W, 1R2W, 3R1W, and 2R2W access families,
and the first 2R2C any/all changed family. Further access/watch arities should be
added only when real system signatures require them. Occupancy/density
measurements may show that some shapes should remain dense or bypass change
refinement entirely. Public tracked-write scopes, structural
creation/migration/removal events, optional filters, per-worker structural arenas
and bulk playback, generated public chunk jobs, scheduler worker tokens, and
field/serializer/prefab descriptors remain later work.

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
