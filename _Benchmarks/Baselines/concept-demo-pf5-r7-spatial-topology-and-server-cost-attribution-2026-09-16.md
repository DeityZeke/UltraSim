# PF5-R7 — spatial topology and whole-server cost attribution

PF5-R7 started from clean `main` at
`a6dfccc04c9253df266dd4fb582aa23a12fe8eae`. No intervening commit changed the
accepted PF5-R6 replication policy. The exact rated measurement tree is
`4d5d7ca4bfad168f4657c231fc92a8a67ae320b9`.

## Disposition

PF5-R7 is characterization only. Production semantics and policy remain unchanged:

```text
BackgroundPlatformBatch
CurrentBundled
PF5-R6 compatible immutable body sharing ON
singleton compression OFF
Immediate/Near/Far 1/2/4
CrossLane unreliable aggregation cap 2
Game manifest v29
transport revision 2
product participant cap 64
```

The spatial result is not `cost = f(recipients)`. It follows the combination of dirty source
chunks, observers per dirty chunk, recipient-publication references, records, messages, and wire
bytes. Dense layouts retain excellent body reuse but pay far more fanout. Sparse layouts lose body
reuse while becoming much cheaper because each change has few observers and much smaller bodies.

The R350 composed chaos row is 6.993 ms p95 for the paired server-owned tick. Its remote
replication phase is 4.384 ms p95 and its combined locomotion/current-physics phase is 2.681 ms
p95. The 4 ms Network engineering line is therefore crossed without approaching the desired
12–14 ms composed authority envelope or the absolute 16.67 ms frame.

PF6 remains unblocked. No correctness defect was found and no production optimization or policy
change was made.

## Measurement boundary and limitations

- Linux x86-64, .NET 8.0.29, 16 logical processors, Release.
- 30 warmup ticks, 270 rated ticks, and a separate 30-tick detailed topology census.
- Real secure loopback UDP and the selected bounded background native batch sender.
- The large raw result is tracked separately at
  `_Benchmarks/results/pf5-r7-spatial-topology-and-server-cost-attribution-2026-09-15.json`;
  SHA-256 `9a78fafa9ff26054ad619c0b4ae5b8b1e464ce676770815ab69a619971255011`.
- Topology groups are separated by 192 m and use ordinary production AOI. One group is a region
  or chunk group, not necessarily one physical chunk.
- R200/R350 compose the real current Game authority and current secure-UDP ChunkPublication path
  in the same tick. Because the product cap remains 64, these are benchmark-capacity participants,
  not product CCU.
- Game movement uses current accepted input, held locomotion, jump, Simple Physics, Inventory
  maintenance, and world commit. A deterministic public-world mirror supplies the large
  replication fixture; Game actor positions are not directly handed to that mirror.
- Chat timing uses the real authoritative `GameChatService` submit/fanout path, but the composed
  matrix does not include remote Chat encoding/delivery. It must not be read as a full Chat wire
  cost.
- The harness is in-process. Server-owned phase clocks exclude measured client receive/apply work,
  but process CPU and RSS include server, clients, and background sender. They are fixture context,
  not server-process resource claims.
- `Tick`/whole-flow includes interleaved in-process clients. `ServerOwnedTick` is the valid paired
  server-owned active-work sample.

## Spatial topology

All rows use E=R and 35% distributed dirty entities unless noted. Counters are from the separate
identical-work diagnostic census; timing is from diagnostics-off rated runs.

| R | Layout | Source chunks | Memberships | Dirty chunks | Dirty fanout | Bodies | Hit % | Recipient reads | Remote p95 ms | Whole-flow p95 ms |
| --: | --- | --: | --: | --: | --: | --: | --: | --: | --: | --: |
| 350 | dense | 18 | 10,401 | 210 | 57,270 | 363 | 98.82 | 30,666 | 4.322 | 18.746 |
| 350 | 8 groups | 24 | 10,416 | 240 | 10,470 | 355 | 95.81 | 8,472 | 1.769 | 7.144 |
| 350 | sparse | 350 | 11,200 | 3,660 | 3,660 | 3,660 | 0 | 3,660 | 1.225 | 4.032 |
| 512 | 1×512 | 26 | 15,212 | 270 | 87,810 | 465 | 98.96 | 44,644 | 7.867 | 31.919 |
| 512 | 8×64 | 32 | 15,224 | 330 | 21,120 | 489 | 97.00 | 16,308 | 3.069 | 12.421 |
| 512 | 64×8 | 64 | 15,552 | 690 | 5,520 | 690 | 87.50 | 5,520 | 1.916 | 6.481 |
| 512 | 512×1 | 512 | 16,384 | 5,370 | 5,370 | 5,370 | 0 | 5,370 | 1.819 | 6.051 |

