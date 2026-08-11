# ECS 3.1.0 Bounded System Scheduler: Acceptance Report

Status: accepted on 2026-08-11 under semantic profile
`ecs-3.1.0-bounded-system-scheduler`.

The answer to both admission questions is yes. A real UltraSim system can now
own persistent state, run at a declared stage/cadence, express generated and
supplemental access, execute the 3.0.36 chunk machine serially or across bounded
persistent workers, run non-query ranges, publish tracked changes, record
deterministic structural output, and fault safely without giving workers an
`EcsWorld`. The orchestration layer remains one cold-compiled graph around the
existing query, range, and structural machines; it does not introduce a second
storage/query/task architecture.

## 1. Accepted public API

World construction optionally supplies `EcsSchedulerOptions` through
`EcsWorldOptions`. `default`/`Disabled` creates no scheduler, workers, scheduler
arrays, or worker command lanes.

The public scheduler surface is:

```text
HasScheduler
TickCount
SystemCount
SchedulerRunMode
SchedulerReservedBytes

RegisterRangeSystem<TSystem,TJob>(definition, runtime)
RegisterChangedRowsSystem<TRead,TChanged,TSystem,TJob>(definition, plan, runtime)
RegisterReadSystem / RegisterWriteSystem / RegisterReadWriteSystem
RegisterReadReadSystem / RegisterReadReadReadSystem
RegisterReadReadWriteSystem / RegisterReadWriteWriteSystem
RegisterReadReadReadWriteSystem / RegisterReadReadWriteWriteSystem
RegisterTrackedWriteSystem / RegisterTrackedReadWriteSystem
RegisterTrackedReadReadWriteSystem / RegisterTrackedReadWriteWriteSystem
RegisterTrackedReadReadReadWriteSystem / RegisterTrackedReadReadWriteWriteSystem

Tick(deltaSeconds)
RunManual(systemId, deltaSeconds)
SetSystemEnabled(systemId, enabled)
SetSystemSchedule(systemId, periodTicks, startOffset)
CopySystemRuntimeSnapshots(destination)
GetSchedulerRuntimeSnapshot()
```

Generated registrations exist for `1R0W`, `0R1W`, `1R1W`, `2R0W`, `3R0W`,
`2R1W`, `1R2W`, `3R1W`, and `2R2W`; every writable family has a tracked variant.
Jobs implement the corresponding `IScheduled...ChunkJob` interface and receive
the same 3.0.36 chunk view plus a `SystemJobContext`. `IRangeJob` receives
`(context,start,count)`. Exact one-watch changed-row systems use a success-only
cursor runtime and deliberately execute serially.

## 2. System definition and persistent runtime state

`SystemDefinition` is a cold immutable class containing canonical key, stage,
schedule, execution policy, manual flag, optional persistence ID/schema version,
dependency IDs, supplemental reads/writes, structurally writable components,
parallel crossover/minimum-partition settings, optional disjoint-random-write
proof, and opt-in staggering. It defensively copies cold lists.

`ISystemRuntime<TJob>` is the persistent user-owned state object. Owner-thread
`Initialize`, `Prepare`, `Complete`, and `Shutdown` define lifetime. `Prepare`
returns an ephemeral job struct. Participant-local job copies return to
`Complete` in stable participant order, providing the minimum bounded reduction
surface without hot atomics or a general dataflow framework. Persistence ID and
schema version reserve the future serialization binding; 3.1.0 does not serialize
runtime objects.

## 3. Fixed sizes

| Value | Locked size |
| --- | ---: |
| `SystemId` | 4 bytes |
| `SystemSchedule` | 16 bytes |
| `EcsSchedulerOptions` | 44 bytes |
| internal chunk partition | 16 bytes |
| internal partition shape | 8 bytes |
| `Entity` | 12 bytes |
| `DeferredEntity` | 16 bytes |
| structural command record | 24 bytes |
| command record + result/phase link | 40 bytes |

## 4. Permanent scheduler memory

All capacities are fixed before world construction: systems, dependencies, work
items, structural segments, participants, partitions/system, commands/producer,
and payload bytes/producer. `SchedulerReservedBytes` accounts flat node/mask/
route/work/partition/segment/participant arrays, participant job slots, the main
command arena, and every worker lane. It excludes CLR headers, event objects, and
OS thread stacks. The accepted 100K soak configuration reserves exactly 502,464
bytes of scheduler payload. Disabled worlds report zero.

## 5. Worker count and concurrency

`BackgroundWorkerCount` is exactly the number of persistent background threads.
`ExecutionConcurrency = BackgroundWorkerCount + (CallerParticipates ? 1 : 0)`.
There is no hidden “minus one.” A caller-free scheduler requires at least one
background worker. Serial mode uses exactly one participant even when the caller
does not participate; a focused test found and fixed an early implementation
that could otherwise run independent serial nodes on different workers.

