# P1 Framework Control Plane — implementation and unresolved canonical gate

**Disposition (2026-09-27): P1 ACTIVE, implementation present; not closed.** P2 and PF6 were not started. The P1-specific contracts and owner adapters pass their focused tests and the architecture gate, but the required uninterrupted canonical Release verifier did not pass. Its exported Single Player warm Official-content control intermittently segfaults in the native Godot/.NET finalizer. The same failure and native stack occur with an unchanged pre-P1 Single Player export. This excludes P1 source as a necessary trigger, but does **not** explain or repair the native failure.

## Lineage and environment

- Actual clean `main` at task start: `fe57d9295fd4ff149ff1f96f849ca0fe6bca434a`.
- During implementation, legitimate intervening documentation-only commits advanced `main` to `9c3bb78a47ff95ccc8ce0f2750ada535df15587e`; they were preserved. The Gate 0 production closure remains `e3a63ea21f8f5ea4fa69e3768a055c409edc8616`.
- Initial P1 implementation: `2efff975cfa40f3fbdf2746f1fca6a24d72f513d`. Owner-boundary/authorization hardening and final tested source: `942398c8378fa41c07f4f6ead0ff21d74203fc32`. This report's evidence commit is its own `git log -1 -- _Benchmarks/Baselines/framework-p1-control-plane-2026-09-27.md`; it does not change the tested source.
- Linux 7.2.6-1-cachyos, AMD Ryzen 7 7800X3D (8 cores/16 threads), .NET SDK 8.0.131 / runtime 8.0.31, Godot 4.7.1 Mono, Release. The tests use in-process real ECS/Runtime/Settings owners; the export control launches separate actual Godot client processes. No benchmark affinity or isolated CPU reservation was applied.

## Implemented P1 boundary

`Framework/ControlPlane` is BCL-only and owns bounded canonical IDs, six exact `ControlValue` kinds (Bool, Int64, UInt64, finite Float64, Text, Option), typed schemas, immutable descriptors, separate owner state, requested/effective/pending and per-value provenance, apply/persistence vocabulary, trusted-capability/audience checks, generation-fenced registration/retirement, revision checks, provider-local batches, actions, bounded collections and audit observations. The generic viewer has no ECS-type branches. There is no global settings store, cross-provider transaction, UI framework, module host, admin network protocol or hot-path publication.

Bounds: 64 providers; 512 publications/provider; 1–96-character canonical IDs; category depth 8/length 192; text 1,024; options 32; batch 32; action arguments 16; collection columns 24 and rows 1,024. All input sequences are bounded while copied. Authorization precedes generation disclosure for known publications; denied/malformed requests do not reveal provider generation/revision. Unexpected owner failures remain observable and are not converted into ordinary rejection.

Opt-in adapters sit above the Framework: `Bridges/ControlPlane.ECS`, `Bridges/ControlPlane.ServerRuntime`, and `Client/ControlPlane`. ECS Kernel has no Control Plane reference. The ECS provider binds a frozen maximum-120-system graph, publishes public runtime snapshots and seven separate read-only cold fact columns, and exposes only idle-owner Enabled/Period/Offset/RunMode controls. It serves a last-idle snapshot and refuses writes during graph execution. ServerRuntime publishes an owner-thread cold snapshot and respects fixed versus variable playback/frequency policy. Client Settings still resolves and persists through its existing document/FileStore; its seven representative controls preserve global/Realm provenance, recommendation consent and next-entry vegetation pending state. A full 128-entry Realm table cannot prevent an independent device-global vegetation edit or create an empty Realm scope.

The accepted export-composition verifier found no Control Plane adapter assembly in Single Player, Online or Complete. The only changes to source assemblies also present in the Single Player product are a narrow read-only ECS execution-state probe and a read-only ServerRuntime snapshot. No Game, Network, renderer, client-content or product settings semantics were changed.

## Tests and observed work

| Gate | Result on `942398c8` |
| --- | --- |
| Focused Control Plane | **19/19 pass**: typed values/limits, registration, authorization, stale generations/revisions, batch/action/audit, collections, viewer retirement, ECS/Runtime/Settings owners, graph-execution refusal and Settings scope saturation. |
| Existing ECS | **221/221 pass** in canonical partial run. |
| Existing Server Runtime | **25/25 pass** in canonical partial run. |
| Existing Client Core / Content | **36/36 / 38/38 pass** in canonical partial run. |
| Architecture | `bash ./verify.sh --architecture-only` **pass**: dependency boundaries, built-in presentation assets, export composition, documentation links. |
| Integrated acceptance | **39/39 pass** before native export failure. |
| Canonical Release | **not passed**; see exact attempt lineage below. |

