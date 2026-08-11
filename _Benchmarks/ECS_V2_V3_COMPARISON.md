# ECS V2/V3 Semantic Comparison Adapter

Status: executable Current/Next baseline, protocol
`ecs-v2-v3-semantic-comparison-v1`, ECS 3.1.1 scheduler-hardening checkpoint captured
2026-08-11

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
- `UltraSim.ECS.Comparison.Tests` contains twelve direct tests for hash sensitivity,
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

WideBlock64 values are hashed as eight independent 64-bit fields. The hash
deliberately excludes world IDs, physical entity indices, raw generation
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
contains 102 comparable cross-generation rows:

- checked random Position read and tracked write;
- V3 callback-bounded prepared read/write jobs against the closest V2 path;
- warmed one- and two-column read traversal;
- warmed three-column read traversal;
- warmed Position-read/Velocity-write traversal;
- serial and four-participant parallel scheduled Position-read/Velocity-write
  ticks plus a four-participant `RangeJob`;
- immediate default create and immediate destroy;
- deferred create, full destroy, sparse ten-percent destroy, add-default, and
  remove, each split into record-only, playback-only, and end-to-end boundaries;
- provisional balanced ten-percent destroy/create churn in one 72-byte archetype,
  also split into record, playback, and end-to-end boundaries;
- add/remove migration round trips;
- destroy/create slot reuse and stale-handle validation;
- unchanged change polling;
- ten-percent and all-row Scalar write/changed-entity collection.
- typed Add payload publication.

Ten parity rows remain explicitly unsupported in the joined report. They cover
prefab, persistence, and lifecycle streams, plus semantics that V2 cannot
reproduce such as independent change cursors, multi-watch any/all queries,
enable-bit row filters, same-barrier deferred tokens, and fixed-capacity
structural failure.
Unsupported rows must not disappear; they become measured only when a common
state contract is executable.

## Reproduction

Build and run the primary order:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --output-dir=_Benchmarks/results/ecs-v2-v3
```

Compare a future capture with the current accepted baseline:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --baseline=_Benchmarks/Baselines/ecs-v2-v3-3.1.1-2026-08-11.json --output-dir=_Benchmarks/results/ecs-v2-v3-current
```

This emits both the cross-generation report and `ecs-v2-v3-regression.{json,md}`.
The latter reports absolute V2/V3 baseline/current means, current/baseline mean
and p95 ratios, a V2-control-normalized V3 signal, allocation deltas, and
independent V2/V3 canonical-state checks. Frozen V2 is rerun as the control: when
both generations move similarly, host noise is more likely; V3-only movement is
more likely to be caused by the rewrite. Catalog additions remain visible,
while state, descriptor, and measurement-policy mismatches suppress ratios.

The original accepted artifact is
[`Baselines/ecs-v2-v3-2026-08-09.json`](Baselines/ecs-v2-v3-2026-08-09.json).
The 3.0.31 consolidation artifact is
[`Baselines/ecs-v2-v3-v31-2026-08-10.json`](Baselines/ecs-v2-v3-v31-2026-08-10.json).
The 3.0.32 exact-whole-source artifact is
[`Baselines/ecs-v2-v3-3.0.32-2026-08-10.json`](Baselines/ecs-v2-v3-3.0.32-2026-08-10.json).
The previous 3.0.34 artifact is
[`Baselines/ecs-v2-v3-3.0.34-2026-08-10.json`](Baselines/ecs-v2-v3-3.0.34-2026-08-10.json).
The previous 3.0.35 artifact is
[`Baselines/ecs-v2-v3-3.0.35-2026-08-10.json`](Baselines/ecs-v2-v3-3.0.35-2026-08-10.json).
The current 3.0.36 artifact is
[`Baselines/ecs-v2-v3-3.0.36-2026-08-11.json`](Baselines/ecs-v2-v3-3.0.36-2026-08-11.json).
The previous 3.1.0 artifact is
[`Baselines/ecs-v2-v3-3.1.0-2026-08-11.json`](Baselines/ecs-v2-v3-3.1.0-2026-08-11.json).
The current 3.1.1 artifact is
[`Baselines/ecs-v2-v3-3.1.1-2026-08-11.json`](Baselines/ecs-v2-v3-3.1.1-2026-08-11.json).
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

