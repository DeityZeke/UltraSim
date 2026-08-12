# UltraSim Simulation Slice 0 — independent audit and final evidence

Workload identity: `ultrasim-simulation-slice-0-v2-audited`

ECS profile: `3.1.2 scheduler proof hardening`
Status: corrected downstream benchmark; provisional evidence, not an immutable baseline

## 1. Audit verdict

**Luna's implementation required consumer and benchmark repairs.** After those
repairs, Simulation Slice 0 is credible evidence that ECS 3.1.2 is a usable,
fast, bounded simulation substrate.

No production ECS file was changed and no ECS defect survived reduction. The
original `PartitionDisjoint` failure was a violated caller-supplied ownership
proof. The corrected, mechanically unique target domain passes repeatedly.

The slice still identifies public lifecycle publication as the strongest next
kernel capability. It also confirms a smaller public generated-filter authoring
gap. That filter bridge should land with the lifecycle milestone (early enough
for the next slice), not delay lifecycle as a separate optimization project.

The final accepted 100K tiered profile measures approximately:

- serial: `284.2–284.3 us/tick` mean;
- C8: `203.6–210.7 us/tick` mean;
- zero timed caller or worker allocation and zero Gen0/1/2 collections;
- identical oracle, serial, and C8 canonical state;
- 10 archetypes and 359 occupied chunks;
- about `20.0 MiB` serial / `21.7 MiB` C8 of known ECS reservation.

At 1M configured entities, the same workload measures `2.199 ms` serial and
`0.701 ms` C8 mean. The final hashes match exactly.

## 2. Isolation and files

The original Luna change was isolated to:

```text
_Benchmarks/UltraSim.ECS.SimulationSlice0/
_Benchmarks/results/simulation-slice-0/
```

The audit changed only the Slice 0 consumer and its provisional results/report.
It did not modify the ECS, generator, ECS documentation, baselines, version,
solution files, or another benchmark.

The executable uses only public ECS APIs. It does not use reflection, unsafe
storage access, `InternalsVisibleTo`, copied kernel code, or a lifecycle shim.

## 3. Findings and ownership

| Finding | Classification | Original behavior | Repair / disposition |
| --- | --- | --- | --- |
| Damage `PartitionDisjoint` divergence | Slice consumer bug | Luna assumed distinct operation indices implied distinct victims | Proved that respawn-default `Target=0` creates duplicate victims; default remains `SerialOnly`; opt-in probe now uses a provably unique target domain |
| Shared simulation/oracle arithmetic | Semantic-oracle methodology bug | ECS and oracle called the same `SimulationMath` helpers | Added independent `OracleMath`; tiny hand-derived checks guard both transcriptions |
| Canonical comparison allocated heavily | Benchmark methodology bug | default struct `Equals` boxed/reflected while comparing every row | Added explicit field equality; validation now allocates zero bytes |
| Scheduler structural lane exhausted at 100K | Slice capacity bug | fixed 1,024-command lane could not hold the startup 2,048-effect expiration burst | Derive per-producer capacity from worst-case effect, spawn, death, and respawn demand |
| Death/respawn cost dominated the tick | Slice consumer performance mistake | two serial RangeJobs performed checked lookup scans over every actor | Replaced with public generated tracked chunk jobs over the actual alive/dead shapes, using paid entity reconstruction only for accepted transitions |
| Archetype storage was greatly over-reserved | Slice capacity-planning mistake | guessed `maximumChunks` as population divided by 64 | Use public `ChunkLayoutPlanner` + `ArchetypeCapacityPlan`, with two explicit spare chunks |
| Duplicate effect-slot ownership was not detected | Oracle/canonical validation gap | snapshot capture silently overwrote a prior slot entry | Preallocated seen-table now rejects duplicate live slot ownership |
| Data-bearing tier keys | Public API/documentation friction | four 4-byte key columns substitute for required tag filters | Quantified below; no kernel workaround added |
| Create/destroy/membership facts unavailable | Missing ECS capability | changed cursors cannot report topology facts or tombstones | Lifecycle publication remains the recommended next kernel feature |
| Component-mask conflicts serialize disjoint tier systems into different waves | Safe/intentional behavior | scheduler cannot prove route-level disjointness from component masks | Retain conservative behavior; measure before considering route-disjoint graph proofs |

