# ECS 100K Mixed-Operation Soak

Status: executable 3.0.35 baseline; single-owner kernel/facade workload, not yet a
scheduler, network, persistence, prefab, or full gameplay benchmark

## Purpose

This soak is the first production-shaped longitudinal workload for ECS V3. It is
designed to answer a different question from the isolated primitive suite:

> Can the current ECS repeatedly combine traversal, random access, exact change
> tracking, and structural churn over a stable 100,000-entity world without
> allocation, state drift, fragmentation growth, or pathological tail latency?

It is not a V2/V3 comparison and does not change the ECS kernel version. ECS
3.0.35 remains the active checkpoint. Future public jobs, lifecycle streams,
scheduler, prefab, persistence, and networking work should rerun this stable
scenario to show when and how previously established costs change.

The stable machine-readable identities are report
`ultrasim-ecs-mixed-soak-report-v1`, workload
`ecs-v3-100k-mixed-operations-v1`, and canonical state
`ultrasim-ecs-mixed-soak-canonical-v1`. A future semantic change must use a new
profile rather than silently joining incompatible captures.

## Workload contract

The default world contains exactly 100,000 live entities split evenly between:

- a base archetype containing identity, position, enableable velocity, health,
  and exact-tracked activity;
- a wide archetype containing the same retained components plus 64 bytes of wide
  payload.

Every tick performs:

| Phase | Useful work |
| --- | ---: |
| generated component systems | four complete passes / 400,000 row visits |
| logical query traffic | 5,200,000 component bytes |
| exact tracked scalar writes | 5,000 unique entities |
| checked random reads | 500 entities |
| exact changed-row consumer | all activity rows visible to its independent cursor |
| deferred destroy | 100 entities, balanced by archetype |
| deferred default create | 100 replacements in the destroyed entities' archetypes |
| deferred transition | 100 base-to-wide plus 100 wide-to-base |
| structural commands | 400 total |

The four generated passes are velocity-to-position movement, position-to-health
derivation, health-to-velocity response, and a read-only position/health telemetry
pass. They perform deliberately small but real component arithmetic. Query time is
therefore not pure ECS dispatch overhead.

The population remains exactly 100,000. Logical slots survive destroy/create
cycles while their runtime entity handle and lifecycle revision advance. Balanced
transitions keep both archetypes at 50,000 rows, permitting the soak to detect
unexpected occupancy and fragmentation drift instead of hiding it behind changing
population.

## Deterministic replay and canonical state

The runner synthesizes operations from a fixed xorshift seed without allocating
an event stream. Before timing, two independently constructed worlds replay 256
ticks and compare canonical snapshots at ticks 0, 64, 128, 192, and 256.

The canonical snapshot is ordered by benchmark-local logical identity and covers:

- liveness and complete population;
- lifecycle revision and entity generation;
- logical archetype membership;
- identity, position, velocity, health, activity, and optional wide values;
- velocity enable state;
- base/wide archetype population;
- entity-location ownership, verified by a physical identity traversal outside
  the normalized hash;
- world and consumer change versions;
- rows and values visible to a copied exact-change cursor without acknowledging
  the real consumer;
- cumulative query, access, change, and structural work counts.

World IDs, chunk handles, physical row order, and chunk placement are excluded.
Every logical entity must occur exactly once in the physical identity traversal,
and its entity-pool index must agree with packed-row ownership before a hash is
accepted.

The two sustained measured worlds then run the same seed and must finish with the
same canonical hash and checksum. Canonical scans are outside timed ticks. This is
a same-implementation determinism proof, not an independent semantic reference
model or cross-generation hash.

## Measurement policy

The accepted default uses:

- 2,000 warmup ticks per measured world;
- 30,000 measured ticks per world;
- two independently constructed measured worlds;
- 60,000 total timing samples, giving approximately 60 observations in the
  reported p99.9 tail;
- natural GC behavior with no forced collection or no-GC region;
- `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0` for the current accepted
  steady-state profile;
- per-phase timestamp samples, with deterministic target selection reported
  separately from measured ECS work;
- allocation counters around only the measured tick loop;
- managed heap, retained managed bytes, working set, native reservation, chunk
  occupancy, and metadata snapshots outside the timed loop.

`MeasuredEcsWork` includes generated query visitors, checked reads, tracked
writes, exact change consumption, command recording and playback, and initialized
publication of new entities. It excludes target selection and canonical hashing.
`TotalTick` adds target selection and timestamp gaps. Neither number includes
gameplay outside the listed component arithmetic.

## Accepted 3.0.35 result

The accepted local capture is
`Baselines/ecs-v3-3.0.35-100k-mixed-soak-2026-08-11.json`, recorded on an AMD Ryzen 7
7800X3D under .NET 8.0.29/CachyOS. Both 30,000-tick runs passed. Their total-tick
means were 1,314.472 and 1,316.761 microseconds, a difference of approximately
0.17%.

