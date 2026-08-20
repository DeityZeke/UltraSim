# UltraSim benchmark methodology

Benchmarks are evidence, not authority. Every accepted result is tied to a
semantic contract, canonical-state validation, declared capacities/runtime
settings, allocation scope, and immutable machine-readable JSON.

## Performance baseline

Unless a scenario explicitly studies cadence, authoritative Runtime benchmarks
use:

```text
simulation cadence       fixed 60 Hz
playback                 1x
runtime cadence changes OFF
adaptive cadence         OFF
```

Missing the fixed-rate budget remains visible; lowering cadence cannot make a
normal regression pass. Replication/render cadence will be disclosed separately
when those systems exist.

## Suites

| Project | Purpose |
| --- | --- |
| `UltraSim.ECS.Benchmarks` | Current ECS micro, scheduler, structural, soak, change, lifecycle, and content scenarios. |
| `UltraSim.ECS.Comparison` | Isolated V2/V3 coordinator and contract-aware report. |
| `UltraSim.ECS.V2ComparisonAdapter` | Frozen V2 scenarios in a child process. |
| `UltraSim.ECS.V3ComparisonAdapter` | Current V3 scenarios in a child process. |
| `UltraSim.ECS.Persistence` | Capture/encode/preflight/restore scale and compatibility. |
| `UltraSim.ECS.SimulationSlice0` | Simulation-shaped public-consumer workload. |
| `UltraSim.ECS.ExtremeSphere*` | CPU and Godot MultiMesh extreme-scale stress. |
| `UltraSim.ECS.BitmapExperiment*` | Retained experimental storage oracle; not production ECS. |
| `UltraSim.Network.ComparisonCore` | N0 contract-aware evidence admission; refuses relative headlines for mismatched profiles. |
| `UltraSim.Network.Comparison` | N0.10 isolated V2/V3 bot matrix coordinator; both process orders and selectable connected-pairs/shared-listener V3 topology. |
| `UltraSim.Network.V2ComparisonAdapter` | Frozen V2 bot-harness child-process adapter and normalized owner-state evidence. |
| `UltraSim.Network.V3ComparisonAdapter` | Current V3 connected/shared-UDP bot adapter with bounded pressure, memory, wire, semantic-latency telemetry, cadence/stall acceptance, and V3-only integrated `spread_mix`/`dense_mix`. |
| `UltraSim.Game.ReplicationBenchmarks` | N0.1 100K canonical changed-state capture and OfflineDirect semantic apply. |
| `UltraSim.Spatial.Benchmarks` | Terrain T0.0 dependency-neutral spatial math and fixed game-position profile: normalization, reconstruction, key/hash/order, AABB distance, and terrain-cell lookup. |
| `UltraSim.WorldSpatialIndex.Benchmarks` | Terrain T0.1 split post-step/index costs: idle observation, same-home change, cross-chunk migration, create/remove, full rebuild, and member reads. |
| `UltraSim.Network.KernelBenchmarks` | N0.2 generated codec, packet ownership, ring, deterministic datagram, and connection reuse primitives. |
| `UltraSim.Network.ProtocolBenchmarks` | N0.3 secure unreliable/reliable delivery, 4 KiB fragmentation, and deterministic impairment convergence. |
| `UltraSim.Network.Application.Tests --benchmark` | N0.4 paired OfflineDirect/secure-ProtocolLoopback real Admission/UI workflow and established Inventory interaction evidence. |
| `UltraSim.Network.Application.Tests --benchmark-udp` | N0.5 paired secure ProtocolLoopback/real localhost UDP run of the exact N0.4 application workflow and established Inventory interaction. |
| `UltraSim.Network.Application.Tests --benchmark-n06` | N0.6 paired ProtocolLoopback/localhost UDP complete controlled-input, authority-tick, owner-state, and client-reconciliation cycle. |
| `UltraSim.Game.ReplicationBenchmarks --n07` | N0.7 maintained spatial interest, newest pending state, semantic fanout, application ACK, all-recipient same-cell movement, region traversal, idle, allocation, and reservation evidence. Density/radius/region/fanout/dirty parameters are configurable; the preserved sensitivity matrix currently covers density, region edge, fanout, and dirty ratio. |
| `UltraSim.Network.Application.Tests --benchmark-n08` | N0.8 paired ProtocolLoopback/localhost UDP atomic 256-entity baseline capture, preparation, segmented transfer, private client commit, application ACK, allocation, and reservation evidence. |