The two mistakes already caught by Luna remain documented:

- its first oracle cleared cooldown while the ECS transition retained it;
- its first oracle retained payload for an inactive destroyed effect.

Both were correctly repaired in the consumer.

## 4. Corrected logical model

The primary 100K profile contains:

```text
89,952 actors
 8,000 world items
 2,048 fixed logical effect slots
100,000 configured entity slots
```

Actors are divided into four fidelity tiers:

```text
T0 = 999 actors, period 1
T1 = 8,995 actors, period 2
T2 = 19,989 actors, period 8
T3 = 59,969 actors, period 60
```

Every actor has a stable logical identity and tier. Alive actors contain
Position, Velocity, Needs, Activity, Health, Cooldown, Target, SpatialCell, and
one tier key. Dead actors retain identity, position, health, cooldown, and tier,
and add `DeadState`. Alive/dead transitions retain the same ECS entity.

The exact rules, independently transcribed in the oracle, are:

- initial position is a deterministic modular function of logical ID;
- initial activity and health are deterministic functions of logical ID;
- a due Needs job updates hunger/fatigue modulo 1,000;
- Steering derives integer velocity from ID, current needs/activity, and tick;
- Movement integrates velocity;
- Spatial recomputes floor-divided 32-unit cells;
- Target Acquisition updates at most 512 alive source actors;
- Damage examines the first at most 256 actors and applies deterministic damage
  to their current alive target;
- actors with health `<= 0` transition to dead with cooldown 20;
- dead actors decrement cooldown and transition alive on a later tick when due;
- world-item values update every 60 ticks;
- effects decrement lifetime, destroy at zero, and are recreated through a
  fixed slot/generation ring with deferred create plus four typed replacements.

Effect velocity is initialized and canonically checked but Slice 0 intentionally
does not include an effect-movement system. This is a workload limitation, not
an omitted ECS operation.

All state is integer-based. No wall clock, runtime RNG, worker ordinal, entity
row, chunk handle, or physical archetype ID affects the logical result.

## 5. Tick and barrier semantics

The graph writes component data immediately through issued capabilities, but
structural commands commit only at the post-simulation barrier.

Death semantics are therefore:

```text
damage tick N
→ Death records alive→dead tick N
→ Respawn still sees the pre-barrier alive topology tick N
→ transition commits after graph N
→ earliest dead-shape processing is tick N+1
```

Effect destruction and creation are similarly recorded during Cleanup and
become visible only after the graph. The oracle models the same ordering. A
newly created effect can first be observed by a changed-row system on the next
tick because the observer graph ran before the creation barrier.

Initial effects all begin with the same lifetime. The first expiration boundary
therefore destroys all 2,048 effects and begins refilling the ring. The 256-tick
warmup is long enough to reach steady churn before measurement.

## 6. Independent semantic oracle

`SemanticOracle` owns separate actor, effect, generation, item, and just-died
arrays. It has no archetypes, chunks, scheduler, worker identities, command
buffer, or ECS entity handles.

The audit removed shared arithmetic helpers. `OracleMath` independently
implements position, velocity, cell, effect, health, activity, and item rules.
The canonical snapshot is indexed only by actor logical ID, effect slot, and
item logical ID. It compares every field before producing the hash.

Additional deterministic self-tests cover:

- hand-derived actor-zero position/activity/health;
- cadence boundaries at ticks `0,1,2,7,8,59,60,61`;
- forced death and no same-tick respawn;
- next-tick respawn when cooldown reaches the threshold;
- effect expiration before spawn-ring activation;
- same-barrier effect slot replacement and generation increment;
- original random-write ownership collision;
- 256 ticks of collision-free corrected random-write ownership.

