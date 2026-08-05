# UltraSim ECS Next: Feature Inventory, Rebuild Specification, and Validation Plan

Status: active implementation specification; Phase 1 in progress, executable archetype-row prototype implemented 2026-08-04
Scope: ECS kernel, prefabs, persistence, scripting seams, networking seams, engine-agnostic server/client host seams, wrapper integration, inspection, tests, benchmarks, and migration tooling  
Primary objective: maximum practical throughput and minimum tail latency with zero steady-state ECS allocation after world sealing

## 1. Purpose

This document defines how UltraSim's next ECS should be built from scratch while retaining the useful behavior and integration seams of the current ECS. Public APIs are not compatibility requirements. Correct behavior, data compatibility where valuable, test coverage, and measured performance are the compatibility requirements.

The design intentionally favors:

- zero managed allocation in steady-state simulation;
- predictable memory use;
- low cycles per entity;
- low p99 and maximum tick latency;
- cache-friendly sequential access;
- coarse, efficient parallel work;
- deterministic structural barriers;
- generated, strongly typed hot paths;
- direct integration with replication and persistence without random gather passes;
- explicit failure and capacity behavior.

Tests and benchmarks are part of the design, not work postponed until the design is complete. Every implementation slice should add or port tests first, add a comparable benchmark where performance matters, and compare Legacy, Current, and Next while the feature is taking shape.

The expected direction in this document is intentionally strong, but it is not sacred. Tests, disassembly, hardware counters, allocation traces, soak results, and production-shaped replays may show that a proposed layout or algorithm is worse. When that happens, follow the evidence, document the change, and update the specification.

This specification also incorporates a repository-wide audit of all 37 Markdown files present at the time of writing, not only the ECS README files. Cross-kernel, server, scripting, client UI, content, inventory, persistence, and benchmark documents contain contracts that the ECS itself must not own but that its replacement must continue to serve. Section 13 records that audit and distinguishes implemented behavior from planned behavior.

Documentation is requirement evidence, not proof that a feature currently exists. When sources disagree, use this order:

1. executable code and focused tests;
2. fresh benchmark results and launch smokes;
3. current subsystem README status sections;
4. active implementation plans;
5. historical/old-system reference material.

Accordingly, a feature described as planned elsewhere is a Next design input, not part of the Current feature inventory unless code/tests were also verified.

### 1.1 Active implementation status

Phase 1 is active in root `Kernels/ECS`. The implemented bounded slices now include:

- transient process-unique `WorldId` values with no retained global world registry and no wrap/reuse;
- an initial 12-byte world/index/generation `Entity` layout;
- fixed-capacity, preallocated, single-owner entity-slot allocation;
- explicit capacity failure, stale/foreign rejection, and generation-wrap slot retirement;
- scalar and span-based bulk allocation/free paths;
- source-generated closed component manifests using `[EcsComponent]` and `[EcsSchema]` declarations;
- deterministic dense `ComponentId` assignment by canonical key, independent of declaration order;
- deterministic 128-bit `SchemaId` fingerprints covering canonical identity, version, flags, persistence identity, type identity, and field signature;
- exact runtime size/alignment capture, zero-storage tags, enableable flags, and nonzero stable persistence IDs;
- a compact 16-byte hot `ComponentMetadata` record with names, stable hashes, and runtime type handles kept in cold parallel storage;
- immutable instance-owned `ComponentSchema` objects with direct metadata-by-ID access, a generated generic ID path, and allocation-free type/key/hash/persistence lookup;
- linear lookup for schemas of eight or fewer components and bounded open-address indexes above that threshold, avoiding dictionaries, boxing, and quadratic construction for larger schemas;
- an allocation-free `ChunkLayoutPlanner` with checked arithmetic, caller-provided output spans, deterministic physical column ordering, 64-bit enable masks, explicit alignment/padding/tail accounting, and binary-searched maximum row capacity;
- `ArchetypeCapacityPlan` population accounting and `WorldCapacityPlanner` aggregation of chunk, entity-metadata, structural-command, change-tracking, and scratch reservations;
- embedded narrow/normal/wide layout facts for 16/32/64 KiB candidates in every Phase 1 benchmark artifact, including physical column offsets and 100k-population chunk reservations;
- fixed-capacity native-aligned and pinned-managed chunk-arena candidates sharing a 12-byte arena/index/generation handle, preallocated slot state, O(1) acquisition/recycling, stale/foreign rejection, and terminal generation retirement;
- clear-on-acquire chunk reuse plus validated, allocation-free typed views over local entity indices, ordinary SoA columns, and enable-word columns;
- explicit arena disposal and cold payload/released-byte/slot/high-water accounting, with an unmanaged finalizer only as a leak safety net rather than the ownership contract;
- an internal executable `ArchetypeChunkStore<TArena>` over both storage candidates, with exclusive arena ownership, a compact per-chunk metadata record, intrusive O(1) full/partial/empty lists, lazy first acquisition, and retained-empty reuse;
- an eight-byte chunk-index/row location candidate, dense packed occupancy with at most one partial chunk, scalar and true contiguous-range bulk row allocation, and explicit zero-default component initialization;
- enableable components and tags that begin enabled, preserve their state through swap removal, and clear dead-row bits; prebound physical column indices avoid repeated metadata scans on hot access;
- within- and cross-chunk swap removal that copies every data column and enable bit, clears the donor row, requires the expected local entity index to reject stale locations, and reports the moved entity's old/new location to the entity-location table;
- a fixed-capacity instance-owned archetype registry with canonical sorted full-signature identity, stable hash acceleration, collision-safe open addressing, dense four-byte IDs, exact arena/store ownership, an explicit seal boundary, concurrent sealed reads, and allocation-free signature lookup/ID routing;
- cold-built immutable archetype transition plans that merge canonical signatures once, validate retained physical storage, and prebind exact source/destination column pairs;
- allocation-free transactional cross-archetype migration that preserves retained data and enable state, keeps added fields at zero/enabled defaults, drops removed state, repairs source swap-donor locations, leaves the source untouched on destination-capacity failure, and rolls back later rejected steps;
- a minimal internal instance-owned world composition root with process-unique identity, fixed entity slots, owned registry/stores/plans, an explicit created/sealed/disposed lifecycle, checked immediate create/transition/destroy operations, capacity-atomic creation, stale-handle invalidation, donor repair, and O(1) rejection of foreign registry plans even when dense archetype IDs match;
- a compact 16-byte archetype-specific component binding built from source-generated IDs before sealing, with one-time schema type/flag/size/membership/layout validation and allocation-free entity-level presence, copied read/write, enable-state, and initialized-create operations that never expose a mutable ref;
- cold executable-layout validation covering range overlap, reserved-header intrusion, alignment, tag/data disagreement, duplicate IDs, and exact data/enable accounting before any raw storage path is accepted;
- compile-time diagnostics for malformed schema/component declarations, invalid keys/versions, field-bearing tags, duplicate types/keys/nonzero persistence IDs, and exhaustion of the reserved `ushort` ID space;
- `86` executable runtime/primitive tests, including warmed zero-allocation assertions, indexed-schema tests, defensive construction tests, planner boundary/invariant/overflow tests, randomized brute-force layout parity, randomized chunk-slot lifecycle parity, native/pinned randomized archetype reference-model parity, scalar/bulk and byte parity, within/cross-chunk relocation, entity-location binding and donor updates, an integrated randomized pool/store model, canonical registry collision/capacity/ownership/concurrent-read tests, transactional bidirectional migration and donor relocation, destination-capacity rollback, retained/default/removed state, composed-world lifecycle/capacity/generation/disposal/foreign-plan tests, randomized world and bound-access models, 16-byte binding layout and misuse tests, generated-ID/type/membership validation, exact-archetype/stale/foreign component access, initialized creation, enabled-default/state preservation, retained-empty reuse, alignment/range/type rejection, disposal/accounting, terminal retirement, capacity accounting, backend equivalence, and concurrency/isolation checks;
- `13` executable Roslyn generator tests covering exact checked-in golden output, generated compilation, order determinism, schema evolution, every current diagnostic, unique persistence acceptance, and the full `65,536`-entry overflow boundary;
- `44` Next-only primitive/layout/schema/capacity/arena/archetype-row/registry/migration/world-lifecycle/bound-access benchmark scenarios with JSON output, natural GC reporting, focused scenario filtering, and explicit non-comparable status until equivalent Current adapters exist.

The first local run favored direct 12-byte field access over the packed 8-byte candidate at both one million and ten million handles, while every scenario measured zero managed bytes. That is a hypothesis-generating result, not an accepted layout decision: both representations still need chunk traversal, entity-aware query, metadata-cache, memory-footprint, and repeated isolated-process measurements.

The first layout-planner run also remained allocation/collection free. At a projected 100,000 entities, the 32 KiB candidate reserved the fewest bytes for the narrow and normal fixtures (`1,212,416` and `2,064,384` bytes), while 64 KiB reserved the fewest for the wide fixture (`8,519,680` bytes). The margins are small and exclude actual traversal/cache/TLB behavior, so 32 KiB is only the initial general-purpose arena prototype candidate; 16 and 64 KiB remain in the executable matrix.

The first arena run compared equivalent native-aligned and pinned-managed storage at 16, 32, and 64 KiB. It measured zero managed bytes and zero Gen0/1/2 collections in all twelve new arena scenarios. Paired lifecycle/clear/traversal means differed by no more than roughly three and a half percent on that run: too close to select a winner. Native remains the expected server direction because its lifetime and footprint are independent of the managed heap, while pinned storage remains an executable engine/platform candidate. Repeated isolated processes, server-scale retained footprints, induced managed-heap pressure, first-touch/NUMA behavior, cache/TLB counters, and disposal/leak soaks must decide. The current arena types are internal because a public raw typed-span API could request a same-sized but semantically wrong component; generated archetype/query bindings must own that mapping before exposure.

The first row-store measurements also allocated zero managed bytes and caused no measured Gen0/1/2 collection. Bulk default creation of 100,000 rows is approximately `1.3-1.5 ns/row` for both backends and remains order/noise sensitive. Three exact-filter isolated runs measured native swap-remove-plus-add at approximately `64.6-67.8 ns/cycle` and pinned at `68.6-75.9 ns/cycle`; this is directional until Current has an equivalent adapter. Prebinding an enableable component's physical column index reduced the native toggle path from roughly `13.2` to `11.3 ns` and the pinned path from roughly `17.0` to `14-16 ns`, directly supporting generated/prebound access. Whole-suite native swap p95 spikes disappeared in exact-filter processes, demonstrating why scenario-order and OS-preemption noise must not be mistaken for a storage-backend tail-latency property.

The local Ollama/Qwen review workflow lives in `_Tools/LocalAI`. It is used for bounded adversarial architecture, patch, and benchmark reviews; its output remains untrusted until verified by source, tests, or measurements. Early reviews usefully exposed the need to make world-ID allocation process-unique, to test slot retirement through the pool, to reserve `ComponentId` value `0xFFFF`, and to remove quadratic large-schema validation. They also demonstrated why model suggestions must be filtered: proposals such as dynamically growing the fixed free list, reclaiming retired generations, or unconditionally replacing tiny-schema scans with managed dictionaries contradict measured or bounded-memory goals.

The generated manifest, registry, arenas, row stores, transition executor, minimal internal world, and archetype-specific entity component bindings now form an executable bounded lifecycle, but not yet a usable public scheduler/host ECS. Field IDs/descriptors, serializers/schema migrations, prefab writers, barrier-scoped generated chunk/query views, query/system metadata, public initialized/running/faulted lifecycle, and structural command arenas are not implemented yet. The entity pool validates a runtime `Entity` generation before exposing its 12-byte archetype/chunk/row route; the location intentionally does not duplicate entity generation.

## 2. Generation Names and Comparison Policy

The project has three useful ECS generations:

| Name in this document | Location or intended location | Role |
| --- | --- | --- |
| Legacy | `_V2/_OLD/UltraSim/ECS` | Historical implementation and existing old benchmark baseline, retained inside the V2 snapshot |
| Current | `_V2/Kernels/ECS` | Frozen V2 functional reference, integration reference, and primary comparison baseline |
| Next | `Kernels/ECS` | New implementation described here, built directly in the normal root location |

The repository move makes the generation boundary physical: `_V2/` contains both Legacy and Current, while unprefixed root paths belong to Next. Thus, an unprefixed `Kernels/ECS`, `Server/...`, `Client/...`, `_Tests/...`, or `_Benchmarks/...` path in a proposed-layout section means Next; a Current source, test, tool, document, or artifact path must start with `_V2/`.

Legacy and Current must remain runnable during Next development. Do not rewrite the functional baselines while using them as performance references. Corrections required to make relocated benchmark projects compile, execute, or write into their own V2 result tree are allowed, but must be narrowly recorded and followed by a new baseline artifact rather than silently treating an older artifact as post-repair output.

Relocation verification on 2026-07-21 confirmed that all `11` projects beneath `_V2/_Benchmarks` build in Release, the full `77`-scenario Legacy and `80`-scenario V2 Current ECS runners complete, and `_V2/_Benchmarks/UltraSim.ECS.Compare` consumes their fresh JSON successfully. The V2 phase-stagger and scheduler-feature report modes also complete, and a bounded four-scenario soak smoke produces valid JSON without a collection during the measured one-second windows. These checks prove runner integrity after relocation; their short/single-process measurements are not a replacement for a controlled accepted baseline.

The existing separate-process benchmark approach is appropriate because Legacy and Current may have conflicting namespaces and types. Add a third Next runner and let the comparison tool join the three JSON reports by stable scenario ID.

Comparison priorities are:

1. Next versus Current: primary engineering comparison.
2. Next versus Legacy: historical comparison and regression guard.
3. Current versus Legacy: retained to detect benchmark drift.

Behavioral parity means parity with Current only where Current behavior is correct. The following are deliberate non-parity cases and must have tests proving the corrected behavior:

- default batch creation must never reuse stale component bytes;
- scheduler dependencies must be honored in parallel execution;
- external callers must not mutate entity arrays or component storage outside tracked write scopes;
- partial dirty/change reads must not discard unread changes;
- removals and destruction must produce lifecycle tombstones;
- component lookup must remain correct across multiple concurrent schemas/worlds;
- persistence restore must be bounded and transactional;
- failed prefab or structural application must not leave an undocumented partial world;
- failed runtime content-group unmount must not leave a group marked mounted after partially removing its owned registrations;
- runtime entity handles must not masquerade as durable cross-world identities.

Tests should encode each non-parity decision before the corresponding Next subsystem is considered complete.

