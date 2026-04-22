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
- AOI subscription maintenance

Current interpretation guidance:

- shared-frame fanout is now both a clear allocation win and a throughput win on the current synthetic benchmark after the direct scheduled-work dispatch path replaced the intermediate projection step
- the integrated `schedule -> fanout` scenarios are now present and currently look healthy enough to treat this bridge shape as viable
- the first server-runtime publish scenarios are now in place and currently show the operator/status bridge slice is cheap enough to keep building on
- the first subscriber-scaling curve for `ServerRuntimePublishAndFanout_*` is now in place, and current measurements remain well below a `16 ms` server tick budget even at `2048` subscribers
- the first ECS-dirty publish scenarios are now in place, and they already caught one real bridge bug: partial-buffer dirty drains dropping updates; that regression is now fixed and test-covered
- the first mixed ECS classification scenario is now in place, exercising realtime latest-state plus bulk ordered-event publication through the same bridge
- AOI subscription maintenance is now real and benchmarked, and the first heavy allocation cliff was materially improved by moving reconcile ownership into the subscription registry
- the next benchmark-driven tuning target is no longer the basic fanout bridge shape; it is the deeper server-facing publish integration that will sit on top of it
