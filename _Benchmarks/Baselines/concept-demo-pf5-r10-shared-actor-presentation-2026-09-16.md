# PF5-R10 — Shared actor-presentation preparation

PF5-R10 closes as **Outcome A**. A sampled CPU profile of the real dedicated server, with
detailed timing disabled, found that public actor-presentation source capture and recipient
reconciliation consumed 35.27% of sampled authority-thread cycles. The selected repair prepares
one bounded slot-indexed authoritative view, performs direct slot lookup, and skips unchanged
recipient reconciliation while retaining independent delivery and proof state.

This is an actual-product optimization. The synthetic R300/R350
`RemoteChunkWorldStateServer` fixture does not construct `RemoteGuestPlayableApplication`, so its
separate rated crossing does not move.

## Lineage and environment

| Item | Value |
| --- | --- |
| Starting/before-profile HEAD | `d2e693284d3bdc26a307ab4bcbc929a9c6bb6a55` |
| Implementation commit | `b7c4e46b5f5013d7fb80cda50b58ded4b6c163f2` |
| Replication semantic candidate | `14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9` — unchanged |
| Configuration | Release; ServerOnly authority; ClientOnly bots |
| Runtime | .NET SDK 8.0.131; Microsoft.NETCore.App 8.0.31; x64 |
| Host | AMD Ryzen 7 7800X3D, 8 cores / 16 threads, 96 MiB L3, 32 GiB RAM |
| OS / profiler | CachyOS Linux 7.2.6-1; `perf` 7.2.6-1 |

The v29 policy is unchanged: `ChunkPublication`, compatible immutable body sharing,
`CurrentBundled`, multi-entry message-148 LZ4 at 384/32, singleton compression off, 1/2/4
cadence, cross-lane cap-two aggregation, `BackgroundPlatformBatch`, transport revision 2, 60 Hz
authority, and product participant cap 64.

## Ownership before and after

```text
BEFORE — every recipient progress pass
  capture/resolve every participant actor, equipment, and vertical state
  compact actor rows
  for each bounded recipient slot, linearly search compact rows by slot
  reconcile recipient delivery/proof row

AFTER — source lifetime / authoritative revision
  prepare one bounded slot-indexed actor-presentation view

AFTER — each recipient progress pass
  read shared source version
  unchanged and no recipient wake -> skip reconciliation
  otherwise direct slot lookup
  retain recipient-local delivery ID, pending row, exact proof, and retry
```

The shared view contains no application epoch, delivery ID, queue state, proof state, or applied
knowledge. Storage is authority-owned and bounded by participant capacity. Join, leave, slot
reuse, generation/tick change, accepted Inventory mutation, and accepted presentation actions
invalidate source facts. A proof retiring an older pending row independently wakes that recipient,
so newer truth cannot disappear behind an in-flight delivery. The cached preparation delegate and
retained arrays allocate zero warmed managed bytes.

## Matched real-product control

Both rows use one dedicated Direct Connect ServerOnly authority, 64 real ClientOnly UDP bots,
`crowd`, seed `509921`, 60 s warmup, 120 s measured, warmed content, the production v29 policy,
and detailed Game/Network/background timing Off. All 64 participants were retained through both
accounting boundaries.

| Metric | Before | After | Change |
| --- | ---: | ---: | ---: |
| Duration | 120.284 s | 120.235 s | matched |
| Server process CPU | 60,630 ms | 40,110 ms | -33.84% |
| CPU/core | 0.504057 | 0.333597 | **-33.82%** |
| Authority ticks | 7,217 | 7,214 | -0.04% |
| Published chunk revisions | 54,872 | 55,132 | +0.47% |
| Chunk delta reads | 82,274 | 82,680 | +0.49% |
| Presentation 144 sent / proof 145 received | 107,136 / 107,136 | 106,496 / 106,496 | exact pairing |
| Applied-proof 130 received | 462,016 | 461,854 | -0.04% |
| Message-148 sent | 485,600 | 507,537 | +4.52% |
| UDP payload bytes sent | 437,483,038 | 477,293,882 | +9.10% |
| UDP datagrams sent | 787,663 | 832,414 | +5.68% |
| Server measured allocated bytes | 565,144,184 | 401,079,296 | **-29.03%** |
| Managed-heap high-water | 162,103,760 | 163,765,832 | +1.03% |
| Process RSS high-water | 268,447,744 | 281,559,040 | +4.88% |
| Participants/connections start/end | 64/64 | 64/64 | equal |
| Physical/logical accounting | reconciled | reconciled | equal |
| Source commit gaps | 0 | 0 | equal |
| Final joins/leaves/debt | 64/64/clean | 64/64/clean | equal |