The fixed R512 sweep holds recipients and authoritative mutations constant. Dense 1×512 performs
only 465 distinct body fills and hits 98.96%, but those bodies feed 44,644 recipient reads and
1,742,790 recipient record references in the diagnostic window. It emits 413,241,671 UDP payload
bytes. Sparse 512×1 fills 5,370 bodies with zero reuse, yet has only 5,370 reads/record references
and emits 14,982,300 bytes. The extra distinct-body work is much smaller than the fanout removed.

The smaller approximately fanout-matched R64 control deliberately changes source-body count and
source records while keeping recipient reads near 600–720:

| Layout | Memberships | Dirty fanout | Recipient reads | Source records | Bodies | Remote mean ms | Remote p95 ms |
| --- | --: | --: | --: | --: | --: | --: | --: |
| 1×64, mutation every 6 ticks | 1,903 | 640 | 640 | 100 | 10 | 0.184 | 0.543 |
| 8×8 | 1,944 | 720 | 720 | 600 | 90 | 0.222 | 0.235 |
| 64×1 | 2,048 | 600 | 600 | 600 | 600 | 0.202 | 0.216 |

The three means remain within 0.038 ms despite a 60× body-count range. The dense p95 is the
intentional once-per-six-ticks burst, not sustained higher occupancy. This supports the primary
finding: body cardinality is measurable, but recipient obligations/fanout and output dominate.

The extent control holds R64/8 groups/dirty20 constant and adds 4,096 inactive stored chunks.
Remote p95 is 0.233/0.235 ms with identical 1,944 memberships, 720 reads, and 90 bodies. There is
no evidence of a full-world inactive-chunk scan.

## Low-change dense crossing

These E=1,000/dirty20 rows locate the selected remote-replication curve; they are not product CCU.

| R | Memberships | Dirty fanout | Remote mean ms | Remote p95 ms |
| --: | --: | --: | --: | --: |
| 384 | 11,407 | 107,220 | 3.65 | 3.856 |
| 400 | 11,880 | 112,230 | 3.85 | 4.099 |
| 450 | 13,371 | 126,360 | 4.30 | 4.576 |
| 512 | 15,212 | 138,180 | 4.95 | 5.242 |

The sampled 4 ms p95 crossing is between R384 and R400 for this synthetic dense low-change shape.

## Whole-server N×M×C matrix

`N` is current remote world replication, `M` is current movement/locomotion/Simple Physics, and
`C` is authoritative Chat. Connected secure sessions remain present in every cell. Phase p95
columns are diagnostic and are not additive.