The generic ECS panel proof rendered 3 systems across Movement, WorldSim and Cleanup stages with public run counts, timings, work items and reserved bytes; cold thresholds/partition/policy are typed Collection cells, not writable controls. Registering an idle provider added **zero provider observations** over 20,000 ECS ticks and **zero warmed managed bytes** in both plain/registered cases. Those unpaired tick timings are diagnostic only, not an asserted latency win. The provider contains no per-tick hook.

## Canonical attempt lineage and blocker

1. The first `2efff975` run was intentionally **interrupted**, not passed, after finding Settings scope-capacity, authorization-order and ECS graph-boundary guardrail defects. The defects were repaired in `942398c8`; its raw log remains `release-1.log`.
2. The first uninterrupted `942398c8` run passed the focused and integrated gates but failed at actual Single Player product copying with `System.IO.IOException: No space left on device`. `/tmp` was a 16 GiB tmpfs with 0 free bytes; the repository volume had more than 800 GiB free. This run's exact scratch directory was moved intact to task-owned `.output/p1-controls/failed-release-scratch-2811942`, freeing approximately 412 MiB. No older or unrelated scratch was removed. Raw log: `release-final.log`.
3. A rerun with a dedicated repository-volume `TMPDIR` still failed immediately with child `dotnet` exit 135/SIGBUS while `/tmp` remained full. The earlier task-owned scratch directory was moved intact to `.output/p1-controls/interrupted-release-scratch-2780811`, freeing approximately 830 MiB. A direct Network probe then passed. Raw log: `release-final-tmpdir.log`.
4. The next uninterrupted `942398c8` run used isolated disk-backed scratch. It passed P1, architecture, integrated acceptance, Godot native acceptance, and the first exported Single Player Official-content control. Its **second identical warm-content launch exited 139/SIGSEGV** before the client-ready line. The verifier correctly failed. Raw log: `release-final-isolated.log`.

The finalizer coredump for current product PID `2859884` reports TID `.NET Finalizer`, `pthread_mutex_lock`, native executable offsets `0xb55d75 → 0x2c47b24 → 0xb4a5dc`, then `libcoreclr`. A pre-P1 export from the unchanged `9c3bb78a` worktree reproduced the same finalizer signal and native offsets (PID `2870188`) when its binary was placed under the current repository path. The pre-P1 source has no P1 Control Plane code. A minimal exported composition smoke passed 50/50, while representative warm Official-content batches varied: current deep-path export 6/10 pass, current short-cache export 9/10, current short-executable export 7/10, and copied pre-P1 export 6/10. Cold current Official-content controls passed 5/5. These small controls do **not** prove that warm content causes the crash, that path length causes it, or that the native lifetime bug is in UltraSim rather than Godot/.NET. They prove the failure is real, intermittent and not introduced solely by P1 source.

The exact task-owned raw logs are in [framework-p1-control-plane-2026-09-27.raw](framework-p1-control-plane-2026-09-27.raw); machine-readable outcomes are in the companion JSON. Coredumps remain available through `coredumpctl info 2859884` and `coredumpctl info 2870188` on this host. Do not erase or reinterpret the failed canonical runs as passes.

## Next decision

P1's implementation/architecture proof is ready for review, but its explicit canonical Release exit criterion remains unmet. Investigate the pre-existing exported Single Player finalizer failure with a managed/native stack or deterministic lifetime control, repair causally without weakening the Official-content journey, and rerun one uninterrupted canonical Release verifier. Until then: **P1 ACTIVE; P2 held/not started; Gate 1 planned; PF6 planned/not ready/not started; W8/W9/W10 not started.** P1 did not begin P2 or P5 product UI.

Reproduction commands (from the main repository, substituting a clean disk-backed temporary directory for `<scratch>`):

```bash
dotnet run --project _Tests/UltraSim.ControlPlane.Tests/UltraSim.ControlPlane.Tests.csproj -c Release
bash ./verify.sh --architecture-only
TMPDIR=<scratch> PATH="$PWD/.output/f112-r3-runtime:$PATH" DOTNET_ROOT="$PWD/.output/f112-r3-runtime" bash ./verify.sh --configuration=Release
```