The corresponding executable tests under `_Tests` validate comparison catalogs,
bitmap semantics, persistence, and benchmark invariants.

Network N0.1 adds game-projection/OfflineDirect evidence, N0.2 adds fixed-runtime
kernel micro evidence, N0.3 adds a standalone secure ProtocolLoopback baseline,
N0.4 adds application-level semantic parity and interaction evidence, N0.5
changes only byte transport/session ownership while retaining that oracle, and
N0.6 adds a continuous one-owner input/state/reconciliation profile. N0.7 adds
maintained many-entity/many-recipient interest and application-knowledge sweeps.
N0.8 adds an atomic recovery/generation-replacement baseline profile.
None is mislabeled as a V2/V3 protocol ratio. Ratios become eligible only when an
equivalent reviewed profile exists.

## Running

Representative commands from the repository root:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Benchmarks -c Release -- --quick
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison -c Release
dotnet run --project _Benchmarks/UltraSim.ECS.Persistence -c Release
dotnet run --project _Benchmarks/UltraSim.ECS.SimulationSlice0 -c Release -- --mode both
dotnet run --project _Benchmarks/UltraSim.Game.ReplicationBenchmarks -c Release
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Spatial.Benchmarks -c Release -- \
    --output=_Benchmarks/results/terrain-t0.0-spatial-contracts-local.json
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.WorldSpatialIndex.Benchmarks -c Release -- \
    --output=_Benchmarks/results/terrain-t0.1-world-spatial-index-local.json
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Network.KernelBenchmarks -c Release
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Tests/UltraSim.Network.Application.Tests -c Release -- \
    --benchmark-n06 --output _Benchmarks/results/network-n0.6-local-run.json
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Game.ReplicationBenchmarks -c Release -- \
    --n07 --entities=5000 --recipients=16 --dirty-percent=10 \
    --output=_Benchmarks/results/network-n0.7-local-run.json
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Tests/UltraSim.Network.Application.Tests -c Release -- \
    --benchmark-n08 --output _Benchmarks/results/network-n0.8-local-run.json
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Network.Comparison -c Release -- \
    --clients=32,64,128,256 --stage-ticks=120 --settle-ticks=30 \
    --output-dir=_Benchmarks/results/network-v2-v3-bot-local
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter -c Release -- \
    --replication-suite --replication-profile=all --clients=400 \
    --ticks=120 --warmup=30 --dirty-percent=2 \
    --output=_Benchmarks/results/network-v3-spread-dense-local.json

# Opt-in causal attribution; diagnostic probes deliberately perturb normal timing.
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter -c Release -- \
    --replication-suite --replication-profile=dense_mix \
    --replication-traffic=causal --phase-attribution=true --clients=400 \
    --ticks=120 --warmup=30 --dirty-percent=2 \
    --output=_Benchmarks/results/network-v3-dense-event-causal-local.json
