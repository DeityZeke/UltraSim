# ECS V2/V3 semantic comparison

Protocol: `ecs-v2-v3-semantic-comparison-v1`.

The comparison measures frozen V2 and current V3 only where both can perform the
same useful work and reach the same normalized logical state. It is a regression
instrument, not a claim that internal representations or all capabilities match.

## Process model

- `UltraSim.ECS.ComparisonCore` owns the catalog, report schema, measurement,
  canonical writer/hash, and strict joiner.
- `UltraSim.ECS.V2ComparisonAdapter` compiles frozen `_V2` in a child process.
- `UltraSim.ECS.V3ComparisonAdapter` references current V3 in another child.
- `UltraSim.ECS.Comparison` runs and joins them by stable scenario ID/scale.
- comparison tests protect descriptors, hash sensitivity, mismatch suppression,
  environment validation, unsupported rows, and baseline ratios.

Separate processes are mandatory because both generations use the same namespace
and because tiered JIT/PGO, GC, and cache state must not leak between them. The
accepted gate runs both V2-first and V3-first. Runtime, architecture, GC, tiering,
ReadyToRun, protocol, and descriptor mismatch suppress a ratio.

## Canonical contract

Each timed iteration builds a fresh fixture, executes one workload, then captures
logical state outside timing. Canonical state includes stable scenario identity,
liveness/stale behavior, logical component membership and values, enable state,
logical archetype populations, relevant changed entities, and slot-reuse facts.

It excludes world IDs, physical entity indices where not semantic, generation
starting values, local component/archetype/chunk/row identity, allocator address,
and backend. Wide values hash every field.

Fixture construction, JIT preparation, forced pre-iteration collection,
canonical capture, validation, and disposal are not timed. The timed region
records duration distribution, operations/logical bytes, checksum, caller-thread
allocation, and Gen0/1/2 collections.

Caller-thread allocation is complete only for single-threaded rows. V3 worker
allocation is separately instrumented by scheduler suites; V2 worker allocation
is not available and must not be inferred from the caller counter.

## Classification

- **Direct** — equivalent public contract and useful work.
- **Equivalent observable semantics** — mechanisms differ; normalized work/state
  match.
- **Provisional/Directional** — a documented semantic/mechanism gap remains.
- **Unsupported** — no honest equivalent; retained in the catalog with no ratio.

Unsupported capabilities include stronger independent change/lifecycle cursor,
deferred-token, bounded-failure, and full durable transactional persistence
contracts that frozen V2 cannot reproduce. New V3 capabilities do not receive a
fabricated V2 number.

## Coverage

The current catalog covers representative scales for:

- checked random reads/tracked writes and prepared lookup controls;
- one/two/three-column traversal and read/write generated jobs;
- serial/parallel scheduled chunk and range work;
- immediate/deferred create/destroy, sparse/full destruction, add/remove,
  transition round trips, balanced churn, record/playback/end-to-end boundaries;
- slot reuse/stale identity, enable state, unchanged/dense/sparse changed polling;
- typed structural payloads and constant-default prefab spawn;
- explicit unsupported lifecycle/persistence/filter/failure rows.

The exact current row count and descriptors belong to the emitted JSON, not this
document.

## Reproduction

Primary order:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --output-dir=_Benchmarks/results/ecs-v2-v3
```

Reverse order:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --v3-first --warmups=4 --iterations=15 --output-dir=_Benchmarks/results/ecs-v2-v3-reverse
```

Regression against an accepted artifact:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --baseline=_Benchmarks/Baselines/ecs-v2-v3-3.4.2-2026-08-14.json --output-dir=_Benchmarks/results/ecs-v2-v3-current
```

The orchestrator fixes tiering/ReadyToRun policy for both children and embeds it
in reports. `--filter` and `--scale` are development aids, not substitutes for
the full two-order gate. `--accept-baseline=PATH` refuses to overwrite existing
evidence.

## Reading regressions

The regression report shows absolute baseline/current V2 and V3 mean/p95,
current-to-baseline ratios, a V2-control-normalized signal, allocation movement,
and independent canonical checks. Similar movement in frozen V2 and V3 suggests
host noise; V3-only movement warrants investigation. Descriptor/catalog changes
remain visible and suppress invalid ratios.

Accepted artifacts are immutable under [Baselines](Baselines/README.md). Git
history contains the checkpoint-by-checkpoint prose; current ECS mechanisms are
documented in [`Kernels/ECS`](../Kernels/ECS/README.md).
