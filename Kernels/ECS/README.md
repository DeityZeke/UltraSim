# UltraSim ECS Next

This is the new engine-agnostic ECS described by the root
[`ECS_NEXT_DESIGN.md`](../../ECS_NEXT_DESIGN.md). The V2 Current and Legacy
implementations remain isolated under `_V2/` for differential tests and
benchmarks.

For the public execution-path decision tree, cost model, structural visibility,
and common misuse cases, start with
[`ECS_USAGE_GUIDE.md`](ECS_USAGE_GUIDE.md). In short: composition-selected rows
belong in generated chunk jobs; `RangeJob` is for bounded external or already-
selected work; `ComponentLookup<T>` is sparse checked random access.
For RunUO-style `BaseClass`/Object Script authoring, stable prefab identity,
compiled catalog layout, and one-command spawning, see
[`ECS_CONTENT_AUTHORING.md`](ECS_CONTENT_AUTHORING.md).
Acceptance evidence and the complete 3.2/3.3 comparison are in
[`../../ECS_3_3_0_CONTENT_AUTHORING_REPORT.md`](../../ECS_3_3_0_CONTENT_AUTHORING_REPORT.md).

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
  bootstrap mutation, and `BeginRun` publishes the running state. The facade is
  deliberately engine-neutral and single-owner. ECS 3.1.2's optional scheduler
  grants persistent workers only expiring `SystemJobContext` capabilities rather
  than allowing non-owner access to the facade. ECS 3.2.0 adds an optional fixed
  lifecycle-history capacity; zero preserves the disabled publication path.
- Public `ArchetypeHandle` values are eight-byte world/archetype pairs, preventing
  dense archetype IDs from colliding across worlds. Public immediate creation,
  initialized creation, transition, destruction, liveness, and current-archetype
  operations preserve the internal stale/entity/transaction checks.
- ECS 3.3.0 adds a cold `ContentCatalogBuilder` with explicit Object Script
  registration and direct-DOD definitions. It resolves additive BaseClass
  composition/defaults, sorts stable `PrefabKey` paths, deduplicates canonical
  signatures, binds defaults to physical columns, and freezes one immutable
  world catalog before `Seal`. The runtime retains 12-byte `PrefabHandle` values,
  32-byte entries, 12-byte default descriptors, one byte blob, and cold key/name
  metadata—not the authoring object hierarchy. Immediate and deferred prefab
  creation allocate ordinary ECS rows, copy compiled constants, and publish one
  ordinary `Created`; no reflection, managed callback, or runtime `IsA` graph
  exists on spawn or tick.
