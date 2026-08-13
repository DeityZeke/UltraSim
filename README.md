# UltraSim Next

The root tree is the new implementation. Frozen V2 Current and Legacy source,
tests, benchmarks, and historical documents live under `_V2/`.

The rebuild starts with the ECS specification in
[`ECS_NEXT_DESIGN.md`](ECS_NEXT_DESIGN.md). Network Next remains specified by
[`NETWORK_NEXT_DESIGN.md`](NETWORK_NEXT_DESIGN.md) and will build on validated
ECS capture/identity seams.

## Integrated S0 contract

[`S0 Integration Design.md`](S0%20Integration%20Design.md) is the authoritative
contract for the first cross-layer runtime milestone. S0.0 — the contract and
scaffolding slice — is accepted in [`S0.0 Acceptance Report.md`](S0.0%20Acceptance%20Report.md).
S0.1 — the concrete runtime and generation slice — is accepted in
[`S0.1 Acceptance Report.md`](S0.1%20Acceptance%20Report.md). S0.0 froze
ownership and dependency direction. S0.2 — the InternalHost, HeadlessHost, and
minimal typed host capabilities slice — is accepted in
[`S0.2 Acceptance Report.md`](S0.2%20Acceptance%20Report.md). Later slices
introduce UI, admission, and game composition incrementally.

## Current executable slices

Build and run the ECS runtime and generator tests:

```bash
dotnet run --project _Tests/UltraSim.ECS.Tests/UltraSim.ECS.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.ECS.Generator.Tests/UltraSim.ECS.Generator.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.Server.Persistence.Tests/UltraSim.Server.Persistence.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.Server.Runtime.Tests/UltraSim.Server.Runtime.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.Server.Hosting.Tests/UltraSim.Server.Hosting.Tests.csproj \
  -c Release
```

ECS 3.4.1 full-world persistence is specified in
[`Kernels/ECS/ECS_PERSISTENCE.md`](Kernels/ECS/ECS_PERSISTENCE.md), with accepted
measurements and V2 comparison limits in
[`ECS_3_4_0_PERSISTENCE_REPORT.md`](ECS_3_4_0_PERSISTENCE_REPORT.md), plus the
adversarial correctness results in
[`ECS_3_4_1_PERSISTENCE_HARDENING_REPORT.md`](ECS_3_4_1_PERSISTENCE_HARDENING_REPORT.md).

Run the Phase 1 entity/schema/layout/arena/archetype-row benchmark suite without replacing an accepted baseline:

```bash
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release -- --output /tmp/ultrasim-next-ecs-phase1.json
```

## Local Qwen reviewer

Create the repository-specific model once:

```bash
ollama create ultrasim-qwen -f _Tools/LocalAI/Modelfile.ultrasim-qwen
```

Then run a bounded review:

```bash
_Tools/LocalAI/qwen-review.sh ecs \
  Kernels/ECS/Entities/Entity.cs \
  Kernels/ECS/Entities/EntityPool.cs
```

See [`_Tools/LocalAI/README.md`](_Tools/LocalAI/README.md) for review modes,
limits, and environment overrides.
