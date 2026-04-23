# ECS Benchmarks

This folder contains an offline benchmark suite for comparing:

- `Kernels/ECS` as the new kernel
- `_OLD/UltraSim/ECS` as the legacy kernel

The suite avoids external packages so it can run inside the repo without pulling BenchmarkDotNet.

## Coverage

The benchmark matrix currently covers:

- bootstrap/registration
- deferred structural creation
- direct component writes
- safe component reads
- 1/2/3-component queries
- deferred add/remove/destroy
- sparse-victim destroy / swap-remove pressure
- add/remove migration cycles
- create/destroy reuse churn
- dirty drain cost
- scheduler tick cost in serial and parallel modes
- oscillating/pulsating sine-wave movement tick cost in serial and parallel modes
- oscillating/pulsating sine-wave movement tick cost at `100k` entities over `200` ticks
- dedicated older-old `v1`-style `pulse -> velocity -> movement` pipeline benchmarks at `100k` entities over `50` ticks
- activity-shaped movement benchmarks with a deterministic `35%` moving subset reshuffled every `10` ticks at `100k / 200 ticks`
- persistence-view capture cost

It also includes kernel-specific scenarios:

- new kernel: immediate create and explicit patch writes
- old kernel: mutable ref write path

## Running

```powershell
powershell -ExecutionPolicy Bypass -File .\_Benchmarks\run_ecs_benchmarks.ps1
```

Results are written to:

- `_Benchmarks/results/new-ecs-benchmarks.json`
- `_Benchmarks/results/old-ecs-benchmarks.json`
- `_Benchmarks/results/ecs-benchmark-summary.md`
- `_Benchmarks/results/network-benchmarks.json`

## GC Soak Mode

The new-kernel runner also supports a sustained soak mode for checking allocation and GC behavior
without the forced full collections used by the microbenchmark harness.

Example:

```powershell
dotnet run --project .\_Benchmarks\UltraSim.ECS.NewBenchmarks\UltraSim.ECS.NewBenchmarks.csproj -c Release -- --soak --seconds 60 --interval 10
```

Useful options:

- `--seconds <n>`: wall-clock duration per scenario
- `--interval <n>`: sample interval in seconds
- `--warmup-ticks <n>`: ticks to run before measurement
- `--workers <n>`: override ECS worker thread count for the soak run
- `--scenario <id>`: repeat to limit the run to specific scenarios
- `--output <path>`: override the JSON output path

Default soak scenarios:

- `serial-v1-active`
- `parallel-v1-active`
- `serial-osc-active`
- `parallel-osc-active`

Soak results are written to `_Benchmarks/results/new-ecs-soak-report.json` by default and include:

- mean / p50 / p95 / p99 / max tick time
- total allocated bytes during the run
- managed heap delta before final collection
- retained managed bytes after a final full GC
- working set delta
- Gen0 / Gen1 / Gen2 collection counts
- interval samples for trend inspection

Current interpretation guidance:

- use soak mode to catch allocation drift, GC pressure, and retained-memory cliffs that the microbenchmark harness intentionally hides by forcing full collections between scenarios
- current heavy-scheduler soak runs were used to validate the active movement `100k` cases, including worker-count retention diagnostics for parallel dirty-capture behavior
- the present soak coverage is strong enough to flag obvious GC/retention regressions, but it is still not a replacement for longer mixed-host runtime validation once wrapper fronts exist

## Network Benchmarks

There is now a first standalone network benchmark runner focused on:

- shared fanout versus naive per-recipient rebuild
- integrated `schedule -> fanout` cost for scoped stream work
- AOI subscription update/reconciliation cost

```powershell
dotnet build .\_Benchmarks\UltraSim.Network.Benchmarks\UltraSim.Network.Benchmarks.csproj -c Release
dotnet .\_Benchmarks\UltraSim.Network.Benchmarks\bin\Release\net8.0\UltraSim.Network.Benchmarks.dll
```

Results are written to `_Benchmarks/results/network-benchmarks.json`.

Current intent:

- validate the `scheduled work -> scoped published frame -> subscriber fanout` bridge
- validate the direct `stream -> schedule -> fanout` bridge
- validate the first higher-layer `Server/Core -> Server/Network -> Kernels/Network` publish path
- compare shared-frame fanout against naive per-recipient rebuild
- measure the first `player position -> AOI ring overlap -> subscription reconciliation` path
- track both time and allocation behavior early, before server/client integration hides the cost shape