The result was not obtained by reducing ChunkPublication traffic: the final after run sent more
message-148 traffic, UDP bytes, and datagrams. RSS and heap differences are retained as measured
and are not claimed as improvements.

The Off run intentionally emits no detailed topology samples. The generic PF5 shell wrapper's
last topology assertion therefore exits 1 after the clean run; its bot summary, server summary,
accounting reconciliation, source-gap, participant-boundary, and clean-retirement gates pass.

## Sampled profile

The profiles used separate server processes and split main and egress threads. The after capture
retained 19,747 samples over 120.94 s with zero lost samples. Its cycle-period split was 97.861%
authority/main thread and 1.323% UDP egress; before it was 98.631% and 0.963%.

| Authority-thread symbol | Before | After |
| --- | ---: | ---: |
| `SharedPlayableAuthority.CopyActorPresentations` source capture | 22.78% | replaced |
| `RemoteGuestPlayableApplication.ReconcileSharedPresentation` | 12.49% | 0.381% |
| `SharedPlayableAuthority.PrepareActorPresentations` | n/a | 0.173% |
| combined selected work | 35.27% | **0.554%** |

Multiplying sampled shares by profiled process/core and main-thread shares estimates 0.1770 core
before and 0.0018 core after for these principal methods. Their 0.1752-core estimated reduction
accounts for 99.3% of the profiled whole-process 0.1765-core reduction. This is sampled
attribution, not subtraction of wall-time percentiles.

The post-repair top managed product cost is now `RemoteGuestPlayableApplication.IsSubscribed`
(7.67% of sampled main-thread cycles), associated in part with the separate public-item path.
PF5-R10 does not optimize or declare that path redundant; it is the strongest remaining
product-specific lead for a future measurement-first experiment.

Raw local captures remain ignored under:

```text
_Benchmarks/results/pf5-r9-r2-sampled-cpu-20260916/
_Benchmarks/results/pf5-r10-shared-presentation-20260916/
```

The after viewer input is `perf-b-symbolized/server.perf.data`, with
`server.perf.map`. The stable summary is the companion JSON; the 166 MiB `perf.data` is not
committed.

## Regressions and verification

Permanent tests prove shared preparation/direct slots, unchanged-version behavior, accepted-action
invalidation, leave/slot-reuse fencing, zero warmed allocation, fewer reconciliation than progress
passes, and newer-state delivery after an older exact proof wakes a skipped row. Recipient proof
identities remain independent and real UDP clients converge and retire cleanly.

Focused Release results: all 178 Game replication tests and all Network application tests passed.
`./verify.sh --configuration=Release` also passed the complete canonical Release, composition,
actual-export, Contentless Online, mixed bot/native-client, lifecycle, and parity gates.

Matched control command:

```bash
ULTRASIM_TELEMETRY=Off \
Tools/NetworkBots/run-pf5-baseline.sh crowd 120s \
  _Benchmarks/results/pf5-r10-shared-presentation-20260916/control-d-final \
  64 509921 60s /tmp/ultrasim-pf5-r9-r2-profile-cache \
  60 60 30 0 0 compliant 1 10 100 1
```

Profiler shape, attached after admission/warmup:

```bash
DOTNET_PerfMapEnabled=3 <start-dedicated-server-command>
perf record -F 499 -e cycles:u --call-graph dwarf,8192 \
  --pid <server-pid> -o server.perf.data -- sleep 121
perf report -i server.perf.data
```

PF5-R10 changes no wire format, manifest, transport revision, AOI, cadence, compression,
aggregation, participant policy, delivery identity, or proof semantics. PF6 remains ready.