## 3. Non-Negotiable Targets

### 3.1 Correctness targets

- Stale entity handles never resolve after slot reuse.
- Entity handles from one world never resolve in another world.
- Every structural transition preserves all retained components exactly.
- Newly created default components are bitwise defaulted.
- Declared scheduler dependencies hold in serial and parallel modes.
- No public or extension write path bypasses change tracking.
- Dense and sparse change tracking report exactly the intended rows.
- Multiple consumers may independently observe changes.
- Component removal and entity destruction are observable.
- Structural barriers have documented deterministic ordering.
- Prefab failure is atomic for the spawned entity or explicitly transactional for the batch.
- Content-generation publication and content-group mount/unmount are atomic across definition registries, or execute a tested compensating rollback before returning failure.
- Persistence input is bounded before allocation and validated before world mutation.
- Restore either commits completely or leaves the target unchanged.
- World execution is non-reentrant and has explicit fault behavior.

Every target above requires direct unit tests, randomized/model-based tests where applicable, and at least one regression test named for the failure it prevents.

### 3.2 Allocation and memory targets

- After `World.Seal()` and warmup, core simulation ticks allocate exactly `0` managed bytes within configured capacity.
- Scheduled serial and parallel execution allocate exactly `0` managed bytes per tick.
- Dense and sparse write tracking allocate exactly `0` managed bytes per tick.
- Structural barriers allocate exactly `0` managed bytes while command counts remain within configured capacity.
- Prefab spawning allocates exactly `0` managed bytes after prefab binding.
- Replication extraction allocates exactly `0` ECS-side managed bytes with caller-provided/preallocated output storage.
- Persistence capture should avoid boxing and per-component object lists. Asynchronous file/codec layers may allocate only where explicitly measured and accepted.
- Native memory, retained managed memory, chunk high-water counts, and scratch high-water counts are observable.
- Capacity overflow is explicit: fail fast, reject the command, or enter a separately measured overflow mode. It never silently triggers an unbounded allocation.

Allocation tests should run after warmup and assert deltas over hundreds or thousands of ticks. Soak tests should verify that the allocation rate remains flat over time and that native arenas do not grow unexpectedly.

### 3.3 Performance targets

- Next must meet or beat Current in designated hot scenarios before cutover.
- A performance win cannot hide a correctness mismatch; canonical world-state comparison is required alongside checksums.
- Lightweight workloads should remain serial when parallel dispatch is slower.
- Heavy workloads should scale across configured workers without excessive atomics, wake latency, or false sharing.
- p99 and maximum tick time matter at least as much as mean throughput.
- Optimization claims require repeatable Release measurements with environment metadata.

Initial gates should be strict but revisable based on evidence. A suggested starting policy is no more than a 5% regression against Current in a designated parity scenario and a required material win in dirty writes, structural churn, allocation, and end-to-end replication. Statistical noise must be considered before accepting or rejecting a change.

## 4. Current ECS Feature Inventory

This section records functionality that exists today and should either be retained as behavior, replaced by a faster equivalent, or explicitly rejected by a tested design decision.

### 4.1 Kernel and ownership model

Current features:

- engine-agnostic .NET kernel;
- instance-owned `EcsSchema`;
- instance-owned `World`;
- optional instance-owned prefab resolver and persistence adapter through `EcsWorldServices`;
- explicit lifecycle states: created, sealed, initialized, and shutdown;
- automatic sealing/initialization when ticking;
- world tick counter;
- explicit shutdown;
- no intended process-global mutable registry.

Next requirements:

- retain engine independence and explicit ownership;
- make the world disposable because it will own workers and possibly native arenas;
- add world identity to runtime handles;
- add a faulted state and explicit recovery/disposal rules;
- make execution non-reentrant;
- make `Seal()` the allocation boundary and plan-compilation boundary;
- make capacity configuration first-class;
- retain extension seams without putting transport, UI, or storage policy in the kernel.

Tests:

- lifecycle transition tests;
- use-after-dispose tests;
- double-dispose tests;
- tick reentrancy tests;
- concurrent tick rejection tests;
- faulted-world behavior tests;
- capacity-overflow policy tests;
- multi-world isolation and cross-world handle rejection tests.

### 4.2 Components and schema

Current features:

- unmanaged components implementing `IComponent<TSelf>`;
- sequential component IDs assigned during schema build;
- duplicate registration returns the existing ID;
- builder freezes after build;
- runtime type-to-ID lookup;
- component metadata containing CLR type, column factory, and copier;
- reflection-based registration for runtime `Type` values;
- static generic component ID cache intended to accelerate typed lookup;
- component masks used by scheduler conflict analysis;
- archetype signatures represented by component bitsets.

Next requirements:

- generate canonical IDs, sizes, alignment, tag status, persistence IDs, and serializer metadata at build time;
- generate schema-specific tables rather than using process-global mutable caches;
- permit a cold dynamic registration path only if scripting truly needs component types unknown at build time;
- normalize and validate every signature;
- make signatures compact value types or stable generated IDs rather than heap objects used as dictionary keys;
- represent zero-sized tags without allocating data columns;
- support enableable components/tags for high-frequency toggles;
- generate scheduler masks and query descriptors from declared system access;
- retain multiple isolated world/schema instances without races.

Tests:

- stable generated ID snapshot tests;
- duplicate and invalid component tests;
- schema isolation tests running concurrently;
- tag/no-column tests;
- enable/disable tests;
- signature canonicalization property tests;
- generated metadata reflection-equivalence tests during migration;
- tests proving no schema lookup or metadata allocation occurs during a hot tick.

Expected direction:

Use source generation for all production components. Keep reflection only in tooling/bootstrap and verify generated output against reflection in tests until confidence is high. If measurements show a generated static schema harms a required dynamic scripting workflow, retain a slower dynamic world class as a separate mode rather than penalizing every production world.

### 4.3 Entities and identity

Current features:

- 64-bit packed entity value;
- 32-bit index and version/generation;
- null entity value;
- entity allocation and bulk allocation;
- free-list slot reuse;
- version increment on free;
- alive/valid checks;
- entity-to-archetype/slot location table;
- bulk location updates;
- stale-handle tests and swap-relocation tests.

Next requirements:

- retain compact generational runtime handles;
- include or validate a world identifier;
- pack runtime metadata to minimize cache footprint;
- prefer a dense integer chunk ID plus row over an object reference plus row;
- validate generation and location with as few memory reads as practical;
- support bulk allocation directly into destination chunk entity spans;
- retain a preallocated free-index stack;
- separate transient runtime identity from durable entity identity;
- provide explicit durable-ID-to-runtime-handle remapping during restore;
- never serialize a runtime packed handle as a durable reference.

Tests:

- stale handle after millions of reuse cycles;
- generation wrap policy;
- cross-world rejection;
- bulk allocation/free parity with scalar allocation;
- random entity model tests;
- packed metadata bit-boundary tests;
- durable ID remap tests;
- inventory/runtime reference resolution tests before and after restore.

Benchmarks:

- allocate/free scalar and bulk;
- alive/valid checks for live, dead, stale, and foreign handles;
- random component lookup with hot and cold metadata;
- free-list churn at 1k, 10k, 100k, and 1m entities;
- entity metadata cache-miss and memory-footprint comparisons.

### 4.4 Archetypes and component storage

Current features:

- archetypes keyed by normalized component signatures;
- structure-of-arrays component columns;
- dense entity rows;
- swap removal;
- dynamic array growth;
- empty archetype;
- component-ID-to-column mapping;
- transition-plan caching for add/remove;
- component copier delegates bound into transition steps;
- broad query-plan matching by archetype signature.

Next requirements:

- fixed-size/paged chunks rather than one growing array per archetype;
- benchmarked chunk payload target, initially testing 16, 32, and 64 KiB;
- 64-byte alignment for hot columns where beneficial;
- unmanaged or pinned slab allocation selected by measurements, with unmanaged arenas as the expected high-performance path;
- archetype-local full/partial/empty chunk lists;
- O(1) chunk acquisition and recycling;
- stable dense archetype and chunk IDs;
- generated component offsets/typed access;
- no runtime `is ComponentColumn<T>` checks in generated queries;
- no whole-archetype copy when capacity grows;
- optional chunk partition keys for spatial/ownership locality;
- bulk row allocation and bulk default initialization;
- explicit clearing of every default-created row;
- storage-free tags and enable bitsets.

Tests:

- every chunk capacity boundary;
- partially full and completely full chunks;
- chunk acquisition/recycling;
- swap remove within and across chunks;
- default initialization after churn;
- migration preserving all retained components;
- tag and enable bit behavior;
- alignment assertions;
- native allocation/disposal accounting;
- randomized operations across hundreds of archetypes;
- canonical snapshot comparison against Current where Current behavior is sound.

Benchmarks:

- sequential scans across component sizes and chunk sizes;
- many small archetypes versus one large archetype;
- fragmentation and sparse occupancy;
- chunk growth without full-archetype relocation;
- cache and TLB behavior at 100k and 1m entities;
- memory bytes per entity by signature;
- managed arrays versus pinned arrays versus native aligned slabs.

Expected direction:

Fixed-size chunks are the default hypothesis, not an untouchable conclusion. Tests may show that tiny worlds benefit from an inline/single-page representation or that a different chunk size is best for particular signatures. A hybrid is acceptable if it remains generated, predictable, and allocation-free after sealing.

Implemented evidence as of 2026-08-04: the native and pinned candidates share identical fixed geometry and slot semantics, guarantee 64-byte-aligned bases for the current matrix, and pass typed byte-equivalence and zero-allocation tests. The generic executable row store owns row counts, full/partial/empty state, retained-empty reuse, bulk initialization, enable defaults, and swap removal over either backend. A preallocated entity-location table binds live generations to dense archetype IDs and chunk rows, prevents freeing located slots, and applies checked donor relocations without managed allocation. The sealed registry maps canonical signatures to those IDs and owned stores with collision-safe allocation-free lookup. Cold-built transition plans prebind retained physical columns, and the migration transaction reserves the destination before publishing a new location or removing the source. It preserves retained values and enable bits, keeps additions at their default state, discards removals, and repairs source donor locations. The minimal internal world now composes these operations behind a sealed lifecycle and rejects foreign plans by registry/store identity rather than collision-prone dense IDs alone. Its 16-byte component binding similarly validates generated type/layout membership once, then provides copied entity access without metadata scans or ref escape. The first focused registry guard measured roughly `11.5 ns` per 16-signature lookup and `1.07 ns` per ID route with zero allocation. Exact-filter 100,000-operation migration runs measured approximately `76.9 ns/move` native and `78.5 ns/move` pinned; complete create-transition-destroy cycles measured `149.3 ns/cycle` native and `149.4 ns/cycle` pinned. Final exact-filter runs of a four-call bound set/get/set-enabled/get-enabled round measured `51.0 ns` native and `52.6 ns` pinned. All guards allocated zero managed bytes. Slower paired pinned tails did not reproduce in isolation and are treated as process/scenario interference. This satisfies fixed-chunk, location-ownership, archetype-registration, primitive cross-archetype migration, minimal immediate world-lifecycle, and safe entity component-access requirements, not the final backend or public world-storage gate: barrier-scoped generated chunk/query bindings, scheduler-owned mutation barriers, and Current differential adapters remain outstanding.

### 4.5 Read, lookup, and query features

Current features:

- `GetComponent`, `TryGetComponent`, and `HasComponent`;
- explicit `SetComponent`, `PatchComponent`, and `SetOrAddComponent`;
- one-, two-, and three-component read queries;
- struct visitor fast path for one-component reads;
- read-only chunk callbacks;
- dirty-aware patch queries;
- dense writable chunk callbacks;
- sparse writable chunk callbacks with dirty index output;
- prepared patch-plan cache objects;
- parallel chunk patch paths;
- query chunk enumerators;
- archetype-level traversal;
- query cache invalidation when archetypes change.

Next requirements:

- generated typed query plans with direct typed storage access;
- generic struct jobs or generated system bodies instead of delegate callbacks;
- no closure allocation and no delegate invocation in entity loops;
- query arities sufficient for actual systems, generated rather than handwritten;
- required, excluded, optional, enabled, disabled, and changed-since filters;
- read-only and writable access encoded in the query type;
- safe arbitrary-width query support without exposing raw mutable archetypes;
- no external mutation of entity handle storage;
- no public mutable component span without a tracked write scope;
- random-access `ComponentLookup<T>` cached per tick for workloads that cannot query sequentially;
- query plans compiled at seal time and incrementally extended only if runtime archetype creation is allowed;
- queries that omit entity handles when the job does not use them;
- dense writes mark chunk versions once;
- sparse writes return/merge bitsets, not per-row integer arrays.

Tests:

- matching for every filter combination;
- optional component presence/absence;
- enableable filtering;
- changed-since consumer versions;
- read/write alias rejection;
- no untracked public write path tests;
- generated query output compared with a slow reference scan;
- scalar versus chunk versus parallel parity;
- empty, one-row, boundary, and multi-chunk cases;
- query plan update tests if dynamic archetypes remain supported;
- allocation assertions around repeated queries;
- tests that generated jobs do not box struct state.

Benchmarks:

- one through eight component reads;
- one through four writes where representative;
- exclude/optional/enable/change filters;
- entity-free versus entity-aware iteration;
- delegate Current path versus struct-job Next path;
- one archetype versus 1k matching/nonmatching archetypes;
- random lookup versus sequential query crossover;
- scalar, auto-vectorized, and explicit SIMD kernels;
- plan warm and cold measurements reported separately.

### 4.6 Structural commands and transitions

Current features:

- immediate scalar create and destroy;
- deferred batch create and destroy;
- immediate internal add/remove;
- deferred typed add/remove;
- repeated-value add batches;
- prefab commands;
- thread-safe shared structural queue;
- double-buffered lists;
- deterministic broad phase order;
- whole-archetype and ordered-same-source destroy fast paths;
- destination reservation;
- cached add/remove transitions;
- structural statistics in world profiles.

Next requirements:

- one fixed-capacity command arena per worker plus one main-thread arena;
- single-writer append with no locks or atomics;
- unmanaged fixed-layout command records;
- double-buffered or epoch-reset pages;
- no per-barrier `List`, `Dictionary`, `ToArray`, or shared `ArrayPool` operation;
- commands grouped by operation, source archetype, and destination transition;
- dense archetype-indexed scratch counts plus touched-index lists;
- bulk destination allocation;
- bulk component copy and source compaction;
- explicit conflict policy for contradictory commands in one barrier;
- deterministic merge order independent of thread lock acquisition;
- exception/fault policy that never silently strands commands;
- transactional prefab spawn behavior;
- enableable tags for common toggles to avoid migration;
- optional structural budgets per tick to protect latency.

