# ECS 100K Mixed-Operation Soak

Status: accepted 3.0.35 internal control, accepted 3.0.36 public serial generated
chunk-job control, and accepted 3.1.1 serial/parallel scheduled parity profile;
not yet a network, persistence, prefab, or full gameplay benchmark

## Purpose

This soak is the first production-shaped longitudinal workload for ECS V3. It is
designed to answer a different question from the isolated primitive suite:

> Can the current ECS repeatedly combine traversal, random access, exact change
> tracking, and structural churn over a stable 100,000-entity world without
> allocation, state drift, fragmentation growth, or pathological tail latency?

It is not a V2/V3 comparison. The original 3.0.35 internal profile remains
immutable. ECS 3.0.36 adds a separately named public-job profile with the same
useful workload and canonical state. ECS 3.1.1 adds scheduler serial/parallel
execution while preserving the same logical work; lifecycle streams, prefab,
persistence, and networking work should continue adding profiles rather than
silently rewriting either control.

The stable machine-readable identities are report
`ultrasim-ecs-mixed-soak-report-v1`, workload
`ecs-v3-100k-mixed-operations-v1`, and canonical state
`ultrasim-ecs-mixed-soak-canonical-v1`. A future semantic change must use a new
profile rather than silently joining incompatible captures. The public execution
profile is `ecs-v3-100k-mixed-operations-public-jobs-v1`.
The scheduled parity profile is
`ecs-v3-100k-mixed-operations-scheduler-parity-v1`. The separate 3.1.0
scheduler-shaped stress workload is intentionally not assigned either
longitudinal identity because its target selection and structural mix differ.

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

The 3.1.1 parity runner additionally emits a normalized logical hash that omits
observation-version numbering while retaining entity generations, ownership,
membership, values, enable state, lifecycle revisions, change-visible rows,
structural outcomes, checksums, and work counters. This is necessary because
5,000 public scalar writes advance 5,000 versions while one scheduled logical
system invocation intentionally shares one publication version. Public,
scheduled-serial, and scheduled-parallel worlds must match the normalized hash;
the two scheduled worlds must also match the full observation-version hash.

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

## Accepted 3.0.36 public-job profile

The accepted public capture is
`Baselines/ecs-v3-3.0.36-100k-mixed-soak-public-jobs-2026-08-11.json` on the same
host/runtime policy. It moves the four system-like query passes, exact changed-row
consumer, change cursors, canonical ownership traversal, and store/arena
inspection through public APIs. It removes reflection/private `_core` access from
that profile. The 5,000 arbitrary entity-list activity writes remain checked
random access, so the useful workload has not been disguised as a 100K scan.

Both 30,000-tick runs passed with the same canonical hash as the original
3.0.35 control, `4450A5AC6A7A1296151BFC6C084C3C6B`, zero timed allocation,
zero Gen0/1/2 collections, zero structural rejection, stable 50K/50K population,
and complete reuse of all 1.5 million eligible temporary holes per run.

Combined 60,000-tick public distribution:

| Phase | Mean | p95 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: |
| public generated queries | 711.610 us | 755.063 | 791.613 | 855.602 | 1,289.428 |
| checked random reads | 23.917 us | 26.810 | 31.540 | 77.359 | 299.208 |
| exact tracked writes | 278.893 us | 295.117 | 332.307 | 399.136 | 686.994 |
| public exact change consume | 190.984 us | 203.008 | 242.328 | 278.048 | 424.607 |
| command record | 2.887 us | 2.900 | 4.270 | 6.900 | 65.489 |
| structural playback | 60.158 us | 65.580 | 79.549 | 119.429 | 312.457 |
| create initialization | 25.797 us | 28.859 | 34.579 | 82.919 | 334.957 |
| measured ECS work | 1,294.247 us | 1,341.828 | 1,404.268 | 1,503.947 | 2,181.111 |
| total tick | 1,300.487 us | 1,348.068 | 1,411.027 | 1,510.956 | 2,198.000 |

The original and public captures were separate processes, so small deltas are
directional. The public total mean is about `1.15%` lower than the original,
query mean about `1.42%` lower, and total p95 about `0.68%` lower; p99/p99.9/max
are within approximately `0.4%/0.4%/2.5%` in the opposite direction. This is an
effective tie, not a speedup claim. It demonstrates that the public lifecycle/
plan boundary adds no measurable per-row tax to this sustained workload.

