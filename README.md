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
[`S0.2 Acceptance Report.md`](S0.2%20Acceptance%20Report.md). S0.3 — the UI-0
definition/compiler kernel — is accepted in
[`S0.3 Acceptance Report.md`](S0.3%20Acceptance%20Report.md). S0.4 — the local
client UI runtime and minimal Godot renderer — is accepted in
[`S0.4 Acceptance Report.md`](S0.4%20Acceptance%20Report.md). S0.5 — game-owned
admission with optional minimal Accounting — is accepted in
[`S0.5 Acceptance Report.md`](S0.5%20Acceptance%20Report.md).
S0.6 — real ECS + Accounting transactional persistence — is accepted in
[`S0.6 Acceptance Report.md`](S0.6%20Acceptance%20Report.md). S0.7 — the
composed game-owned ECS inventory vertical and generic inventory Gump — is
accepted in [`S0.7 Acceptance Report.md`](S0.7%20Acceptance%20Report.md).
S0.7 introduces the concrete `Game/Composition` root because multiple game
domains now require one schema, content catalog, generation root, and
persistence publication boundary. `Game/Contracts` keeps the narrow Admission
policy seam free of UI/client dependencies. Later slices extend the composition
root rather than making a feature project own the game around it.
S0.8 — the deterministic hosted admission/UI/inventory/simulation/save/restore
acceptance slice — is accepted in
[`S0.8 Acceptance Report.md`](S0.8%20Acceptance%20Report.md). This completes the
Integrated S0 milestone with canonical InternalHost/HeadlessHost parity and a
native Godot acceptance using the exact game UI definitions. The 2026-08-14
acceptance-hardening pass additionally makes the persisted generation
`ECS + Accounting/game state + authoritative clock`, moves shared UI protocol
types into neutral `Kernels/UI`, rejects runtime publication/lifecycle
reentrancy, rebinds or invalidates Admission sessions on replacement, separates
owner-thread capture pause from detached durable save latency, and makes
post-commit presentation failures detach rather than misreport authority.

The subsequent authoritative-cadence hardening is accepted in
[`Authoritative Cadence Independence Report.md`](Authoritative%20Cadence%20Independence%20Report.md).
Tick is step identity, delta is elapsed simulation time, fixed
20/30/40/50/60 Hz are valid host policies, durable time is independent of the
saving host's frequency, and runtime cadence changes are opt-in and owner-bound.
Normal performance validation remains fixed 60 Hz, 1x playback, with cadence
changes/adaptation off. No automatic adaptive controller was added.

The post-acceptance classification keeps S0 accepted: real transport remains a
Network V3 milestone; `Game/UI` is the intentional server-side game workflow
layer over neutral `Kernels/UI`; generated scheduling remains chunk-partitioned;
and `_V2` is compiled only by explicit comparison projects. The live comparison
catalog now accurately describes implemented V3 lifecycle publication.

Run the canonical repository verifier from any working directory:

```bash
./verify.sh --all-configurations
```

The verifier runs architecture dependency assertions first, then every active
engine-neutral executable gate, the S0 Godot headless acceptance (when the known
Godot binary or `ULTRASIM_GODOT` is available), and `git diff --check`. Use
`./verify.sh --architecture-only` for the fast dependency-boundary gate or
`./verify.sh --skip-godot` on a machine without Godot.

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
dotnet run --project \
  _Tests/UltraSim.UI.Tests/UltraSim.UI.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.Game.Admission.Tests/UltraSim.Game.Admission.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.Game.Persistence.Tests/UltraSim.Game.Persistence.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.Game.Inventory.Tests/UltraSim.Game.Inventory.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.S0.Integration.Tests/UltraSim.S0.Integration.Tests.csproj \
  -c Release
dotnet build \
  _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj \
  -c Debug
'/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' \
  --headless --path _Tests/UltraSim.S0.Godot.Tests \
  --scene res://S08HeadlessAcceptance.tscn
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