Initial barrier order should retain the current semantic shape unless tests justify a change:

1. destroy;
2. create default;
3. create prefab;
4. remove;
5. add.

The exact ordering must be documented and tested for conflicts such as destroy-plus-add, remove-plus-add, duplicate destroy, and multiple writes to the same component.

Tests:

- scalar/batch parity;
- mixed command ordering;
- parallel producer determinism;
- duplicate and contradictory commands;
- command capacity boundaries;
- overflow policy;
- failure injection during each phase;
- no stale values after default creation;
- migration preservation across every generated transition;
- random command replay against a reference model;
- structural queue allocation tests;
- long-running chunk fragmentation tests.

Benchmarks:

- homogeneous and mixed command streams;
- sorted and randomized entity order;
- one source versus many sources;
- whole-chunk destroy;
- sparse destroy;
- add/remove cycles;
- enable-bit toggle versus archetype migration;
- 1, 2, 4, physical-core, and logical-core producer counts;
- barrier p50/p99/max under command bursts;
- fixed-capacity normal mode and explicitly measured overflow mode.

### 4.7 Change tracking and dirty state

Current features:

- global dirty set;
- per-component dirty sets;
- epoch-based entity-index deduplication;
- copy and destructive drain APIs;
- thread-local append buffers during parallel capture;
- touched-component tracking for merge reduction;
- dense unique-range marks;
- sparse dirty index marks;
- dirty retention diagnostics.

Next requirements:

- per-chunk/per-component change versions for dense writes;
- per-chunk sparse row bitsets when exact rows matter;
- per-consumer cursors or last-seen versions;
- fixed lifecycle event rings for created, component-added, component-removed, and destroyed events;
- independent consumers without destructive shared drains;
- no duplicated global dirty stream;
- no random entity-index deduplication for dense chunk writes;
- no lost remainder on partial reads;
- direct changed-column access for replication and persistence;
- explicit wrap handling for versions and cursors;
- backpressure and overflow policy for lifecycle rings.

Tests:

- dense version changes exactly once per written chunk;
- sparse bitsets mark exactly touched rows;
- multiple writes deduplicate correctly;
- multiple consumers progress independently;
- slow consumer behavior;
- partial output buffers retain unread changes;
- removal and destroy tombstones;
- cursor/version wrap;
- parallel bitset merge;
- consumer exception does not lose unacknowledged changes;
- zero-allocation repeated write/read cycles;
- canonical change stream comparison with reference events.

Benchmarks:

- dense writes at 1%, 10%, 35%, and 100% activity;
- sparse bitsets versus integer index buffers;
- one, two, and many consumers;
- drain/scan with and without value serialization;
- change tracking enabled versus disabled;
- version scan with many unchanged chunks;
- structural lifecycle event throughput;
- slow-consumer retention and memory ceiling.

### 4.8 Scheduler and systems

Current features:

- `SystemBase` lifecycle hooks;
- stages: Input, Movement, SpatialIndex, Perception, Combat, AI, WorldSim, Networking, and Cleanup;
- cadence period and start offset;
- automatic phase staggering for selected noncritical stages;
- read/write component masks;
- explicit `Requires` dependencies;
- dependency-cycle detection;
- manual systems;
- serial and parallel-batch modes;
- optional intra-system parallelism in serial mode;
- persistent manual worker threads;
- caller participates in parallel work;
- due-tick wrap handling;
- per-system timing and runtime snapshots;
- control descriptors for host/operator tooling.

Next requirements:

- generated, validated system graph;
- dependency edges treated as hard execution barriers;
- missing and impossible cross-stage dependencies rejected at build/seal time;
- generated read/write masks and query access where possible;
- one job graph rather than nested system-level and query-level dispatchers;
- fixed chunk jobs as the normal parallel unit;
- struct jobs/function pointers with no captured delegates;
- static range partitioning for uniform jobs;
- work stealing only for measured imbalance;
- persistent workers using sequence dispatch with spin-then-park behavior;
- cache-line-padded worker state;
- per-worker reductions followed by one merge, rather than atomics inside hot loops;
- adaptive serial/parallel thresholds based on measured workload cost;
- configurable worker budget that leaves resources for networking/runtime services;
- optional thread affinity and NUMA partitioning for known deployments;
- system fusion for local dependency pipelines that walk the same chunks;
- retained manual systems and cadence scheduling;
- retained phase staggering, but every default remains benchmark-driven;
- deterministic mode and high-throughput mode if their tradeoffs diverge;
- explicit fault propagation with system/job/chunk context.

Tests:

- stage ordering;
- cadence and offset behavior;
- tick wrap;
- dependency cycles;
- missing dependency rejection;
- cross-stage dependency validation;
- dependency ordering inside parallel execution;
- serial/parallel canonical world parity;
- deterministic command/event order across repeated parallel runs;
- worker shutdown and disposal;
- dispatcher capacity and reentrancy;
- fault injection in worker jobs;
- adaptive threshold decisions using a deterministic cost fixture;
- fused and unfused pipeline equivalence;
- allocation assertions for thousands of ticks.

Benchmarks:

- empty scheduler overhead;
- 1, 4, 16, 64, and hundreds of systems;
- trivial, memory-bound, branch-heavy, and compute-heavy jobs;
- serial/parallel crossover by entity and chunk count;
- worker scaling by physical and logical cores;
- static partition versus atomic work index versus work stealing;
- worker wake latency and barrier tail latency;
- fused versus unfused movement pipelines;
- cadence burst versus phase staggering;
- profiling disabled/enabled overhead;
- reductions using global atomics versus worker-local merge.

Expected direction:

The current benchmarks already show that parallel execution helps some heavy workloads and hurts some light workloads. Next should choose granularity per job or pipeline. Tests and repeated measurements may show that a simpler static scheduler beats a sophisticated adaptive scheduler; if so, choose the simpler measured winner.

### 4.9 Prefabs

#### Intended RunUO-style authoring model

The prefab layer is deliberately an object-oriented authoring facade over an ECS runtime. It is not merely a dictionary of serialized component blobs, and it is not intended to turn runtime entities into managed objects.

The conceptual mapping is:

| Author-facing concept | ECS runtime result |
| --- | --- |
| Base item/mobile class | A wide, inherited/composed archetype shape |
| Derived base class | The base component set plus additional components |
| Concrete item/mobile script | A spawnable prefab bound to one resolved archetype |
| Constructor/configuration values | Constant or per-spawn component initializers |
| Spawned script object | A generational ECS entity occupying a chunk row |
| Script inheritance | Load-time composition of component membership and defaults |
| Script add/remove/change | Recompile/reload definitions and publish a new immutable content generation |

The desired surface should feel familiar to RunUO content authors:

```csharp
public class BaseItem : ArchetypeShape
{
    protected override void Configure(ArchetypeBuilder shape)
    {
        shape.Add<ItemIdentity>();
        shape.Add<InventoryItem>();
    }
}

public class BaseWeapon : BaseItem
{
    protected override void Configure(ArchetypeBuilder shape)
    {
        base.Configure(shape);
        shape.Add<WeaponData>();
    }
}

public sealed class IronDagger : PrefabDefinition<BaseWeapon>
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.Set(new ItemIdentity(1001));
        prefab.Set(new InventoryItem(1, 2, 1));
        prefab.Set(new WeaponData(3, 7));
    }
}
```

This inheritance exists at authoring/definition time. At content-load time, it must collapse into:

```text
ArchetypeId + ChunkLayout + PrefabId + typed component initialization plan
```

There is no managed `IronDagger` object for every spawned dagger. The class instance is a simple definition-building tool. The spawned dagger is an ECS entity with components. Behavior remains composable through components and systems rather than per-entity virtual methods.

This separation is a central requirement: OOP simplicity at the content surface, ECS composition and contiguous data in the runtime.

Content authors may still need an instance-like surface for commands and rare event handling. Provide a transient, allocation-free entity facade/context that looks object-oriented but resolves to tracked ECS operations:

```csharp
public void OnUse(ref ItemContext self, in UseRequest request)
{
    ref readonly WeaponData weapon = ref self.Read<WeaponData>();
    self.Set(new Durability(weapon.Durability - 1));
    self.Add(new RecentlyUsed(request.Tick));
    self.Remove<Hidden>();
}
```

`ItemContext` is a stack-only/generated facade containing a validated entity handle plus restricted read/write/structural access. `Set` participates in change tracking; `Add` and `Remove` emit deferred structural commands. It must not be a persistent heap object and must not expose raw chunk/archetype storage. Rare script events may route to definition handlers through prebound numeric handler IDs, while high-volume behavior should compile or lower into ordinary batch systems. Tests must prove stale/foreign handles are rejected, writes are tracked, structural changes are deferred correctly, and repeated event dispatch allocates zero memory after binding.

If a script author declares per-instance state, that state must become a component, a bit/tag, or a handle into an explicitly managed blob store. It must not become an arbitrary field on a long-lived managed script object. This retains the useful mental model of item/mobile instances without surrendering ECS layout and lifetime control.

Single inheritance should not force deep inheritance trees for every reusable trait. The authoring layer should also support shape fragments/traits or explicit inclusion for horizontal composition, while resolving duplicate components deterministically. Tests should compare inherited and fragment-composed shapes and prove they compile to the same canonical signature when their component sets are equal.

Current features:

- typed `IPrefab<T>`;
- template `ITemplatePrefab<T>`;
- resolved-column `IResolvedPrefab<T>`;
- runtime `PrefabDefinition`;
- instance-owned `PrefabCatalog`;
- key-based resolver;
- immediate typed, definition, and key-based spawning;
- deferred scalar and batch spawning;
- template plus per-instance data;
- scripted definitions compiled into the same prefab catalog;
- `ArchetypeShape` classes whose `Configure` methods compose component membership;
- ordinary C# inheritance with `base.Configure(...)` to widen derived archetype shapes;
- concrete `PrefabDefinition<TArchetype>` classes bound to a base/wide shape;
- constant, random, and per-spawn component values;
- author-editable C# scripts compiled and discovered through the scripting host;
- applied components marked dirty.

Next requirements:

- retain typed, template, data-driven, and script-authored prefab capabilities;
- explicitly preserve the RunUO-style class/inheritance authoring facade;
- keep authoring definition objects out of the spawned runtime entity representation;
- compile inherited archetype classes into a canonical component union;
- compile each concrete prefab class into one immutable typed spawn plan;
- support reusable shape fragments/traits in addition to single inheritance;
- compile every runtime key to a dense `PrefabId` during loading;
- bind component columns once per schema/archetype;
- never allocate a resolved-column array during spawn;
- constrain prefab writers to the allocated row/range;
- prevent prefab code from mutating unrelated rows or entity handles;
- true bulk spawning into contiguous chunk ranges;
- constant template columns initialized with bulk fill/copy;
- per-instance arrays copied in bulk;
- generated prefab initialization where definitions are known at build time;
- runtime-script-compiled typed initialization where definitions are loaded from C# content assemblies;
- build-time generated metadata for compiled/core component types and script-compile-time generated or cold-bound metadata for script-defined component types, with no reflection remaining in hot spawn/query paths;
- allocation-free transient item/mobile entity contexts for instance-like reads, tracked writes, and deferred add/remove operations;
- prebound numeric routes for rare item/mobile script events;
- a defined threshold/direction for lowering high-volume event behavior into batch systems;
- no per-spawn `object` boxing, reflective writer lookup, captured factory allocation, or shared RNG lock;
- deterministic per-worker or per-spawn-sequence RNG streams for random initializers;
- atomic cleanup on scalar failure;
- defined batch transaction policy on failure;
- chunk versions/lifecycle events updated once per batch where possible;
- optional prefab inheritance/composition resolved entirely during import, not spawn.

Content generation and reload requirements:

- a successful script compile produces a complete immutable content generation containing archetype descriptors, prefab descriptors, numeric IDs, and bound spawn plans;
- script modules register definitions during a deterministic `Configure` phase before schema/world sealing; later `Initialize` work may consume the configured registries but must not silently change a sealed component schema;
- a failed compile leaves the active content generation untouched;
- built-in, script-authored, data-authored, generated, and future plugin-authored definitions register into the same logical registry and carry origin, source name, content-group owner, editability, reloadability, revision, and content hash metadata;
- prefab keys are globally unique across every active origin; duplicate registration is a hard diagnostic containing both sources, never silent priority replacement;
- ergonomic CLR short-name keys may remain available for small scripts, but durable production content should prefer canonical namespaced/path keys; persisted records store canonical keys or explicit aliases, never generation-local numeric IDs;
- value-only prefab edits may atomically replace the spawn plan for future spawns at a safe content barrier;
- removing a prefab disables future lookup/spawn without invalidating already spawned entities;
- existing entities do not retain a managed dependency on their prefab definition;
- changing a base archetype shape invalidates and recompiles every dependent prefab;
- shape changes involving already registered component types may optionally migrate existing entities through an explicit, tested migration plan;
- introducing/removing runtime component types requires either a staged world/schema generation swap or a restart/rebuild policy; it must not mutate a sealed schema unsafely;
- persistence must define what happens when a saved durable prefab key no longer exists;
- content keys remain stable across reloads, while numeric IDs are generation-local unless a generated manifest deliberately stabilizes them;
- operators can inspect the active content generation and see compile source, archetype ancestry, canonical components, prefab assignments, and reload diagnostics;
- runtime content groups own their prefab/archetype registrations; unmount builds and validates a replacement immutable registry generation, runs bridge-owned cleanup in defined reverse order, and publishes only after all dependency checks succeed;
- archetype removal is rejected while any remaining prefab references it, while live entities continue to depend only on their resolved ECS layout rather than the authoring descriptor;
- data-authored prefab saves use durable versioned files, server-held edit leases, expected-revision/hash checks, validation against registered components/assets/archetypes, and an atomic file-plus-registry publication protocol;
- deleting or editing a prefab does not rewrite existing entities unless an explicit, audited migration is requested;
- dangling component, archetype, asset, recipe, or spawn-set references remain visible as invalid registry diagnostics instead of disappearing or crashing load.

The initial expected policy should be conservative: new definitions and value changes affect future spawns; existing entities change only through an explicit migration/patch operation. Tests may show that automatic migration is safe and useful for selected changes, but it should not be implicit.

Tests:

- typed/template/resolved parity with Current;
- script catalog parity;
- base-item/base-weapon/concrete-item inheritance fixtures;
- base-mobile/base-humanoid/human/concrete-mobile inheritance fixtures;
- inherited component union and duplicate normalization;
- shape fragment/trait composition parity;
- concrete prefab is bound to exactly one canonical wide archetype;
- prefab cannot initialize a component outside its resolved archetype;
- unspecified archetype components receive documented default values;
- constants, random values, and sequence-based per-spawn values;
- deterministic random spawn values across serial and parallel worker counts when deterministic mode is requested;
- instance-like script context read/set/add/remove behavior;
- instance-context stale-handle and foreign-world rejection;
- rare event-handler dispatch parity and zero-allocation behavior;
- script-declared per-instance state is represented by ECS data rather than managed object fields;
- script compile/reload success atomically changes future spawns;
- script compile/reload failure preserves the active generation;
- script module `Configure` ordering and schema-seal tests;
- cross-origin duplicate key diagnostics and canonical-key/alias persistence tests;
- content-group mount/unmount success, dependency rejection, cleanup failure, and rollback tests;
- data-authored edit lease, stale revision, atomic save/swap, invalid reference, disable, erase, and rollback tests;
- prefab removal blocks new spawns but leaves existing entities valid;
- base-shape changes invalidate all dependent prefab plans;
- explicit existing-entity migration and rollback tests;
- missing durable prefab key restore policy;
- scalar and bulk output equivalence;
- missing/duplicate key validation;
- component membership validation;
- constrained writer cannot access unrelated rows;
- initializer failure rollback;
- batch failure policy;
- template aliasing/mutation tests;
- zero-allocation spawn after binding;
- default and per-instance column correctness after chunk reuse.

Benchmarks:

- cold script compile/discovery/definition-graph build;
- cold inheritance/fragment resolution;
- cold immutable content-generation publication;
- cold content-group mount/unmount and failed-generation rollback;
- data-prefab parse/validate/build/snapshot-swap separated from hot spawn;
- scalar versus bulk spawn;
- typed versus data-driven/script prefab;
- one to many components;
- constant versus per-instance data;
- constant versus random/per-spawn generator initialization;
- Current boxed/shared-RNG script spawn versus Next typed/per-worker-RNG spawn;
- prebound script-event dispatch versus managed object/virtual-dispatch reference fixtures;
- isolated event dispatch versus batch-system lowering for high-volume events;
- cold binding reported separately from hot spawning;
- 1k, 10k, and 100k spawn bursts;
- spawn plus replication lifecycle extraction;
- spawn plus persistence capture.

### 4.10 Persistence and serialization

Current features:

- stable persistence IDs via attributes;
- serializable component interface;
- instance-owned persistence type registry;
- canonical persistence signatures;
- grouped in-memory entity snapshots;
- filtered persistence views;
- bit reader/writer base;
- binary snapshot serializer;
- binary world adapter;
- component schema version passed to deserializers;
- system save/load hooks;
- server-level participants, manifests, backup rotation, compression, recovery modes, and timing outside the kernel.

Next requirements:

- generated component serializers and migration dispatch;
- no boxing or `List<object?>` capture path;
- direct chunk-column serialization into caller-provided/preallocated buffers;
- entity save records that retain canonical prefab/archetype provenance where useful plus authoritative component data; prefab defaults are never substituted for saved mutable component state;
- explicit endianness and format version;
- strict configured limits for groups, entities, components, strings, and payload bytes before allocation;
- validation of section uniqueness, signature consistency, component identity, and counts;
- unknown component and newer-version policy;
- transactional restore through a staging world/arena or validated commit plan;
- explicit replace, merge, and append modes;
- durable entity IDs and reference remapping;
- immutable snapshot views or ownership-transfer leases;
- checksum/corruption detection;
- asynchronous compression and storage outside the simulation thread;
- optional incremental snapshots using chunk change versions;
- copy-on-write or frozen chunk epochs if live asynchronous capture is required.

The server save-set coordinator remains the transaction owner above ECS. It already stages participants, promotes only complete save sets, rotates whole-save backups, supports recovery modes and backup restore, and records participant timing/artifact facts. Next must plug into that contract rather than reimplementing it. Planned coordinator improvements such as artifact checksums, optional parallel participant snapshot/write work, richer retention, and background capture must be benchmarked as host features. Accounting/character data must still load before ECS entities that may refer to it.

Tests:

- component round trip for every serializable component;
- golden binary fixtures;
- schema migration fixtures for every supported version transition;
- unknown component/version behavior;
- truncated and corrupt data fuzzing;
- malicious size-prefix limits;
- transactional failure leaves world unchanged;
- durable reference remapping;
- missing/renamed canonical prefab key handling without losing saved component values;
- participant ordering with accounting/character identity restored before ECS reference resolution;
- replace/merge/append semantics;
- deterministic canonical serialization;
- concurrent snapshot policy tests;
- backup fallback integration tests;
- allocation and peak-memory tests at large scale.

Benchmarks:

- view creation, capture, serialization, deserialization, validation, and commit measured separately;
- full and incremental snapshots;
- direct chunk streaming versus Current boxed snapshot capture;
- raw, ZIP, Zstandard, and any future codec measured as end-to-end CPU/size tradeoffs;
- 100k and 1m entities;
- hot-world capture tail latency;
- backup recovery time;
- native and managed peak memory during restore.

### 4.11 Inspection, profiling, and control tools

Current features:

- entity inspection snapshots;
- component summaries;
- public field/property value formatting through reflection;
- world phase timing;
- structural apply counts;
- per-system last/average/peak/min timing;
- system schedule/runtime snapshots;
- optional control descriptors;
- server and Godot shell surfaces that consume runtime snapshots;
- network publication of ECS/system inspection information.

Next requirements:

- keep inspection out of the simulation hot path unless explicitly enabled;
- generate one component-field metadata source for inspection, persistence field IDs/versions, migrations, admin editability/ranges/permissions, replication field policy, and tooltip/cache invalidation hints where declared;
- expose read-only snapshots through caller-owned buffers or enumerable views;
- report managed allocation, native allocation, chunk counts, occupancy, fragmentation, dirty versions, command high-water marks, and worker utilization;
- preserve per-system timing and control metadata;
- expose scheduler graph and fusion decisions;
- expose adaptive serial/parallel decisions;
- make profiling sampling configurable to avoid measuring every tick;
- never allow inspection to expose mutable storage;
- keep UI, permissions, remote transport, and override persistence above the kernel.
- expose typed, validated edit-command descriptors rather than reflection setters; the server bridge owns permission checks, audit records, confirmation, and command execution at a safe barrier;
- retain and extend the ECS-owned `SystemSettings` / control-descriptor vocabulary instead of creating a UI-only duplicate; schema/admin panels translate those descriptors above the kernel;
- support batched/sampled telemetry snapshots suitable for a bridge cadence such as 100-250 ms without causing every system to publish UI messages per tick.

Tests:

- inspection correctness for generated metadata;
- no mutation through inspection;
- disabled profiling has zero allocation and negligible measured overhead;
- enabled profiling reports internally consistent counts;
- snapshot capacity/partial read behavior;
- concurrent operator reads follow a documented snapshot policy;
- server/Godot adapter integration tests.
- generated edit descriptor validation, permission/audit bridge tests, and apply/revert/persisted-value state tests.

Benchmarks:

- profiling off, sampled, and every-tick modes;
- inspection of small and wide entities;
- full world metrics snapshot;
- remote publication and fanout;
- admin telemetry at multiple refresh cadences and open-panel counts;
- allocation and p99 tick impact while operator tooling is active.

### 4.12 Runtime, build, and deployment configuration

Current position:

- the ECS targets .NET 8;
- the kernel project does not currently define an explicit unsafe/native-memory policy;
- Release benchmarks are the meaningful performance baseline;
- current worker defaults derive from logical processor count;
- runtime/JIT/GC configuration is not yet treated as part of an ECS performance profile;
- dynamic reflection remains in cold schema, prefab, persistence, and inspection paths.

Next requirements:

- define and test an explicit unsafe-code policy for generated storage/query code;
- warm every designated hot job before measuring or entering a latency-sensitive simulation window;
- record tiered compilation, profile-guided optimization, ReadyToRun, and native compilation settings in every benchmark report;
- compare warmed JIT/profile-guided builds, ReadyToRun deployment, and NativeAOT only after reflection-dependent hot/runtime paths have generated replacements;
- retain a non-AOT host or out-of-process script compilation/loading boundary if dynamic scripting prevents full native compilation;
- compare workstation and server GC in end-to-end host workloads rather than assuming one is universally faster;
- consider a no-GC region only after the entire measured server path is allocation-free and capacity is reserved;
- expose configured physical/logical worker counts and reserve cores for networking, storage, and runtime work;
- support optional worker affinity for known dedicated-server deployments;
- investigate first-touch arena allocation and world/spatial sharding on NUMA machines;
- provide hardware-intrinsic SIMD paths only when tests prove scalar equivalence and benchmarks prove a gain;
- use `SkipLocalsInit`, unchecked arithmetic, unsafe reference loops, and aggressive inlining only in generated/measured kernels where initialization and overflow tests make them safe;
- inspect code size and instruction-cache impact before accepting broad inlining or system fusion;
- keep debug validation available in test/diagnostic builds while compiling validation branches out of the production hot path where safe.

Tests:

- run core correctness tests under every supported compilation/deployment mode;
- scalar and SIMD equivalence across boundary values, NaNs, infinities, and deterministic simulation requirements;
- no-GC-region entry/exit and capacity-failure behavior if that mode is supported;
- worker affinity/worker-count changes do not alter canonical results;
- native compilation smoke tests for generated metadata, prefabs, persistence, and inspection;
- debug-validation and production-path behavior parity;
- native arena first-touch/disposal tests on supported platforms.

Benchmarks:

- cold JIT, warmed JIT, profile-guided, ReadyToRun, and NativeAOT reported separately;
- server versus workstation GC under the full host rather than an isolated query loop;
- physical cores versus SMT threads;
- affinity off/on;
- no-GC region off/on after allocation reaches zero;
- scalar/auto-vectorized/explicit-intrinsic kernels;
- safe span loops versus generated unsafe reference loops;
- fused job code size, instruction-cache misses, and throughput;
- single-socket versus NUMA-shaped deployment where relevant.

Runtime tuning is the final multiplier, not a substitute for the architecture. Tests and allocation traces must show that the kernel is already predictable before GC or compilation switches are credited with a performance improvement.

## 5. Existing Fixtures and Built-On Features to Preserve

The ECS is already embedded in server features. Next is not complete until replacement adapters or direct integrations cover these behaviors.

### 5.1 Gameplay components

Current ECS-backed gameplay types include:

- server movement body;
- biological profile;
- human profile;
- humanoid profile;
- server spawner component;
- server spawner runtime state.

Next should retain unmanaged compact gameplay data and add generated metadata. Tests should compare Current and Next world state for the same spawn, movement, and despawn command traces.

### 5.2 Server movement world

Current behavior includes:

- schema construction;
- movement and profile component registration;
- script prefab catalog attachment;
- prefab-backed session entity creation;
- input application and movement state projection;
- operator/system runtime snapshot hooks;
- persistence participation.

The current live host often uses the ECS as authoritative storage without driving gameplay through `World.Tick()`. Next integration should make an explicit choice:

- migrate movement into generated scheduled jobs; or
- keep a direct authoritative-storage mode with generated lookups.

Test both paths if both remain supported. Benchmark them end-to-end; do not assume the scheduler version is faster merely because it is more architecturally uniform.

### 5.3 Scripting and data-driven prefabs

Current behavior includes:

- script definition compilation;
- archetype and prefab builders;
- RunUO-style C# definition classes used as the content-authoring surface;
- inheritance of `ArchetypeShape` classes to compose progressively wider base item/mobile shapes;
- concrete `PrefabDefinition<TArchetype>` scripts that bind to a wide archetype and fill its component values;
- component writer registration;
- validation of component types and membership;
- duplicate key detection;
- catalog construction;
- script prefab spawning through the ordinary ECS prefab seam.

Next should preserve this author-facing model directly. Content authors should be able to add, remove, inherit, and edit ordinary C# item/mobile definitions without learning chunk storage, query plans, or serializers. The compiler/loader should turn those definition objects into immutable numeric prefab plans during load. Tests must run identical inherited item/mobile script fixtures against Current and Next and compare canonical archetype membership and component output.

The runtime boundary must remain strict: managed definition instances and reflection are allowed during script compile/load, but not in the hot spawn or simulation path. Hot spawning should use no strings, dictionaries, reflection, boxing, shared RNG lock, or allocation. Existing entities remain ordinary ECS entities and should not require their original definition object to stay alive.

Script reload tests should cover value-only edits, new concrete prefabs, removed prefabs, changed base shapes, failed compilation, and explicit live-entity migrations. Benchmarks should report cold compile/reload separately from hot numeric-ID spawning.

### 5.4 Networking and AOI

Current features include:

- dirty-component publication;
- `Realtime` and `Bulk` latest-state rules;
- `ReliableEvent` and `Bulk` ordered-event rules;
- AOI ring scopes;
- ring cadence policy;
- movement input application;
- movement state projection;
- ECS/system runtime snapshot publication;
- network item key helpers;
- stable latest-state item identity across AOI scope/ring changes;
- shared fanout and bot-harness coverage above the ECS.

Next should replace entity-ID drain plus random `TryGet` gathering with direct changed-chunk serialization. It must add remove/destroy tombstones and independent consumer cursors. The ECS-to-network bridge must acquire immutable/leased publish views at a safe barrier; network workers must never read live mutable ECS columns. The bridge, not the ECS kernel, classifies continuous observer state into `Realtime`, discrete gameplay/lifecycle facts into `ReliableEvent`, and large compression-friendly state into `Bulk`. AOI ring policy and cadence also remain bridge/network policy.

Replication item identity must be independent of the scope currently carrying it. Moving the same entity/component family between AOI rings must update one logical client slot rather than manufacture a new logical item key. Durable/network keys should be generated from stable semantic family IDs plus the appropriate runtime or durable entity identity; raw archetype row positions and generation-local prefab IDs are forbidden on the wire.

Tests should compare published semantic items, not merely counts. Include scope-transition identity, lifecycle ordering, consumer lag/recovery, baseline/delta coherence, and immutable-view lifetime tests. Benchmarks should compare Current gather-based publication with Next direct-column publication at multiple dirty percentages, AOI densities, lane counts, and subscriber counts. The widened bot-harness profiles should include realistic `0.35` and `0.40` movement participation, three AOI rings/cadences, owner correction, observer state, a second realtime family, bulk state, reliable events, dense hotspots, client decode/apply, allocation, and Gen0/1/2 counts.