## 6. Graph and wave representation

Registration IDs are dense four-byte indices. Seal validates stable key and
persistence identity, builds flat read/write/structural bit masks, validates
dependencies, and assigns each node an integer compiled wave inside one of nine
strict stages. Runtime execution is nested stage/wave scanning over dense arrays;
there are no delegate nodes, tasks, dictionaries, or runtime topological sort.
Read/read may share a wave. Overlapping write/read, write/write, declared random
access, or structural access creates a hard wave separation unless an explicit
disjoint random-write proof applies.

## 7. Dependency and cadence semantics

`Requires` is an ordering edge and hard barrier when both nodes run. Seal rejects
missing, repeated, self, cyclic, and reverse-stage dependencies. Cadence is
`enabled + periodTicks + startOffset`. A slow or disabled prerequisite does not
force itself to run and does not suppress an independently due dependent; the
edge still fixes order on coincident ticks. Runtime enable/schedule changes do
not alter sealed graph topology. Automatic phase staggering is deterministic,
opt-in, capped, and excludes manual, Movement, and Combat nodes.

## 8. Serial and parallel partitioning

Serial work uses the same prepared route and chunk loop as direct 3.0.36 jobs.
Parallel generated jobs statically partition the already bound occupied chunks;
range jobs statically partition `[0,count)`. Partition count is bounded by work
capacity, execution concurrency, per-system maximum, row threshold, and minimum
rows/partition. Participant jobs are copied into fixed slots. One scheduler
dispatch handles independent nodes and intra-node partitions; nested scheduling
is forbidden. `Auto` remains serial below the declared crossover and does not
wake workers.

## 9. Work stealing decision

Work stealing was not retained. Uniform chunk and range work scales strongly
with static partitions, while fixed queues/stealing would enlarge hot state,
wake behavior, ordering state, and proof surface. The decision is evidence-based,
not permanent: an imbalanced production workload may justify a bounded temporary
experiment later.

## 10. Reuse of the 3.0.36 machine

Scheduler nodes bind the existing public job plans and invoke the same generated
route loop, stack-only typed chunk views, writable/tracked wrappers, and
constrained struct calls. There is no scheduler component view or reflection
fallback. The representative 100K direct/scheduled boundary is
169.787/169.293 us; orchestration is below noise at that scale.

## 11. Missing generated families

The common query-family generator now emits public direct and scheduled surfaces
for `1R0W`, `0R1W`, and `3R0W` rather than expressing write-only work with a false
read or maintaining handwritten runtime-only families. Golden output,
compilation, deterministic generation, access masks, spans, and tracked-write
emission are covered by 23 generator tests.

## 12. Optional full Entity

Chunk views continue to expose cheap local `uint` entity indices. A restricted
job may explicitly call `TryGetEntity(index,out Entity)`, which reconstructs the
current world/index/generation identity through the entity pool. At 100K rows,
the accepted local-index control is 114.191 us and full reconstruction is
612.465 us: a paid delta of 498.274 us, or 4.983 ns/entity. It is for occasional
relationships/targets, not dense default materialization.

## 13. Parallel change publication

Each writable system invocation owns one `SchedulerChangePublication`. All
participants lazily share one candidate `ChangeVersion`; the first actual
tracked write publishes it with CAS. A no-write tracked job does not advance the
world version. Dense stamps remain conservative, exact row stamps remain precise,
and component rows remain race-free because the graph/partition proof gives one
writer ownership. Scheduled exact changed-row cursors acknowledge only after
successful execution.

## 14. RangeJob

`IRangeSystemRuntime<TJob>.GetRangeCount` supplies a bounded non-ECS index space.
The same workers, dispatch, restricted context, result slots, fault transaction,
and structural lanes execute `IRangeJob.Execute(context,start,count)`. Every
index is statically assigned exactly once. It is intentionally not a general
task graph or nested `ParallelFor`.

## 15. Restricted worker access

Workers receive an expiring `SystemJobContext`, never `EcsWorld`. It exposes
tick/delta/system/participant/logical-work identity; declared prepared lookup
`Has/TryRead/TryWrite/TryGetEnabled/TrySetEnabled`; optional entity
reconstruction; and a structural writer. Default contexts, foreign threads,
stale epochs, post-callback use, undeclared components, and invalid lookups are
rejected. Lifecycle callbacks alone receive the owner-thread world.

## 16. Worker structural arenas

Each execution participant owns one fixed scheduler-mode `StructuralCommandBuffer`
plus a fixed aligned byte payload arena. Capacity is
`StructuralCommandsPerProducer` and `StructuralPayloadBytesPerProducer`; it never
grows or boxes. One nonempty logical work item becomes a bounded segment
descriptor referencing its participant lane range.