Representative public normalized costs are `1.779 ns/query row`, `47.84
ns/random read`, `55.78 ns/tracked scalar write`, `37.49 ns/exact delivered
row`, `7.22 ns/recorded command`, and `150.40 ns/playback command`.

## Accepted 3.1.1 scheduled parity profile

The accepted scheduler artifact is
`Baselines/ecs-v3-3.1.1-scheduler-capability-parity-hardening-2026-08-11.json`.
Unlike the preserved 3.1.0 scheduler stress workload, this profile reuses the
original deterministic target-selection and canonical-state machinery. Its
public control and both scheduler modes execute the same four 100K query passes,
5,000 arbitrary selected writes, 500 RNG-selected reads, exact change consumer,
100 destroys, 100 default creates, 200 transitions, and replacement publication.

Combined 30,000-tick distributions after 2,000 warmups:

| Surface | Mean | p95 | p99 | Allocation |
| --- | ---: | ---: | ---: | ---: |
| public generated-job control | 1,188.522 us | 1,235.919 | 1,289.798 | 0 B |
| scheduled serial | 1,271.007 us | 1,322.398 | 1,376.587 | 0 B |
| scheduled parallel, 8 participants | 577.820 us | 656.644 | 744.593 | 0 B |

All three produce normalized logical hash
`55107F0D2B7B1F2F76BF7E710E9C6AD5`, identical work counters, no rejected
commands, and zero caller/worker allocation or GC collection. Scheduled serial
costs about `6.9%` more than the direct public control; scheduled parallel is
`2.06x` faster than that control and `2.20x` faster than scheduled serial. The
comparison exposes orchestration cost instead of using parallel speedup to hide
it.

This profile also validates an important semantic distinction: the public and
scheduled full observation hashes differ only because their legitimate change-
version publication schedules differ. Scheduled serial and parallel observation
hashes match exactly.

## Running it

From the repository root:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release -- --soak
```

Add `--soak-public-jobs` to run the public 3.0.36 profile. Add
`--soak-scheduler-serial` or `--soak-scheduler` for the 3.1.1 scheduled parity
profile; `--soak-scheduler-workers <count>` controls background workers for the
parallel form. Default result names include the active kernel version and append
the selected profile.
Supported controls are:

- `--soak-output <path>`;
- `--soak-entities <count>`;
- `--soak-warmup <ticks>`;
- `--soak-ticks <ticks>`;
- `--soak-runs <count>`;
- `--soak-validation-ticks <ticks>`;
- `--soak-checkpoint-interval <ticks>`;
- `--soak-seed <decimal-or-0x-hex>`;
- `--soak-public-jobs`;
- `--soak-scheduler-serial`;
- `--soak-scheduler`;
- `--soak-scheduler-workers <count>`.

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
- scheduler crossover under changed entity count, system cost, route count, and
  participant count, while retaining this accepted parity workload;
- lifecycle-stream publication once implemented;
- prefab spawn/despawn, persistence checkpoint, and network capture/apply phases;
- one-hour and overnight native-retention runs;
- child-process captures across several machines and cache sizes.

Tests and measurements may change which expansion should come first. The current
profile says the next useful question is the cost surface of sparse tracked writes
plus exact changed-row consumption, while preserving this accepted scenario as
the control.

That investigation is complete at
[`ECS_CHANGE_TRACKING_DECOMPOSITION.md`](ECS_CHANGE_TRACKING_DECOMPOSITION.md).
It finds that version advancement itself is negligible; random checked access,
chunk locality, resident rows in touched chunks, and fragmented exact-range
dispatch dominate. ECS 3.0.36 then admitted the public serial generated chunk-job
surface without changing this workload's canonical state or sustained timing.
ECS 3.1.0 subsequently assembled the bounded scheduler, and ECS 3.1.1 repaired
its capability boundaries and longitudinal parity evidence. The next
implementation should now follow production need. A bounded batch-partition or
mask-bearing change-consumer remains a measured option if arbitrary entity-list
writes or fragmented exact delivery dominate a migrated subsystem; it is not
justified by this scheduler admission alone.