### 5.5 Persistence coordinator

Current server-level persistence provides participant ordering, save-set manifests, backups, compression, restore modes, and timing. These policies should remain above the ECS.

Next needs a new ECS participant implementing the same coordinator contract. Tests should run Current and Next participants through identical save, corrupt-primary, backup-recovery, and migration fixtures. Restore semantics must be explicit and transactional before the participant replaces Current.

### 5.6 Inventory references

Current inventory code can encode/decode packed ECS entity handles and resolve runtime owners/requesters.

The full packed generational handle is the correct compact reference while both the inventory record and owner are explicitly runtime-local and loaded; retaining the generation bits is safer than retaining only an entity index. It is not a durable identity.

Next must distinguish two forms instead of replacing one indiscriminately:

- `RuntimeEntityRef`: world identity plus packed generational handle, valid only against the owning live world;
- `DurableEntityRef`: stable world/object identity resolved through a server-owned map after load.

Persistent/world-persistent inventory owners, save data, offline references, cross-world queues, and long-lived external records use the durable form. Ephemeral live intents may use the runtime form. Compatibility parsing may exist at the bridge, but tests must prove that slot reuse, world replacement, restart, unloaded owners, and destroyed owners cannot silently resolve to the wrong entity.

### 5.7 Kernel and bridge boundaries

The repository-wide design rule is explicit: ECS, Network, UI, and Inventory remain standalone kernels and do not reach into one another for runtime behavior. Scripting is server startup/content infrastructure, not an ECS dependency. Therefore Next exposes only small kernel-owned contracts—handles, immutable views, change cursors, generated metadata, prefab descriptors, persistence adapters, and inspection snapshots. Integration belongs in `Server`, `Client`, or another explicit bridge.

`Server` and `Client` are also engine-agnostic application runtimes, not aliases for Godot projects:

- `Server/Core`, `Server/Hosting`, `Server/Network`, gameplay, persistence, scripting, admission, accounting, and other server services compose the actual server;
- `Client/Core`, `Client/Network`, prediction, interpolation, projected state, content/cache, and neutral UI services compose the actual client;
- `Server/_Console` is the RunUO-style command-line entry point over the shared server lifecycle/control surface;
- `Server/_Headless` is the silent dedicated, internal, automation, and embedded/background single-player entry point over that same server runtime;
- `Server/_Godot` is a Godot-presented operator terminal with buttons, settings, logs, metrics, and inspection views over the same typed control surface;
- `Client/_Godot` is the graphical presentation/input/audio/resource wrapper over the same engine-neutral client runtime;
- none of those wrappers owns a separate ECS world model, scheduler, prefab implementation, persistence path, gameplay path, or network protocol.

Single-player remains server-authoritative. The client starts or attaches to the ordinary silent `_Headless` composition, waits for explicit readiness, and connects its normal session driver to localhost. It must traverse the normal handshake, admission, input, ECS authority, replication, AOI, persistence binding, disconnect, and cleanup paths. The client cannot directly read or mutate the server world, share entity/archetype storage, call server gameplay objects instead of sending input, or substitute a local projected-world mutation for network replication.

The in-memory network loopback remains a deterministic test/benchmark transport. Production-equivalence single-player and wrapper-parity tests should use the real localhost protocol. An optional embedded transport optimization is acceptable only if it preserves identical serialization, lanes, queues, ownership, session, authority, and apply-barrier semantics and passes the same trace suite; it cannot become an undocumented alternate gameplay architecture.

ECS host integration therefore exposes neutral lifecycle, tick, capture, persistence, inspection, and command-barrier contracts. Wrappers may start/pump/observe these contracts, but Godot frame callbacks, console reads, or headless process loops do not redefine ECS stage order or provide a second mutation route.

Tests should:

- enforce project-reference and forbidden-namespace boundaries and build every kernel, server runtime, and client runtime independently of Godot;
- reject Network, UI, Inventory, Godot, console-I/O, CLI, wrapper, account, or script-host dependencies in the ECS kernel;
- prove that `_Headless`, `_Console`, and server `_Godot` adapters produce the same canonical ECS/world result for identical configuration and deterministic commands;
- prove that console commands and Godot buttons reach ECS only through the same typed command/control and safe-barrier surfaces;
- run single-player startup, localhost connect, entity spawn, movement, save, disconnect, and shutdown without any direct client-to-server ECS reference;
- inject local bind/startup failure, server crash, cancellation, reconnect, and save-on-stop and verify world/worker/arena disposal;
- compare real localhost UDP with loopback semantic traces where both declare equivalent delivery behavior.

A performance shortcut that introduces a Network, UI, Inventory, Godot, wrapper, account, or script-host dependency into the ECS kernel is a design failure even if its microbenchmark is faster. A single-player shortcut that bypasses the server-authoritative/networked boundary is likewise a design failure. Optimize the neutral handoff instead.

### 5.8 Script modules and runtime content groups

The current server script runtime discovers explicit modules with deterministic `Configure` and `Initialize` hooks. Its runtime content-group registry tracks package-like units, mounted state, metadata, and reverse-order unmount actions. Commands, UI providers, archetypes, and prefabs can already be registered under group ownership; archetype removal is blocked while a prefab still references it.

The current cleanup is not fully transactional: each successful reverse-order action is removed immediately. If a later action throws, the group remains marked mounted but earlier commands/providers/definitions may already have been removed. Treat that as a known correctness defect, not behavior to preserve.

Next must make definition registration transactional at content-generation granularity:

- `Configure` collects modules, components, archetypes, prefabs, handler routes, and ownership metadata before schema/world sealing;
- a complete candidate generation is validated and bound off to the side;
- publication is one safe atomic pointer/generation swap;
- failed configure, bind, cleanup, dependency validation, or unmount leaves the prior generation active;
- bridge registries attach their own cleanup actions without making ECS know commands, panels, spawn sets, or UI instances;
- unmount affects future resolution/registration; existing entities remain valid ECS data unless an explicit group migration/despawn policy is requested;
- open UI/provider instances and other external consumers use their own supersession/close policy above ECS.

Tests must cover deterministic discovery, dependency-independent module correctness, duplicate ownership, mount/unmount order, partial cleanup failure, rollback, remaining-prefab/archetype dependency rejection, and repeated mount/unmount allocation/retention. Benchmarks must separate cold group graph construction and atomic publication from hot spawn/tick work.

### 5.9 Live data-authored prefabs

The script/runtime plans intentionally reserve a live item/mobile/content builder. These definitions are durable data under runtime `data/prefabs`, not generated C# and not disposable compiler-cache output. They compose already registered component types, archetype descriptors, assets, and rule/catalog references and then enter the same prefab registry and ECS spawn path as built-in/script content.

Next must support this without making it second-class:

- origin/editability/reloadability/revision/hash metadata;
- global canonical key uniqueness across origins;
- generated-signature reuse before creating a new canonical archetype;
- registered component/field/range and required-component validation;
- asset/category/reference validation in server bridges;
- explicit separation between the gameplay prefab key and namespaced client presentation/content IDs; ECS components may carry compact stable IDs/hashes, but never Godot resources, file paths, or content-pack objects;
- edit leases, expected revision/hash, audit identity, rollback target, and atomic temp-write/validate/registry-swap/file-promotion orchestration;
- invalid/dangling definitions remain diagnosable and disabled;
- edits affect future spawns by default; existing entities change only through an explicit migration command;
- data cannot invent CLR component types or executable behavior.

Client content-pack priority/replacement is presentation policy and must not replace a server gameplay prefab or alter its component truth. A prefab may reference logical model/icon/sound/material slots through bridge-owned registries; required-pack advertisement, download, hashing, trust, cache, and rendering remain outside ECS.

Tests should run identical data and script descriptors through canonicalization and compare signatures, spawn values, persistence provenance, inspection output, replication output, and failure behavior. Benchmarks should measure load/validation/snapshot publication independently from numeric-ID spawning.

### 5.10 Spawners and spawn sets

The server already has ECS spawner components/runtime state and a stable hashed spawn-set key registry. Actual spawn execution, package loading/unloading, smart player-proximity activation, admin editing, and persistence are planned rather than Current ECS features.

Next should make the later execution path efficient without embedding gameplay policy in ECS:

- ECS components keep compact stable hashes/IDs, not strings;
- bridge registries resolve admin-readable canonical spawn-set keys and content-group ownership;
- resolved generation-local `SpawnSetId`/`PrefabId` arrays are cached at group activation and never looked up by string per spawn;
- activation systems query compact proximity/activity state and issue bulk prefab commands;
- spawned entities carry a compact spawner/encounter provenance component only when reconciliation/despawn/accounting needs it;
- active-count changes derive from lifecycle events or validated reconciliation rather than fragile manual counters;
- unmount/disable policy explicitly chooses keep-existing, stop-respawn, or audited despawn at a structural barrier;
- persistent spawner instances save the stable spawn-set key/hash and authored state, not generation-local numeric IDs.

Tests should cover missing/colliding hashes, content reload, activation boundaries, max-active races, spawn failure, destruction reconciliation, save/load, and group unmount policy. Benchmarks should cover dormant 10k/100k spawners, proximity activation waves, bulk spawn bursts, and lifecycle reconciliation.

### 5.11 Accounting, character, and external identity

Accounting already stores durable `CharacterId`, owning `AccountId`, prefab key, and position/yaw snapshots. This clarifies that character identity, prefab definition identity, runtime ECS identity, and network item identity are different domains.

The server bridge must maintain explicit maps:

```text
CharacterId / DurableEntityId
        <-> live WorldId + Entity handle
        <-> network family/item keys
        <-> inventory owner refs
```

The ECS kernel owns only the live and optional durable entity identifiers plus remap hooks. Accounting owns character/account records; Network owns wire/session identity; Inventory owns its neutral owner-ref shape. Tests should follow a character through create, spawn, disconnect snapshot, despawn, save, restart, restore, respawn, and stale old-handle rejection.

### 5.12 Admin UI and live control integration

The planned ECS control panel requires systems/archetypes/entities/components tables, typed settings, timings, counters, inspection, and safe edit actions. This is not a reason for ECS to depend on the UI kernel. Next produces generated descriptors and immutable snapshots; the server admin facade applies permissions, validation, audit, rate limits, and safe-barrier commands; the UI schema layer renders them.

Preserve `SystemBase.Settings`, system control descriptors, schedule application, and system runtime snapshots in evolved form. Add generated component field descriptors and typed edit commands instead of public reflection mutation. Telemetry should be sampled and batched for visible panels—normally around 100-250 ms—not pushed independently by every ECS system every tick.

Tests should cover descriptor generation, server permission denial, stale entity/component rejection, safe-barrier edits, dirty/change tracking after edits, audit output, apply/revert/persisted setting state, telemetry coalescing, panel/session cleanup, and profiling-disabled zero-allocation behavior.

## 6. Proposed Tooling

### 6.1 ECS source generator

Generate:

- component IDs and metadata;
- stable persistent field IDs, component schema versions, field range/editor/permission metadata, and declared aliases;
- layout/alignment metadata;
- tag and enableable metadata;
- archetype descriptors;
- typed query plans;
- system read/write declarations;
- scheduler graph;
- system fusion candidates;
- prefab bindings;
- persistence serializers and migrations;
- replication serializers;
- inspection metadata;
- typed admin edit-command validators/applicators;
- tooltip/state-cache invalidation metadata where component declarations opt in;
- stable manifest files used by tests.

Generation has two entry points. Core/gameplay projects use the normal build-time source generator. Runtime C# scripts that declare new components run the same analyzer/generator logic inside the Roslyn script compilation and include the generated component manifest, serializers, prefab writers, and inspection metadata in the script cache key. A cold reflection/function-binding fallback may exist for diagnostics or transition, but no runtime-script feature may put reflection, boxing, or dynamic invocation back into a hot query, spawn, serialization, replication, or edit loop.

Generator tests should use golden generated-source snapshots plus compilation tests. Runtime parity tests should verify that generated metadata matches the declared source components.

### 6.2 Layout and capacity planner

Provide an offline/build-time tool that reports:

- component size/alignment;
- bytes per entity by archetype;
- calculated rows per chunk for each tested chunk size;
- expected chunk count at configured population;
- estimated entity metadata bytes;
- command/change/scratch arena sizes;
- total managed and native world reservation;
- wasted bytes from alignment and partial chunks.

Tests should validate planner arithmetic at boundaries. Benchmark runs should embed the chosen layout report so results remain reproducible.

### 6.3 World diagnostics dump

Provide a low-frequency diagnostics export containing:

- archetypes and signatures;
- chunks, capacity, occupancy, and fragmentation;
- change versions and lifecycle ring positions;
- command high-water marks;
- worker utilization and steals;
- system graph, stages, dependencies, and fusion groups;
- capacity warnings;
- managed/native allocation totals.

Tests should confirm diagnostics are observational only and that disabled diagnostics do not allocate during ticks.

### 6.4 Deterministic replay tool

Record or synthesize a stream of:

- spawns;
- prefab IDs and per-instance data;
- component sets;
- add/remove/destroy commands;
- input frames;
- tick delta values;
- random seeds;
- persistence checkpoints.

Replay the same stream into Legacy where supported, Current, Next serial, and Next parallel. Compare canonical snapshots at checkpoints. Tests should minimize failing randomized streams into compact regression fixtures.

### 6.5 Benchmark comparison/report generator

Extend the current report generator to produce:

| Scenario | Scale | Legacy | Current | Next | Next/Current | Next/Legacy | Allocations | GC | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |

Reports should include:

- commit/hash for each implementation;
- dirty-worktree marker;
- runtime and build configuration;
- OS and CPU model;
- physical/logical core counts;
- worker count and affinity;
- memory size;
- scenario seed;
- warmup and iteration policy;
- mean, median, min, p95, p99, maximum, and standard deviation;
- operations and cycles per operation where available;
- managed bytes allocated;
- native bytes reserved/committed;
- Gen0/1/2 collections for sustained tests;
- correctness checksum and canonical-state digest;
- explicit comparable/noncomparable status.

Tests should validate report joins, missing scenarios, unit conversions, ratio direction, and stale/mismatched environment detection.

## 7. Test Strategy

Testing is continuous. A phase is not “implemented, tests later.” A feature begins with a behavioral test or reference fixture, then implementation, then differential tests, then performance tests.

### 7.1 Test layers

