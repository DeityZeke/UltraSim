# PF5-R12 — Remaining product scheduling and allocation costs

PF5-R12 closes as **Outcome A**. It first repairs a real item-presentation ordering defect, then
uses a fresh dedicated-server CPU profile, a CLR allocation trace, and an operation census to
select two bounded actual-product changes: skip presentation delivery-table scans when the last
reconciliation proved no row ready, and retain two generation-capture delegates instead of
allocating them on every recipient service pass.

## Lineage and policy

| Item | Value |
| --- | --- |
| Starting HEAD | `56967aaa798305aaa97843a449949a4395233b4b` |
| Fully retired item revision repair | `0117c910` |
| Polling measurement | `ea7cb405` |
| Readiness implementation | `5e643f6d` |
| Delegate reuse | `7a9f6ddf` |
| Chunk-replication semantic candidate | `14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9` — unchanged |
| Integrated product implementation | `7a9f6ddf54a15102dceba6ac29b6063252bd0faa` — advances because `0117c910` is a product correctness repair |
| Configuration | Release; one ServerOnly authority; one ClientOnly bot host; telemetry Off |
| Runtime / host | .NET 8.0.31; AMD Ryzen 7 7800X3D; 8C/16T; 96 MiB L3; 32 GiB |
| OS | CachyOS Linux 7.2.6-1-cachyos x64 |

The selected v29 policy is unchanged: `ChunkPublication`, compatible body sharing,
`CurrentBundled`, selective multi-entry message-148 LZ4 at 384/32, singleton compression off,
1/2/4 cadence, CrossLane cap-two aggregation, `BackgroundPlatformBatch`, transport revision 2,
60 Hz authority, and participant cap 64.

## Correctness repair

A fully proven item removal cleared its retained server row. Reintroducing the same live public
identity then initialized the recycled row at metadata revision 1, while the client correctly
retained the prior identity/revision floor. The server now allocates item-presentation revisions
from one monotonic per-application sequence. Row retirement and reuse remain bounded, but cannot
reset ordering within that application lifetime.

The real-UDP regression establishes a presentation, proves its removal, allows the server row to
retire, reintroduces the same item without reconnecting, and requires a strictly newer revision
and exact applied proof. Pickup/equip/drop and the existing pending-proof wake case remain covered.

## Polling census

The pre-change census used 64 crowd clients, seed `509921`, 60 seconds warmup and 60 seconds
measurement. The post-change census uses the same configuration with a 120-second measurement.
Counters are cumulative from application start through the measurement endpoint, so comparisons
use work per progress call rather than raw totals.

| Path | Before progress | Before empty searches | Before search rows | After clean skips | After search rows | After readiness rows | Rows/progress before → after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Actors | 6,246,426 | 5,931,123 | 383,001,804 | 9,079,544 | 5,274,560 | 6,641,438 | 61.315 → **1.248** (-97.97%) |
| Public items | 6,246,426 | 6,246,150 | 399,753,664 | 9,549,121 | 64 | 6,562,688 | 63.997 → **0.687** (-98.93%) |

Before, actor delivery admitted 103,744 messages and item delivery admitted 64; both had zero
queue refusals and zero admitted retries. The dominant work was therefore clean polling rather
than pressure recovery. After, readiness is recomputed only with actual reconciliation. Source or
topology change, exact proof completion, queue refusal/retry, removal, and application lifetime
reset retain their existing wake paths. Readiness never advances applied knowledge.

## Allocation attribution

A 45-second `dotnet-trace gc-verbose` capture sampled 28,357,728 allocated bytes before and
5,974,592 after (-78.93%). The accepted unprofiled crowd totals agree: 76,036,024 bytes before
and 15,726,032 after over 120 seconds (-79.32%).

Before, two recurring delegate types account for 22,494,864 sampled bytes:

- `QueueOwnerStateCore` generation capture: 12,259,000 bytes;
- `DrivePublicWorldCore` generation capture: 10,235,864 bytes.

Both disappear after retaining one method-group delegate per application. The largest remaining
recurring application stack is `ProcessPlayerCommandCore` at 1,380,592 sampled bytes per 45
seconds. Process-accounting `/proc` reads now constitute a larger share. The trace is a sampled
allocation attribution; `AllocatedBytes` above is the accepted process total.