Capture rejects two live ECS entities claiming one logical effect slot. Logical
death/respawn/create/destroy counts must equal the oracle, attempted commands
must equal scheduler-merged commands, and final alive count must equal the
canonical population.

## 7. `PartitionDisjoint` verdict

**Verdict: consumer violated the contract; no ECS defect reproduced.**

The original rolling acquisition updates sources:

```text
(tick * 17 + operation) % actorCount
```

Damage always reads sources `0..255`. When one of those actors dies, its Target
component disappears. Respawn initializes a new Target column to zero. Unless
that source happens to be inside the current rolling acquisition window, several
respawned attackers can retain target zero.

The independent analyzer finds the first reduced collision at tick 31:

```text
source 224 → target 0
source 240 → target 0
```

The original unsafe code failed 8/8 isolated 100K C8 runs between ticks 32 and
54 with variant/lost health updates. Distinct operation indices were not a proof
of distinct writable component rows.

The default production profile correctly keeps Damage `SerialOnly`. The
`--probe-random-writes` profile now makes Target Acquisition cover the fixed
damage-source range every tick; target selection is an additive permutation, so
all live victim Health rows are unique. The final corrected probe passed 4/4
isolated 2,048-tick processes (12/12 including earlier corrected-domain runs),
with one canonical hash and zero ECS allocation.

Target Acquisition itself is safe: its operation-to-source mapping is injective
for the bounded operation count, so each participant writes a distinct Target
row. The current 3.1.2 contract remains correct: `PartitionDisjoint` is a
caller-supplied complete ownership proof, not a hint.

## 8. Tier filter authoring verdict

The public generated facade has no filter-authoring overload, while generated
data columns correctly reject storage-free tags. Luna therefore used four
data-bearing tier-key components. The limitation is confirmed in the public
surface; internal filter machinery exists but is not exposed.

For the 100K tiered profile the workaround costs:

| Quantity | Cost |
| --- | ---: |
| Additional data component types | 4 |
| Physical tier columns across alive/dead archetypes | 8 |
| Resident marker payload | 359,808 B |
| Average tier rows processed per tick | 8,994.61 |
| Marker bytes read by four dense families per tick | 143,913.7 B |
| Bytes avoidable with tags + existing LogicalIdentity | 71,956.9 B/tick |
| Dense system registrations / prepared plans | 16 / 16 |

Needs and Steering require logical identity anyway, so half of marker bandwidth
can be replaced by the already-present `LogicalIdentity` read. Movement and
Spatial need no identity; their marker reads are wholly avoidable. Different
cadences still require four scheduled registrations per family, so a filter API
does not magically collapse the scheduler graph.

The smallest useful future API is a cold, world-owned filter handle containing
required/excluded/enabled/disabled component IDs, accepted by generated
`Prepare...Job` methods. Runtime execution should continue using the existing
sealed routes. No new runtime query framework is justified.

This is meaningful ergonomics and modest bandwidth/memory pressure, not a reason
to postpone lifecycle publication.

## 9. Corrected system graph

