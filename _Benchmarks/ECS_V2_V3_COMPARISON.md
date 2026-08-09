# ECS V2/V3 Semantic Comparison Adapter

Status: executable Current/Next baseline, protocol
`ecs-v2-v3-semantic-comparison-v1`, captured 2026-08-09

This adapter compares the frozen V2 Current ECS with the root V3 Next ECS only
where both can perform the same useful work and reach the same normalized final
state. It is a regression baseline for adding V3 features; it is not a claim that
V3 is already a complete V2 replacement.

## Projects and process isolation

- `UltraSim.ECS.ComparisonCore` owns the shared catalog, report schema,
  measurement harness, logical-state writer, 128-bit canonical hash, and strict
  report joiner.
- `UltraSim.ECS.V2ComparisonAdapter` compiles the frozen `_V2/Kernels/ECS`
  sources into a child executable.
- `UltraSim.ECS.V3ComparisonAdapter` references the root V3 kernel. Its assembly
  uses the existing benchmark friend identity so it can measure the internal
  generated query surface without making benchmark-only APIs public.
- `UltraSim.ECS.Comparison` runs each generation in a separate process and joins
  results by stable scenario ID plus scale.
- `UltraSim.ECS.Comparison.Tests` contains ten direct tests for hash sensitivity,
  ordering, mismatch suppression, unsupported-row retention, descriptor
  identity, environment rejection, saved-baseline ratios, and cross-time state
  drift.

Separate processes are mandatory because both kernels intentionally use the same
`UltraSim.ECS` namespaces and type names. The joiner refuses different protocol,
descriptor, runtime, architecture, tiering, ReadyToRun, or GC settings. It does
not publish a trustworthy comparison when canonical state differs.

## Canonical state contract

Setup and timed execution use generation-local entity handles, archetype IDs,
rows, and command representations. Validation converts the result to a common
logical stream containing:

- scenario ID and scale;
- entities in benchmark-local ordinal order;
- alive/stale state and normalized lifecycle revision;
- logical component signature;
- Position, Velocity, Health, and Scalar values when present;
- normalized enable-state bits;
- populated logical archetype signature and population;
- normalized changed-entity ordinals;
- verified slot-reuse count.

The hash deliberately excludes world IDs, physical entity indices, raw generation
starting values, internal archetype IDs, chunks, packed rows, allocator addresses,
and backend identity. Those differ by implementation and are validated by each
kernel's own tests. Lifecycle workloads separately prove that all old handles are
stale, all freed slots are reused, and replacement generations advanced before
writing the normalized revision.

Every warmup and measured iteration builds a fresh fixture, executes once, then
captures and validates its complete logical state outside timing. Fixture setup,
JIT preparation, forced pre-iteration collection, canonical hashing, validation,
and disposal are excluded. The timed region records current-thread allocation,
Gen0/1/2 collections, checksum, elapsed distribution, operations, and effective
logical bytes where meaningful.

The forced full collection before each measured iteration isolates setup and the
fresh fixture from the timed boundary; it does **not** model production GC cadence
or prove the full server can remain in a no-GC region. Allocation accounting is
`GC.GetAllocatedBytesForCurrentThread`, which is complete for the current
single-threaded scenarios. Scheduler/worker cases must add process- or worker-wide
accounting rather than reusing that claim.

## Current coverage

The executable matrix uses 1,000, 10,000, and 50,000 entities and currently
contains 87 cross-generation rows:

- checked random Position read and tracked write;
- V3 callback-bounded prepared read/write jobs against the closest V2 path;
- warmed one- and two-column read traversal;
- warmed Position-read/Velocity-write traversal;
- immediate default create and immediate destroy;
- deferred create, full destroy, sparse ten-percent destroy, add-default, and
  remove, each split into record-only, playback-only, and end-to-end boundaries;
- add/remove migration round trips;
- destroy/create slot reuse and stale-handle validation;
- unchanged change polling;
- ten-percent and all-row Scalar write/changed-entity collection.

Fifteen parity rows remain explicitly unsupported in the joined report. They
cover V3's missing generated 3R0W family, typed add payloads, scheduler, prefab,
persistence, and lifecycle streams, plus semantics that V2 cannot reproduce such
as independent change cursors, multi-watch any/all queries, enable-bit row
filters, same-barrier deferred tokens, and fixed-capacity structural failure.
Unsupported rows must not disappear; they become measured only when a common
state contract is executable.

## Reproduction

Build and run the primary order:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --output-dir=_Benchmarks/results/ecs-v2-v3
```

Compare a future capture with the accepted baseline:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --baseline=_Benchmarks/Baselines/ecs-v2-v3-2026-08-09.json --output-dir=_Benchmarks/results/ecs-v2-v3-current
```

This emits both the cross-generation report and `ecs-v2-v3-regression.{json,md}`.
The latter reports current/baseline mean and p95 ratios, allocation deltas, and
independent V2/V3 canonical-state checks. Frozen V2 is rerun as the control: when
both generations move similarly, host noise is more likely; V3-only movement is
more likely to be caused by the rewrite. Catalog additions remain visible,
while state, descriptor, and measurement-policy mismatches suppress ratios.

The accepted primary artifact is
[`Baselines/ecs-v2-v3-2026-08-09.json`](Baselines/ecs-v2-v3-2026-08-09.json).
`--accept-baseline=PATH` creates a new immutable artifact and refuses to overwrite
an existing one.