## 17. Deterministic command merge

Logical work ordinals are assigned from stable stage/wave/system/partition order
before dispatch. After joining workers, the owner walks segment ordinals in that
order, computes required command/payload capacity, copies each segment to the
main command arena, and remaps local deferred slots. There is no timing-order
append and no runtime sort. Post-barrier playback remains the established 3.0.35
production engine and its scalar/packed oracles.

## 18. DeferredEntity semantics

Worker-created `DeferredEntity` values are valid only in the same logical work
segment and lane epoch that created them. This permits create-then-add/transition/
destroy inside one job segment without inventing cross-worker scheduling meaning.
Cross-segment, expired, stale-epoch, or foreign-lane consumption faults.

## 19. Source-independent Add/Remove routes

At Seal, each declared structural component compiles a direct route table from
current archetype to existing prepared add/replacement or remove transition and
destination physical column. Runtime `TryAdd<T>`/`TryRemove<T>` therefore does
not require callers to know a source archetype or look up signatures. Tags use
signature membership rather than nonexistent physical data columns.

## 20. Typed payload representation

`TryAdd<T>` copies the unmanaged value bytes into the lane payload arena using
the schema-known size/alignment. The existing command union stores the payload
offset. Merge copies payload spans and rewrites offsets into the main arena.
Replacement and add share this route. Payload exhaustion rejects explicitly and
leaves state unchanged; no object, box, or per-command side array exists.

## 21. Command-size result

The 24-byte command record and 40-byte total command slot survived unchanged.
No scheduler command kind, executor mode, or persistent command-buffer array was
added beyond the two source-independent component operations admitted by the
task.

## 22. Manual systems

Manual nodes are excluded from ordinary ticks and may be invoked only through
`RunManual`. Manual execution does not increment `TickCount`. Its worker output
is deterministically merged into the main arena but deliberately waits for the
next external tick's pre-barrier; manual invocation cannot make half-applied
topology visible implicitly.

## 23. Fault transaction

Preparation, dispatch, completion, merge, and both barriers are one owner-led
terminal transaction. Worker exceptions preserve original exception plus system,
stage, logical-work, and participant identity. The owner joins dispatch, discards
all lanes, skips post playback, releases graph scope, leaves `TickCount`
unchanged, and terminally faults the world. Conservative stamps already published
for exposed writable spans remain visible. Initialization unwinds and shutdown
runs in reverse order; disposal joins every worker.

## 24. Runtime and profiling snapshots

Caller-provided spans receive per-system ID/key/stage/cadence/policy/wave,
last/average/peak time, run count, last rows/chunks/work items, and last serial/
parallel decision. The scheduler snapshot exposes worker/caller concurrency,
dispatch/wake/park totals, merge command/segment/payload totals, paid entity
resolutions, fixed reserved bytes, worker allocation, and optional cumulative
pre/graph/merge/post timestamps. Profiling is construction-fixed; off mode avoids
timestamp reads and identity-resolution atomics.

## 25. Serial scheduler overhead

Empty profiling-off graph cost is 0.042 us; 1/4/16/64/256 no-work systems cost
0.072/0.164/0.575/2.073/8.218 us. Profiling raises the 16-node control from
0.565 to 1.331 us. The representative generated 1R1W boundary is:

| Rows | Direct mean | Scheduled mean | Delta |
| ---: | ---: | ---: | ---: |
| 1 | 0.041 us | 0.177 us | +0.137 us |
| 1K | 1.475 us | 1.686 us | +14.3% |
| 10K | 15.284 us | 16.364 us | +7.1% |
| 100K | 169.787 us | 169.293 us | -0.3% |

All nine 100K generated families remain canonical and allocation-free. Their
scheduled/direct mean deltas range from -2.2% to +14.7%; most are within about
5%, while W1/R1W1/R2W1 warrant longitudinal guards rather than an API rewrite.

## 26. Parallel crossover

Eight-way static execution becomes useful at roughly 10K rows for the tested
kernel; 1K is a tie/serial region.

| Rows | 1-op serial / parallel mean us | Speedup | 8-op serial / parallel mean us | Speedup |
| ---: | ---: | ---: | ---: | ---: |
| 1K | 1.824 / 1.849 | 0.99x | 7.073 / 6.856 | 1.03x |
| 10K | 16.308 / 13.620 | 1.20x | 63.912 / 21.323 | 3.00x |
| 50K | 85.018 / 31.750 | 2.68x | 318.998 / 102.058 | 3.13x |
| 100K | 167.971 / 41.540 | 4.04x | 637.386 / 169.978 | 3.75x |
| 250K | 426.872 / 138.580 | 3.08x | 1565.897 / 284.862 | 5.50x |
| 1M | 3051.495 / 777.882 | 3.92x | 7134.764 / 1457.275 | 4.90x |