| Systems | Stage | Period | Generated access | Supplemental access | Structural | Execution / random policy | Dependency |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| Needs ×4 | Input | 1/2/8/60 | tier + activity read; needs write | none | none | Parallel | none |
| Steering ×4 | Movement | 1/2/8/60 | tier + needs + activity read; velocity write | none | none | Parallel | same-tier Needs |
| Movement ×4 | Movement | 1/2/8/60 | tier + velocity read; position write | none | none | Parallel | same-tier Steering |
| Spatial ×4 | SpatialIndex | 1/2/8/60 | tier + position read; cell write | none | none | Parallel | same-tier Movement |
| Target Acquisition | Perception | 1 | RangeJob | needs read; target write | none | Parallel / PartitionDisjoint | none |
| Damage | Combat | 1 | RangeJob | needs + target read; health write | none | Parallel / SerialOnly | Target |
| Death | Combat | 1 | identity + needs + health read; tracked cooldown write | entity reconstruction | alive→dead | Auto | Damage |
| Respawn | Cleanup | 1 | identity + dead read; tracked cooldown + health write | entity reconstruction | dead→alive | Auto | Death |
| Effect Lifetime | Cleanup | 1 | identity read; lifetime write | entity reconstruction | destroy | Parallel | none |
| Effect Spawn | Cleanup | 1 | RangeJob | none | create + four typed replacements | Parallel | Effect Lifetime |
| World Items | WorldSim | 60 | identity + position read; item write | none | none | Parallel | none |
| Presentation | Cleanup | 1 | exact changed Position | cursor | none | Serial | Respawn + Spawn |
| Replication | Cleanup | 2 | exact changed Health | cursor | none | Serial | Respawn + Spawn |
| Persistence | Cleanup | 60 | exact changed Position | cursor | none | Serial | Respawn + Spawn |

There are 26 registered systems. Component-level graph conflicts conservatively
place same-component tier writers in distinct waves even though their routes are
archetype-disjoint. This is safe and deterministic. Route-disjoint graph proofs
may be a future optimization only if a real workload shows the lost concurrency
matters.

The compiled IDs/waves and 10,000-tick tiered run counts are:

| IDs | Family | Stage | Periods | Compiled waves | Runs per member |
| --- | --- | --- | --- | --- | --- |
| 0/4/8/12 | Needs T0/T1/T2/T3 | Input | 1/2/8/60 | 0/1/2/3 | 10000/5000/1250/166 |
| 1/5/9/13 | Steering T0/T1/T2/T3 | Movement | 1/2/8/60 | 0/2/4/6 | 10000/5000/1250/166 |
| 2/6/10/14 | Movement T0/T1/T2/T3 | Movement | 1/2/8/60 | 1/3/5/7 | 10000/5000/1250/166 |
| 3/7/11/15 | Spatial T0/T1/T2/T3 | SpatialIndex | 1/2/8/60 | 0/1/2/3 | 10000/5000/1250/166 |
| 16/17/18 | Target / Damage / Death | Perception / Combat | 1 | 0/0/1 | 10000 each |
| 19/20/21 | Respawn / Effect lifetime / Effect spawn | Cleanup | 1 | 0/0/1 | 10000 each |
| 22 | World items | WorldSim | 60 | 0 | 166 |
| 23/24/25 | Presentation / Replication / Persistence | Cleanup | 1/2/60 | 2/2/2 | 10000/5000/166 |

All start offsets are zero. The grouped semantic table above records generated,
supplemental, structural, and dependency capabilities; the final JSON preserves
the complete per-node public scheduler snapshot.

Public system timing is wave-inclusive for nodes executed concurrently in one
wave. Those values are useful for locating expensive waves but must not be
summed as exclusive per-system CPU time.

## 10. Correctness validation

The final validation matrix includes:

- clean Debug and Release builds;
- deterministic self-tests on every process start;
- 100K, 256 warmup + 2,048 measured ticks;
- serial and C8 with a full canonical checkpoint every eight ticks;
- final oracle/serial/C8 hash `B51A17A12EE109CC`;
- four final isolated corrected `PartitionDisjoint` probes;
- serious 100K serial/C8 hash `61C8B1872C75BC67`;
- 1M serial/C8 hash `8E9978581C27A90A`;
- all-active serial/C8 hash `EB98FEB8AAA139DF`;
- exact logical structural count equality;
- attempted-command/merged-command equality;
- duplicate effect ownership rejection;
- zero warmed caller/worker allocation and zero timed GC.

A final post-edit smoke run against the exact current Release binary used 100K,
64 warmups, and 256 measured ticks. Serial/C8 both produced
`311A81DCC2443D6D` with zero ECS allocation.

No canonical, observer, population, structural, or allocation mismatch remains.

## 11. Measurement method

Accepted timing processes use:

```text
Release
DOTNET_TieredCompilation=0
DOTNET_ReadyToRun=0
DOTNET_GCServer=1
7 background workers + caller for C8
256 warmup ticks
fixed 1/60 delta
```

The serious 100K profile runs 10,000 measured ticks in isolated process order:

```text
Serial A → Parallel A → Parallel B → Serial B
```

Correctness runs use interleaved checkpoints. Serious timing uses
`--deferred-validation`: ECS ticks run continuously inside the measured region;
the independent oracle catches up and performs the full comparison afterward.
This avoids full-world validation polluting the next timing sample.

The original struct `Equals` validation allocated about 3.47 GB over one
512-tick 10K audit run. Explicit equality reduced both interleaved and deferred
validation to zero allocation. Oracle and JSON work are outside `World.Tick`.

## 12. Final 100K results

All values are microseconds per complete ECS tick.

| Process | Mean | p50 | p95 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Serial A | 284.334 | 276.218 | 537.625 | 1,151.280 | 1,396.127 | 1,451.207 |
| Parallel A | 203.555 | 193.938 | 334.197 | 528.766 | 664.664 | 1,075.490 |
| Parallel B | 210.691 | 202.008 | 344.837 | 545.105 | 857.612 | 1,565.377 |
| Serial B | 284.156 | 277.738 | 536.725 | 1,151.890 | 1,392.528 | 1,436.277 |

The two-process means are approximately `284.245 us` serial and `207.123 us`
C8: a `1.37x` speedup, or about 27% lower mean tick time. The cadence and
persistence bursts intentionally produce a multimodal tail.

The C8 A phase totals over 10,000 ticks are:

| Phase | Total | Mean/tick |
| --- | ---: | ---: |
| Graph | 1,866.949 ms | 186.695 us |
| Structural lane merge | 37.257 ms | 3.726 us |
| Post barrier playback | 128.074 ms | 12.807 us |
| Pre barrier | 0.201 ms | 0.020 us |

## 13. 1M scale results

The 1M profile uses 917,952 actors, 80,000 items, and 2,048 effect slots. It
runs 256 warmups and 1,000 measured ticks.

| Mode | Mean | p50 | p95 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Serial | 2.199 ms | 1.325 ms | 4.767 ms | 11.756 ms | 14.197 ms | 14.207 ms |
| C8 | 0.701 ms | 0.645 ms | 1.586 ms | 3.335 ms | 4.386 ms | 4.860 ms |

C8 is `3.14x` faster by mean. The workload scales without a storage or scheduler
cliff; fidelity cadence creates the expected periodic spikes.

C8 phase means are approximately:

```text
graph             683.541 us/tick
structural merge    4.021 us/tick
post barrier       12.690 us/tick
total             700.661 us/tick
```

The generated Death system visits roughly 918K alive rows in about 175 us per
invocation. The exact Position observer's normal wave is about 156 us at 3,585
occupied chunks; a due 60-tick persistence burst is about 1.32 ms and delivers
up to 919,872 rows. These wave-inclusive observations explain the tail.

## 14. Consumer repair impact

The first audited numbers measured consumer mistakes, not the best public ECS
expression. They are retained as diagnostic evidence only.

| Population/mode | Full actor RangeJobs | Generated chunk repair | Improvement |
| --- | ---: | ---: | ---: |
| 100K serial | ~6.04 ms | ~0.284 ms | ~21.3x |
| 100K C8 | ~5.84 ms | ~0.207 ms | ~28.2x |
| 1M serial | ~58.38 ms | 2.199 ms | ~26.5x |
| 1M C8 | ~59.70 ms | 0.701 ms | ~85.2x |

Replacing guessed chunk limits with the public planners reduced known ECS
reservation from roughly `81.7/83.4 MiB` to `20.0/21.7 MiB` at 100K and from
roughly `710.6/712.3 MiB` to `146.7/148.5 MiB` at 1M (serial/C8 respectively).

