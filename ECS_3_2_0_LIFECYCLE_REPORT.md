# ECS 3.2.0 lifecycle publication acceptance report

Date: 2026-08-12  
Verdict: **Accepted ECS 3.2.0**  
Semantic profile: `ecs-3.2.0-lifecycle-publication`

## 1. Executive verdict

ECS 3.2.0 is accepted. It adds exactly one bounded topology-observation machine,
exposes the existing cold archetype-filter machinery through the public generated
job facade, and makes audited Simulation Slice 0 consume both capabilities.

The result remains engine-neutral, fixed-capacity, deterministic, and allocation-
free after warmup. It does not introduce a second structural interpreter, query
executor, scheduler, event bus, persistence identity, or replication protocol.

## 2. Exact lifecycle semantic contract

| Successful operation | Published lifecycle facts |
| --- | --- |
| Create entity in an archetype | exactly one `Created(entity)` |
| Destroy live entity | exactly one `Destroyed(entity tombstone)` |
| Add absent component/tag | one `ComponentAdded(entity, component)` |
| Add existing component / replace value | none |
| Remove present component/tag | one `ComponentRemoved(entity, component)` |
| Remove absent component | none |
| General transition | removed IDs ascending, then added IDs ascending |
| Retained component | none |
| Value or enable-state write | none |
| Failed, stale, duplicate, or rejected command | none |
| Donor relocation, compaction, hole reuse, reset, tail shrink | none |
| Command recording or scheduler merge without playback | none |

Creation never emits an initial-component Add storm. Destruction never emits a
component-removal storm. Lifecycle records successful runtime topology commits;
change history remains responsible for retained values.

## 3. Architecture and publication point

```text
structural intent
    -> existing checked/prevalidated structural executor
    -> successful logical commit
    -> lifecycle publication
```

Immediate facade create/destroy/transition operations publish after their checked
core commit. Main-arena and scheduler-lane commands publish only during actual
canonical playback. `RunManual` merge publishes nothing until a later barrier.

Transition plans compute immutable removed/added component-ID arrays from their
canonical source/destination signatures during cold preparation. Scalar, grouped,
whole-source, packed-suffix, typed, and fallback execution call the same logical
publication helpers. Optimized batch routes publish after physical finalization,
in command order. Storage, compaction, and entity-location repair remain blind to
lifecycle.

## 4. One unified stream

The world owns one globally ordered lifecycle sequence rather than four separate
kind streams. Consumers therefore do not need to merge independently ordered
birth, death, add, and remove queues to reconstruct barrier order. The one-stream
model also makes pre-barrier versus post-barrier ordering and deterministic worker
lane merge directly observable.

## 5. Record and cursor layout

`LifecycleRecord` is 24 bytes:

- 64-bit world-local wrap-safe sequence;
- exact 12-byte runtime `Entity` identity/tombstone;
- 16-bit schema-local `ComponentId`;
- one-byte `LifecycleKind` plus reserved layout byte.

`LifecycleCursor` is 16 bytes: next sequence plus world ownership. It is a
copyable independent value, registers no subscriber, and owns no allocation.

Records deliberately omit component values, archetype/chunk/row handles,
persistent IDs, prefab IDs, and serialized payloads. A destroyed record retains
the destroyed generation even after the slot is reused.

## 6. Ring, retention, cursors, and overflow

`EcsWorldOptions.LifecycleRecordCapacity` defaults to zero. Zero creates no ring
and disables publication. A positive capacity allocates exactly
`capacity * 24` managed payload bytes once.

The producer never waits for readers. It retains the most recent fixed window.
`ReadLifecycle` copies into caller-owned spans and returns `NoData`, `Success`, or
`ResyncRequired`. Partial reads advance only through delivered records. A lagged
cursor is not silently moved; its owner must rebuild state and explicitly capture
a new cursor. Owner-thread reading matches the 3.2 public world contract.

Unsigned sequence-distance arithmetic is tested across `ulong` wrap. A randomized
reference model ran 32 seeds x 2,000 mixed publish/read operations with capacities
1-16, copied cursors, zero-length/partial spans, resync, and near-wrap starts.

The dedicated Slice self-test uses capacity four, proves `ResyncRequired`, then
recaptures and resumes incremental consumption.

## 7. Failure semantics

Lifecycle follows the existing terminal-fault model, not rollback fiction. A
focused injected post-preflight fault first commits a create, then faults a later
whole-source transition. The earlier `Created` fact remains readable from the
core history and matches committed state; the failed transition publishes no
membership delta. Expected rejection paths publish nothing.

## 8. Structural differential and ordering evidence

The permanent 64-seed production-versus-scalar mixed-command model now compares
both canonical world state and lifecycle records. Focused controls additionally
force:

- grouped and scattered partial transitions;
- exact whole-source and `>256` exact packed-suffix transitions;
- exact whole-store and packed-suffix destruction;
- balanced wide-row hole reuse;
- scalar donor relocation.

All normalized sequences match the scalar oracle. Native/Pinned facts match.
Scheduler tests cover multiple lanes, pre-barrier plus post-barrier order, typed
Add/Remove and replacement, deferred create chains, `RunManual`, and failure.
Slice 0 produces identical raw ordered lifecycle hashes in serial and C8.

## 9. Capacity and memory ownership

Lifecycle bytes appear exactly once in:

- `EcsWorldOptions`;
- `WorldCapacityPlanner.PlanFromOptions` / `ManagedLifecycleBytes`;
- `WorldReservedPayloadSnapshot.LifecycleHistoryBytes`;
- lifecycle history diagnostics and benchmark reports.

The fixed history reports capacity, retained count, high-water mark, overwritten
record count, oldest/next sequence, and reserved bytes. `OverwrittenCount` counts
normal ring rotation after it becomes full; consumer loss is represented only by
`ResyncRequired`.

## 10. Lifecycle microbenchmarks

Accepted artifact:
[`_Benchmarks/Baselines/ecs-v3-3.2.0-lifecycle-publication-2026-08-12.json`](_Benchmarks/Baselines/ecs-v3-3.2.0-lifecycle-publication-2026-08-12.json)

All cases allocate zero managed bytes after warmup.

| Operation | Mean | p95 | Notes |
| --- | ---: | ---: | --- |
| publish 1K | 2.14 ns/record | 2.16 | fixed ring |
| publish 10K | 2.15 ns/record | 2.12 | one isolated p99 tail |
| publish 100K | 2.12 ns/record | 2.16 | 19.2 GiB/s record writes |
| consume 1K/full | 0.42 ns/record | 0.44 | one cursor |
| consume 10K/full | 0.33 ns/record | 0.34 | one cursor |
| consume 100K/full | 0.44 ns/record | 0.44 | one cursor |
| consume 100K/full x3 | 0.45 ns/record | 0.47 | independent cursors |
| consume 100K/span64 x3 | 0.94 ns/record | 0.96 | repeated partial drains |

The one-record timing is dispatch/timer dominated and is retained only as a
fixed-call control.

## 11. Structural cost: disabled versus enabled

The paired benchmark alternates A/B order and validates equal arithmetic state.

| 100K route | Disabled | Enabled | Added cost/entity | Records/entity |
| --- | ---: | ---: | ---: | ---: |
| immediate create | 22.31 ns | 24.28 ns | 1.97 ns | 1 |
| immediate destroy | 25.73 ns | 28.66 ns | 2.93 ns | 1 |
| immediate transition | 79.22 ns | 83.87 ns | 4.65 ns | 2 |
| deferred create playback | 20.01 ns | 21.79 ns | 1.78 ns | 1 |
| whole destroy playback | 7.34 ns | 8.90 ns | 1.56 ns | 1 |
| whole transition playback | 35.53 ns | 43.32 ns | 7.79 ns | 2 |

The enabled percentage is largest on whole-store routes because their structural
base is exceptionally small, not because publication changes algorithmic shape.
The observed incremental cost remains mechanically close to roughly 1.6-3.9 ns
per published record plus route-specific loop interaction.

Lifecycle-disabled comparison rows preserve canonical state and zero allocation.
One historical parallel scheduler artifact appeared 16-32% faster than both new
process orders. A same-session three-run isolation measured 3.2.0 at
`0.573-0.627 ns/row` and a clean 3.1.2 worktree at `0.653-0.657 ns/row`; serial
scheduler and parallel RangeJob rows remained stable. This rejects a 3.2 hot-path
regression and classifies the immutable `0.534 ns/row` capture as a favorable
historical scheduling sample. No scheduler code was changed to chase it.

## 12. Public filter API and validation

`EcsWorld.PrepareQueryFilter(required, excluded)` returns an immutable,
world-bound `EcsQueryFilter`. Filtered overloads exist for every generated public
job family, the handwritten 1R1W family, and changed-row preparation. Existing
unfiltered overloads remain source-compatible.

Preparation deduplicates terms and rejects default/foreign handles, invalid or
unknown IDs, required/excluded contradictions, and exclusion of a job data
column. The empty prepared filter is valid. Storage-free tags are supported.

Filter terms are selection-only. They do not become generated data spans,
supplemental lookup capabilities, or scheduler hazards. `Seal` compiles matching
routes once; generated execution traverses those routes directly with no per-row
membership branch and no hot allocation. This exposes existing filter machinery,
not a second query system.

The accepted 50K-row filtered path measured `36.077 ns/row` versus
`35.704 ns/row` for a pre-separated one-archetype control (`+1.04%`, noise band),
with zero allocation. Filtered plan+Seal was `4.17 ms` in the earlier final run
versus `0.26 ms` for the simpler control; this cold cost includes registering and
compiling the two-route filtered fixture and is not a hot-path target.

