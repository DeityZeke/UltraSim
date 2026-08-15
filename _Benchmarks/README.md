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
| `UltraSim.Network.Comparison` | N0.10 isolated V2/V3 bot matrix coordinator; both process orders and provisional-topology reports. |
| `UltraSim.Network.V2ComparisonAdapter` | Frozen V2 bot-harness child-process adapter and normalized owner-state evidence. |
| `UltraSim.Network.V3ComparisonAdapter` | Current V3 connected-UDP bot adapter with bounded pressure, memory, wire, and semantic-latency telemetry. |
| `UltraSim.Game.ReplicationBenchmarks` | N0.1 100K canonical changed-state capture and OfflineDirect semantic apply. |
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
```

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
