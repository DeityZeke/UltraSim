# P2 server C# authoring and Extension terminology — foundation evidence

**Disposition:** the compiled server-authoring foundation passes its focused and canonical Release gates. P2 remains **ACTIVE**; this does not close P2, start P3, or make PF6 ready. This companion supersedes the *terminology and pending-authoring description* in the [earlier P2 foundation record](framework-p2-runtime-semantics-2026-09-28.md); it does not rewrite that historical record.

## Lineage and environment

- Clean starting `main`: `5e381ed278431cf6c48be4a0a617888c50bcf70f`.
- Package/service/Extension implementation: `bda7a773`.
- Server authoring, actual Direct Connect opt-in, documentation, and tests: `98586fbe6f50c86f5fb8a2f849973ba18bae64f0`.
- Tested implementation HEAD: `98586fbe6f50c86f5fb8a2f849973ba18bae64f0`. This companion is a later evidence-only commit and does not change the tested implementation.
- Linux 7.2.6-1-cachyos x86-64, AMD Ryzen 7 7800X3D (16 visible logical processors), .NET SDK 8.0.125 / .NET and ASP.NET runtimes 8.0.25 from `/home/nathan/.dotnet`, Godot 4.7.1 Mono, Release. No CPU affinity or isolation was asserted.

## Corrected architectural vocabulary

| Canonical term | Meaning |
| --- | --- |
| Runtime Package | Authored/deployed compilation and composition unit. Stage/class, ID/version, dependencies, declarations, and optional entrypoint belong to the package. |
| Package capability | Declarative `Provides`/`Requires`/`Optional` relationship; it does **not** imply a live runtime service object. |
| Engine Core | Domain engine owned by a package; neither the package itself nor an Object Extension. |
| Runtime Service | Explicit live object declared separately under `services`, provided by a lifecycle owner, and reached by a generation-fenced typed handle. |
| Object Extension (Extension) | Sparse owner/global attached managed state or behavior outside ECS component storage; bounded typed `ExtensionRegistry`, not a package. |
| Contribution | Bounded multi-contributor proof distinct from exclusive services; not yet a universal hook system. |

The ACC archive's historical “Module” corresponds to an UltraSim **Extension**, not a Runtime Package. “Module” and “Sidecar” are no longer current UltraSim names for these concepts. Package classes use concise names (`Runtime`, `Kernel`, `Schema`, `Framework`, etc.), not “...Extension”. The separately qualified `ContentTrustClass.ExecutableExtension` remains a client-content security term.

## Implemented boundary and assurance

`UltraSim.RuntimePackage/v3` separates composition capabilities from declared live services. A compiled Contracts-only package may be selected and `Available` without inventing an entrypoint or service. The planner checks bounded IDs, version ranges, stage edges, declared relations, duplicate exclusive services, and cycles before code executes. One host lifecycle owns selected entrypoints in deterministic Register → Configure → Initialize order; failure rolls back entered owners, preserves the previous authority, and retains independent cleanup failures. Runtime service handles and package contexts remain generation-fenced. The Control Plane bridge reports both the manifest-only and resolved runtime composition fingerprints, package state, compile key and cache status; it does not become the package planner.

`Server/Authoring` is a **server-owned opt-in** cold adapter using Roslyn 4.5/C# 11. `Framework.RuntimeHost` has no Roslyn, filesystem, Game, Network, or ECS dependency. Source admission accepts only `Scripts/<canonical-package-id>/manifest.json` and top-level `*.cs`; ordinal deterministic discovery rejects nested/unexpected files, case conflicts and symlinks/reparse paths. Limits are 128 packages, 64 source files/package, 256 KiB/package and 2 MiB/composition. Compile identity hashes exact manifest bytes, ordered relative paths/source hashes, version, admitted dependency/reference hashes, compiler options and runtime architecture; it excludes timestamps, absolute source paths and host generation. Private cache publication follows successful compile; cached images are checked before reuse and corruption recompiles. A composition load context binds canonical host assemblies to Default and selected compiled images to admitted identities. This is dependency/lifetime fencing for trusted server C#, **not a code sandbox** or remote-code admission.