## 13. Slice 0 tier-key removal

Four four-byte fake tier data components became four storage-free tags. The four
periods (1/2/8/60) still require 16 filtered dense registrations and 16 prepared
plans across Needs, Steering, Movement, and Spatial systems.

| Scale | Removed resident marker payload | Removed marker reads/tick | New payload/read |
| --- | ---: | ---: | ---: |
| 100K | 359,808 B | 143,914 B/tick | 0 / 0 |
| 1M | 3,671,808 B | 1,468,714 B/tick | 0 / 0 |

The timing change cannot be attributed solely to filtering because the same
checkpoint adds useful lifecycle publication and three consumers.

## 14. Slice lifecycle integration

Presentation drains Position changes plus lifecycle every tick. Replication
drains Health changes plus lifecycle every two ticks. Persistence drains Position
changes plus every lifecycle fact every 60 ticks. All use independent cursors and
fixed 2,048-record scratch spans.

The semantic oracle independently predicts effect birth/death and actor alive/dead
membership facts. Runtime entity identities are normalized through logical actor/
effect identity for oracle comparison. Separately, raw ordered runtime lifecycle
hashes match across all three cursors and serial/C8 worlds.

Capacity is derived rather than guessed:

```text
max records/tick = damage operations * 12 + effect spawns * 2
retention = 60 consumer ticks + 4 ticks margin
capacity = max(4096, max records/tick * 64) = 200,704 records
reserved = 4,816,896 bytes
```

## 15. 100K Slice result

Profile: 89,952 actors, 8,000 items, 2,048 effect slots; 256 warmups and 10,000
measured ticks.

| Mode | Mean | p50 | p95 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Serial | 0.189 ms | 0.172 | 0.343 | 0.824 | 1.052 | 1.227 |
| C8 | 0.166 ms | 0.166 | 0.265 | 0.397 | 0.455 | 0.590 |

Canonical state hash is `0x61C8B1872C75BC67` in oracle, serial, and C8.

Over 10,000 ticks each consumer received 1,290,856 records:

- 320,000 Created;
- 320,000 Destroyed;
- 325,280 ComponentAdded;
- 325,576 ComponentRemoved;
- 129.086 records/tick, or 3,098.1 lifecycle bytes/tick.

Presentation maximum lag/burst was 232; replication 422; persistence 9,156.
Persistence drained 167 times at 97.1 us serial / 99.1 us C8 average. All cursors
ended at zero lag with zero resyncs. Logical lifecycle hash is
`0xFC187D2D68963364`; ordered runtime hash is `0xF02667DE0F1BC4E7` in both modes.

Known ECS payload is 23,918,961 bytes serial and 25,646,330 bytes C8, including
the 4,816,896-byte history and 183,543 / 1,452,160 scheduler bytes.

## 16. 1M Slice result

Profile: 917,952 actors, 80,000 items, 2,048 effect slots; 256 warmups and 1,000
measured ticks.

| Mode | Mean | p50 | p95 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Serial | 1.488 ms | 1.021 | 3.032 | 7.903 | 10.276 | 10.361 |
| C8 | 0.484 ms | 0.361 | 0.999 | 2.290 | 2.514 | 3.326 |

Canonical state hash is `0x8E9978581C27A90A` everywhere. Each consumer received
109,258 lifecycle records (109.258/tick; 2,622.2 bytes/tick), with zero resync.
Persistence maximum lag/burst was 6,690. The normalized lifecycle hash is
`0x26F63CC7F9A8D631`; ordered runtime hash is `0xF48EBADF4CE4DFC5` in both modes.

Known ECS payload is 142,547,019 bytes serial and 144,274,388 bytes C8.

## 17. All-active cadence control

The 100K/10,000-tick all-active control measured:

| Mode | Mean | p50 | p95 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Serial | 0.748 ms | 0.734 | 0.781 | 1.170 | 1.213 | 1.833 |
| C8 | 0.416 ms | 0.410 | 0.452 | 0.500 | 0.648 | 1.289 |

This is 3.96x serial and 2.51x C8 over the tiered means, demonstrating that
cadence suppresses real work rather than only an in-job branch.

## 18. Allocation and GC

Every lifecycle microbenchmark, 141-scenario permanent kernel benchmark, 86-row
public-job matrix, 314-row change decomposition, and 149-row scheduler matrix
reports zero warmed managed allocation. The Slice 100K, 1M, and all-active runs
report zero timed caller bytes, zero scheduler-worker bytes, and zero Gen0/1/2
collections in both modes. Canonical/oracle work is outside ECS timing and also
reported separately.

## 19. Existing regression evidence

