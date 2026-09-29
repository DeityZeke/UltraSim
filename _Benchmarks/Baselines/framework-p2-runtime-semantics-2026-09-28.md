# P2 runtime semantics and neutral save-set foundation

Status: **accepted as a second P2 foundation pass; P2 remains ACTIVE**. This is not the dynamic C# server-authoring/compiler checkpoint.

## Lineage and environment

- Starting clean `main`: `d03a1f0c81a743f629dae2c82527543732cb1dae`.
- Tested implementation: `7c55a99c0c837d4fe1d27c018a56518b0969ace5` (four implementation/verifier commits from the start).
- Evidence is a later documentation-only commit; its SHA is reported in the handoff rather than embedded recursively in this file.
- Linux 7.2.6-1-cachyos x86-64, 16 visible logical processors; .NET SDK 8.0.125 and runtime 8.0.25 from `/home/nathan/.dotnet`; Godot 4.7.1 Mono. The system `/usr/share/dotnet` had .NETCore.App 8.0.31 but no ASP.NET runtime, so the canonical command used the installed local SDK/runtime.
- Canonical command: `env PATH="/home/nathan/.dotnet:$PATH" bash ./verify.sh --configuration=Release`.
- The uninterrupted canonical Release log was captured at `/tmp/ultrasim-p2-7c55a99c-release.log` during this run; SHA-256 `a806b71421032c72861e86876f24d94021fe0e586ae2806f4152c982135d5bf2`. The log path is transient, not a repository artifact.

## Implemented contracts

| Area | Accepted behavior |
| --- | --- |
| Package compatibility | `UltraSim.ServerModule/v2` uses an inclusive minimum and optional exclusive maximum version. The manifest-only fingerprint includes both bounds. v1 was an unshipped foundation and is rejected, not reinterpreted. |
| Classification | `EngineCore` and `FrameworkExtension` are explicit diagnostic package classes. A package is not a sidecar instance. |
| Runtime lifecycle | Built-in packages use deterministic Register → Configure → Initialize → Running; failed startup rolls entered packages back in reverse order, aggregates independent cleanup faults, and never publishes a partially Running composition. Stop is owner-thread-bound and idempotent after success. Retained contexts/capability handles are generation-fenced. |
| Exclusive vs contribution | Declared `Provides` remains exclusive. A distinct bounded `ModuleContributionId` admits multiple contributors without duplicate-capability conflict. No generic hook ordering or definition registry is claimed yet. |
| Sparse sidecars | `SidecarOwnerId` + independent `SidecarTypeId` + instance key supports two same-type instances on one owner, a default singleton key, and global/no-Entity ownership. The storage is finite and cold, not another ECS. |
| Save-set ownership | Generic contracts, coordinator and durable filesystem moved to BCL-only `Framework/Persistence`. `PersistentReferenceKey(domain, scope, local)` replaces the mandatory ECS key; `Server/Persistence/EcsPersistentReference` performs ECS-specific conversion. The on-disk save-set format/layout is unchanged. Staged namespace ownership fences unrelated providers and active-generation fallback. |
| Control Plane | Explicit opt-in bridge publishes host lifecycle and selected package rows. The RuntimeHost retires attached publications before package Stop; stale provider generations reject. Control Plane does not resolve package dependencies or own services. |

The no-ECS proof project references only Framework.RuntimeHost and Framework.Persistence. Its sample participant serializes owner, sidecar type, instance and value for two same-type sidecars in one bounded artifact; restore stages and validates before replacing live state. An unsupported artifact version rejects without changing live state, and compatible recovery chooses the earlier complete generation.

The retained ACC reference contributed the idea of sparse owner-attached modules and subsystem-owned serialization. This pass did **not** copy CLR `Type` as durable identity, one-module-per-Type, reflective hot construction, global static registration or per-system arbitrary save files.

## Verification

| Gate | Result on tested implementation |
| --- | --- |
| RuntimeHost focused suite | 12/12 pass: range/fingerprint/manifest rules, deterministic order, lifecycle rollback and aggregate faults, owner-thread/generation fences, same-type sidecars, exclusive/contributor distinction, explicit Control Plane publication and retirement. |
| Framework-only persistence proof | Pass: no ECS project reference; two same-type sidecars round-trip; unsupported staged state preserves live generation and prior complete recovery. |
| Existing Server save-set transaction suite | Pass, including crash boundaries and staged ECS namespace fencing. |
| Existing Game persistence suite | 13/13 pass; existing ECS/Accounting/world restore behavior retained. |
| Architecture-only | Pass: active project graph, assets, composition, documentation/links. Framework.RuntimeHost and Framework.Persistence have no internal project references; the Control Plane bridge references Framework.ControlPlane and Framework.RuntimeHost only. |
| Canonical Release | **PASS uninterrupted** on `7c55a99c` including ECS, Game, Network, native Godot, exported client products, F1.12 dogfood, mixed lifecycle, and native Online/bot swarm. |
| Export closure | Single Player 23 UltraSim assemblies/no Network; Online 13/client-only Network/no authority or Framework persistence; Complete 32/full Network. Official installed/absent/primitive controls and final composition parity pass. |
| Warmed direct service allocation | Four owner-thread 10,000-call windows after warming: `[0,0,0,0]` managed bytes, with zero RuntimeHost management calls. This proves no registered-host polling tax in that control, not a dynamic-package performance claim. |

Earlier exploratory Release attempts are not represented as passes: the system `dotnet` path lacked `Microsoft.AspNetCore.App`; a later non-isolated run failed in MSBuild `GenerateDepsFile`/`File.Create` (a standalone rebuild succeeded; its cause was not established); an implementation-tree run then exposed an outdated Single Player exact export-assembly allowlist after the new Framework assembly was introduced. The allowlist was corrected, while Online/bot exclusions were kept explicit. The final exact-tree run above passed without competing builds.

## Open P2 work and disposition

No production server yet loads these packages, and no source compiler, dependency/source-keyed cache, isolated dynamic assembly admission, definition alias/remap pipeline, typed Engine facets, or real compiled Contracts-only survivability proof is claimed. The next bounded P2 slice is server-owned `Scripts/` discovery, staged Roslyn compilation with source/dependency provenance, isolated loading into this same lifecycle, and a real edit→compile→register→initialize→Control Plane sample. P2 remains **ACTIVE**; P3–P11 and Gate 1 remain planned, and PF6 is not made ready by this foundation pass.
