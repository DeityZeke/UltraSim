# ECS Release Checklist

Use this checklist before shipping builds from this repository.

## Runtime Isolation

- `project.godot` `run/main_scene` points to the shared bootstrap scene (`res://main.tscn`).
- `MainSceneRoot.cs` routes correctly for the intended export feature set:
  - `client_lite`
  - `client_full`
  - `server_build`
- The target scene for the selected export feature exists in the exported resource set.
- Benchmark harness scene is isolated to `_tmp/ECSBenchmarkHarness.tscn`.

## World/Runtime Toggles

- `EnableDebug` is disabled in production worlds.
- `EnableStatistics` is disabled unless explicitly required for telemetry.
- `EnableDirtyTracking` is enabled only when consumers use dirty drains.
- `ExecutionMode` is explicitly set for target deployment.

## Benchmark/Test Toggles

- `_tmp` benchmark nodes have `AutoRun = false` by default.
- Any benchmark scene used for profiling is not used as `run/main_scene`.
- MultiMesh/visual benchmark bridges are only enabled in benchmark scenes.

## Validation

- Run full tests: `dotnet test --nologo`.
- Run release build: `dotnet build GodotECS.csproj -c Release`.
- Confirm no unexpected runtime logs/errors on startup.