## Original accepted as-is baseline

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

## 3.0.31 consolidation checkpoint (historical V31)

The V31 primary and reversed-order captures each produced 96 comparable rows,
zero state mismatches, 15 explicit unsupported rows, and zero V3 timed managed
allocation. The original 87 rows also retained zero cross-time state mismatches;
the nine catalog additions are the expected three scales by three timing
boundaries for balanced wide churn.

At 50,000 residents, balanced-wide playback measured V2/V3 at
`169.854/52.697` and `166.281/53.343 ns/operation`, about `3.22x/3.12x` in
V3's favor. End-to-end measured `168.730/61.752` and `167.588/62.455`, about
`2.73x/2.68x` faster for V3. V3 allocated zero; V2 allocated about 604,160 bytes
for playback and 684,208 bytes end-to-end per iteration. V3 command recording
remains much slower in isolation, but bounded and allocation-free.

This comparison is provisional because canonical validation exposed a V2 defect:
after sparse batch destroy, deferred default creation may publish stale donor-row
component bytes. Survivor payload, component presence, liveness, and replacement
lifecycle are compared exactly; replacement values are normalized only for the
cross-generation hash, while V3 separately requires actual zero defaults. The
adapter does not redefine stale bytes as valid default-create semantics.

Regression reports now allow the V3 profile label to change across checkpoints
while requiring the frozen V2 profile and all runtime/measurement settings to
remain equal. Baseline and current V2/V3 profile names are printed in every
regression report, eliminating the previous misleading retention of a v27 label.

## 3.0.32 exact whole-source transition checkpoint

The production transition planner now recognizes an ordered homogeneous route
that contains the complete source population, exceeds the 256-row execution
block, and has enough destination capacity for an all-or-fallback decision. It
validates the complete route before mutation, publishes destination rows in
record order through bounded blocks, then resets source occupancy once. All
conflict, scattered-order, and partial-capacity cases retain the existing grouped
path.

Both process orders produced 96 comparable rows, zero canonical mismatches, 15
unsupported rows, and zero V3 timed managed allocation. At 50,000 entities:

| Scenario | 3.0.31 V3 ns/op | 3.0.32 V3 accepted/reverse | V2 accepted/reverse | Result |
| --- | ---: | ---: | ---: | --- |
| Add-default playback | 77.478 | 43.063 / 45.183 | 19.969 / 19.884 | V3 about 42-44% faster than 3.0.31; still 2.2-2.3x V2 |
| Remove playback | 78.865 | 43.252 / 44.358 | 16.279 / 16.156 | V3 about 44-45% faster; still 2.7x V2 |
| Add/remove round trip | 85.650 | 50.403 / 50.435 | 19.694 / 19.563 | V3 about 41% faster; still 2.6x V2 |

The gap reduction is attributable to removing repeated source removal and donor
repair for exact evacuation routes. It does not generalize to partial-source,
scattered, conflicting, or insufficient-capacity transitions, and the report
does not present it as a universal structural speedup.

## 3.0.33 whole-source validation amortization checkpoint

The exact strategy and its selection rules are unchanged. Once the coordinator
has validated the entire route and destination capacity, block execution now
reads source rows without repeating public liveness/generation rejection and
publishes replacement locations through a trusted EntityPool commit. Debug
builds assert the complete proof. Scalar and ordinary grouped migration remain
checked, and unexpected post-preflight failure remains a terminal world fault.

A temporary checked 3.0.32 executor was retained only for two same-binary A/B
orders, then removed. At 100,000 entities:

| Scenario | Checked 3.0.32-style mean | Trusted 3.0.33 mean | Checked p95 | Trusted p95 | Mean result |
| --- | ---: | ---: | ---: | ---: | ---: |
| End-to-end whole-source | 49.081 / 49.083 ns/entity | 34.802 / 34.978 | 4.938 / 5.086 ms | 3.509 / 3.525 ms | 28.7-29.1% faster |
| Whole-source playback | 44.178 / 44.028 ns/entity | 29.834 / 30.223 | 4.444 / 4.427 ms | 3.006 / 3.088 ms | 31.4-32.5% faster |
| Wide-retained playback | 45.817 / 45.850 ns/entity | 31.734 / 31.894 | 4.631 / 4.624 ms | 3.264 / 3.219 ms | 30.4-30.7% faster |

