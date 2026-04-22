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
- AOI subscription update/reconciliation cost

```powershell
dotnet build .\_Benchmarks\UltraSim.Network.Benchmarks\UltraSim.Network.Benchmarks.csproj -c Release
dotnet .\_Benchmarks\UltraSim.Network.Benchmarks\bin\Release\net8.0\UltraSim.Network.Benchmarks.dll
```

Results are written to `_Benchmarks/results/network-benchmarks.json`.

Current intent:

- validate the `scheduled work -> scoped published frame -> subscriber fanout` bridge
- compare shared-frame fanout against naive per-recipient rebuild
- measure the first `player position -> AOI ring overlap -> subscription reconciliation` path
- track both time and allocation behavior early, before server/client integration hides the cost shape

Current interpretation guidance:

- shared-frame fanout is already a clear allocation win over naive rebuild, but not yet a universal throughput win
- AOI subscription maintenance is now real and benchmarked, and the first heavy allocation cliff was materially improved by moving reconcile ownership into the subscription registry
- the next benchmark-driven tuning target is the shared-frame projection/dispatch path, not AOI correctness
