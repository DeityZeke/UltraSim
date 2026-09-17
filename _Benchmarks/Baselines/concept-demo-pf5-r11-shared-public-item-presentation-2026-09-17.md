# PF5-R11 — Shared public-item presentation preparation

PF5-R11 closes as **Outcome A**. It first repairs ASTRA's confirmed PF5-R10 source-cache
failure path, then follows the sampled public-item lead. Public ground-item facts are now captured
once into one bounded authority-owned view. Recipient interest, delivery identity, retries,
applied knowledge, and exact proof remain recipient-local.

## Lineage and environment

| Item | Value |
| --- | --- |
| Starting HEAD | `506f217c571c7d0ca0f46e34c01488c392a77cd6` |
| Actor failure repair | `afe257c42b0f6e0c75f514dbda3a7fc66767fb83` |
| Item implementation | `e2a9be3065133abbe4646fa3becb6ea781a46d83` |
| Deterministic proof regression | `3e94c3dfa64d94e3162e6963a3575906978b0bdb` |
| Prior replication semantic candidate | `14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9` — unchanged |
| Integrated production correctness candidate | `e2a9be3065133abbe4646fa3becb6ea781a46d83`; actor failure and stale item revision are production correctness repairs |
| Configuration | Release; ServerOnly authority; ClientOnly bots; telemetry Off |
| Runtime | .NET SDK 8.0.131; Microsoft.NETCore.App 8.0.31; x64 |
| Host | AMD Ryzen 7 7800X3D, 8 cores / 16 threads, 96 MiB L3, 32 GiB RAM |
| OS / profiler | CachyOS Linux 7.2.6-1; `perf` 7.2.6-1 |

The v29 policy is unchanged: `ChunkPublication`, compatible immutable body sharing,
`CurrentBundled`, multi-entry message-148 LZ4 at 384/32, singleton compression off, 1/2/4
cadence, cross-lane cap-two aggregation, `BackgroundPlatformBatch`, transport revision 2, 60 Hz
authority, and product participant cap 64.

## Correctness repairs

The PF5-R10 actor source used incremental retained-row mutation. If a later actor capture failed,
an earlier changed row could remain behind the old source version. Both a false capture and an
exception now invalidate the prepared view, so the next complete preparation necessarily advances
the version before exposing changed truth. Focused tests establish a recipient at the old version,
change an early actor, fail a later slot, recover without another mutation, and require the newer
fact over real UDP.

The item proof-wakeup regression independently found an older defect: after a proven removal, the
same item and weapon could be re-presented with the removal's metadata revision. Clients correctly
rejected that stale revision. Reappearance now advances the row revision. The regression holds a
remove pending, restores the source, then requires both a newer client presentation and the exact
server proof wake. Three consecutive focused real-UDP runs passed. The first canonical run exposed
and rejected a test sampling race; the final regression waits for proof arrival rather than assuming
client apply and server proof processing are simultaneous.

## Selected item-source design

```text
authoritative item state
    -> one transactional, bounded 64-row shared source preparation
    -> version advances only after a complete changed preparation

recipient progress
    -> read shared source version
    -> unchanged source + unchanged subscriptions + no proof wake: skip reconciliation
    -> otherwise filter through the recipient's sorted subscription topology
    -> retain recipient-local desired/applied row, delivery ID, retry, and exact proof
```

Two retained 64-row arrays bound source and staging storage. A cached generation operation avoids
per-call delegate allocation. Inventory mutations and accepted item-affecting commands explicitly
invalidate same-tick prepared state; generation/tick changes also force preparation. Preparation is
transactional and allocates zero warmed managed bytes. No application epoch, delivery ID, queue
state, proof state, or applied knowledge is shared.

## Matched real-product control

Every row below uses one dedicated Direct Connect ServerOnly authority, 64 real ClientOnly UDP
bots, `crowd`, seed `509921`, 60 s warmup, 120 s measurement, warmed content, and all detailed
Game/Network/background timing disabled. The ServerOnly product was explicitly rebuilt before the
accepted after rows because the runner intentionally launches it with `--no-build`.