| R | N | M | C | Server p95 | Remote p95 | Input p95 | Locomotion+physics p95 | Chat p95 | World commit p95 | Residual p95 |
| --: | -: | -: | -: | --: | --: | --: | --: | --: | --: | --: |
| 200 | 0 | 0 | 0 | 1.138 | 0.369 | 0.000 | 0.770 | 0.000 | 0.008 | 0.000 |
| 200 | 0 | 0 | 1 | 1.147 | 0.372 | 0.000 | 0.770 | 0.031 | 0.008 | 0.000 |
| 200 | 0 | 1 | 0 | 1.782 | 0.367 | 0.009 | 1.400 | 0.000 | 0.020 | 0.000 |
| 200 | 0 | 1 | 1 | 1.793 | 0.366 | 0.009 | 1.415 | 0.030 | 0.021 | 0.000 |
| 200 | 1 | 0 | 0 | 2.422 | 1.660 | 0.000 | 0.774 | 0.000 | 0.009 | 0.000 |
| 200 | 1 | 0 | 1 | 2.435 | 1.653 | 0.000 | 0.793 | 0.026 | 0.009 | 0.000 |
| 200 | 1 | 1 | 0 | 3.540 | 2.123 | 0.012 | 1.431 | 0.000 | 0.022 | 0.000 |
| 200 | 1 | 1 | 1 | 3.524 | 2.118 | 0.011 | 1.424 | 0.027 | 0.022 | 0.000 |
| 350 | 0 | 0 | 0 | 2.125 | 0.658 | 0.000 | 1.462 | 0.000 | 0.018 | 0.000 |
| 350 | 0 | 0 | 1 | 2.128 | 0.650 | 0.000 | 1.472 | 0.043 | 0.015 | 0.000 |
| 350 | 0 | 1 | 0 | 3.325 | 0.655 | 0.019 | 2.637 | 0.000 | 0.042 | 0.000 |
| 350 | 0 | 1 | 1 | 3.362 | 0.651 | 0.018 | 2.661 | 0.047 | 0.041 | 0.000 |
| 350 | 1 | 0 | 0 | 4.466 | 3.021 | 0.000 | 1.501 | 0.000 | 0.018 | 0.000 |
| 350 | 1 | 0 | 1 | 4.477 | 3.013 | 0.000 | 1.496 | 0.051 | 0.019 | 0.000 |
| 350 | 1 | 1 | 0 | 7.010 | 4.381 | 0.021 | 2.724 | 0.000 | 0.047 | 0.000 |
| 350 | 1 | 1 | 1 | 6.993 | 4.384 | 0.022 | 2.681 | 0.054 | 0.047 | 0.000 |

Factorial contrasts use whole-run means, not differences of p95s:

| R | N main | M main | C main | M at N=0 | N at M=0 | N×M | N×C | M×C | N×M×C |
| --: | --: | --: | --: | --: | --: | --: | --: | --: | --: |
| 200 | 1.481 | 1.007 | 0.003 | 0.777 | 1.251 | 0.460 | -0.003 | -0.001 | -0.005 |
| 350 | 2.931 | 2.052 | 0.002 | 1.395 | 2.274 | 1.313 | -0.010 | 0.011 | -0.007 |

The positive N×M interaction is real: movement creates additional public truth and recipient work.
Chat is tiny at the fixed 45-submission window, and the small signed interaction estimates are
measurement noise rather than evidence of negative work.

For R350 N1/M1/C1, same-tick means are: 6.18 ms server-owned total, 6.18 ms attributed, 0.00016
ms residual, 0.013 ms input/session, 0.0048 ms Chat, 1.97 ms locomotion/current physics, 0.041
ms world commit, and 4.15 ms remote replication. These exclusive means reconcile on each tick;
their independent p95 values are not summed.

## Physics and absent systems

Free movement is represented by the M=1 matrix cells. The additional dense convergence control is
honestly named `DenseConvergenceNoActorCollision`: current Simple Physics does not implement
actor-vs-actor dynamic collision and exposes no retained broadphase/contact counters.

| R | Workload | Movers | Accepted inputs | Jump attempts/accepted | Locomotion+physics p95/p99 | Server p95 |
| --: | --- | --: | --: | ---: | --: | --: |
| 200 | free movement (N0/M1) | 70 | 6,300 | 210/176 | 1.400/1.45 class | 1.782 |
| 200 | dense convergence, no actor contacts | 70 | 6,300 | 210/161 | 1.338/1.369 | 1.726 |
| 350 | free movement (N0/M1) | 122 | 10,980 | 366/321 | 2.637/2.7 class | 3.325 |
| 350 | dense convergence, no actor contacts | 122 | 10,980 | 366/291 | 2.376/2.480 | 3.070 |

The old ~3 ms Physics planning reserve is close to the current R350 high-movement combined
locomotion/Simple-Physics p95, but it is not a separately measured collision budget and includes no
future pathfinding. Sim Core, pathfinding, and Dungeon Core have no meaningful current production
tick workload and are `N/A`, not assigned synthetic milliseconds. Persistence/content are
event-driven and inactive in rated ticks; diagnostics are cold reads.

## V2 archaeology and normalized context