Current network benchmark scenarios now include:

- shared-frame fanout versus naive rebuild
- direct `schedule -> fanout`
- `ServerRuntimePublish_*`
- `ServerRuntimePublishAndFanout_*`
- `EcsDirtyPublish_*`
- `EcsDirtyPublishAndFanout_*`
- `EcsClassifiedPublishAndFanout_*`
- `EcsAoiLatestStatePublishAndFanout_*`
- `EcsAoiClassifiedPublishAndFanout_*`
- `ClientMovementLoopback_*`
- `ServerMovementLoopback_*`
- `UdpMovementRoundTrip_*`
- `UdpMovementMiniRamp_*`
- AOI subscription maintenance

Current interpretation guidance:

- shared-frame fanout is now both a clear allocation win and a throughput win on the current synthetic benchmark after the direct scheduled-work dispatch path replaced the intermediate projection step
- the integrated `schedule -> fanout` scenarios are now present and currently look healthy enough to treat this bridge shape as viable
- the first server-runtime publish scenarios are now in place and currently show the operator/status bridge slice is cheap enough to keep building on
- the first subscriber-scaling curve for `ServerRuntimePublishAndFanout_*` is now in place, and current measurements remain well below a `16 ms` server tick budget even at `2048` subscribers
- the first ECS-dirty publish scenarios are now in place, and they already caught one real bridge bug: partial-buffer dirty drains dropping updates; that regression is now fixed and test-covered
- the first mixed ECS classification scenario is now in place, exercising realtime latest-state plus bulk ordered-event publication through the same bridge
- the first AOI-backed ECS publish scenario is now in place, exercising real ring-scoped latest-state publication through the same bridge
- the first mixed AOI-classified ECS scenario is now in place, exercising `Realtime`, `ReliableEvent`, and `Bulk` through player-centered ring scopes rather than synthetic scopes
- the first client loopback movement benchmark is now in place, measuring the narrow client-side input-send plus authoritative-state receive path before any Godot wrapper or bot harness exists
- the first paired server/client movement benchmark is now in place, measuring the narrow end-to-end authoritative movement round-trip over loopback before any real socket transport or bot harness exists
- the first UDP socket-backed movement benchmark is now in place, measuring the same narrow movement round-trip after real control-plane handshake over a real transport
- the first small multi-client UDP ramp benchmark is now in place, measuring live handshake + movement through shared client/server session hosts before a standalone bot harness exists
- AOI subscription maintenance is now real and benchmarked, and the first heavy allocation cliff was materially improved by moving reconcile ownership into the subscription registry
- the next benchmark-driven tuning target is no longer the basic fanout bridge shape; it is the deeper server-facing publish integration that will sit on top of it

## Network Bot Harness

There is also now a first standalone network ramp harness:

- project: `_Benchmarks/UltraSim.Network.BotHarness`
- report output: `_Benchmarks/results/network_bot_harness_*.json`

It is intentionally narrower than the old bot harnesses:

- real UDP sockets
- shared session-host transport polling
- handshake + movement round-trip
- staged client ramps
- per-stage `tick_avg`, `tick_p95`, `tick_p99`, `tick_max`
- simple pending-input backlog stats

Current first standalone harness snapshot:

- `32` clients for `5` seconds at `62 Hz`
- `tick_avg = 0.312 ms`
- `tick_p95 = 0.336 ms`
- `tick_p99 = 0.393 ms`
- `tick_max = 0.869 ms`
- `pending_max = 0`

This is the first standalone counterpart to the earlier in-runner mini-ramp, and it is the right place to scale toward `64 / 128 / 256` before widening replication breadth further.

Current scaling note from the first longer harness pass:

- `256` clients / `30s`: `tick_p95 = 1.959 ms`
- `384` clients / `30s`: `tick_p95 = 3.592 ms`
- `480` clients / `30s`: `tick_p95 = 3.517 ms`
- `496` clients / `30s`: `tick_p95 = 3.954 ms`
- `512` clients / `30s`: borderline, observed between `3.748 ms` and `4.199 ms` across runs
- `640` clients / `30s`: `tick_p95 = 5.194 ms`

So the current movement-only UDP/session-host path is healthy up to roughly `~500` active clients at `62 Hz` before crossing a `4 ms` network-tail budget, with zero pending-input backlog in the passing range.

The harness now also tracks control-plane ping RTT and supports two explicit stop categories:

- `--stop-on tick`
  for the practical “real use” server/network budget ceiling