- ECS 3.4.1 retains the 3.4 optional fixed durable-identity storage and generated,
  column-oriented full-world snapshots. The production contract separates
  complete stable runtime shape from authoritative payload, remaps internal
  `Entity` references through dense save ordinals, persists selected enable
  planes/prefab provenance/system state, and restores into a validated clean
  staging world. `Server/Persistence` owns common-epoch multi-participant capture
  and crash-durable generation publication. See
  [`ECS_PERSISTENCE.md`](ECS_PERSISTENCE.md).
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
  five-scan executor remains an internal differential oracle. Contiguous runs of
  at least eight commands using the same prepared transition are executed in
  fixed stack blocks of at most 256 entities. Each block reserves and
  default-initializes destination rows in ranges, validates and commits entities
  once in record order, and removes any anonymous unused tail in reverse order.
  A valid, distinct, physically contiguous source prefix takes a locality path
  before source rows move: retained data copies column-at-a-time across maximal
  simultaneous source/destination runs, enable bits copy word ranges, dense
  history merges once per run, and exact history remains row-precise. Fragmented
  or conflicting prefixes fall back to the v25 ordered path. A v27 compaction
  path activates only when that contiguous set contains the current donor tail
  and either contains at least 32 rows or evacuates the donor chunk. It plans the
  exact tail once, skips donors that are also leaving, moves each surviving donor
  at most once, clears tail columns/enable bits/history in ranges, changes each
  affected chunk list once, and transactionally repairs migrated and donor
  locations. Nonoverlapping and low-density batches retain v26 scalar removal.
  Duplicate, stale, wrong-source, partial-capacity, donor-repair, enable-state,
  and dense/exact change-history semantics remain scalar-equivalent. V28 adds a
  separate exact whole-archetype live-destroy path: the phase chain retains its
  count, complete ordered entity coverage is validated before mutation, occupied
  chunks reset occupancy plus dense/exact history without eagerly clearing
  payload that creation always initializes, and the validated slots retire in
  original command order. V29 moves dead-row clearing to the row-publication
  boundary: removal only shrinks occupancy, while reservation explicitly resets
  entity identity, payload, enable defaults, and exact history before reuse.
  Scalar destroy also commits a caller-validated relocation and retirement
  without repeating pool liveness, generation, and location checks. Exact
  ordered partial suffixes validate backward against the store's real
  partial/full donor chain and shrink in bulk; numeric chunk IDs are never
  mistaken for physical adjacency. Incomplete, skipped, duplicate, stale,
  unordered, mixed-archetype, and sparse sets retain scalar destruction. A
  measured attempt to apply transition-style planned compaction to arbitrary
  partial destroy sets regressed both sparse and donor-tail cases and was
  removed. ECS 3.0.35 adds exact route-level packed-suffix transition planning
  above the 256-row grouped horizon. After whole-source precedence, it proves a
  complete homogeneous run against a store-owned forward cursor derived from
  the real donor tail, proves full destination capacity, streams trusted
  destination publication in bounded blocks, and shrinks the source tail once.
  Successful suffixes move no surviving donor; failed, scattered, stale,
  conflicting, or partial-capacity candidates fall back before mutation. The
  authoritative current strategy model lives in the root
  `ECS_STRUCTURAL_PLAYBACK.md`. ECS 3.1.0 adds per-participant arenas,
  deterministic work-segment merge, and typed component payloads without changing
  those packed strategies. Grouping across separated route runs, broader density/
  entropy-driven compaction remains later structural work and must be measured
  against this baseline. ECS 3.3.0 adds compiled prefab instantiation as one
  ordinary create-phase command without changing the 24-byte command or 40-byte
  slot.
- `VisitReadChunks` and `VisitWriteChunks` are the first system-facing execution
  path. They reuse an exact binding, walk only occupied partial/full chunk lists,
  and invoke constrained struct visitors with stack-only component/entity-index
  views. Enable words stay encapsulated behind checked row operations.
- The synchronous chunk barrier rejects nested visits, structural operations,
  scalar writes, enable writes, and world disposal until the visitor returns.
  A `finally` releases it on exceptions. Safe managed code cannot retain the
  view or its spans beyond the callback. ECS 3.1.0 reuses this binding/view ABI
  for parallel scheduled jobs rather than adding another view type.
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
- ECS 3.2.0 exposes the narrow public `EcsQueryFilter` bridge for required and
  excluded topology membership. Every generated job family plus handwritten
  1R1W and changed-row plans has a filtered preparation overload. Storage-free
  tags can select routes without becoming data columns or scheduler access
  hazards. Seal still compiles exact matching routes, so required/excluded
  filtering adds no per-row predicate to warmed traversal.
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
- ECS 3.0.36 exposes those same generated route/chunk loops through the public
  single-owner facade as serial chunk jobs. Opaque eight-byte typed job plans
  reuse the existing bounded prepared-plan table and are compiled at `Seal`;
  default, foreign, pre-runtime, disposed, and wrong-thread use is rejected once
  per invocation. Public struct-job interfaces and the internal visitor controls
  converge on the same constrained `Execute(in Chunk)` contract rather than
  adding a parallel callback framework. Stack-only public views expose only
  declared typed spans, enable operations, count, and the existing world-local
  entity-index span; no arena, store, column, enable-word, or `ChunkRow`
  capability escapes.