## 27. Worker scaling

For the 100K/8-op generated query, concurrency 1/2/4/8/16 means are
635.790/327.387/187.796/114.975/119.580 us. Eight participants are best on this
16-logical-processor host (`5.53x` over one); 16 slightly regresses and has worse
tail behavior, confirming that maximum hardware concurrency is not the default.
Independent 50K-row systems scale from serial/parallel 125.769/125.716 us at one
system to 1977.839/320.045 us at 16 systems.

## 28. RangeJob and V2 ParallelFor

100K RangeJob means for concurrency 1/2/4/8/16 are
75.307/37.819/24.454/17.699/27.811 us at one operation and
591.998/312.184/159.288/110.940/103.869 us at 16 operations. In the isolated
four-participant V2/V3 contract, V2-first is 1.640/1.686 ns/op (V3/V2 1.028x)
and V3-first is 1.655/1.681 (1.015x): a practical tie, while V3 allocates 0 B
versus V2's 64 B.

## 29. Structural record/merge/playback

For 10K destroys plus 10K creates (20K commands):

| Concurrency | Total mean us | Record+graph | Merge | Playback | Total p95 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1282.389 | 329.455 | 180.466 | 772.227 | 1295.969 |
| 2 | 1392.066 | 444.226 | 177.628 | 769.950 | 1414.548 |
| 4 | 1138.080 | 193.808 | 175.949 | 768.064 | 1148.110 |
| 8 | 1156.159 | 203.915 | 179.288 | 772.640 | 1177.279 |
| 16 | 1489.475 | 373.283 | 222.570 | 892.982 | 2402.929 |

Merge/playback are the serial tail; four participants are best here. Structural
playback itself remains flat through 8, so scheduler input did not regress the
existing engine. The 100K scheduled soak's ordinary 400 commands/tick cost only
about 4.30 us merge plus 24.17 us post barrier on average.

## 30. V2 parity and deliberate non-parity

V2-equivalent capabilities retained: persistent state, lifecycle, tick/cadence,
stage order, dependencies, manual nodes, persistent workers, caller participation,
serial/parallel generated query execution, non-query range work, basic runtime
inspection, and phase-stagger control.

Intentional corrections/non-parity: fixed reservations instead of dynamic
growth; generated masks instead of runtime component discovery; static partitions
instead of delegate `ParallelFor`; no unrestricted worker world; per-worker
bounded command lanes instead of a shared locked queue; stable logical merge;
ordering-only dependency/cadence semantics; no work stealing, fusion, runtime
schema/system mutation, or scheduler persistence serialization. Unsupported
comparison rows remain visible rather than receiving fabricated ratios.

## 31. Scheduled 100K soak

The accepted run performs 60,000 measured ticks after 256 warmups in both modes.
Each tick visits 400K generated query rows, targets 5K tracked writes, performs
500 lookups and 400 structural commands, and consumes exact changes.

| Mode | Mean | p50 | p95 | p99 | Max |
| --- | ---: | ---: | ---: | ---: | ---: |
| Serial | 981.229 us | 973.422 | 1036.430 | 1105.880 | 2131.291 |
| Parallel | 409.010 us | 377.986 | 508.556 | 613.015 | 2890.565 |

Parallel mean is `2.40x` faster. Mean phases are
pre/graph/merge/post `0.026/954.350/3.936/22.494 us` serial and
`0.030/380.047/4.303/24.168 us` parallel. Both modes hash to
`8E25297D3A12A623`, allocate 0 B on caller and workers, and collect 0/0/0.

## 32. Determinism

Independent 10,000-tick executions at concurrency 1/2/4/8/16 plus two extra
8-way replays all produce `29A337A61592C2E3`, advance exactly 10,000 ticks, and
deliver exactly 50,000,000 changed rows. The 60K serial/parallel soak also
converges. This validates scheduler partition/merge determinism for the admitted
workload; jobs must still use deterministic arithmetic/state deliberately.

## 33. Validation totals

- 218 kernel/runtime tests plus 23 scheduler tests pass in Debug and Release.
- 23 generator tests, 12 comparison-core tests, and 18 bitmap experiment tests
  pass: 294 distinct cases, 535 final configuration-specific invocations.
- Permanent kernel benchmark: 141/141 rows at 0 B.
- Public generated jobs: 60 traversal + 19 tracked + 7 dispatch rows, canonical
  and allocation-free.
- Scheduler admission: 143/143 timings plus long soak/replays, canonical and 0 B.
- Change decomposition: 314 rows, correctness and zero-allocation gates pass.
- Original internal and public-job soaks: two 30K-tick worlds each, canonical
  hash `4450A5AC6A7A1296151BFC6C084C3C6B`, 0 B, 0/0/0 GC.
