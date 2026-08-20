# Registry Load Testing

Purpose: provide a simple local harness for measuring registry read throughput, write/publisher pressure, and runtime poll/persistence behavior.

## Tool

Project:
- `tests/UltraSim.Registry.LoadTool/UltraSim.Registry.LoadTool.csproj`

Default read target:
- `http://127.0.0.1:5180/api/v1/servers`

Runtime diagnostics endpoint:
- `http://127.0.0.1:5180/api/v1/admin/runtime`

## Usage

Basic read load:

```powershell
dotnet run --project tests/UltraSim.Registry.LoadTool -- --concurrency 100 --duration-seconds 30
```

Conditional GET load:

```powershell
dotnet run --project tests/UltraSim.Registry.LoadTool -- --concurrency 100 --duration-seconds 30 --conditional
```

Benchmark bypass load:

```powershell
dotnet run --project tests/UltraSim.Registry.LoadTool -- --concurrency 100 --duration-seconds 30 --bypass-token local-benchmark
```

Publisher/write stress:

```powershell
dotnet run --project tests/UltraSim.Registry.LoadTool -- --mode publish --concurrency 25 --duration-seconds 30 --bypass-token local-benchmark --runtime-snapshot
```

Publisher/write stress with poller pressure against a dead endpoint:

```powershell
dotnet run --project tests/UltraSim.Registry.LoadTool -- --mode publish --concurrency 25 --duration-seconds 30 --bypass-token local-benchmark --poll-target-base-url http://127.0.0.1:65534/discovery/status --runtime-snapshot
```

Custom endpoint:

```powershell
dotnet run --project tests/UltraSim.Registry.LoadTool -- --base-url http://127.0.0.1:5180 --path /api/v1/regions
```

## Reported Metrics

The tool prints:
- total requests
- failures
- throughput
- bytes transferred
- p50 / p95 / p99 latency
- HTTP status counts
- optional runtime snapshot from `/api/v1/admin/runtime`

The runtime snapshot includes:
- store counts
  - total records
  - active / delisted / disabled
  - online / stale / offline / pending
  - active leases
  - poll candidates
  - visible rows
  - persistence enabled / pending persistence
- mutation and persistence counters
  - register / heartbeat / presence / metadata / delete calls
  - poll rounds / successes / failures
  - lifecycle sweep counts
  - `SaveState()` call count, bytes written, and duration

## Notes

- This is a lightweight harness, not a full distributed load rig.
- It is useful for:
  - validating cached read behavior
  - validating conditional GET behavior
  - checking basic rate-limit behavior
  - measuring publisher-side write churn
  - observing poller and persistence behavior during stress
- the registry can optionally expose a benchmark-only rate-limit bypass header when explicitly enabled in configuration
- this should remain disabled outside controlled local or lab testing
- `Services/UltraSim.Registry/appsettings.Development.json` currently enables the local benchmark bypass token `local-benchmark`
- publish mode intentionally creates many registry records; if state persistence is enabled, clear or rotate the dev state file between runs when you want a clean baseline
- persistence is now coalesced through a background flush loop; `PersistenceFlushIntervalMilliseconds` controls the maximum flush cadence