Every A/B row allocated zero managed bytes. The separately captured 3.0.32
production control measured `49.212`, `44.240`, and `46.034 ns/entity`, closely
matching the temporary checked path rather than the trusted result.

Both isolated child-process orders retained 96 comparable rows, zero canonical
or contract mismatches, 15 unsupported rows, and zero V3 timed allocation. At
50,000 entities:

| Scenario | 3.0.32 V3 | 3.0.33 V3 primary/reverse | 3.0.33 V3 p95/base | V2 primary/reverse |
| --- | ---: | ---: | ---: | ---: |
| Add-default playback | 43.063 ns/entity | 30.217 / 31.095 | 0.717x / 0.751x | 20.055 / 20.009 |
| Remove playback | 43.252 ns/entity | 29.968 / 30.869 | 0.717x / 0.753x | 16.237 / 16.380 |
| Add/remove round trip | 50.403 ns/entity | 35.915 / 35.631 | 0.701x / 0.686x | 19.887 / 19.446 |

The frozen V2 controls did not share the 28-31% V3 movement. The complete
129-scenario V3 suite remained zero-allocation, and focused production p95
barriers stayed near `3.55`, `3.08`, and `3.27 ms` for end-to-end, ordinary
playback, and wide-retained playback.

## 3.0.34 prepared-migration trusted commit checkpoint

This checkpoint does not add a structural strategy. It removes repeated
release-mode EntityPool validation only after the existing contiguous-prefix
preflight has proved identity, generation, source membership, and physical row
order. The non-planned path rereads a current row because earlier packed removal
may move a later target as donor; planned compaction keeps the preflight rows.
Both commit replacement locations and successful store-authored donor
relocations through narrow trusted helpers with full Debug proof assertions.

Temporary same-process controls ran eight warmups and 32 interleaved samples in
both A/B orders. Tail 8 improved `89.353→81.084` and `86.650→81.226 ns/op`
(9.25%/6.26%). Tail 32, 128, and 256 improved 3.40-4.51%; the 128-row
64-byte-retained case improved 3.99-4.45%. Their p95 barriers improved in every
order and every measured row allocated zero bytes. These round trips repair
8/32/128/256 donors on the partial-source leg and zero survivors on the complete
planned leg.

The broader hypothesis was narrowed by evidence. Immediate migration's isolated
mean movement was only 1.9%/3.8% with an inconsistent p95, and scattered
playback was flat to slower. Their trusted calls were removed, leaving expected
per-entity rejection on the ordinary checked APIs. Exact whole-source controls
were flat and remain the unchanged 3.0.33 implementation. The temporary executor
mode, command-buffer branch, world/facade hooks, duplicate migrator controls, and
benchmark-pair framework were all removed.

The accepted and reverse isolated captures each report 96 comparable rows, zero
canonical/contract mismatches, 15 unsupported rows, and zero timed V3 allocation.
The current shared catalog uses exact whole-source routes rather than partial
prepared prefixes, so it is a regression control rather than the causal speedup
measurement. At 50,000 entities, primary/reverse V3 means are:

| Scenario | 3.0.33 primary/reverse | 3.0.34 primary/reverse | V2 3.0.34 primary/reverse |
| --- | ---: | ---: | ---: |
| Add-default playback | 30.217 / 31.095 | 30.203 / 30.342 | 20.104 / 20.037 |
| Remove playback | 29.968 / 30.869 | 28.654 / 30.249 | 16.370 / 16.134 |
| Add/remove round trip | 35.915 / 35.631 | 35.443 / 35.361 | 19.720 / 19.451 |

All state and measurement-policy comparisons against the immutable 3.0.33
artifact passed. Exact packed-suffix transition planning remains the next
structural experiment; it must not be conflated with this commit-only change.

## 3.0.35 exact route-level packed-suffix transition checkpoint

