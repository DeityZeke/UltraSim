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
  disposal. The arenas remain internal until generated archetype/query bindings
  can prevent callers from requesting a same-sized but semantically wrong type.

`EntityPool` is intentionally single-owner. Parallel systems will submit
structural work through bounded per-worker command arenas; they will not mutate
the live pool concurrently.

The current runtime and primitive suite has 47 passing tests, including
randomized chunk-slot lifecycle parity, alignment/range/type rejection,
alignment extremes and concurrent arena-ID uniqueness, clear-on-reuse,
disposal/accounting, terminal generation retirement, and warmed zero-allocation
lifecycle and typed-access loops. The generator suite has 13 passing tests.

This is not yet a usable component/archetype world. The current generator emits
the core manifest only. The next storage slice must put row occupancy,
full/partial/empty lists, bulk row reservation, swap removal, and recycling over
the arena candidates. Field/serializer/query/prefab descriptors and a public
world lifecycle remain later work.

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