The lesson is not that RangeJobs are slow. They are appropriate for bounded
non-ECS work and prepared sparse lists. The mistake was scanning every actor
through checked random access for work naturally selected by archetype shape.

## 15. Fidelity cadence control

The optional `--all-active` control runs every tier pipeline every tick without
duplicating a second simulation implementation.

| 100K mode | Tiered 1/2/8/60 mean | All-active mean | Work avoided by cadence |
| --- | ---: | ---: | ---: |
| Serial | ~0.284 ms | 1.240 ms | ~77.1% tick cost |
| C8 | ~0.207 ms | 0.553 ms | ~62.6% tick cost |

All-active serial/C8 state matches canonically. This confirms that scheduler
cadence avoids real query execution; it is not an in-job branch over 100K rows.

## 16. Random, structural, and observer work

Over the final 100K/10,000-tick C8 A run:

```text
Target attempts/writes/dead skips     5,120,000 / 5,115,429 / 4,571
Damage attempts/alive sources/writes  2,560,000 / 2,275,626 / 890,424
Actor deaths/respawns                 54,275 / 54,201
Effects created/destroyed             320,000 / 320,000
Structural commands attempted/merged  2,028,476 / 2,028,476
Typed structural payload merged       8,960,000 bytes
```

Observer results:

| Consumer | Calls | Rows delivered | Mean/call | Maximum |
| --- | ---: | ---: | ---: | ---: |
| Presentation Position | 10,000 | 89,759,072 | 8,975.9 | 89,947 |
| Replication Health | 5,000 | 491,916 | 98.4 | 260 |
| Persistence Position | 166 | 15,250,752 | 91,872 | 91,872 |

The independent cursors do not destructively interfere. Persistence's deliberate
60-tick lag creates the expected large burst.

At 1M C8, Presentation delivers 91,342,939 rows over 1,000 calls; Replication
delivers 33,927 over 500; Persistence delivers 14,717,952 over 16, with a
919,872-row maximum burst.

## 17. Memory and chunk topology

| Profile | Mode | Working set | Private bytes | Known ECS | Known bytes/configured slot | Occupied chunks |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 100K | Serial | 73.8 MiB | 98.1 MiB | 19.1 MiB | 199.8 B | 359 |
| 100K | C8 | 75.4 MiB | 157.0 MiB | 20.7 MiB | 217.1 B | 359 |
| 1M | Serial | 312.1 MiB | 374.6 MiB | 139.9 MiB | 146.7 B | 3,585 |
| 1M | C8 | 311.5 MiB | 433.4 MiB | 141.6 MiB | 148.5 B | 3,585 |

The parallel private-memory difference includes persistent workers, stacks, and
per-participant scheduler/structural reservations. Managed oracle/entity arrays
also contribute to process memory and are intentionally kept distinct from
known ECS payload.

Planned row capacities are:

```text
alive actor archetype  264 rows/chunk
dead/effect archetype  496 rows/chunk
item archetype         800 rows/chunk
```

Every archetype reserves exactly its public capacity-plan requirement plus two
explicit spare chunks. Full per-archetype snapshots are present in final JSON.

## 18. Allocation and GC

Every accepted final timing run reports:

```text
ECS tick caller bytes       0
scheduler worker bytes      0
Gen0 / Gen1 / Gen2          0 / 0 / 0
```

System snapshot copying, canonical capture, oracle catch-up, memory capture, JSON
construction, and console output occur outside primary tick timing.

## 19. Lifecycle evidence

The corrected workload still cannot incrementally publish these facts:

| Fact | What exists today | Missing information |
| --- | --- | --- |
| Effect created | initialized component change may be visible next tick | one correlated create fact and resulting identity |
| Effect destroyed | row disappears | destroyed identity/tombstone for delayed consumers |
| Alive→dead | retained values/history survive migration | source/destination membership transition |
| Dead→alive | later component values are readable | explicit topology transition |
| Component add/remove | changed data columns can be observed | membership delta, including storage-free tags |