This checkpoint removes the 256-row source-planning horizon only for a complete
homogeneous run that is exactly the source store's real packed suffix. Whole-
source planning retains precedence. The coordinator resolves and validates every
target, proves record-order membership through a store-owned donor-order cursor,
and proves full destination capacity before mutation. It then streams the same
trusted route blocks used by whole-source execution and shrinks the source tail
once. Runs of 256 or fewer, scattered rows, conflicts, stale/wrong-source targets,
and partial-capacity routes keep the 3.0.34 grouped semantics.

The temporary same-binary 3.0.34 grouped control ran both A/B orders in two fresh
processes. Across a 100K resident source, qualifying normal playback improved
61.9% at 257 rows, 74.2% at 512, 70.6% at 1K, 58.9% at 10K, and 47.5-48.6% at
25K-75K. Wide-retained playback improved 32.7% at 257 and approximately 49-51%
from 512 through 75K. A 10K exact-history case improved 51.4% playback and about
46% end-to-end. The 128/256, scattered, and full-source controls were flat within
the expected noise boundary. All 172 measurement rows allocated zero bytes,
matched normalized logical-state hashes, and every successful suffix reported
zero surviving donor relocations.

The accepted V2-first and reversed V3-first regression captures each report 96
comparable rows, zero state/contract/catalog mismatches, 15 unsupported rows, and
zero V3 timed allocation. No shared partial exact-suffix scenario exists, so
these captures are regression gates rather than a V2 speed ratio for the new
strategy. A suspected saved-capture whole-destroy regression was disproved by a
same-host, same-JIT back-to-back build of committed 3.0.34 versus 3.0.35:
`14.931/14.849 ns/entity`.

## What this baseline says to optimize next

The adapter changes the optimization discussion from speculation to named
regression targets:

1. Exact whole-archetype and packed-suffix deferred destroy now have post-
   baseline fast paths. They validate complete logical coverage against the
   store's real donor order, shrink/reset occupancy and change metadata without
   eagerly clearing inaccessible payload, and retire the validated entity set
   in record order. The accepted baseline remains the immutable pre-change point.
2. Arbitrary sparse destroy remains scalar. An attempted reuse of the v27 planned
   compactor regressed the shared 10%-sparse playback from roughly `85` to `155
   ns/destroy`; donor-tail gating reduced but did not justify the complexity, so
   the partial path was removed. A future sparse algorithm must group by source
   chunk and prove a crossover before landing.
3. ECS 3.0.35 now plans exact packed suffixes beyond 256 rows without survivor
   movement. General partial-source planning remains deliberately absent. Any
   arbitrary sparse or source-chunk-aware planner must independently prove its
   crossover against grouped execution without weakening partial-capacity record
   order.
4. Fixed command recording writes and validates much richer per-command state
   than V2's block-copied lists. Consider bounded batch descriptors or a compact
   fixed target arena so span commands amortize validation and phase linkage
   without losing tokens, deterministic order, capacity failure, or the scalar
   oracle. Record-only benchmarks prevent this cost from hiding inside playback.
5. Safe random access should not be the default system traversal. Generated chunk
   jobs already win the representative write query. Keep random lookup for sparse
   relationships and quantify which remaining entity/location/route/change-stamp
   checks dominate bounded jobs before weakening safety.
6. Add the missing generated 3R0W family naturally, then turn the three currently
   unsupported V2 rows into canonical comparisons. Continue the working-set and
   effective-byte ladder so small cache-resident query results are not mistaken
   for large-world bandwidth.

Tests must accompany every optimization: scalar-versus-bulk randomized command
streams, stale/duplicate/conflict cases, partial capacity, exact/dense history,
enable state, donor ownership, generation wrap, Native/Pinned parity, allocation
gates, and canonical V2/V3 state. Tests may show that the expected next design is
wrong; the implementation and this document should follow the evidence.

## First post-baseline destroy result

The v28 shared adapter at 50,000 entities retained zero allocation and canonical
equality in both process orders. Full deferred-destroy playback fell from the
accepted V3 `72.50 ns/entity` to `29.17/29.03 ns/entity`. After normalizing each
run against its V2 control, the current/baseline ratios were `0.397/0.392`: a
repeatable material improvement of roughly 60%, not an order artifact. The
10%-sparse row measured `89.36/86.91` versus `85.03 ns/destroy`; its normalized
ratios (`1.014/0.978`) remain in the noise/tolerated band and establish no sparse
win. V2 still leads full playback at `2.45/2.47 ns/entity`; this is material V3
progression, not parity.