- permanent kernel suite: 141/141, zero allocation;
- public generated jobs: 60 traversal + 19 tracked + 7 dispatch rows, canonical;
- change decomposition: 314 measurements, correct and zero allocation;
- scheduler: 149 timings + 25 cold graph-Seal cases, canonical and allocation-free;
- 30,000-tick scheduler parity: logical hashes/counters match public, serial, C8;
- V2-first and V3-first isolated comparison: 102 comparable, zero state,
  descriptor, measurement, or catalog mismatches; 10 honest unsupported rows;
- accepted cross-generation artifact:
  [`_Benchmarks/Baselines/ecs-v2-v3-3.2.0-2026-08-12.json`](_Benchmarks/Baselines/ecs-v2-v3-3.2.0-2026-08-12.json).

The extreme-sphere integration was not rerun: 3.2 changes neither its disabled
lifecycle configuration nor its unfiltered generated/scheduler hot path, and the
more focused permanent, public, scheduler, same-host 3.1.2, and cross-generation
controls cover those mechanisms with less GPU/host noise.

## 20. Complete tests and builds

- 218 kernel/runtime primitive tests;
- 43 scheduler tests;
- 9 lifecycle/public-filter tests;
- 25 generator tests;
- 12 comparison tests;
- 18 bitmap experiment tests;
- **325 executable tests total**, all green in required configurations;
- Debug and Release warnings-as-errors builds for kernel, generator, tests,
  benchmark, Slice, and comparison projects: zero warnings/errors;
- `git diff --check`: clean.

The lifecycle set includes truth-table, default/foreign misuse, capacity memory,
partial/multiple/copied cursors, delayed/overflow/recovery, wrap/reference fuzz,
optimizer/scalar differential, Native/Pinned parity, fault boundary, warmed
allocation, and Slice overflow tests. Filter tests cover tags/data, required/
excluded, duplicates, zero/one/many routes, foreign/default/invalid/contradictory
handles, generated overloads, and allocation-free traversal.

## 21. Qwen and fresh-consumer review

Qwen's implementation reviews suggested randomized wrap/overflow coverage and
stronger benchmark checksums/tails. The verified useful suggestions were adopted:
the randomized ring model, explicit overflow recovery, p50/p95/p99 reporting, and
paired-state equality. Claims that lifecycle bytes were absent from total memory,
that filter tests did not exist, or that benchmark allocation was unmeasured were
rejected against source and executable tests.

A fresh-context consumer saw only the public usage guide plus a mini workload. It
correctly selected a filtered generated job for all Alive rows, RangeJob plus
prepared lookup for 256 preselected targets, structural writer for death, and
separate change/lifecycle cursors. It also stated that structural visibility occurs
at playback. Its generic claims that the guide failed to distinguish those paths
were rejected: the cited sections and decision table state each distinction
explicitly. No runtime was changed to satisfy model prose.

## 22. Complexity review

Added concepts:

- one fixed lifecycle history;
- one independent lifecycle cursor/result vocabulary;
- one opaque public required/excluded filter handle.

Removed consumer concepts/work:

- four fake tier data columns and their read bandwidth;
- full identity scans for lifecycle inference;
- destructive/shared lifecycle-drain assumptions.

Lifecycle is one observation machine below the canonical structural barrier, not
another ECS layer. Filtering exposes the already-existing cold route compiler,
not another query system. The optional lifecycle reference is deliberately the
last `EcsWorld<TArena>` field: an earlier candidate placement shifted hot world
fields and reproducibly regressed a scheduler microbenchmark by 3.5-6.5%; moving
it last restored same-host 3.1.2 parity without changing semantics.

## 23. Deliberate deferrals

3.2 does not add gameplay events, subscriptions/callbacks, arbitrary payloads,
old values, persistence IDs/serialization, relationship remapping, replication
codecs, networking, prefab compilation/inheritance, route-disjoint scheduler
proofs, work stealing, NUMA/affinity, system fusion, prepared random target
batches, persistent sparse archetypes, dynamic schemas/graphs, optional component
columns, or a general query DSL.

## 24. Final architectural answers

1. **Yes.** Lifecycle gives deterministic, bounded, incremental successful
   topology facts without exposing physical storage behavior.
2. **Yes.** Change history owns retained-value mutation; lifecycle owns runtime
   existence and membership mutation.
3. **Yes.** Storage-free required/excluded filters compile cold into generated
   routes with no fake column bandwidth or row predicate.
4. **Yes.** Slice 0 remains canonical, zero-allocation, and fast at 100K and 1M,
   while three independent consumers observe real lifecycle pressure.
5. **Yes.** The next major milestone should move upward into the RunUO-style
   prefab/authoring compiler and use lifecycle as a bridge contract. Kernel or
   scheduler work should resume only when that vertical slice produces measured
   pressure.