An observer can maintain a full known set and rescan `Has`/`TryRead`, but that is
not incremental publication and fails the delayed-consumer requirement without
duplicating lifecycle state outside the ECS.

The next kernel feature should therefore be fixed-capacity lifecycle streams
with deterministic barrier ordering, independent cursors, partial reads without
loss, tombstone identity, explicit lag/overflow behavior, and zero-allocation
consumption. Slice 0 should become its first integration consumer.

## 20. Positive public-API findings

- Ten bounded archetypes and eight transitions were straightforward to build.
- Public chunk-layout/capacity planning completely solved over-reservation once
  the consumer used it.
- Generated tracked chunk jobs express topology-selected mutation extremely
  well and removed two pathological checked scans.
- Paid entity reconstruction composes cleanly with worker structural writers.
- Deferred create plus typed replacement Add is practical.
- Scheduler cadence, stages, dependencies, static partitions, and snapshots are
  deterministic and allocation-free.
- Independent exact cursors naturally support three consumer cadences.
- Canonical serial/C8 replay remained stable through every consumer repair.

## 21. Remaining limitations

This is still a substrate slice, not the game:

- no real spatial index or AOI query;
- no physics, pathfinding, networking, serialization, or renderer;
- no lifecycle stream;
- no public generated filter authoring;
- fixed deterministic operation sets rather than player-driven input;
- integer movement and no effect movement;
- exact-history consumers report latest changed rows, not every intermediate
  value event;
- system snapshot times are wave-inclusive;
- the 1M profile has 1,000 samples, enough for scale/mean/p95 but not a durable
  long-tail baseline;
- no honest V2 port of this complete slice exists yet.

## 22. Recommendation

Simulation Slice 0 now constitutes credible evidence about ECS 3.1.2.

Issue ownership is:

```text
Luna / downstream consumer:
    invalid random-write ownership proof
    shared oracle helpers
    boxing canonical comparison
    undersized structural lane
    full-population checked RangeJobs
    guessed chunk counts
    missing duplicate effect-slot check

Public API friction:
    no cold generated-job filter authoring
    easy to overstate PartitionDisjoint without a prepared proof object

Missing ECS capability:
    lifecycle publication

Confirmed ECS defects:
    none
```

The next implementation should remain lifecycle publication, immediately wired
into Slice 0's effect and alive/dead consumers. Add the narrow public cold filter
handle during that milestone or directly afterward, before expanding the next
simulation slice. Do not spend the next checkpoint on generic scheduler or
micro-optimization work: the corrected C8 engine already runs this heterogeneous
100K slice in about 0.21 ms and the 1M slice in about 0.70 ms. Let lifecycle and
the next downstream integration expose the next real bottleneck.

## 23. Reproduction

Build:

```bash
dotnet build _Benchmarks/UltraSim.ECS.SimulationSlice0/UltraSim.ECS.SimulationSlice0.csproj -c Release
```

Correctness:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 DOTNET_GCServer=1 \
dotnet run --project _Benchmarks/UltraSim.ECS.SimulationSlice0/UltraSim.ECS.SimulationSlice0.csproj \
  -c Release --no-build -- --population 100000 --warmup 256 --ticks 2048 \
  --validate-every 8 --mode both \
  --output _Benchmarks/results/simulation-slice-0/100k-audited-validation-final.json
```

Serious timing:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 DOTNET_GCServer=1 \
dotnet run --project _Benchmarks/UltraSim.ECS.SimulationSlice0/UltraSim.ECS.SimulationSlice0.csproj \
  -c Release --no-build -- --population 100000 --warmup 256 --ticks 10000 \
  --deferred-validation --mode parallel \
  --output _Benchmarks/results/simulation-slice-0/100k-tiered-parallel-a-final.json
```

Corrected random-write ownership probe:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.SimulationSlice0/UltraSim.ECS.SimulationSlice0.csproj \
  -c Release --no-build -- --population 10000 --warmup 256 --ticks 2048 \
  --validate-every 32 --mode parallel --probe-random-writes
```
