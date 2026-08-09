# Accepted ECS comparison baselines

`ecs-v2-v3-2026-08-09.json` is the first immutable joined V2/V3 semantic
baseline for protocol `ecs-v2-v3-semantic-comparison-v1`. It was captured with
4 warmups and 15 measured iterations per scenario, V2 first, tiered compilation
and ReadyToRun disabled, on the environment embedded in the JSON.

Do not overwrite an accepted artifact. Create a newly dated file when the
protocol, host, runtime, measurement policy, or intentionally accepted V3 state
changes. The orchestrator enforces this for `--accept-baseline`.

For an ordinary future regression capture:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --baseline=_Benchmarks/Baselines/ecs-v2-v3-2026-08-09.json --output-dir=_Benchmarks/results/ecs-v2-v3-current
```

The regression report reruns frozen V2 as a control. Similar V2 and V3 movement
usually indicates host/runtime noise; V3-only movement is evidence of a rewrite
change. Canonical-state, descriptor, methodology, and environment checks take
precedence over timing ratios.