Combined 60,000-tick distribution:

| Phase | Mean | p95 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: |
| queries | 721.880 us | 748.344 | 785.423 | 843.132 | 1,220.629 |
| checked random reads | 24.090 us | 26.700 | 29.140 | 56.970 | 254.777 |
| exact tracked writes | 280.390 us | 292.697 | 317.577 | 367.007 | 553.675 |
| exact change consume | 191.009 us | 201.058 | 220.878 | 255.668 | 320.648 |
| command record | 2.969 us | 3.020 | 4.310 | 7.250 | 51.970 |
| structural playback | 62.833 us | 67.579 | 80.039 | 106.269 | 230.178 |
| create initialization | 26.075 us | 29.000 | 33.190 | 58.440 | 127.388 |
| measured ECS work | 1,309.245 us | 1,350.618 | 1,397.988 | 1,493.527 | 2,126.361 |
| total tick | 1,315.617 us | 1,357.328 | 1,404.578 | 1,505.327 | 2,143.741 |

Representative normalized costs are:

- generated queries: 1.805 ns/visited row and 7.203 GB/s logical component
  traffic across the mixed read/write passes;
- checked random reads: 48.18 ns/read;
- exact tracked scalar writes: 56.08 ns/write;
- exact changed-row delivery: approximately 37.49 ns/delivered row at an average
  5,095.015 delivered rows/tick;
- structural command recording: 7.42 ns/command;
- mixed structural playback: 157.08 ns/command;
- six-component/enable publication of replacements: 260.75 ns/entity.

The complete measured pair executed 24 billion generated query row visits, 300
million tracked writes, 30 million checked reads, 24 million structural commands,
and approximately 305.7 million exact changed-row deliveries.

Correctness and stability facts:

- interval canonical replay matched;
- both measured worlds produced final hash
  `4450A5AC6A7A1296151BFC6C084C3C6B`;
- zero timed managed allocation and zero Gen0/Gen1/Gen2 collections;
- zero rejected structural commands;
- 1.5 million eligible wide-row temporary holes per measured world were all
  reused, with zero leftover compaction and zero donor relocation;
- base/wide populations remained 50,000/50,000;
- acquired and occupied chunks remained 50/50 for base and 149/149 for wide;
- reserved native chunk payload remained 36,175,872 bytes total;
- exact-row change metadata was 5,943,936 bytes total;
- the fixed 400-command arena reserved 16,044 payload bytes and reached exactly
  its configured high-water mark each tick.

The 1.316 ms mean consumes about 8.22% of a 16 ms tick. The complete listed ECS
work is above the aspirational 0.5 ms infrastructure target, but that comparison
must not erase useful work: 54.9% of total time is four component-system passes,
21.3% is 5,000 checked/exact writes, and 14.5% is delivery of about 5,095 exact
changes. Command record, playback, and replacement initialization together cost
about 91.9 microseconds/tick. The soak therefore identifies tracked random writes
and exact change delivery—not structural churn—as the next likely performance
investigation if this operation mix resembles production.

The p99.9 total was 1.505 ms and the observed maximum was 2.144 ms, both far below
the 16 ms server-tick budget on this machine. These are local single-owner results,
not a cross-machine latency guarantee.

## Running it

From the repository root:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release -- --soak
```

The default output is `_Benchmarks/results/ecs-3.0.35-100k-mixed-soak.json`.
Supported controls are:

- `--soak-output <path>`;
- `--soak-entities <count>`;
- `--soak-warmup <ticks>`;
- `--soak-ticks <ticks>`;
- `--soak-runs <count>`;
- `--soak-validation-ticks <ticks>`;
- `--soak-checkpoint-interval <ticks>`;
- `--soak-seed <decimal-or-0x-hex>`.

At least 1,000 measured ticks and two measured replays are enforced. A useful
development smoke is 4,096 entities, 100 warmups, 1,000 measured ticks, two runs,
64 validation ticks, and 16-tick checkpoints.

## Next benchmark expansions

Keep this exact workload and seed stable as a longitudinal guard. Add separate
profiles rather than silently changing its contract:

- query-light versus query-heavy mixes;
- 1%, 5%, 10%, and dense tracked-write/change densities;
- lower and higher structural churn;
- unbalanced create/destroy topology that leaves holes for final compaction;
- many-archetype and fragmented-route variants;
- a deliberately slow change consumer;
- scheduler serial/parallel parity and crossover once public jobs exist;
- lifecycle-stream publication once implemented;
- prefab spawn/despawn, persistence checkpoint, and network capture/apply phases;
- one-hour and overnight native-retention runs;
- child-process captures across several machines and cache sizes.

Tests and measurements may change which expansion should come first. The current
profile says the next useful question is the cost surface of sparse tracked writes
plus exact changed-row consumption, while preserving this accepted scenario as
the control.