Reverse child-process order as a noise/thermal check:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --v3-first --warmups=4 --iterations=15 --output-dir=_Benchmarks/results/ecs-v2-v3-reverse
```

The orchestrator forces `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0`
for both measured child processes. This represents fully optimized steady-state
server code and avoids the tier-promotion/order distortion observed in the first
diagnostic 2-warmup/9-iteration pass. Both settings are embedded in every report.
Use `--filter=...` and `--scale=...` for focused development checks, but do not
replace the complete two-order gate with a focused result.

## Accepted as-is baseline

The accepted V2-first capture and its V3-first verification on the
16-logical-processor .NET 8.0.29 host produced:

- 87 comparable rows;
- 0 canonical-state mismatches;
- 15 visible unsupported rows;
- zero timed managed allocation and zero Gen0/1/2 collections in all 87 V3 rows;
- zero timed allocation in 39 of 87 V2 rows; V2's remaining rows often trade
  substantially faster execution for dynamic allocation retained or reclaimed
  outside the timed call.

The current baseline does **not** show V3 generally beating V2. Using a 5% noise
band, the two process orders agree that V3 wins 6 rows, V2 wins 79, and 2 are
mixed/near the band. This is useful evidence, not a rewrite failure: V3 has
stronger boundedness, safety, independent change history, and zero-allocation
invariants, while several V2 batch paths are extremely compact and specialized.

The most repeatable 50,000-entity results were:

| Scenario | V2 ns/op | V3 ns/op | V3/V2 | Main interpretation |
| --- | ---: | ---: | ---: | --- |
| 1R1W query | 3.00 | 1.17 | 0.39x | V3 generated visitor is about 2.6x faster, zero allocation on both |
| Immediate create | 47.71 | 43.86 | 0.92x | V3 is about 8% faster and allocates 0; V2 allocates about 8.28 MB/iteration |
| 1R query | 0.44 | 0.57 | 1.31x | V3 read-only traversal still has a modest dispatch/view cost |
| 2R query | 0.86 | 1.00 | 1.16x | close enough to require continued isolated guards |
| Checked lookup read | 5.83 | 27.38 | 4.70x | V3 safe per-entity resolution is expensive |
| Checked lookup write | 24.28 | 31.84 | 1.31x | change stamping narrows the gap |
| Bounded lookup write | 24.14 | 28.12 | 1.16x | validating once helps, but entity route/stamp work remains |
| Deferred create playback | 32.25 | 45.71 | 1.42x | V3 allocates 0; V2 allocates about 3.61 MB |
| Immediate destroy | 29.56 | 72.30 | 2.45x | V3 scalar packed removal/location repair is a clear target |
| Full deferred-destroy playback | 2.41 | 72.50 | 30.06x | V2 batches whole-store destruction; V3 still replays scalar destroys |
| Sparse 10% destroy playback | 13.99 | 85.03 | 6.08x | bulk evacuation alone is insufficient; sparse source plans matter |
| Add-default playback | 20.03 | 82.70 | 4.13x | route-block destination work improved, source removal remains costly |
| Remove playback | 16.14 | 85.99 | 5.33x | same source-compaction/bookkeeping floor |
| Add/remove round trip | 20.01 | 89.35 | 4.46x | current narrow migration parity baseline |
| Ten-percent change write/collect | 27.05 | 47.83 | 1.77x | provisional: destructive V2 dirty set versus exact V3 cursor |
| All-row change write/collect | 25.66 | 32.53 | 1.27x | dense change cost is substantially closer |

The reversed-order ratios were materially consistent for the large gaps. Small
differences under roughly ten percent remain noise candidates and require more
isolated repetitions or hardware counters before becoming optimization claims.

## What this baseline says to optimize next

The adapter changes the optimization discussion from speculation to named
regression targets:

1. Deferred destroy needs phase-grouped source planning and bulk packed
   compaction. Whole-source/chunk evacuation should bypass scalar donor discovery,
   while sparse batches should group by source archetype/chunk and choose a
   measured scalar/plan crossover. The scalar command executor remains the
   differential oracle.
2. Transition compaction currently works on fixed 256-entity prefixes and only
   takes the donor-tail plan when that prefix contains the current tail. A large
   ascending homogeneous route therefore leaves most blocks on scalar source
   removal. Route-level planning across blocks is the natural continuation of
   v27, with exact history, conflict, capacity-prefix, and rollback differential
   tests before timing.
3. Fixed command recording writes and validates much richer per-command state
   than V2's block-copied lists. Consider bounded batch descriptors or a compact
   fixed target arena so span commands amortize validation and phase linkage
   without losing tokens, deterministic order, capacity failure, or the scalar
   oracle. Record-only benchmarks prevent this cost from hiding inside playback.
4. Safe random access should not be the default system traversal. Generated chunk
   jobs already win the representative write query. Keep random lookup for sparse
   relationships and quantify which remaining entity/location/route/change-stamp
   checks dominate bounded jobs before weakening safety.
5. Add the missing generated 3R0W family naturally, then turn the three currently
   unsupported V2 rows into canonical comparisons. Continue the working-set and
   effective-byte ladder so small cache-resident query results are not mistaken
   for large-world bandwidth.

Tests must accompany every optimization: scalar-versus-bulk randomized command
streams, stale/duplicate/conflict cases, partial capacity, exact/dense history,
enable state, donor ownership, generation wrap, Native/Pinned parity, allocation
gates, and canonical V2/V3 state. Tests may show that the expected next design is
wrong; the implementation and this document should follow the evidence.