- ECS 3.1.0 compiles cold `SystemDefinition` registrations into a staged
  dependency/read-write-conflict graph over that same chunk ABI. Persistent
  system runtimes prepare struct jobs; static bounded partitions execute serially
  or on dedicated workers; the caller may participate; `RangeJob` shares the
  worker runtime; and `Auto` avoids waking workers below declared crossover
  thresholds. New generated 1R0W, 0R1W, and 3R0W families complete the fundamental
  access catalog without inventing false reads.
- ECS 3.1.1 hardens that scheduler without changing its execution architecture:
  generated spans no longer authorize arbitrary same-component random lookups;
  direct retained-facade access is rejected throughout `Prepare`/job/`Complete`;
  fixed chunk partitions may span archetype routes so the per-system maximum is
  real in fragmented worlds; and structural declarations are explicitly deferred
  capabilities rather than false in-graph hazards.
- ECS 3.1.2 closes the scheduler's remaining reproduced proof gaps without adding
  another execution model. Seal now places late-ready systems against every
  already-compiled same-stage conflict; registration is validate-then-commit;
  supplemental random mutation is serial unless the caller declares one precise
  partition-disjoint ownership policy; and its exceptional parallel path uses
  atomic enable-word and dense-version publication while generated chunk-owned
  writes remain non-atomic. A retained main structural arena is graph-gated, and
  structural batching identifies runs by command kind, phase, and numeric route
  rather than by a coincidentally equal numeric ID.
- Workers receive an expiring declaration-checked context for optional full
  `Entity` reconstruction, prepared random access, and one fixed structural lane.
  Per-work segments merge in stable graph/work order into the existing command
  executor. Seal-compiled source-independent `Add<T>`/replacement and `Remove<T>`
  routes use fixed unboxed payload arenas; worker-local deferred tokens cannot
  cross work segments. Pre/post barriers, cadence, manual execution, lifecycle,
  terminal fault transactions, parallel change publication, snapshots, and
  synchronous worker shutdown are specified in
  [`ECS_SYSTEM_SCHEDULER.md`](../../ECS_SYSTEM_SCHEDULER.md). The complete
  3.1.0 admission evidence and full V2/V3 table are in
  [`ECS_3_1_0_ACCEPTANCE_REPORT.md`](../../ECS_3_1_0_ACCEPTANCE_REPORT.md); the
  audit findings, parity repair, and 3.1.1 gates are in
  [`ECS_3_1_1_HARDENING_REPORT.md`](../../ECS_3_1_1_HARDENING_REPORT.md); the
  scheduler/structural proof consolidation and measured 3.1.2 admission are in
  [`ECS_3_1_2_HARDENING_REPORT.md`](../../ECS_3_1_2_HARDENING_REPORT.md).
- Writable public generated families have an explicit dense/conditional split.
  `ExecuteJob` retains conservative pre-callback dense publication and its direct
  spans. `ExecuteTrackedJob` exposes `TrackedWritableComponentChunk<T>`:
  `DenseSpan` publishes one exposed range, while `GetTracked`, `SetTracked`, and
  enable mutation publish exactly touched rows. The first actual tracked write
  lazily acquires one version shared across the invocation and stamps the dense
  chunk gate at most once per writable column/chunk; a no-write conditional job
  does not advance the version. No temporary bitmap, dirty-index list, sort,
  plan slot, or persistent reservation was added.
- `ChangedRowsJobPlan<TRead,TChanged>` and `ExecuteChangedRowsJob` are the first
  public exact changed-row consumer surface. They preserve independent cursors,
  success-only acknowledgement, exception retry, and the existing contiguous
  range-per-run delivery. Public two-watch any/all authoring,
  mask-bearing delivery, additional access arities, and full entity
  materialization remain measured follow-ons. The concise contract and admission
  evidence live in [`ECS_JOB_EXECUTION.md`](../../ECS_JOB_EXECUTION.md).