The opt-in Direct Connect `--scripts-root` / `--scripts-cache` path composes before Concept Demo authority and Network startup. The real sample has `sample.contracts` (compiled Contracts-only, no entrypoint), `sample.core` (optional Core with runtime services, Control Plane provider and non-ECS save-set participant), and `sample.scripts` (Scripts consumer). Removing Core leaves the consumer's Contracts dependency valid; making the service required fails planning before authority. The save participant uses Framework Persistence's Prepare/Resolve/Validate/Commit path and does not serialize individual Extensions as independent save sets. This path does not change Concept Demo Game semantics, add dynamic Game hooks, or poll compiler/cache on authority ticks.

## Verification on the exact implementation HEAD

| Gate/control | Result |
| --- | --- |
| `dotnet run --project _Tests/UltraSim.Framework.RuntimeHost.Tests/UltraSim.Framework.RuntimeHost.Tests.csproj -c Release` | **13/13 pass**; declarative Contracts-only package, separate exclusive service relationships, typed Extension registry, lifecycle rollback/fences, Control Plane publication, zero warmed built-in service allocation (`[0,0,0,0]` bytes over four 10,000-call windows). |
| `dotnet run --project _Tests/UltraSim.Server.Authoring.Tests/UltraSim.Server.Authoring.Tests.csproj -c Release` | **Pass**: deterministic bounded discovery; cold/warm cache and stable resolved fingerprint; exact source-edit invalidation; corrupt cache recompile; compiled Contracts-only survival and required-Core rejection; bounded compiler diagnostics; unsafe source rejection; stage compile failures; constructor/Register/Configure/Initialize/publication/persistence failure rollback; real Control Plane retirement and non-ECS save recovery. Three warmed direct-service 10,000-call windows allocated `[0,0,0]` managed bytes. |
| Framework.Persistence, Server.Persistence, Game.Persistence | **Pass**; Game Persistence **13/13**. Existing save-set transaction behavior remains green. |
| Direct Connect with `Server/Scripts.Sample` | **Pass** cold and warm; same three selected packages, warm `cache-hits=3`; actual server starts after composition and retains Game protocol v33. |
| `env PATH="/home/nathan/.dotnet:$PATH" bash ./verify.sh --architecture-only` | **Pass**. |
| `env PATH="/home/nathan/.dotnet:$PATH" bash ./verify.sh --configuration=Release` | **PASS uninterrupted**, exit 0, on exact `98586fbe6f50c86f5fb8a2f849973ba18bae64f0`. Includes P2 tests, architecture, existing Game/Network/hosting/integrated tests, native Godot acceptance, physical Single Player/Online/Complete exports, F1.12 product dogfood, contentless Online and native/bot controls, final composition parity, and `git diff --check`. |

The canonical verifier's exported-product checks passed with Single Player's Network excluded, Online's local authority excluded, and Complete's full composition retained. No unrelated product protocol, Game, Network, or client behavior was intentionally changed by P2. The canonical run was executed without a competing build. Its `/tmp/ultrasim-repository-verification/329171` scratch path is ephemeral, not permanent evidence; this artifact records the command, tested SHA and exit outcome.

## Open scope

P2 remains **ACTIVE** for the roadmap's alias/remap pipeline, typed authoring registrations/facets/hooks, operator diagnostics integration, and first-party Game dogfooding. The current opt-in compiler does not claim arbitrary hot reload/unload, proven collectible reclamation, malicious-code isolation, or a shippable general server scripting API. P3–P11 and Gate 1 remain planned; PF6 remains not ready. The older P2 report's “no source compiler yet” statement describes its earlier tested tree, not this one.