Two Next-only focused processes over 100,000 resident entities measured exact
whole-population playback in the ranges `50.27-63.92/11.63-12.03 ns/entity`
(scalar/production). Separate equal-work pairs destroyed the donor-tail 10,000
(10%) while keeping the 100,000-row resident footprint, measuring
`50.69-63.41/44.04-58.91 ns/entity`; all four allocated zero managed bytes in
both processes. Repeated isolated processes remain required before
treating the exact values as accepted; the shared adapter result is the
longitudinal generational guard.

## V29 trusted destroy and exact-suffix result

V29 moves clearing from row death to row publication, adds a trusted
post-removal entity-pool commit, removes redundant release ownership probes, and
bulk-shrinks exact suffixes after walking the store's actual partial/full donor
chain backward. A false candidate that ends at the current tail but skips packed
chunks is differential-tested against the scalar oracle; numeric chunk IDs do
not establish adjacency.

The complete 87-row comparison passed in both child-process orders with zero
canonical-state, descriptor, measurement-policy, or catalog mismatches. V3
allocated zero timed managed bytes in every row. At 50,000 entities:

| Scenario | Immutable V3 ns/op | V29 V3-first/last ns/op | V2 current ns/op | Control-normalized V29/baseline |
| --- | ---: | ---: | ---: | ---: |
| Full deferred destroy playback | 72.50 | 14.37 / 13.36 | 2.41 / 2.40 | 0.199 / 0.185 |
| Immediate destroy | 72.30 | 45.64 / 44.19 | 29.03 / 28.96 | 0.643 / 0.624 |
| Sparse 10% destroy playback | 85.03 | 56.65 / 56.27 | 14.47 / 14.86 | 0.644 / 0.623 |
| Add-default playback | 82.70 | 78.34 / 76.70 | 20.05 / 20.04 | 0.946 / 0.927 |
| Remove playback | 85.99 | 80.49 / 78.64 | 16.20 / 16.20 | 0.933 / 0.911 |

The destroy movement is repeatable and material, and transition playback did
not regress. It is not parity: V2 remains about `5.6x` faster for complete
deferred evacuation and about `3.8x` for sparse 10% playback. The next sparse
algorithm must therefore be selected with source-chunk locality and density
sweeps; it must not infer that the exact-suffix result generalizes to holes.

## 3.0.36 public serial job regression checkpoint

The public job capability does not add an honest V2-equivalent scenario to this
adapter: V2's complete system execution includes scheduler/state/cadence/context
semantics that 3.0.36 deliberately does not claim. The existing 96 matched rows
therefore remain a regression gate rather than being relabeled as public-system
parity.

Both 2026-08-11 child-process orders completed with 96 comparable rows, zero
canonical-state/descriptor/measurement mismatches, 15 explicit unsupported rows,
zero catalog changes, and zero timed V3 allocation/GC. The accepted primary
artifact is `Baselines/ecs-v2-v3-3.0.36-2026-08-11.json`. Order-sensitive timing
signals remain noise controls; no structural/query semantic regression was
admitted. Direct public-versus-internal evidence lives in
[`../ECS_JOB_EXECUTION.md`](../ECS_JOB_EXECUTION.md), where identical generated
job arithmetic and paired canonical component state provide the meaningful
3.0.36 comparison.

## 3.1.0 bounded scheduler checkpoint

ECS 3.1.0 admits three honest scheduler-shaped contracts: a serial generated
tick at 10K rows, a four-participant parallel generated tick at 100K rows, and a
four-participant 100K `RangeJob`. These compare observable work and canonical
state; they do not claim identical internal scheduling policy.

Both four-warmup/fifteen-iteration isolated child-process orders report 102
comparable rows, zero canonical-state mismatches, and 10 explicit unsupported
rows. V3 allocates zero timed managed bytes in 102/102 rows; V2 does so in
42/102. At a 5% mean band, V2-first classifies 14 V3 wins, 86 V2 wins, and 2
near; V3-first classifies 15/86/1. This remains a mixed longitudinal baseline,
not a single winner score.