- ECS 3.2.0 adds one optional fixed-capacity lifecycle observation machine above
  successful logical commits. Its 24-byte records form one globally ordered
  timeline of `Created`, `Destroyed`, `ComponentAdded`, and `ComponentRemoved`
  facts. Independent 16-byte cursors drain into caller spans, partial reads
  advance only delivered records, and retention loss is explicit
  `ResyncRequired`. Creation/destruction do not emit membership storms;
  transitions emit stable removed-then-added signature deltas; rejected commands,
  value/enable writes, donor moves, and record/merge alone emit nothing. The
  scalar oracle and optimized whole/suffix/grouped strategies publish identical
  logical sequences. See [`ECS_LIFECYCLE_PUBLICATION.md`](ECS_LIFECYCLE_PUBLICATION.md).
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
- Runtime handles are not durable IDs and are never serialized as identity.
  ECS 3.4.1 uses a fixed optional side table of monotonic 64-bit
  `PersistentEntityId` values and remaps internal references through dense
  32-bit save ordinals after every restored entity has been allocated.
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
  `WorldCapacityPlanner` produces a preconstruction estimate from archetype
  chunks, fixed entity metadata, and caller-supplied cold reservation budgets.
  After Seal, `WorldReservedPayloadSnapshot` mechanically reports the exact
  world-owned payload categories known to the runtime: entity metadata, store
  payload/metadata, route tables, change metadata, the main structural arena,
  and scheduler graph/worker/payload arenas. The main arena has one owner and is
  reported even when scheduling is disabled. CLR headers, thread objects, OS
  stacks/event internals, schema objects, and nested cold-plan arrays remain
  explicit exclusions rather than being hidden inside an ambiguous total.
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
- Scalar and bulk adds explicitly reset every entity-index/data row and exact
  row history. Enableable components and tags start enabled. Swap removal works
  within or across chunks, preserves all live data, enable states, and history,
  leaves its inaccessible donor tail lazy, rejects an expected-entity mismatch,
  and returns the donor's relocation for the entity-location table. Tests prove
  that no stale identity, payload, enable bit, or history is observable on reuse.
- Executable layouts are revalidated once before use. Hot access can bind a
  physical column index once, avoiding repeated component metadata scans.

`EntityPool` is intentionally single-owner. Parallel systems will submit
structural work through bounded per-worker command arenas; they will not mutate
the live pool concurrently.

The current runtime and primitive suite has 218 passing tests, plus 42 focused
scheduler tests, including
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
suite has 23 passing tests. Composed-world tests cover lifecycle ordering,
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
and acknowledgement without migration-generated replay. Planned-compaction tests
cover the scalar crossover, donor-tail overlap, whole-chunk evacuation, survivor
state/enable/exact-history relocation, backend parity, transactional batch
location rejection, and warmed zero-allocation grouped playback.
Whole/tail-destroy tests add multi-chunk scalar-oracle parity, stale and duplicate
sparse fallback, a false-tail candidate that skips packed chunks, exact
generation/free-list reuse ordering, survivor history preservation, initialized
default state after lazy payload reuse, and 256,000 warmed zero-allocation
operations.
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
250,000-transition warmed zero-allocation gate. Grouped-transition tests add
Native/Pinned parity for retained values, enable state, defaults, and exact row
history through donor-heavy migration; cross-chunk anonymous-tail rollback;
precise partial-capacity prefix behavior; a route run beyond the 256-entity
block boundary with duplicates, stale generations, wrong-source targets, and
capacity pressure; and a warmed 256-command zero-allocation barrier.
The contiguous-transfer layer adds independent source/destination chunk-boundary
splits over a 160-entity enable pattern, dense-to-exact and exact-to-exact history
transfer, and scalar-reference enable-range copies across offsets and lengths
around every 64-bit word boundary.

This is now a usable public single-owner component/archetype ECS with an optional
bounded system scheduler, compiled content, lifecycle observation, and
whole-world transactional persistence. The current generators emit the core manifest, a
systematic catalog containing 1R0W, 0R1W, 1R1W, 2R0W, 3R0W, 2R1W, 1R2W, 3R1W,
and 2R2W access families,
and the first 2R2C any/all changed family. Further access/watch arities should be
added only when real system signatures require them. Occupancy/density
measurements may show that some shapes should remain dense or bypass change
refinement entirely. Incremental persistence, richer field/data-prefab
descriptors, lifecycle-to-host bridges, system fusion, and networking integration
remain later work.

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