## Matched product controls

All rows use 64 real ClientOnly UDP clients, 60 seconds warmup, 120 seconds measurement, a freshly
built ServerOnly product, telemetry Off, and background-egress timing Off.

| Scenario / seed | CPU/core before | CPU/core after | Change | Allocated before | Allocated after | Change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| quiet / `812120` | 0.056978 | **0.035017** | -38.54% | 71,169,920 | **11,982,584** | -83.16% |
| crowd / `509921` | 0.226917 | **0.207410** | -8.60% | 76,036,024 | **15,726,032** | -79.32% |
| mixed-chaos / `731911` | 0.264956 | **0.245667** | -7.28% | 77,594,888 | **17,540,792** | -77.39% |

Crowd UDP payload was 428,462,538 versus 426,943,314 bytes; mixed-chaos was 612,155,997 versus
601,162,275 bytes. These are natural workload differences, not a wire-policy claim. Every run
reconciled physical and logical accounting and retired all 64 clients.

The final mixed-chaos run completed 65 application/proof lifetimes with one reconnect, 4,510
movement attempts, 1,551 jumps, 2,074 interactions, 1,534 Primary attempts, and 634 sword
interactions. Pickup, equip, and drop were all observed. It recorded zero bot faults, zero source
gaps, 1,517 chunk crossings, 3,961 vertical transitions, 1,359 landings, and clean retirement.

## CPU profile and bounded cost

The accepted post-change profile uses `perf` `cycles:u` at 997 Hz for 45 seconds, with the .NET
JIT perf map retained, telemetry Off, 8,326 main-thread leaf samples, 1,278 sender/runtime samples,
and zero lost samples. Main-thread leaf shares are:

| Method | Post-change share |
| --- | ---: |
| `QueuePublicItemPresentationMetadata` | **0.1441%** |
| `ReconcilePublicItemPresentation` | 0.3363% |
| `QueueSharedPublicPresentationMetadata` | 0.4324% |
| `ReconcileSharedPresentation` | 0.9008% |

The comparable PF5-R11 item queue sample was 3.0381%. A first PF5-R12 capture whose temporary JIT
map was not retained is preserved but excluded from method attribution.

Retained cost is bounded: two booleans, two delegate references/instances, and finite scalar
counters per application; no array, queue, journal, or unbounded storage was added. The estimated
64-client addition is roughly 20 KiB including diagnostic scalars and delegate objects, subject to
runtime object alignment. Matched RSS and managed-heap high-water both decreased, but the result
is not described as a structural memory guarantee.

## Verification and disposition

Focused verification:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf5-r12-only
```

passes all 180 Game replication tests and the complete Network application suite. It covers
stable O(1) clean returns, proof wake, pending-proof source changes, full removal/reappearance,
queue transactionality through the retained application tests, reconnect/lifetime fences, and
zero warmed allocation paths. The complete canonical Release/export verifier also passes on the
final artifact tree.

Remaining work is deliberately not claimed minimal. Command-processing allocation and actor/item
reconciliation remain measured future leads. The synthetic R300/R350 ChunkPublication fixture
bypasses this application path, so its crossing is unchanged and is not rerun or reinterpreted.
PF6 remains eligible after canonical exact-tree closure.

Raw run and profiler data remains ignored under:

```text
_Benchmarks/results/pf5-r12-scheduling-allocation-20260917/
```

Reproduction shape:

```bash
dotnet build Server/DirectConnect/UltraSim.Server.DirectConnect.csproj \
  -c Release -p:UltraSimNetworkFlavor=ServerOnly
dotnet build Tools/NetworkBots/UltraSim.NetworkBots.csproj \
  -c Release -p:UltraSimNetworkFlavor=ClientOnly

ULTRASIM_TELEMETRY=Off Tools/NetworkBots/run-pf5-baseline.sh crowd 120s \
  <output> 64 509921 60s /tmp/ultrasim-pf5-r12-cache \
  60 60 30 60 30 compliant 1 10 100 1

perf record -F 997 -e cycles:u -g --call-graph fp -p <server-pid> \
  -o server.perf.data -- sleep 45

/home/nathan/.tools/dotnet-trace collect -p <server-pid> --profile gc-verbose \
  --duration 00:00:00:45 -o server-gc.nettrace
```