```

The replication causal control runs `full`, `worldstate-only`, `event-only`, and
`updates-only`
with identical topology, maintained relations, dirty updates, capacities,
cadence, and transport. Its phase report separates authority/update, Event
publish/schedule, WorldState scheduling, server/client application work,
combined protocol pumping, and opt-in server/client protocol-role estimates.
`updates-only` preserves dirty canonical/AOI maintenance while suppressing both
delivery families. Opt-in WorldState attribution additionally records relation
and reverse-edge visits, per-tier scheduler examinations/selections/skips,
bundle stop reasons and record histogram, ACK records/time, and adapter
copy/codec/queue time.
The same report includes the WorldState application-proof census at the existing
flush boundary. It distinguishes proofs produced during the drain from the
retained backpressure ring, reports immediate 2+/3+/4+ packing opportunities and
status mix, and never delays proof to improve a batch.
Stable-update attribution also separates accepted `Far→Far` one-pass updates
from general-path upserts and reports eligibility, necessary relation visits,
avoided reverse/second/ensure visits, Immediate transitions, tier changes, and
pending version publications. These counters are opt-in and do not justify
dictionary removal or other AOI changes by themselves.
Bundle-expiration attribution separately records build-time expiration requests,
consecutive distinct/repeated semantic times, completed sweeps/cache hits, total
and active slots examined, actual expirations, `_bundleHigh` high-water, and
cumulative sweep time. It exists to distinguish global timeout housekeeping from
pending-debt selection; ordinary acceptance keeps all attribution probes off.
Pending-selection attribution additionally times selectable/cadence/retry/
already-selected classifications, traversal and tier overhead, record/commit
work, counts all-deferred `NoData` calls, and preserves logarithmic remaining-
delay histograms. Per-candidate clocks are intentionally diagnostic overhead;
their absolute totals are not probes-off production cost.
Server-protocol attribution further separates shared-listener poll, receive
syscall and route/admission time; server-peer endpoint receive, AEAD, frame
admission and residual bookkeeping; send selection/encode/AEAD/listener
admission/syscall; and empty peer calls. The listener census records complete
no-wait receive drains and theoretical native call counts at 4/8/16/32. Use
`--worldstate-attribution=false` with `--phase-attribution=true` when measuring
protocol/role tails without the much heavier per-relation WorldState clocks.
`--shared-socket-io=platform-send-receive-batch` selects the bounded Linux
`recvmmsg` experiment; the default `platform-batch` remains the send-only control.
Cold freshness scans remain outside the timed tick. Probe-enabled figures are
compared only with other probe-enabled rows; ordinary acceptance uses probes off.

Each executable's `--help`/argument parser is the source of truth for filters,
scales, process order, output, worker count, and longer profiles. Accepted runs
use Release, warmup, both A/B orders where relevant, and stable machine/runtime
settings recorded in the artifact.
Do not point an ordinary rerun at an existing immutable baseline filename; admit
new evidence under a new descriptive name only after review.

Godot extreme-stress setup and exact controls remain in
[ECS_EXTREME_SPHERE_STRESS.md](ECS_EXTREME_SPHERE_STRESS.md). Cross-generation
contract details remain in [ECS_V2_V3_COMPARISON.md](ECS_V2_V3_COMPARISON.md).
The provisional N0.10 network instrument is documented in
[NETWORK_V2_V3_BOT_COMPARISON.md](NETWORK_V2_V3_BOT_COMPARISON.md).
Its v2 evidence schema distinguishes unavailable metric families from measured
zero, preserves all numeric frozen-V2 stage diagnostics, and reports V3
occupancy, freshness/fairness, handshake, wire/security, syscall, CPU, and
marginal-scale context. The cross-generation `sparse_observer_movement` profile
now reports observer counts and V3 tier freshness/AoI amplification while
explicitly preserving the V2/V3 admitted-set difference. Dense/Event metrics
remain owned by the separate V3-only integrated replication suite and recovery
metrics remain tied to their recovery workloads.

The 1,024→4,096 sparse diagnostic can be reproduced with:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project _Benchmarks/UltraSim.Network.Comparison -c Release -- \
  --scenario=sparse_observer_movement \
  --clients=1024,1280,1536,1792,2048,2304,2560,2816,3072,3328,3584,3840,4096 \
  --tick-hz=62 --input-hz=20 --stage-ticks=310 --settle-ticks=10 \
  --spawn-spacing=12 --spawn-grid-width=64 --active-ratio=0.35 \
  --v3-topology=shared-listener \
  --v3-socket-io=platform-send-receive-batch \
  --v3-sparse-region-edge-mm=10000 \
  --v3-sparse-capacity-profile=current10 \
  --single-order --alternate-order \
  --output-dir=_Benchmarks/results/network-v2-v3-sparse-ccu
```

The V3 adapter also accepts `--sparse-region-edge-mm=<positive integer>` and
`--sparse-capacity-profile=auto|current10|legacy128`. The
comparison coordinator forwards it as `--v3-sparse-region-edge-mm`. This changes
only the conservative broadphase cell profile; the exact 7.5/23.5/39.5 m game
policy, cadence, and transport semantics remain fixed. `auto` selects the
evidence-backed `current10` bounds only at 10 m and retains `legacy128` for other
region sizes; the coordinator forwards the capacity option as
`--v3-sparse-capacity-profile`. Pass
`--deep-timing` directly to the V3 adapter for diagnostic phase attribution.
Those probe-enabled rows are not directly comparable with ordinary probes-off
timing or suitable for a joined V2/V3 canonical run.