1. **Primitive unit tests**: IDs, masks, bitsets, packing, chunk math, rings, arenas.
2. **Subsystem tests**: storage, queries, structural barriers, changes, scheduler, prefabs, persistence.
3. **Reference-model tests**: compare random ECS operations with a deliberately slow dictionary/set model.
4. **Differential tests**: identical supported operations against Current and Next.
5. **Three-generation tests**: Legacy, Current, and Next where semantics overlap.
6. **Parallel parity tests**: Next serial versus every supported worker count.
7. **Determinism tests**: repeat identical seeds and compare canonical snapshots/events.
8. **Fault-injection tests**: capacity exhaustion, thrown jobs, malformed persistence, failed prefab initialization.
9. **Allocation tests**: exact post-warmup allocation assertions.
10. **Performance tests**: microbenchmarks with correctness validation.
11. **Soak tests**: long-running allocation, retention, latency, and fragmentation checks.
12. **Integration tests**: scripting, content-group mount/unmount, data-authored prefabs, spawners, movement, networking, persistence coordinator, accounting/character identity, inventory, admin UI descriptors, and host tooling.

### 7.2 Current test baseline

The current repository validation baseline is:

- ECS: 73 passing tests;
- server: 111 passing tests;
- server persistence: 16 passing tests;
- server scripting: 32 passing tests;
- server inventory bridge: 29 passing tests;
- total reviewed baseline: 261 passing tests.

Next development should retain these suites and add Next-specific suites. Do not delete or rewrite Current tests merely because Next has a different API. Extract behavioral fixtures or replay inputs so both implementations can be tested without forcing an API compatibility layer.

### 7.3 Required new regression tests

Add early tests for known gaps:

- batch default create after destroy does not resurrect stale values;
- `Requires` dependency is honored under parallel batches even with empty/nonconflicting masks;
- foreign-world handles are rejected;
- no external mutable entity span exists;
- no untracked mutable component span exists;
- partial change reads retain the remainder;
- two consumers receive the same changes independently;
- component removal emits a tombstone;
- destruction emits a tombstone;
- schema lookup remains correct under concurrent multi-schema access;
- failed prefab spawn leaves no entity;
- failed structural phase has documented recovery/fault behavior;
- malformed persistence size prefixes are rejected before large allocation;
- restore failure leaves the target unchanged;
- durable entity references remap after restore;
- runtime inventory refs reject stale handles while durable inventory refs remap after restore;
- replication item identity remains stable while an entity moves between AOI scopes;
- content-group unmount failure leaves the active definition generation unchanged;
- content-group unmount failure restores every already-processed owned registration, not only the group mount-state flag;
- stale data-prefab edit leases/revisions cannot overwrite the active revision;
- spawner active counts reconcile after prefab failure, entity destruction, and content-group disable;
- generated admin edits pass through tracked write/structural barriers and emit audit-visible results;
- world disposal terminates every worker and frees every native allocation.

### 7.4 Property and fuzz testing

Generate randomized sequences containing:

- create default;
- create prefab;
- set;
- add;
- remove;
- destroy;
- enable/disable;
- query and expected-count assertions;
- change-consumer reads;
- snapshot/restore checkpoints.

Compare Next with a slow model after every operation or barrier. Run seeds in CI and retain every discovered seed as a named regression. Add shrink/minimization so a million-operation failure becomes a small reproducible sequence.

Persistence decoders require coverage-guided or structured fuzzing with strict memory/time limits. Worker scheduling needs repeated randomized yield/injection tests to widen race windows.

### 7.5 Test-driven course correction

Tests and benchmarks may contradict this design. Examples:

- 32 KiB chunks may lose to 16 KiB for wide archetypes;
- static partitioning may lose to coarse work stealing for fragmented worlds;
- native slabs may not beat managed fixed arrays for small worlds;
- automatic fusion may increase instruction-cache misses;
- bitsets may lose to append lists below a sparse threshold;
- strict zero-allocation buffers may reserve too much memory.

When evidence contradicts the expected direction:

1. reproduce it with a focused test/benchmark;
2. verify semantic equivalence;
3. inspect allocation and hardware counters;
4. test at more than one scale;
5. record the decision and chosen crossover/algorithm;
6. keep the losing experiment if useful as a benchmark variant;
7. update this document.

## 8. Benchmark Strategy

### 8.1 Benchmark rules

- Setup is never included in hot steady-state measurements unless the scenario is explicitly a bootstrap benchmark.
- Cold and hot results are separate.
- Allocation is measured after warmup.
- Forced collections may isolate microbenchmark iterations, but their collection counts are not reported as workload GC behavior.
- Sustained GC/retention is measured in separate soak processes without forced collections.
- Every benchmark validates output.
- Checksums are supplemented by canonical world-state digests for structural/system scenarios.
- Scenario IDs and semantics remain stable across Legacy, Current, and Next.
- Unsupported scenarios are reported as unsupported, not omitted.
- Measurements are repeated in randomized order or separate processes to reduce order bias.
- Performance gates use distributions and noise thresholds, not a single fastest run.

### 8.2 Retain existing comparable scenarios

Retain and normalize the current matrix:

- schema build/component registration;
- deferred default create;
- immediate create where supported;
- set component;
- patch component;
- random try-get;
- one-, two-, and three-component read queries;
- deferred add;
- deferred remove;
- deferred destroy;
- sparse-victim destroy;
- add/remove migration cycle;
- create/destroy reuse churn;
- dirty/change extraction;
- filtered persistence view;
- serial and parallel query systems;
- serial and parallel read-only systems;
- serial and parallel dirty-aware writes;
- oscillating movement at 10k and 100k;
- active-35%-reshuffle-10 movement;
- V1 pulse-to-velocity-to-movement pipeline;
- scheduler profiling overhead;
- cadence aligned versus staggered;
- sustained GC soak.

The existing benchmark report currently shows strong Current results versus Legacy, but its documentation and tracked summary counts have drifted. Next tooling must generate documentation summaries from result data rather than maintaining claims by hand.

### 8.3 Add storage and layout scenarios

- 0, 1, 31, 32, 33, chunk-capacity-minus-one, exact-capacity, and capacity-plus-one entities;
- 1k, 10k, 50k, 100k, and 1m entities;
- one archetype, dozens, hundreds, and thousands of archetypes;
- narrow, normal, wide, and very wide signatures;
- tag-heavy signatures;
- 10%, 50%, 90%, and 100% chunk occupancy;
- managed fixed array versus native slab variants;
- 16/32/64 KiB chunk candidates;
- spatially ordered versus randomized rows;
- cold-cache and warm-cache scans.

### 8.4 Add query/job scenarios

- one through eight component queries;
- read-only, one-write, and multiple-write jobs;
- required/excluded/optional filters;
- enabled/disabled filters;
- changed-since filters;
- entity-aware and entity-free jobs;
- struct job versus delegate callback;
- generated direct pointer/span plan versus runtime typed-column lookup;
- scalar versus explicit SIMD;
- branch-free dense work versus sparse branch-heavy work;
- random lookup/query crossover.

### 8.5 Add structural scenarios

- scalar and bulk prefab spawn;
- same-archetype and mixed-archetype creation;
- ordered and shuffled destruction;
- full-chunk destruction;
- mixed add/remove/destroy conflicts;
- transition with 1, 4, 16, and 32 retained components;
- enable-bit toggle versus migration;
- commands from 1 through all workers;
- command capacity at 50%, 90%, 100%, and overflow;
- barrier budget deferral;
- failure/fault path cost measured separately.

### 8.6 Add change-tracking scenarios

- dense chunk version mark;
- sparse dirty bitsets at 1%, 10%, 35%, and 90%;
- append-list comparison at the same densities;
- one and many component writers;
- one, two, and eight independent consumers;
- unchanged-chunk scans;
- lifecycle event rings;
- consumer lag and backpressure;
- direct changed-column serialization;
- Current drain-plus-lookup versus Next version/bitset traversal.

### 8.7 Add scheduler scenarios

- empty tick overhead;
- one trivial system;
- many trivial systems;
- dependency chain;
- wide parallel layer;
- mixed chain/layer graph;
- 1/2/4/physical/logical worker scaling;
- static partition, atomic work index, and work stealing;
- spin duration/park policy;
- fused and unfused pipelines;
- adaptive threshold crossover;
- global atomic reduction versus worker-local reduction;
- co-running network workload to detect oversubscription.

### 8.8 Add integration scenarios

- script catalog build as a cold benchmark;
- script-module configure/generator/cache-manifest build;
- runtime content-group mount, unmount, dependency rejection, and rollback;
- data-authored prefab parse, validate, bind, and atomic registry publication;
- hot script-prefab spawn by numeric ID;
- dormant and proximity-activated spawner populations plus activation-wave bulk spawning;
- movement input through authoritative state output;
- changed ECS data through latest-state AOI fanout;
- ordered lifecycle events through event fanout;
- stable replication identity while crossing AOI rings and scope ownership;
- broadened bot profile with `Realtime`, `ReliableEvent`, `Bulk`, `0.35`/`0.40` active movement, three rings/cadences, dense hotspot, and real client consume/apply;
- full persistence capture/write/read/restore;
- save coordinator with compression and backup;
- serial versus parallel persistence participant snapshot/write coordination;
- character/account load followed by durable ECS/inventory reference remap;
- inventory durable-reference resolution;
- operator inspection while simulation runs;
- batched admin telemetry at 100/250/1000 ms and multiple open inspectors;
- network bot harness using Current and Next movement backends.

### 8.9 Soak matrix

Run at least:

- serial read-heavy;
- parallel read-heavy;
- serial write-heavy;
- parallel write-heavy;
- structural churn;
- prefab spawn/despawn churn;
- mixed movement, networking, and persistence checkpoint workload;
- slow change consumer;
- maximum configured entity population;
- many-world creation/disposal loop.

Report:

- allocated bytes per interval;
- Gen0/1/2 collections;
- managed heap before/after final collection;
- native reserved and committed bytes;
- working set;
- chunk counts and occupancy;
- command/change high-water marks;
- mean/p50/p95/p99/p99.9/max tick;
- worker utilization and barrier wait;
- canonical state digest at intervals.

Run short soaks in CI and longer scheduled soaks outside normal PR validation.

### 8.10 Hardware-counter and disassembly work

For designated kernels, capture:

- instructions and cycles;
- cycles per entity;
- IPC;
- L1/L2/LLC misses;
- branch misses;
- context switches and CPU migrations;
- stalled frontend/backend cycles where available;
- generated/JIT assembly;
- vector instruction selection;
- code size for fused versus unfused jobs.

Do not broadly add `AggressiveInlining`, unsafe reference arithmetic, or SIMD without this evidence. Tests must continue validating scalar and optimized path equivalence.

## 9. Proposed Next Repository Shape

Because the prior implementation is now isolated under `_V2/`, Next can use the canonical root project names from its first commit:

```text
ECS_NEXT_DESIGN.md             # Root implementation specification retained during the rewrite
Kernels/ECS/
    Core/
    Memory/
    Storage/
    Entities/
    Queries/
    Structural/
    Changes/
    Scheduling/
    Prefabs/
    Persistence/
    Inspection/
    Generated/
    UltraSim.ECS.csproj
Kernels/ECS.Generators/
    Components/
    Queries/
    Systems/
    Prefabs/
    Persistence/

_Tests/
  UltraSim.ECS.Tests/
  UltraSim.ECS.Differential.Tests/
  UltraSim.ECS.Integration.Tests/

_Benchmarks/
  UltraSim.ECS.Benchmarks/
  UltraSim.ECS.Compare/

_V2/                           # Frozen comparison tree; never linked into Next hot paths
  Kernels/ECS/
  _Tests/UltraSim.ECS.Tests/
  _Benchmarks/UltraSim.ECS.NewBenchmarks/  # V2 Current runner; historical name retained
  _Benchmarks/UltraSim.ECS.OldBenchmarks/  # Legacy runner
```

The outer integration topology remains one portable runtime with thin entry points:

```text
Server/
  Core/                        # Engine-agnostic application runtime
  Hosting/                     # Shared lifecycle/fixed-step/control surface
  Network/                     # Engine-agnostic ECS/network bridge
  _Console/                    # RunUO-style command-line wrapper
  _Headless/                   # Silent dedicated/internal/embedded wrapper
  _Godot/                      # Godot operator terminal wrapper

Client/
  Core/                        # Engine-agnostic projected client runtime
  Network/                     # Engine-agnostic client/network bridge
  _Godot/                      # Presentation/input/audio/resource wrapper
```

ECS Next plugs into the portable `Server`/`Client` layers. It does not grow separate Console, Headless, Godot, or single-player ECS implementations. Single-player composes the ordinary `_Headless` server plus ordinary client and connects them through the normal localhost network/session boundary.

The confusing existing use of “NewBenchmarks” for V2 Current should be resolved in report labels before three-way comparisons begin. Renaming the frozen V2 project is unnecessary; stable report metadata, generation labels, semantic/profile hashes, and explicit executable paths are mandatory.

## 10. Implementation Phases and Gates

### Phase 0: freeze baselines and make comparisons trustworthy

Deliverables:

- stable Legacy, Current, and Next report labels;
- baseline commits and environment metadata;
- extracted common scenario definitions;
- canonical state snapshot format for differential tests;
- empty benchmark baseline;
- corrected generated benchmark summary;
- regression tests for known Current correctness defects, marked as expected Current failures where necessary.

Gate:

- Legacy and Current results reproduce within an understood noise band;
- all existing Current tests remain green;
- known failures are explicit, not hidden.

Tests may reveal that an existing benchmark is not semantically comparable. Mark it noncomparable or repair the scenario before using it as a gate.

### Phase 1: generated schema, capacity plan, and memory primitives

Implementation status as of 2026-08-04:

- implemented: generated component manifest, deterministic component/schema identity, runtime layout/tag/enableable/persistence metadata, schema-local lookup, fixed entity-slot pool, four-byte world-local archetype IDs, 12-byte archetype/chunk/row entity locations, checked location bind/unbind and donor relocation, canonical bounded archetype registration, stable collision-checked signature lookup, dense ID-to-store routing, sealed concurrent reads, exact arena ownership, checked chunk/archetype/world capacity planners, native-aligned and pinned-managed fixed arena candidates, generational chunk slots, typed SoA/entity/enable access, clear-on-acquire reuse, disposal/payload accounting, row-owning executable archetype chunks, full/partial/empty lists, retained-empty recycling, bulk default row reservation, within/cross-chunk swap removal, cold-built prebound transition plans, transactional cross-archetype migration with rollback and donor repair, a sealed minimal internal world with capacity-atomic create/transition/destroy/disposal and cross-world plan isolation, 16-byte generated-ID-backed exact-archetype component bindings with copied read/write/enable/initialization paths, native/pinned randomized row/migration/world/access model tests, integrated randomized pool/store tests, primitive allocation tests, generator golden/diagnostic tests, and initial metadata/entity/layout/arena/row/registry/migration/world/access benchmarks;
- still required: a public scheduler/host world facade with initialized/running/faulted states, persistent field IDs and richer descriptors, barrier-scoped generated safe chunk/query bindings, bounded deferred structural commands, final entity-metadata packing decision, longer native leak/retention soaks, and comparable Current/Next scenario adapters;
- current measurements are directional only. The implemented tests require zero allocation for warmed lookup/entity/chunk/location/registry cycles, and the first native-versus-pinned comparison is a statistical tie. No Phase 1 layout/backend passes the gate until repeated isolated-process, large-footprint, and Current/Next comparisons justify it.