The source artifact is
`_Archive/_V2/_Benchmarks/results/dense_hotspot_new_runner_4families_60s/sharded_20260428_031706/aggregate.json`,
SHA-256 `4a93c44883924fa53dc20814faaca22377d8121d3da13135871fd0c83dac93d5`.
It used a separate server plus four client workers, one session-owned movement entity/client, no
separately evidenced ambient population, 35% active, phased 20 Hz input with held movement on a
62 Hz server, 1 m/width-20 dense spawn, AOI 7.5/23.5/39.5 m, ring cadence 1/2/3, four historical
families, recipient phasing cadence two, no latest-state batching, Chat off, and async server flush.
No archived sparse fixture survived.

| R | V2 server tick p95 | V2 async send p95 | V3 dense server p95 | V3 sparse reconstruction p95 | V3 dense remote p95 | V3 sparse remote p95 |
| --: | --: | --: | --: | --: | --: | --: |
| 256 | 2.350 | 2.584 | 4.840 | 2.726 | 3.070 | 0.913 |
| 300 | 2.913 | 3.374 | 5.761 | 3.270 | 3.709 | 1.059 |
| 350 | 4.018 | 4.522 | 6.945 | 3.911 | 4.340 | 1.228 |

V2 main-tick and background-send percentiles remain separate. The current V3 sparse reconstruction
falls in the historical V2 main-tick range; the current dense hotspot is materially higher. This is
not equivalence: the V2 dense fanout, four-family representation, sent-based knowledge, transport,
security, background flush, and timer boundaries differ. The evidence does establish that spatial
fanout explains a large part of the apparent V2/V3 population gap; it does not establish that every
remaining V3 recipient cost is unavoidable.

## Resource/accounting observations

- Rated post-warmup managed allocation is stable at roughly 20–34 KiB per 270-tick window; the
  instrumentation does not allocate per packet or per recipient publication.
- GC counts and start/end RSS are retained per row in the raw artifact. Sequential in-process rows
  reach multi-GiB RSS because server and hundreds of retained benchmark clients share one process;
  this is not a server-only memory claim.
- Background sender capacity refusals, physical send failures, stale-path drops, and worker faults
  are zero in the representative R350 chaos row. It used one sender, native batches, and a 578
  datagram high-water.
- Every rated topology converged with zero Network capacity drops and selected prepared latest-state
  history-derived steady-state calls equal to zero.

## Current budget view

| Subsystem | Old envelope | R200 chaos p95 | R350 chaos p95 | Status |
| --- | --: | --: | --: | --- |
| Network + remote replication | ~4.0 ms | 2.118 | 4.384 | measured; R350 crosses engineering line |
| locomotion + current Simple Physics | ~3.0 ms Physics | 1.424 | 2.681 | combined current phase; no actor contacts/pathfinding |
| Chat/commands | Other | 0.027 | 0.054 | authority only; remote Chat wire excluded |
| world commit | Other | 0.022 | 0.047 | measured |
| ECS/authority remainder | ~0.2 ms | 0.002 | 0.002 | current benchmark has no meaningful ECS systems |
| Sim Core | ~3.0 ms | N/A | N/A | not implemented/performance-characterizable |
| persistence/content | Other | inactive | inactive | event-driven/background |
| residual | — | <0.001 | <0.001 | paired same-tick residual |
| complete server-owned tick | 12–14 ms desired | 3.524 | 6.993 | substantial current headroom |

These measurements do not automatically become new permanent subsystem budgets.

## Verification

- `dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj -c Release -- --pf5-r7-only`: PASS.
- `./verify.sh --configuration=Release`: PASS on the final source/evidence tree, including all
  kernel, Game, client, native Godot, exported-product, Contentless Online, PF1/PF2 mixed-swarm,
  F0.7 lifecycle, and F0.8 composition gates.
- Rated topology rows completed with zero Network capacity drops, zero selected steady-state
  history reconstruction, and clean semantic convergence.
- No production correctness defect was found. The replication semantic lineage remains
  `992fc541460f18534da9036c73cb711277de11ce` and the PF5-R7 production policy is unchanged.

## Follow-up

Keep the selected production policy. PF6 can proceed. A later performance investigation may target
dense recipient fanout/recipient obligations, and the V2/V3 semantic-family/transport comparison
remains useful. PF5-R7 does not justify enabling singleton compression, changing AOI/cadence, or
raising the product cap. Future collision/pathfinding and Sim Core work need new measurements when
those systems actually exist.