The sparse admission experiment also accepts `--sparse-exact-admission`. It
keeps the 10 m region grid as a broadphase, then applies exact squared-distance
admission and Near/Far classification before creating or updating relations.
It is off by default. The canonical experiment found lower relation and wire
work but higher interest-update and server-role p95, so the mode remains a
diagnostic candidate-index checkpoint rather than a production policy.

Every sparse V3 result performs a cold exact-scope census after measured work.
It reports maintained/exact/coarse-only relations, coarse-vs-exact tiers,
recipient-scope p50/p95/max, and its temporary allocation. The census is an
independent O(recipients × entities) oracle outside tick time and does not alter
the production interest representation. The comparison coordinator retains its
externally observed process peak under
`process_peak_including_cold_scope_census_*`; primary working/private bytes stay
tied to the harness measurement and therefore exclude the cold oracle buffer.

The coordinator writes compact joined/matrix JSON by omitting redundant
per-client canonical rows after their exact hash/match has been established;
raw adapter artifacts retain those rows for an active investigation.

The sparse protocol experiment accepts `--unreliable-frame-aggregation`. It is
off by default and is admitted only by the sparse observer profile. Manifest v11
explicitly fingerprints a seven-frame unreliable-Data receive capability; the
local protocol limit remains an independent send-policy cap, so a cap-one sender
still decodes aggregates. Reliable Data remains scalar. The adapter reports aggregate
frame histograms, original
frames consumed, Data datagrams, plaintext fill, frames per pump, and the
corresponding protocol-stage counters. Deep timing also records a cold
same-lane-only greedy opportunity census for deciding whether mixed-lane fate
coupling is worthwhile. The paired evidence is recorded in
`Baselines/network-v3-sparse-unreliable-multiframe-2026-08-17.md`.
For tail work, `--pooled-tail-windows=N` runs repeated valid sparse windows and
concatenates their raw measured tick/server samples before calculating
p50/p95/p99/p99.9/max. Window setup/reset time is reported separately and is not
included in those primary samples. This is pooled-window aggregation evidence,
not a continuous roaming-session lifetime claim; `current10` still enforces its
six-second movement envelope in every window.

The opt-in N0.10 lifetime-compaction probe enables long-lived `current10` only
when all three bounded policy values are supplied:

```text
--sparse-compaction-headroom=<identities>
--sparse-compaction-safety=<identities>
--sparse-compaction-starts-per-tick=<starts>
```

The client requests maintenance when known-identity occupancy reaches
`capacity - headroom - safety`; a fixed server scheduler admits at most the
configured starts per tick. Results report known-identity p50/p95/max,
pressure-to-start and baseline durations, baseline records/queued bytes,
requests, rejected/timeout/cancel outcomes, and final scheduler debt. The
flags are deliberately opt-in: no capacity is widened and ordinary sparse
benchmarks retain the six-second `current10` evidence restriction. A long-lived
run is accepted only when repeated compactions finish with zero capacity drops,
nonviable recipients, byte failures, unfinished baselines, and exact
convergence.
The accepted 4,096-client, 15-logical-minute result is recorded in
[`Baselines/network-v3-sparse-worldstate-compaction-2026-08-17.json`](Baselines/network-v3-sparse-worldstate-compaction-2026-08-17.json)
with its readable
[`report`](Baselines/network-v3-sparse-worldstate-compaction-2026-08-17.md).
V3 visible-state AoI/fairness is sampled during the untimed 10-tick post-stage
settle window. The result records both the sampling duration and compact
histogram payload reservation, so freshness instrumentation is neither charged
to tick time nor mistaken for production Network/Game reservation.
The first artifact exposed a Near/Far starvation defect and remains immutable
pre-correction evidence. The corrected artifact is
[`network-v2-v3-sparse-ccu-coalescing-corrected-1024-4096-2026-08-17.json`](Baselines/network-v2-v3-sparse-ccu-coalescing-corrected-1024-4096-2026-08-17.json)
with its readable
[`matrix`](Baselines/network-v2-v3-sparse-ccu-coalescing-corrected-1024-4096-2026-08-17.md).
It proves 1/2/3-step Immediate/Near/Far p95 freshness across the full matrix,
zero same-tier deadline deferrals, zero capacity drops, and the larger honest
delivery cost after starvation is removed. Use that artifact—not the earlier
timing row—for subsequent sparse performance decisions.
The current send+receive topology and 16/10/8 m region sensitivity are preserved
in
[`network-v3-sparse-current-topology-region-sensitivity-2026-08-17.json`](Baselines/network-v3-sparse-current-topology-region-sensitivity-2026-08-17.json)
with a readable
[`report`](Baselines/network-v3-sparse-current-topology-region-sensitivity-2026-08-17.md).
The alternating adoption and separately isolated capacity evidence is
[`network-v3-sparse-10m-confirmation-capacity-2026-08-17.json`](Baselines/network-v3-sparse-10m-confirmation-capacity-2026-08-17.json)
with its readable
[`report`](Baselines/network-v3-sparse-10m-confirmation-capacity-2026-08-17.md).