| Metric | Before | After 1 | After 2 | After 3 | After mean |
| --- | ---: | ---: | ---: | ---: | ---: |
| Server process CPU | 40,270 ms | 27,550 ms | 26,200 ms | 26,740 ms | 26,830 ms |
| CPU/core | 0.334922 | 0.229117 | 0.217885 | 0.222363 | **0.223122** |
| Change from before | — | -31.59% | -34.94% | -33.61% | **-33.38%** |
| Measured allocated bytes | 400,676,096 | 76,067,992 | 75,993,064 | 76,080,960 | **76,047,339** |
| Allocation change | — | -81.02% | -81.03% | -81.01% | **-81.02%** |
| RSS high-water | 283,439,104 | 279,199,744 | 279,490,560 | 278,675,456 | 279,121,920 |
| Managed-heap high-water | 162,371,304 | 167,795,600 | 175,896,592 | 172,666,104 | 172,119,432 |
| Authority ticks | 7,215 | 7,215 | 7,215 | 7,216 | 7,215.3 |
| UDP datagrams sent | 802,327 | 813,362 | 779,862 | 776,981 | 790,068 |
| UDP payload bytes sent | 445,751,986 | 449,433,348 | 415,344,756 | 409,563,217 | 424,780,440 |
| Presentation 144 sent / proof 145 received | 108,096 / 108,096 | 107,520 / 107,520 | 106,496 / 106,496 | 107,264 / 107,264 | exact pairing |
| Participants/connections start/end | 64/64 | 64/64 | 64/64 | 64/64 | equal |
| Physical/logical accounting | reconciled | reconciled | reconciled | reconciled | equal |

After 1 is the strongest equal-work guard: it sent 0.83% more UDP payload, 1.38% more datagrams,
and 0.24% more message-148 envelopes than before, while server CPU was 31.59% lower. The mean is
reported with the natural workload/wire variation intact. Heap high-water rose 6.0% on the mean;
RSS fell 1.5%. Neither is hidden or claimed as a memory win.

Three earlier directories named `after-control-*` and `after-perf` are explicitly discarded:
they used the old ServerOnly binary because the maintained runner uses `--no-build`. Their unchanged
profile caught the setup error. Accepted data is only under `after-built-*`.

## Sampled profile

Both 45-second captures used `cycles:u`, 997 Hz, frame-pointer call graphs, separate dedicated
server processes, telemetry Off, and zero lost samples. Percentages below are main-thread leaf
samples resolved against the retained .NET perf map.

| Item-path symbol | Before | After |
| --- | ---: | ---: |
| `IsSubscribed` | 6.2075% | **0.0535%** |
| `ReconcilePublicItemPresentation` | 1.3890% | **0.2353%** |
| `CopyPublicWeaponPresentations` | 1.2100% | **0.0214%** |
| `QueuePublicItemPresentationMetadata` | 0.7661% | 3.0381% |
| selected leaf total | 9.5726% | 3.3483% |

The queue method remains on every recipient progress pass even when its reconciliation fast-path
returns immediately. Its higher percentage is against a process using much less CPU and is not a
new absolute regression. Further work on that shell is not selected without a separate measured
experiment. Before/after profiled process rates were 0.34203 and 0.23414 core.

## Product correctness control

A 64-client `mixed-chaos` control (seed `731911`, 30 s warmup, 60 s measurement) completed with:

- 65 fresh application proof lifetimes and one reconnect;
- 268 sword attempts with pickup, equip, and drop observed;
- 971 interactions, 745 primary attempts, 2,223 movement attempts, and 748 jumps;
- Local/Global Chat, 876 chunk crossings, 1,969 vertical transitions, and 686 landings;
- zero bot faults, zero source gaps, reconciled physical/logical accounting, and clean 65/65 leave.

Focused Release results are all 180 Game replication tests and all Network application tests,
including three consecutive executions of the new real-UDP proof regression. The final
`./verify.sh --configuration=Release` passed the complete canonical suite, actual exports,
Contentless Online, mixed bot/native accounting, lifecycle, composition, edition-boundary, and
packet-free Single Player gates.

Raw profiler and run output remains ignored under:

```text
_Benchmarks/results/pf5-r11-item-presentation-20260917/
```

Reproduction shape:

```bash
dotnet build Server/DirectConnect/UltraSim.Server.DirectConnect.csproj \
  -c Release -p:UltraSimNetworkFlavor=ServerOnly

ULTRASIM_TELEMETRY=Off Tools/NetworkBots/run-pf5-baseline.sh crowd 120s \
  _Benchmarks/results/pf5-r11-item-presentation-20260917/control \
  64 509921 60s /tmp/ultrasim-pf5-r11-cache \
  60 60 30 0 0 compliant 1 10 100 1

perf record -F 997 -e cycles:u -g --call-graph fp \
  -p <dedicated-server-pid> -o server.perf.data -- sleep 45
```

PF5-R11 changes no wire format, manifest, transport revision, AOI, cadence, compression,
aggregation, participant limit, delivery/proof semantics, or authority rate. PF6 remains ready on
the integrated post-repair tree.