- `--stop-on ping`
  for the highest CCU that still keeps ping p95 under the latency SLO

Current split from the latest runs:

- tick-limited ceiling:
  - `512` clients failed the `4 ms` tick-p95 target
  - practical current ceiling is roughly `~480-500`
- ping-limited ceiling:
  - `2048` clients still passed `ping_p95 < 50 ms`
  - `2064` clients failed with `ping_p95 = 50.943 ms`
  - current latency-limited ceiling is roughly `~2048`

The harness has now also been widened with the first observer-replication load:

- optional `--observer-replication`
- real AOI-backed latest-state publish/fanout on top of the same live movement/session path
- actual client-side observer-frame decode/apply on top of that widened path
- report fields for:
  - `replication_published_items`
  - `replication_datagrams`
  - `replication_wire_datagrams`
  - `observer_frames_received`
  - `observer_frames_applied`
  - `observer_state_items`
- configurable live AOI ring radii via:
  - `--aoi-radii-m 7.5`
  - or broader gameplay-shaped policies such as `--aoi-radii-m 7.5,23.5,39.5`
- configurable ring-aware latest-state cadence via:
  - `--ring-cadence-ticks 1,2,3`
  - current intent:
    - ring `0`: every tick
    - ring `1`: every `2` ticks
    - ring `2`: every `3` ticks
  - this cadence is currently applied in the server publish-policy layer, not in `Kernels/Network`

Current broader three-lane, three-ring read:

- profile:
  - `spawn-spacing-m 12`
  - `aoi-radii-m 7.5,23.5,39.5`
  - ring cadence `1,2,3`
  - observer latest-state + visual latest-state + bulk latest-state + ordered events
  - owner deadband/silent-tick policy enabled
  - harness-side owner prediction jitter enabled
- results:
  - `0.35` active:
    - `32`: `tick_p95 = 1.738 ms`
    - `48`: `tick_p95 = 3.037 ms`
    - `64`: `tick_p95 = 4.152 ms`
  - `0.40` active:
    - `32`: `tick_p95 = 1.407 ms`
    - `48`: `tick_p95 = 3.387 ms`
    - `64`: `tick_p95 = 4.343 ms`

Current interpretation:

- ring-aware cadence materially improved the broadened load shape
- the current practical ceiling for this less-dense three-lane profile is around `48` clients before crossing a `4 ms` network-tail target
- `64` is close, but still over the target on the latest runs
- the harness now also reports substage timing splits:
  - `server_pump_*`
  - `publish_*`
  - `dispatch_*`
  - `client_pump_*`
- on the latest broadened `0.40` / `64` run:
  - `server_pump_p95 = 0.365 ms`
  - `publish_p95 = 0.198 ms`
  - `dispatch_p95 = 0.803 ms`
  - `client_pump_p95 = 2.570 ms`
- so the next likely tuning targets are client consume/apply and dispatch/fanout, not server-side publish policy again
- after the client-side single-pass inbound change:
  - `tick_p95` moved from `4.367 ms` to `4.316 ms`
  - `client_pump_p95` moved from `2.570 ms` to `2.147 ms`
  - `dispatch_p95` stayed about the same at `0.803 ms`
- that confirms the first useful optimization after cadence was on the client consume path, not on publish policy

Current observer-replication read with actual client consume:

- dense profile (`spawn-spacing-m 4`, `10s` stages):
  - `64` clients: `tick_p95 = 2.429 ms`
  - `68` clients: `tick_p95 = 6.039 ms`
  - practical current ceiling: about `64`
- less-dense profile (`spawn-spacing-m 12`, `10s` stages):
  - `128` clients: `tick_p95 = 2.643 ms`
  - `132` clients: `tick_p95 = 4.821 ms`
  - practical current ceiling: about `128`

Supporting 5-second replication shape snapshots:

- dense `64`:
  - `replication_datagrams = 535,228`
  - `replication_wire_datagrams = 34,198`
  - `observer_frames_received = 535,228`
  - `observer_frames_applied = 535,228`
- less-dense `128`:
  - `replication_datagrams = 208,252`
  - `replication_wire_datagrams = 39,680`
  - `observer_frames_received = 208,252`
  - `observer_frames_applied = 208,252`

The important correction here is that transport-level bundling is real and measurable, but once the client actually consumes observer state the widened practical ceiling drops sharply below the movement-only ceiling. That is the honest new baseline for the next widened gameplay slice.