## Comparability

Every cross-generation row is classified:

- **Direct** — equivalent public contract and useful work.
- **Equivalent observable semantics** — mechanisms differ but canonical outcome
  and included work are normalized.
- **Provisional/Directional** — useful evidence with a documented contract gap.
- **Unsupported** — no honest equivalent; no ratio is reported.

Canonical state ignores local world/archetype/chunk/row identity unless the
scenario's public contract includes it. It includes stable test identity,
liveness, component membership/values, enable state, and relevant change or
lifecycle state.

V2 and V3 run in isolated child processes in both orders for accepted comparison
evidence. This avoids tiered JIT, dynamic PGO, GC, and cache contamination between
engines. Same-process A/B controls are temporary development instruments and are
removed after a production decision.

## Statistics and allocation

Reports include at least mean and p95 for admitted timed scenarios; tail-focused
workloads add p50/p99/p99.9/max and multiple fresh-process samples. Small sample
sets label p95 honestly rather than treating it as a stable population estimate.

Caller-thread allocation counters do not measure allocations on V2 background
workers. V3 scheduler suites separately instrument worker allocations. Claims
must state their measurement scope. `0 B` means the measured warmed/timed region,
not setup, oracle capture, JSON, or process allocation.

GC counts, reserved ECS/scheduler payload, process working set, and managed heap
are different metrics and remain separately labeled.

## Accepted artifacts

Accepted JSON lives in [Baselines](Baselines/README.md). Artifacts are immutable:
do not overwrite or rename one to refresh numbers. A new accepted checkpoint gets
a new descriptive filename, workload identity, date, runtime/hardware settings,
canonical results, and methodology notes.

Before accepting an artifact:

1. pass the complete semantic/oracle tests;
2. run required A/B/process orders and allocation scopes;
3. investigate stable regressions above the scenario budget;
4. preserve zero-allocation/capacity contracts;
5. run the canonical repository verifier;
6. add the JSON and only concise methodology changes—not a new historical report.

Historical JSON remains for longitudinal analysis. Git history retains the old
prose reports.

## Retained experimental conclusions

- Persistent bitmap-hole storage was rejected for hot archetypes because sparse
  traversal and eager compaction cost more than it saved. The successful idea was
  barrier-local temporary row reuse while query-visible stores remain packed.
- The 100K mixed-operation soak showed structural playback was no longer the
  dominant realistic cost and redirected investigation toward change tracking
  and public system execution.
- Change decomposition showed checked arbitrary entity access and exact-row
  discovery are distinct costs; dense/topology-selected writes belong in
  generated jobs rather than an all-entity `RangeJob` scan.
- Simulation Slice 0 established the current execution-path taxonomy now kept in
  [`Kernels/ECS/EXECUTION.md`](../Kernels/ECS/EXECUTION.md).

The production consequences live in current ECS docs/code/tests. The completed
experiment narratives do not remain a second architecture history.