Deliverables:

- source-generated component manifest;
- world/schema identity;
- capacity planner;
- native or fixed managed arena prototype;
- fixed chunk layout prototype;
- packed entity metadata;
- disposal and native-memory accounting.

Tests:

- generator golden tests;
- arena boundary/alignment tests;
- entity model/fuzz tests;
- multi-world tests;
- disposal tests.

Benchmarks:

- metadata lookup;
- allocation/free;
- sequential scans;
- chunk-size/layout matrix;
- memory per entity.

Gate:

- no correctness failures against the reference model;
- selected layout justified by measurements at small and large scales;
- no native leaks under repeated world construction/disposal.

### Phase 2: typed generated queries and jobs

Deliverables:

- typed chunk views;
- struct jobs;
- generated query filters;
- read/write access enforcement;
- cached random component lookup;
- dense change-version hooks initially stubbed or recorded.

Tests:

- reference scan comparison;
- query filter matrix;
- no untracked writes;
- no boxing/closure allocation;
- Current/Next read result parity.

Benchmarks:

- full query matrix;
- delegate versus struct job;
- typed lookup versus Current column dispatch;
- SIMD experiments.

Gate:

- zero allocation for repeated hot queries;
- Next matches Current and the reference model;
- designated sequential queries meet or beat Current, or the discrepancy is understood before continuing.

### Phase 3: structural commands and migration

Deliverables:

- per-worker command arenas;
- deterministic merge;
- bulk create/destroy/add/remove;
- generated transitions;
- chunk compaction/recycling;
- enableable tags;
- capacity/overflow/fault policies.

Tests:

- port every Current structural test;
- add known-regression tests;
- model-based structural fuzzing;
- parallel command determinism;
- failure injection.

Benchmarks:

- existing structural matrix plus new command/chunk cases;
- allocation and p99 barrier latency;
- Current versus Next transition throughput.

Gate:

- zero normal-path barrier allocation;
- all structural state digests match the reference model;
- no stale default values;
- Next materially improves key churn/migration scenarios or tests lead to an alternative algorithm.

### Phase 4: change tracking and lifecycle streams

Deliverables:

- chunk component versions;
- sparse bitsets;
- per-consumer cursors;
- lifecycle event rings;
- tombstones;
- backpressure policy.

Tests:

- exact dense/sparse changes;
- independent consumers;
- partial reads;
- consumer lag;
- wrap and overflow;
- destroy/remove semantics.

Benchmarks:

- density crossover matrix;
- Current dirty sets versus versions/bitsets;
- direct serialization path.

Gate:

- zero allocation;
- no lost changes;
- selected sparse representation determined by measurements rather than assumption.

### Phase 5: scheduler and worker runtime

Deliverables:

- generated graph;
- dependency-correct batching;
- allocation-free dispatcher;
- static partitioning and optional work stealing;
- adaptive thresholds;
- per-worker reductions;
- fusion prototype;
- lifecycle/disposal/fault handling.

Tests:

- port all Current scheduler tests;
- add parallel dependency regression;
- serial/parallel parity over long replays;
- worker fault/disposal tests;
- fused/unfused equivalence.

Benchmarks:

- scheduler matrix and worker scaling;
- wake/barrier latency;
- V1 and oscillating pipelines;
- co-running network load.

Gate:

- zero scheduler allocation after warmup;
- dependencies correct in every mode;
- automatic mode never chooses a repeatedly demonstrated losing parallel path without an explicit override;
- heavy scenarios scale without unacceptable p99 regression.

### Phase 6: prefabs and scripting

Deliverables:

- preserved RunUO-style archetype-base and concrete-prefab authoring facade;
- inheritance and reusable shape-fragment resolution;
- transient allocation-free item/mobile instance contexts;
- prebound rare-event routes and batch-system lowering policy;
- generated/bound prefab plans;
- numeric prefab IDs;
- restricted row/range writer;
- true bulk spawn;
- script-definition compiler adapter;
- script-compile-time component metadata/serializer/prefab-writer generation and cache-manifest integration;
- immutable content generations with atomic publish/rollback;
- unified origin/provenance/revision metadata and canonical key/alias policy;
- runtime content-group ownership plus transactional mount/unmount;
- live data-authored prefab descriptor import, edit-lease/revision validation, and atomic registry publication seam;
- typed constant and per-spawn initializer plans without boxing;
- deterministic per-worker spawn RNG;
- explicit value-edit, removal, shape-change, and live-entity migration policies;
- failure transaction policy.

Tests:

- port Current prefab and scripting suites;
- retain inherited item and mobile authoring fixtures;
- compare Current and Next canonical shapes and values;
- instance-context tracked mutation and event-routing fixtures;
- compile/reload atomicity and rollback;
- removal and base-shape invalidation;
- cross-origin duplicate keys, canonical aliases, and persisted-key lookup;
- content-group cleanup failure/dependency rollback;
- data-prefab stale edit, invalid reference, disable/erase, and registry rollback;
- scalar/bulk parity;
- rollback/failure tests;
- allocation tests;
- differential script fixtures.

Benchmarks:

- prefab matrix;
- script cold build and hot spawn separated;
- content-group and data-prefab cold publication separated from hot numeric-ID spawn;
- spawn/despawn soak.

Gate:

- all current prefab behaviors represented or explicitly rejected;
- content authors retain the class-like base-shape/concrete-prefab workflow;
- runtime entities remain allocation-free ECS data rather than managed script objects;
- zero hot-spawn allocation;
- no raw archetype mutation escape.

### Phase 7: persistence and durable identity

Deliverables:

- generated serializers/migrations;
- bounded decoder;
- transactional staging/commit;
- durable entity IDs/remapping;
- explicit runtime-versus-durable entity reference contracts and bridge maps;
- incremental chunk snapshots;
- server coordinator participant.

Tests:

- golden files;
- corrupt-input fuzzing;
- transactional failure;
- migration matrix;
- backup/recovery integration;
- inventory durable-reference integration;
- accounting/character identity, canonical prefab-key provenance, disconnect/restart/remap integration.

Benchmarks:

- full/incremental capture and restore;
- peak memory;
- Current versus Next serialization and commit;
- end-to-end coordinator timing.

Gate:

- no malformed input can trigger unbounded allocation;
- restore is atomic;
- durable references survive restart;
- server recovery tests pass.

### Phase 8: networking, AOI, inspection, and host integration

Deliverables:

- direct changed-chunk serializers;
- lifecycle tombstones;
- per-publisher cursors;
- Current rule adapters or replacement policy descriptors;
- AOI integration;
- scope-independent stable replication item keys and immutable publish-view leases;
- generated inspection metadata;
- generated admin field/control descriptors and typed safe-barrier edit commands;
- sampled/batched admin telemetry snapshots;
- spawner/spawn-set bridge with cached numeric plans, proximity activation, bulk spawn, and lifecycle reconciliation;
- engine-agnostic `Server/Core`/`Server/Hosting` runtime snapshot and control adapters;
- engine-agnostic `Client/Core` projection/apply adapters;
- thin `_Console`, `_Headless`, server `_Godot`, and client `_Godot` composition adapters over those shared surfaces;
- embedded/background `_Headless` single-player startup and normal localhost client-session composition;
- movement world backend selection.

Tests:

- latest-state and ordered-event semantics;
- removal/destruction cleanup;
- AOI scopes/cadence;
- AOI scope-transition identity and multi-lane baseline/delta/lifecycle semantics;
- Current/Next semantic publication comparison;
- bot/client/server loopback;
- inspection adapter tests;
- admin permission/audit/edit/telemetry coalescing tests;
- project-reference and forbidden Godot/wrapper dependency tests;
- deterministic canonical-world parity across `_Headless`, `_Console`, and server `_Godot` wrappers;
- proof that console commands and Godot controls use the same typed safe-barrier command path;
- single-player localhost spawn/movement/save/disconnect/shutdown without shared server/client ECS storage or direct calls;
- local startup failure/crash/cancellation/reconnect disposal tests.

Benchmarks:

- publish/fanout matrix;
- AOI density and subscriber scaling;
- movement loopback and bot harness;
- operator inspection under load;
- dormant-spawner, activation-wave, and lifecycle-reconciliation workloads;
- Current and Next server backends under identical traffic replay;
- engine-agnostic runtime alone versus `_Headless`, `_Console`, and `_Godot` wrapper overhead;
- embedded/background versus separate-process local server resource contention;
- real localhost network versus loopback transport with semantic parity labels.

Gate:

- no stale replicated state after remove/destroy;
- zero ECS-side publication allocation within capacity;
- Next matches or beats Current end-to-end latency and allocation targets;
- wrapper choice does not change authoritative ECS semantics;
- single-player remains an ordinary server-authoritative networked composition.

### Phase 9: cutover and deletion decision

Deliverables:

- full parity matrix;
- final three-way reports;
- long soak reports;
- production-shaped replay reports;
- migration documentation;
- explicit list of intentionally removed APIs/behaviors;
- rollback plan during initial deployment.

Cutover gate:

- all required tests green;
- no unexplained canonical-state divergence;
- zero steady-state ECS allocation in designated workloads;
- no unmanaged memory leak;
- Next meets agreed p95/p99/max latency gates;
- Next meets Current on protected scenarios and materially wins the target bottlenecks;
- server scripting, movement, networking, persistence, inventory, and operator integrations pass;
- observed evidence supports the architecture. If it does not, revise the architecture rather than forcing the cutover.

## 11. Feature Parity Checklist

Use this checklist in issue tracking. Each item requires tests and, when performance-sensitive, a three-way benchmark.

### Core

- [x] Generated component registration and metadata
- [ ] Multiple isolated worlds
- [x] World/runtime identity
- [x] Generational entity handles
- [ ] Durable entity identity/remapping
- [x] Fixed chunk storage prototype
- [x] SoA column layout and internal typed arena access
- [x] Storage-free tag layout
- [x] Enableable component/tag row semantics and internal word access
- [ ] Archetype lookup and transitions
- [ ] Immediate create/destroy
- [ ] Deferred bulk structural commands
- [ ] Explicit lifecycle and disposal
- [x] Capacity planning and primitive diagnostics
- [ ] Faulted-world behavior

### Queries and mutation

- [ ] Random read lookup
- [ ] Random tracked write lookup
- [ ] Required component filters
- [ ] Excluded component filters
- [ ] Optional component filters
- [ ] Enabled/disabled filters
- [ ] Changed-since filters
- [ ] Entity-aware and entity-free jobs
- [ ] Dense tracked writes
- [ ] Sparse tracked writes
- [ ] Parallel chunk jobs
- [ ] No public untracked mutation path

### Scheduling

- [ ] All current stages
- [ ] Cadence and offsets
- [ ] Phase staggering
- [ ] Manual systems
- [ ] Read/write conflict graph
- [ ] Required dependencies
- [ ] Cycle/missing dependency validation
- [ ] Serial execution
- [ ] Parallel execution
- [ ] Adaptive granularity
- [ ] System fusion
- [ ] Per-worker reductions
- [ ] Profiling/runtime snapshots
- [ ] Control metadata

### Change tracking

- [ ] Dense chunk versions
- [ ] Sparse row masks
- [ ] Created events
- [ ] Added-component events
- [ ] Removed-component tombstones
- [ ] Destroyed-entity tombstones
- [ ] Independent consumer cursors
- [ ] Partial reads without loss
- [ ] Backpressure/overflow policy

### Prefabs and scripting

- [ ] RunUO-style class authoring facade
- [ ] Inherited wide archetype/base classes
- [ ] Reusable shape fragments/traits
- [ ] Concrete prefab-to-archetype binding
- [ ] Allocation-free transient item/mobile instance facade
- [ ] Tracked instance-style set/add/remove operations
- [ ] Prebound rare script-event handlers
- [ ] High-volume behavior lowering to batch systems
- [ ] Typed prefabs
- [ ] Template prefabs
- [ ] Resolved/bound prefabs
- [ ] Data-driven prefabs
- [ ] Script prefab catalog
- [ ] Core and script-compile-time generated component/prefab metadata
- [ ] Unified built-in/script/data/plugin provenance and canonical keys
- [ ] Runtime content-group ownership
- [ ] Transactional content-group mount/unmount
- [ ] Data-prefab revisions, edit leases, atomic publication, and diagnostics
- [ ] Immediate spawn
- [ ] Deferred spawn
- [ ] True bulk spawn
- [ ] Numeric hot-path prefab IDs
- [ ] Immutable content generations
- [ ] Atomic script reload/rollback
- [ ] Prefab removal semantics
- [ ] Existing-entity policy after prefab edit/removal/unmount
- [ ] Base-shape change invalidation/migration policy
- [ ] Typed allocation-free per-spawn value generators
- [ ] Deterministic spawn RNG
- [ ] Spawn rollback/failure policy

### Persistence

- [ ] Stable persistence IDs
- [ ] Generated component serializers
- [ ] Component schema versions/migrations
- [ ] Binary reader/writer
- [ ] Full snapshot
- [ ] Incremental snapshot
- [ ] Bounded validation
- [ ] Transactional restore
- [ ] Replace/merge/append modes
- [ ] Durable reference remap
- [ ] System-owned persistence hooks or replacement
- [ ] Server manifest/backup/recovery adapter

### Integrations and tools