- V2-first and V3-first: 102 comparable, 0 state mismatches, 10 unsupported each;
  V3 0 B in 102/102, V2 0 B in 42/102.
- Worker disposal joins every captured thread; worker fault skips post playback;
  stale contexts/epochs and cross-segment deferred values are rejected.
- Local Qwen review suggestions were source-audited. Its proposed volatile-array,
  reseal/merge-race, and lifecycle issues were rejected against ownership/join/
  one-shot-Seal invariants. That review prompted a broader audit that found the
  real caller-free serial-participant bug, which was fixed and regression-tested.

## 34. Deliberately deferred

Work stealing, online threshold learning, affinity/NUMA, scheduler fusion,
nondeterministic throughput mode, runtime system/schema/archetype registration,
general tasks/dataflow/reductions, arbitrary public query/filter DSLs, optional
components, multi-watch public changed jobs, lifecycle event rings, persistence
codecs/save coordination, prefab expansion/content reload, replication/network
transport, admin UI, and managed dependency-injection infrastructure remain
beyond 3.1.0. The next natural product slice should be selected from real-system
integration pressure, not another speculative scheduler mechanism.

## Full V2 versus V3 metrics

The following is the complete accepted V2-first 102-row comparable table. Lower
ratios favor V3. The reverse-order full table is available at
[`_Benchmarks/results/ecs-v2-v3-3.1.0-final-v3-first/ecs-v2-v3-comparison.md`](_Benchmarks/results/ecs-v2-v3-3.1.0-final-v3-first/ecs-v2-v3-comparison.md),
and the immutable joined JSON is
[`_Benchmarks/Baselines/ecs-v2-v3-3.1.0-2026-08-11.json`](_Benchmarks/Baselines/ecs-v2-v3-3.1.0-2026-08-11.json).