| Scheduler scenario | V2-first V2 / V3 ns/op | Ratio | V3-first V2 / V3 ns/op | Ratio | Timed allocation V2 / V3 |
| --- | ---: | ---: | ---: | ---: | ---: |
| serial generated tick, 10K | 4.635 / 1.331 | 0.287x | 4.392 / 1.439 | 0.328x | 524,336 / 0 B |
| parallel generated tick, 100K | 5.623 / 0.617 | 0.110x | 5.553 / 0.479 | 0.086x | 216 / 0 B |
| parallel range, 100K | 1.640 / 1.686 | 1.028x | 1.655 / 1.681 | 1.015x | 64 / 0 B |

The range result is a practical tie and is retained because it prevents the
parallel query wins from being generalized to every work shape. The new 3R
direct-query rows are also honest rather than hero results: V3 is roughly
3-12% slower across 1K/10K/50K. The representative 1R1W query remains a strong
V3 path at about 0.33-0.36x V2 in the primary order.

The full primary 102-row metric table is
[`results/ecs-v2-v3-3.1.0-final-v2-first/ecs-v2-v3-comparison.md`](results/ecs-v2-v3-3.1.0-final-v2-first/ecs-v2-v3-comparison.md);
the reverse-order table is
[`results/ecs-v2-v3-3.1.0-final-v3-first/ecs-v2-v3-comparison.md`](results/ecs-v2-v3-3.1.0-final-v3-first/ecs-v2-v3-comparison.md).

## 3.1.1 scheduler hardening regression gate

ECS 3.1.1 deliberately does not expand the cross-generation catalog. Both
child-process orders retain 102 comparable contracts, zero canonical-state or
catalog/contract mismatches, 10 explicit unsupported rows, and zero timed V3
allocation. The accepted V2-first artifact is
[`Baselines/ecs-v2-v3-3.1.1-2026-08-11.json`](Baselines/ecs-v2-v3-3.1.1-2026-08-11.json).

The scheduler-specific rows are:

| Scenario | 3.1.0 V3 ns/op | 3.1.1 V2-first V3 ns/op | Delta | 3.1.1 V3-first V3 ns/op | Delta |
| --- | ---: | ---: | ---: | ---: | ---: |
| serial generated tick, 10K | 1.331 | 1.326 | -0.4% | 1.339 | +0.6% |
| parallel generated tick, 100K | 0.617 | 0.524 | -15.1% | 0.555 | -10.0% |
| parallel range, 100K | 1.686 | 1.722 | +2.2% | 1.794 | +6.4% |

Lower is faster. The serial row is a stable tie. Parallel generated traversal
improves in both orders. Focused 9-warmup/31-sample RangeJob reruns reproduce a
`+2.7%` to `+6.3%` range, so that modest regression remains a visible follow-up
signal accepted with the required capability-safety repair. These three rows do
not by themselves admit a performance
claim for the safety fixes; their primary role is guarding the scheduler seam.
The corrected same-process longitudinal parity soak is the stronger 3.1.1
system-level result and is documented in
[`../ECS_3_1_1_HARDENING_REPORT.md`](../ECS_3_1_1_HARDENING_REPORT.md).

Full current tables and baseline regressions:

- [`results/ecs-v2-v3-3.1.1-v2-first/ecs-v2-v3-comparison.md`](results/ecs-v2-v3-3.1.1-v2-first/ecs-v2-v3-comparison.md)
- [`results/ecs-v2-v3-3.1.1-v2-first/ecs-v2-v3-regression.md`](results/ecs-v2-v3-3.1.1-v2-first/ecs-v2-v3-regression.md)
- [`results/ecs-v2-v3-3.1.1-v3-first/ecs-v2-v3-comparison.md`](results/ecs-v2-v3-3.1.1-v3-first/ecs-v2-v3-comparison.md)
- [`results/ecs-v2-v3-3.1.1-v3-first/ecs-v2-v3-regression.md`](results/ecs-v2-v3-3.1.1-v3-first/ecs-v2-v3-regression.md)