- [ ] Latest-state replication
- [ ] Ordered-event replication
- [ ] AOI rings and cadence
- [ ] Movement input/state bridge
- [ ] Inventory durable references
- [ ] Runtime-versus-durable entity reference mapping
- [ ] Accounting/character durable identity remap
- [ ] Spawner/spawn-set stable key integration
- [ ] Spawner activation/lifecycle reconciliation
- [ ] Entity inspection
- [ ] System runtime inspection
- [ ] Generated component admin/edit metadata
- [ ] Typed permission/audit-safe admin edits
- [ ] Batched admin telemetry
- [ ] Scope-independent replication item identity
- [ ] Immutable network publish views
- [ ] Engine-agnostic `Server/Core` ECS integration
- [ ] Engine-agnostic `Server/Hosting` lifecycle/control integration
- [ ] Engine-agnostic `Server/Network` capture integration
- [ ] Engine-agnostic `Client/Core` projected-world integration
- [ ] Engine-agnostic `Client/Network` apply integration
- [ ] Thin RunUO-style `_Console` adapter
- [ ] Thin silent dedicated/internal/embedded `_Headless` adapter
- [ ] Thin server `_Godot` operator-terminal adapter
- [ ] Thin client `_Godot` presentation adapter
- [ ] No wrapper-specific ECS implementation or mutation path
- [ ] Canonical-world parity across server wrappers
- [ ] Single-player `_Headless` startup/readiness/localhost auto-connect
- [ ] Single-player normal handshake/authority/replication/persistence/cleanup path
- [ ] No direct client/server ECS storage or gameplay-call bypass
- [ ] Real localhost and loopback semantic trace comparison
- [ ] Wrapper and embedded-versus-process overhead benchmarks
- [ ] Capacity/layout planner
- [ ] Generator diagnostics
- [ ] Deterministic replay tool
- [ ] Legacy/Current/Next comparison report
- [ ] Short and long GC/native-memory soaks

## 12. Definition of Done

Next is not done because it has a fast query demo. It is done when:

- the useful Current feature set has a tested Next equivalent;
- intentional behavior changes are documented and covered by regression tests;
- all required server integrations run against Next;
- the engine-agnostic server and client runtimes run against Next without Godot, console, headless-entry-point, or wrapper dependencies entering the ECS kernel;
- `_Console`, `_Headless`, and both `_Godot` entry points remain thin adapters over shared lifecycle, control, capture, and apply surfaces rather than alternative ECS implementations;
- single-player starts or attaches to the ordinary silent `_Headless` server and connects the ordinary client over localhost without sharing authoritative ECS storage or bypassing network/session semantics;
- wrapper parity tests produce identical canonical world results for identical deterministic inputs;
- old, current, and new benchmark runners remain directly comparable;
- hot ticks allocate zero managed ECS bytes after sealing;
- structural and prefab paths allocate zero within configured capacity;
- dirty/change tracking supports multiple consumers and lifecycle tombstones;
- persistence is bounded and transactional;
- runtime and durable identity domains cannot be confused or silently cross-resolved;
- script/content generation publication, mount/unmount, and data-prefab edits have tested atomic failure behavior;
- canonical prefab/spawn-set keys survive content generation changes while numeric IDs remain hot-path-local;
- spawner, accounting/character, inventory, network, and admin bridges pass their lifecycle/identity fixtures;
- parallel dependencies are correct;
- world/worker/native-memory lifecycle is explicit and leak-free;
- sustained soaks show stable allocation, retention, fragmentation, and tail latency;
- Next is at least as fast as Current on protected scenarios and faster on the workloads motivating the rewrite;
- tests and measurements—not attachment to this proposal—support the final choices.

The expected outcome is a generated, chunked, arena-backed, test-driven ECS whose general feature set remains recognizable from Current, while the implementation is designed from the beginning for zero steady-state allocation, direct data flow, predictable memory, and high-performance serial and parallel execution.

## 13. Repository-Wide Markdown Audit

### 13.1 Scope and method

The audit covered all 37 Markdown files in the repository at the time of review: this specification plus all 36 supporting documents. The pre-update corpus was approximately 12,100 lines, of which approximately 10,200 lines were outside this specification. Every supporting file was read, including files with no direct `ECS` keyword, because runtime identity, prefab ownership, save ordering, replication semantics, admin metadata, and benchmark expectations are frequently specified in bridge or UI plans.

The supporting documents were:

| Area | Files read | Relevance to ECS Next |
| --- | --- | --- |
| Current ECS | `_V2/Kernels/ECS/README.md`; `_V2/Kernels/ECS/Extensions/Prefabs/README.md`; `_V2/Kernels/ECS/Extensions/Persistence/README.md` | Current kernel surface, prefab state, persistence state, known defers |
| Kernel boundaries and Network | `_V2/Kernels/KERNELS.md`; `_V2/Kernels/Network/AGENTS.md`; `_V2/Kernels/Network/README.md`; `_V2/Kernels/Network/NETWORK_REWORK_IMPLEMENTATION_PLAN.md`; `_V2/Kernels/Network/NETWORK_PARITY_CHECKLIST.md` | Standalone-kernel rule, immutable handoffs, lanes, AOI, stable item identity, parity/harness gaps |
| Inventory and UI kernels | `_V2/Kernels/Inventory/AGENTS.md`; `_V2/Kernels/Inventory/README.md`; `_V2/Kernels/UI/AGENTS.md` | Runtime/durable owner refs, definition-versus-instance identity, admin/schema bridge requirements |
| Server core and bridges | `_V2/Server/AGENTS.md`; `_V2/Server/SERVER.md`; `_V2/Server/SERVER_WRAPPER_IMPLEMENTATION_PLAN.md`; `_V2/Server/Gameplay/README.md`; `_V2/Server/Inventory/README.md`; `_V2/Server/Persistence/README.md`; `_V2/Server/Accounting/README.md`; `_V2/Server/Admission/README.md`; `_V2/Server/Discovery/README.md`; `_V2/Server/CHAT_COMMAND_SYSTEM_PLAN.md` | Host lifecycle, movement, spawners, persistence coordination, character identity, inventory refs, permissions, network classification |
| Script/content runtime | `_V2/Server/SCRIPT_COMPILER_AND_RUNTIME_LAYOUT_PLAN.md`; `_V2/Server/RuntimeTemplate/README.md`; `_V2/Server/RuntimeTemplate/scripts/README.md`; `_V2/Server/RuntimeTemplate/scripts/Packages/Admission/README.md` | RunUO facade intent, module lifecycle, content groups, prefab keys/origins, live data authoring, script cache/generation |
| Client, UI, and content | `_V2/Client/AGENTS.md`; `_V2/Client/README.md`; `_V2/Client/CLIENT_IMPLEMENTATION_PLAN.md`; `_V2/Client/GODOT_CLIENT_SHELL_ROADMAP.md`; `_V2/Client/UI/README.md`; `_V2/Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md`; `_V2/Client/CONTENT_PACK_PIPELINE_PLAN.md`; `_V2/Client/_Godot/ASSET_LAYOUT.md` | ECS inspector/control-panel needs, generated admin metadata, stable logical content IDs, state binding, content provenance |
| Benchmarks and operations | `_V2/_Benchmarks/README.md`; `_V2/_Benchmarks/UltraSim.Network.BotHarness/README.md`; `_V2/Services/Registry/README.md` | Existing ECS matrix, GC soak, widened network workload/telemetry, operational boundaries |

The audit does not convert every plan into a kernel requirement. It traces which layer owns each need and ensures the ECS exposes a fast neutral seam where necessary.

### 13.2 What the documents confirm about Current

The documents reinforce and clarify these verified Current behaviors:

1. **The prefab authoring intent is exactly the RunUO-style facade described in Section 4.9.** `ArchetypeShape` inheritance builds broad additive component sets; a concrete `PrefabDefinition<TShape>` fills values and spawns a normal ECS entity. The runtime-template mobile package documents `BaseMobile -> BaseHumanoid -> Human` plus `mobiles/human`. This is not an accidental API shape and should be preserved as an authoring experience.
2. **Scripts already register real ECS definitions rather than parallel script entities.** The script host compiles trusted server C#, discovers explicit modules, builds archetype/prefab descriptors, bridges them into the ECS catalog, and proves spawning through the ordinary world path.
3. **Runtime content-group ownership is already more than a proposal.** The server registry tracks mounted groups and reverse-order cleanup. Commands, UI providers, archetypes, and prefabs can be group-owned, and archetype removal is dependency-checked. Its failure path is not atomic: a late cleanup exception can leave earlier registrations removed while the group remains marked mounted. ECS Next must retain the feature and fix that partial-unmount state even though most ownership stays above the kernel.
4. **The server save coordinator is more mature than the narrow ECS persistence README alone suggests.** It stages complete save sets, promotes atomically, rotates backups, validates participant paths/artifacts, supports recovery modes/restore, orders participants, and records timing. Next needs a replacement ECS participant, not a competing save coordinator.
5. **The network bridge already classifies ECS-origin state across three workload lanes.** Continuous state uses `Realtime`, discrete ordered facts use `ReliableEvent`, and large/lower-cadence state uses `Bulk`. Player-centered AOI rings and cadence are higher-layer policy. The widened harness already measures client decode/apply plus allocations/GC, not only server publish cost.
6. **Stable replication item identity across AOI scopes has already produced a measured structural win.** The same logical observed item must not change key merely because it moves between ring scopes. This requirement belongs in the bridge key contract and is now explicit in Section 5.4.
7. **Packed ECS inventory handles are deliberately generational runtime refs.** They preserve version bits specifically to reject stale slot reuse. The gap is not that all packed handles are wrong; it is that persistent/offline/cross-world ownership also needs a separate durable reference. Section 5.6 now makes that distinction.
8. **Accounting already provides durable external identity.** Character records carry stable `CharacterId`, account ownership, canonical prefab key, and last transform. This confirms that runtime ECS handles, character identity, prefab identity, and network item identity must remain separate mapped domains.
9. **Raw read-only ECS inspection exists today.** The server facade adds access checks. Generated component admin metadata, typed edits, rich ECS control panels, telemetry batching, and live builders are mostly planned work, not Current kernel features.
10. **Spawner primitives exist, but execution does not.** ECS components/runtime state and a stable spawn-set hash registry are present. Spawn execution, package reload, smart proximity activation, editing, and persistence remain planned features.
11. **The actual server is engine-agnostic.** `Server/Core`, `Server/Hosting`, `Server/Network`, and the other portable server services form the implementation. `_Console` is the RunUO-style command-line shell, `_Headless` is the silent dedicated/internal/embedded shell, and server `_Godot` is an operator terminal/control presentation over the same runtime.
12. **The actual client is engine-agnostic and single-player remains networked.** `Client/Core` and `Client/Network` own the neutral client runtime. Client `_Godot` owns presentation. Local play starts or attaches a silent `_Headless` server and connects the normal client session over localhost rather than sharing the authoritative ECS or calling server gameplay directly.

### 13.3 Planned features that materially affect the rebuild

The Markdown plans revealed or strengthened requirements that were not explicit enough in the first draft of this specification:

- explicit script `Configure`/`Initialize` phases relative to schema sealing and world load;
- build-time and script-compile-time generation paths for component, serializer, prefab, replication, inspection, and admin metadata;
- unified prefab provenance across built-in, script, data, generated, and future plugin origins;
- canonical stable prefab keys for persistence/account records, with dense generation-local numeric IDs only for hot runtime use;
- transactional runtime content-group mount/unmount and dependency rollback;
- versioned live data-authored prefabs with edit leases, revision hashes, atomic file/registry publication, auditing, invalid-reference diagnostics, and explicit existing-entity migration policy;
- efficient spawn-set/spawner integration with stable durable keys, cached numeric plans, bulk spawn, and lifecycle-based reconciliation;
- explicit `RuntimeEntityRef` versus `DurableEntityRef` contracts and server-owned mapping among character, ECS, network, and inventory identities;
- scope-independent replication item keys, immutable publish views, and no background reads of live ECS storage;
- a single generated component-field metadata source feeding persistence, migration, inspection, admin editing, replication policy, and optional UI invalidation hints;
- typed audited safe-barrier admin edits rather than reflection setters;
- sampled/batched admin telemetry rather than per-system/per-tick UI publication;
- explicit portable server/client runtime boundaries below `_Console`, `_Headless`, and `_Godot` composition roots;
- one shared typed lifecycle/control/capture/apply surface across wrappers;
- single-player `_Headless` readiness and normal localhost session composition with no direct ECS/gameplay bypass;
- wrapper-parity, forbidden-dependency, local failure/cleanup, and embedded-versus-process resource tests;
- integration benchmarks that reproduce the widened network workload, content lifecycle, data-prefab publication, spawner waves, identity remap, and live inspector cost.

These additions are now incorporated into Sections 4-11 rather than left as disconnected future notes.

### 13.4 Important planned features that are not Current ECS features

To prevent the feature list from overstating Current, the following remain planned or partial:

- live data-authored prefab files and in-game item/mobile builders;
- live addition of new component types without restart/staged schema replacement;
- collectible script assembly reload and unload;
- full spawner execution, smart activation, package reload, and spawner persistence;
- generated component serializers/migrations/admin metadata;
- durable entity identity/remapping across world restart;
- component/admin editing through typed audited commands;
- the complete ECS admin/control panel;
- fully mature network parity, dense-hotspot parity, and deep field-level observer deltas;
- asynchronous/background ECS snapshot capture with proven bounded tick impact;
- direct zero-allocation changed-chunk replication from Next storage.

Each should enter the comparison matrix only when its Current or Next implementation is executable and tested. A plan paragraph is not benchmark parity.

### 13.5 Requirements that must stay outside ECS

The audit also prevents scope creep. ECS Next must not absorb:

- Network transport, packet lanes, AOI gameplay policy, session cursors, or fanout;
- Inventory permission, capacity, equipment, transaction, owner-lifetime, or viewer policy;
- UI schema rendering, panel state, permissions, state-binding resolvers, or focus/navigation;
- account credential policy, character-slot policy, or account persistence;
- server save-set backup/retention/operator policy;
- script compilation, module discovery, assembly load contexts, or package UI/command registries;
- client asset-pack trust, cache, model/material, or presentation resolution;
- Godot or wrapper lifecycle behavior.

The ECS contribution is a high-performance neutral contract: stable typed data, live/durable identity hooks, immutable/leased snapshots, generated descriptors, transactional structural and content barriers, and allocation-free extraction paths. Bridges retain domain authority.

### 13.6 Documentation and test maintenance

The audit found normal documentation drift: some tracked test-count prose and generated benchmark summaries do not match the latest executable outputs, and several long implementation plans mix historical next steps with completed status. This does not invalidate their design intent, but it makes manual status prose unsafe as a performance gate.

Next should maintain a generated traceability report with one row per feature:

| Feature | Intent source | Current code/test | Next code/test | Legacy/Current/Next benchmark | Status |
| --- | --- | --- | --- | --- | --- |

Generate test counts and benchmark summary tables from result artifacts. Keep prose for interpretation, ownership, and decisions. Tests determine whether behavior exists; benchmark JSON determines measured performance; the traceability report makes missing or planned-only features visible.