+| Scenario | Scale | Boundary | Strength | V2 ns/op | V3 ns/op | V3/V2 mean | V3/V2 p95 | V2 alloc | V3 alloc | State |
| --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `changes.collect-unchanged` | 1000 | EndToEnd | Provisional | 180.000 | 100.000 | 0.556x | 0.565x | 0 | 0 | match |
| `changes.collect-unchanged` | 10000 | EndToEnd | Provisional | 313.333 | 266.667 | 0.851x | 0.673x | 0 | 0 | match |
| `changes.collect-unchanged` | 50000 | EndToEnd | Provisional | 366.667 | 593.333 | 1.618x | 1.750x | 0 | 0 | match |
| `changes.write-collect-100pct` | 1000 | EndToEnd | Provisional | 23.873 | 31.453 | 1.318x | 1.331x | 0 | 0 | match |
| `changes.write-collect-100pct` | 10000 | EndToEnd | Provisional | 24.599 | 31.380 | 1.276x | 1.249x | 0 | 0 | match |
| `changes.write-collect-100pct` | 50000 | EndToEnd | Provisional | 26.203 | 32.972 | 1.258x | 1.178x | 0 | 0 | match |
| `changes.write-collect-10pct` | 1000 | EndToEnd | Provisional | 27.267 | 48.733 | 1.787x | 1.777x | 0 | 0 | match |
| `changes.write-collect-10pct` | 10000 | EndToEnd | Provisional | 26.393 | 47.533 | 1.801x | 1.761x | 0 | 0 | match |
| `changes.write-collect-10pct` | 50000 | EndToEnd | Provisional | 28.819 | 48.464 | 1.682x | 1.553x | 0 | 0 | match |
| `lifecycle.destroy-create-reuse` | 1000 | EndToEnd | Direct | 30.273 | 41.557 | 1.373x | 1.349x | 16408 | 0 | match |
| `lifecycle.destroy-create-reuse` | 10000 | EndToEnd | Direct | 31.983 | 42.589 | 1.332x | 1.358x | 262168 | 0 | match |
| `lifecycle.destroy-create-reuse` | 50000 | EndToEnd | Direct | 33.751 | 45.284 | 1.342x | 1.226x | 1048600 | 0 | match |
| `lookup.read.position.bounded` | 1000 | HotLoop | Provisional | 4.433 | 26.180 | 5.905x | 5.605x | 0 | 0 | match |
| `lookup.read.position.bounded` | 10000 | HotLoop | Provisional | 4.403 | 26.794 | 6.085x | 5.764x | 0 | 0 | match |
| `lookup.read.position.bounded` | 50000 | HotLoop | Provisional | 4.992 | 26.902 | 5.389x | 3.744x | 0 | 0 | match |
| `lookup.read.position` | 1000 | HotLoop | Direct | 4.420 | 27.440 | 6.208x | 6.107x | 0 | 0 | match |
| `lookup.read.position` | 10000 | HotLoop | Direct | 4.417 | 27.157 | 6.149x | 5.809x | 0 | 0 | match |
| `lookup.read.position` | 50000 | HotLoop | Direct | 4.641 | 27.380 | 5.900x | 5.045x | 0 | 0 | match |
| `lookup.write.position.bounded` | 1000 | HotLoop | Provisional | 22.220 | 32.167 | 1.448x | 2.192x | 0 | 0 | match |
| `lookup.write.position.bounded` | 10000 | HotLoop | Provisional | 23.001 | 27.802 | 1.209x | 1.152x | 0 | 0 | match |
| `lookup.write.position.bounded` | 50000 | HotLoop | Provisional | 24.628 | 27.975 | 1.136x | 0.904x | 0 | 0 | match |
| `lookup.write.position` | 1000 | HotLoop | Direct | 22.333 | 32.080 | 1.436x | 1.378x | 0 | 0 | match |
| `lookup.write.position` | 10000 | HotLoop | Direct | 22.355 | 32.384 | 1.449x | 1.422x | 0 | 0 | match |
| `lookup.write.position` | 50000 | HotLoop | Direct | 24.665 | 32.907 | 1.334x | 1.068x | 0 | 0 | match |
| `query.read-position.write-velocity` | 1000 | HotLoop | Direct | 3.940 | 1.313 | 0.333x | 0.297x | 0 | 0 | match |
| `query.read-position.write-velocity` | 10000 | HotLoop | Direct | 3.366 | 1.215 | 0.361x | 0.461x | 0 | 0 | match |
| `query.read-position.write-velocity` | 50000 | HotLoop | Direct | 3.432 | 1.131 | 0.329x | 0.316x | 0 | 0 | match |
| `query.read.position-velocity-health` | 1000 | HotLoop | Direct | 1.293 | 1.433 | 1.108x | 1.203x | 0 | 0 | match |
| `query.read.position-velocity-health` | 10000 | HotLoop | Direct | 1.118 | 1.228 | 1.098x | 1.033x | 0 | 0 | match |
| `query.read.position-velocity-health` | 50000 | HotLoop | Direct | 1.101 | 1.237 | 1.124x | 1.103x | 0 | 0 | match |
| `query.read.position-velocity` | 1000 | HotLoop | Direct | 1.100 | 1.187 | 1.079x | 1.280x | 0 | 0 | match |
| `query.read.position-velocity` | 10000 | HotLoop | Direct | 0.895 | 1.007 | 1.125x | 1.200x | 0 | 0 | match |
| `query.read.position-velocity` | 50000 | HotLoop | Direct | 0.872 | 0.989 | 1.134x | 1.086x | 0 | 0 | match |
| `query.read.position` | 1000 | HotLoop | Direct | 0.613 | 0.727 | 1.185x | 1.712x | 0 | 0 | match |
| `query.read.position` | 10000 | HotLoop | Direct | 0.475 | 0.563 | 1.187x | 1.040x | 0 | 0 | match |
| `query.read.position` | 50000 | HotLoop | Direct | 0.433 | 0.562 | 1.297x | 1.301x | 0 | 0 | match |
| `scheduler.parallel-tick` | 100000 | EndToEnd | Provisional | 5.623 | 0.617 | 0.110x | 0.171x | 216 | 0 | match |
| `scheduler.range-parallel` | 100000 | EndToEnd | Provisional | 1.640 | 1.686 | 1.028x | 1.027x | 64 | 0 | match |
| `scheduler.serial-tick` | 10000 | EndToEnd | Direct | 4.635 | 1.331 | 0.287x | 0.224x | 524336 | 0 | match |
| `structural.churn.balanced-wide-10pct.end-to-end` | 1000 | EndToEnd | Provisional | 128.033 | 59.833 | 0.467x | 0.415x | 17808 | 0 | match |
| `structural.churn.balanced-wide-10pct.end-to-end` | 10000 | EndToEnd | Provisional | 153.637 | 57.223 | 0.372x | 0.327x | 140208 | 0 | match |
| `structural.churn.balanced-wide-10pct.end-to-end` | 50000 | EndToEnd | Provisional | 171.970 | 60.595 | 0.352x | 0.345x | 684208 | 0 | match |
| `structural.churn.balanced-wide-10pct.playback` | 1000 | PlaybackOnly | Provisional | 120.200 | 55.033 | 0.458x | 0.587x | 16160 | 0 | match |
| `structural.churn.balanced-wide-10pct.playback` | 10000 | PlaybackOnly | Provisional | 150.887 | 50.373 | 0.334x | 0.314x | 124160 | 0 | match |
| `structural.churn.balanced-wide-10pct.playback` | 50000 | PlaybackOnly | Provisional | 171.865 | 53.784 | 0.313x | 0.324x | 604160 | 0 | match |
| `structural.churn.balanced-wide-10pct.record` | 1000 | RecordOnly | Provisional | 5.167 | 8.067 | 1.561x | 1.166x | 1648 | 0 | match |
| `structural.churn.balanced-wide-10pct.record` | 10000 | RecordOnly | Provisional | 0.983 | 7.120 | 7.241x | 6.506x | 16048 | 0 | match |
| `structural.churn.balanced-wide-10pct.record` | 50000 | RecordOnly | Provisional | 0.642 | 9.689 | 15.092x | 12.635x | 80048 | 0 | match |
| `structural.create.deferred.end-to-end` | 1000 | EndToEnd | Direct | 23.960 | 51.400 | 2.145x | 1.619x | 36584 | 0 | match |
| `structural.create.deferred.end-to-end` | 10000 | EndToEnd | Direct | 27.900 | 51.909 | 1.861x | 1.788x | 812728 | 0 | match |
| `structural.create.deferred.end-to-end` | 50000 | EndToEnd | Direct | 31.033 | 54.477 | 1.755x | 1.908x | 4012728 | 0 | match |
| `structural.create.deferred.playback` | 1000 | PlaybackOnly | Direct | 23.453 | 46.780 | 1.995x | 1.488x | 28560 | 0 | match |
| `structural.create.deferred.playback` | 10000 | PlaybackOnly | Direct | 27.937 | 48.969 | 1.753x | 1.668x | 732704 | 0 | match |
| `structural.create.deferred.playback` | 50000 | PlaybackOnly | Direct | 30.336 | 46.951 | 1.548x | 1.601x | 3612704 | 0 | match |
| `structural.create.deferred.record` | 1000 | RecordOnly | Direct | 1.227 | 5.360 | 4.370x | 2.692x | 8024 | 0 | match |
| `structural.create.deferred.record` | 10000 | RecordOnly | Direct | 0.543 | 5.363 | 9.882x | 10.920x | 80024 | 0 | match |
| `structural.create.deferred.record` | 50000 | RecordOnly | Direct | 0.507 | 7.609 | 15.006x | 22.100x | 400024 | 0 | match |
| `structural.create.immediate` | 1000 | EndToEnd | Direct | 37.193 | 47.647 | 1.281x | 1.619x | 24392 | 0 | match |
| `structural.create.immediate` | 10000 | EndToEnd | Direct | 63.511 | 45.433 | 0.715x | 0.677x | 1991144 | 0 | match |
| `structural.create.immediate` | 50000 | EndToEnd | Direct | 47.432 | 46.250 | 0.975x | 0.984x | 8282936 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.end-to-end` | 1000 | EndToEnd | Direct | 27.067 | 66.600 | 2.461x | 1.800x | 824 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.end-to-end` | 10000 | EndToEnd | Direct | 16.660 | 64.607 | 3.878x | 3.956x | 8024 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.end-to-end` | 50000 | EndToEnd | Direct | 15.303 | 68.909 | 4.503x | 4.437x | 40024 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.playback` | 1000 | PlaybackOnly | Direct | 20.133 | 58.533 | 2.907x | 2.905x | 0 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.playback` | 10000 | PlaybackOnly | Direct | 17.467 | 59.293 | 3.395x | 2.994x | 0 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.playback` | 50000 | PlaybackOnly | Direct | 15.427 | 57.195 | 3.708x | 3.407x | 0 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.record` | 1000 | RecordOnly | Direct | 6.600 | 9.333 | 1.414x | 0.926x | 824 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.record` | 10000 | RecordOnly | Direct | 1.213 | 8.547 | 7.044x | 6.336x | 8024 | 0 | match |
| `structural.destroy.deferred-sparse-10pct.record` | 50000 | RecordOnly | Direct | 0.452 | 11.289 | 24.976x | 29.663x | 40024 | 0 | match |
| `structural.destroy.deferred.end-to-end` | 1000 | EndToEnd | Direct | 3.547 | 21.893 | 6.173x | 6.450x | 8024 | 0 | match |
| `structural.destroy.deferred.end-to-end` | 10000 | EndToEnd | Direct | 2.969 | 22.238 | 7.491x | 7.591x | 80024 | 0 | match |
| `structural.destroy.deferred.end-to-end` | 50000 | EndToEnd | Direct | 3.553 | 23.594 | 6.640x | 4.671x | 400024 | 0 | match |
| `structural.destroy.deferred.playback` | 1000 | PlaybackOnly | Direct | 3.027 | 13.273 | 4.385x | 4.431x | 0 | 0 | match |
| `structural.destroy.deferred.playback` | 10000 | PlaybackOnly | Direct | 2.675 | 12.910 | 4.827x | 4.843x | 0 | 0 | match |
| `structural.destroy.deferred.playback` | 50000 | PlaybackOnly | Direct | 2.608 | 13.428 | 5.149x | 5.064x | 0 | 0 | match |
| `structural.destroy.deferred.record` | 1000 | RecordOnly | Direct | 0.747 | 8.687 | 11.634x | 11.292x | 8024 | 0 | match |
| `structural.destroy.deferred.record` | 10000 | RecordOnly | Direct | 0.549 | 9.385 | 17.084x | 9.776x | 80024 | 0 | match |
| `structural.destroy.deferred.record` | 50000 | RecordOnly | Direct | 0.239 | 11.317 | 47.310x | 59.066x | 400024 | 0 | match |
| `structural.destroy.immediate` | 1000 | EndToEnd | Direct | 24.960 | 48.400 | 1.939x | 2.995x | 0 | 0 | match |
| `structural.destroy.immediate` | 10000 | EndToEnd | Direct | 27.397 | 46.337 | 1.691x | 1.706x | 0 | 0 | match |
| `structural.destroy.immediate` | 50000 | EndToEnd | Direct | 29.775 | 45.525 | 1.529x | 1.369x | 0 | 0 | match |
| `structural.migration.add-remove` | 1000 | EndToEnd | Direct | 21.803 | 33.593 | 1.541x | 1.566x | 124192 | 0 | match |
| `structural.migration.add-remove` | 10000 | EndToEnd | Direct | 21.040 | 33.702 | 1.602x | 1.609x | 1541352 | 0 | match |
| `structural.migration.add-remove` | 50000 | EndToEnd | Direct | 20.209 | 36.293 | 1.796x | 1.886x | 6780744 | 0 | match |
| `structural.transition.add-default.end-to-end` | 1000 | EndToEnd | Direct | 26.713 | 33.607 | 1.258x | 1.131x | 82856 | 0 | match |
| `structural.transition.add-default.end-to-end` | 10000 | EndToEnd | Direct | 23.154 | 33.677 | 1.454x | 1.495x | 1038640 | 0 | match |
| `structural.transition.add-default.end-to-end` | 50000 | EndToEnd | Direct | 23.359 | 35.973 | 1.540x | 1.602x | 4531600 | 0 | match |
| `structural.transition.add-default.playback` | 1000 | PlaybackOnly | Direct | 22.647 | 29.267 | 1.292x | 1.324x | 49352 | 0 | match |
| `structural.transition.add-default.playback` | 10000 | PlaybackOnly | Direct | 20.658 | 28.517 | 1.380x | 1.362x | 513424 | 0 | match |
| `structural.transition.add-default.playback` | 50000 | PlaybackOnly | Direct | 20.189 | 29.109 | 1.442x | 1.521x | 2433424 | 0 | match |
| `structural.transition.add-default.record` | 1000 | RecordOnly | Direct | 4.087 | 4.933 | 1.207x | 1.180x | 33504 | 0 | match |
| `structural.transition.add-default.record` | 10000 | RecordOnly | Direct | 2.854 | 5.209 | 1.825x | 2.776x | 525216 | 0 | match |
| `structural.transition.add-default.record` | 50000 | RecordOnly | Direct | 2.538 | 4.773 | 1.881x | 1.887x | 2098176 | 0 | match |
| `structural.transition.remove.end-to-end` | 1000 | EndToEnd | Direct | 19.800 | 33.753 | 1.705x | 1.458x | 24944 | 0 | match |
| `structural.transition.remove.end-to-end` | 10000 | EndToEnd | Direct | 16.976 | 33.591 | 1.979x | 2.094x | 248968 | 0 | match |
| `structural.transition.remove.end-to-end` | 50000 | EndToEnd | Direct | 17.048 | 35.654 | 2.091x | 2.132x | 1208968 | 0 | match |
| `structural.transition.remove.playback` | 1000 | PlaybackOnly | Direct | 18.533 | 29.067 | 1.568x | 1.648x | 16632 | 0 | match |
| `structural.transition.remove.playback` | 10000 | PlaybackOnly | Direct | 16.771 | 28.317 | 1.688x | 1.686x | 168656 | 0 | match |
| `structural.transition.remove.playback` | 50000 | PlaybackOnly | Direct | 17.098 | 30.282 | 1.771x | 1.732x | 808656 | 0 | match |
| `structural.transition.remove.record` | 1000 | RecordOnly | Direct | 1.487 | 4.660 | 3.135x | 2.659x | 8312 | 0 | match |
| `structural.transition.remove.record` | 10000 | RecordOnly | Direct | 0.389 | 5.211 | 13.384x | 17.317x | 80312 | 0 | match |
| `structural.transition.remove.record` | 50000 | RecordOnly | Direct | 0.331 | 7.319 | 22.124x | 18.226x | 400312 | 0 | match |
