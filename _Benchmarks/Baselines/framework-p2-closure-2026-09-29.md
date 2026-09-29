# P2 Runtime Host and C# Server Authoring — closure companion

**Disposition:** P2 **CLOSED** on the tested implementation `e6b6f7cdaf952204f09972d4000f82aedf7e8d32`. P3 is next and was not started here. PF6 remains **NOT READY** pending the later productization/identity gate. This companion does not rewrite the [earlier authoring foundation evidence](framework-p2-server-authoring-and-extension-terminology-2026-09-29.md).

## Lineage and environment

- Clean starting HEAD: `7f6f753d0d8c6d9758ae41c8a9c6509663a2f378`.
- P2 closure implementation/test/docs commit: `e6b6f7cdaf952204f09972d4000f82aedf7e8d32`.
- Canonical Release was run on that exact implementation commit. This later companion is evidence/documentation only; its commit is discoverable from this file's Git history.
- Linux 7.2.6-1-cachyos x86-64; AMD Ryzen 7 7800X3D, 16 visible logical processors; local .NET SDK 8.0.125 and .NET/ASP.NET runtime 8.0.25; Godot 4.7.1 Mono; Release. No CPU isolation or affinity was asserted.

## Bounded closure result

`UltraSim.RuntimePackage/v4` admits terminal package ID aliases. Aliases are validated before compile or authority startup: a selected package ID cannot also be an alias; two packages cannot own one alias; dependencies, ordering and conflicts resolve through the admitted canonical ID. Chains and cycles cannot form because an alias must terminate at a selected identity. Saved package inventory is remapped in selected plan order, with duplicate, missing, ambiguous and version-mismatched inventories rejected. This is a deterministic **plan**, not an automatic durable-data transformer.

Packages that extend durable save schema declare their participant service and artifact key. The exact selected schema-package inventory is persisted as a save-set ledger. On load, changed package identity/version, a missing participant, or a changed artifact key fails before authority publication; an alias alone never migrates stored bytes. Any future migration/drop must be explicit and independently verified. This closure does not implement a general data-transform execution engine.

The public `IEnumerable<IRuntimePackage>` host overload now enforces its package bound while copying, and binding acceptance compares exact semantic manifest declarations rather than requiring the same object reference. Source admission supports canonical bounded nested `.cs` paths (depth at most four, 64 source files and 64 directories per package); it rejects symlinks, unexpected entries and case conflicts, and includes relative paths in compile identity. The opt-in server-owned authoring path and previously accepted Package/Runtime Service/Object Extension split remain unchanged in purpose. `RuntimePackage/v3` manifests are deliberately rejected by the unshipped authoring format's v4 parser; sample manifests were updated. No Game, Network, content protocol or product policy was changed.

## Verification

| Control | Result on `e6b6f7cd` |
| --- | --- |
| RuntimeHost focused Release tests | **15/15 pass**, including alias resolution/conflict, deterministic remap, semantic binding and bounded admission. |
| Server.Authoring focused Release tests | **Pass**, including nested sources/cache identity, source relocation, cold/warm compile cache, durable schema removal/version/rename rejection, post-host admission rollback and Control Plane version/stage diagnostics. |
| Framework, Server and Game Persistence focused Release tests | **Pass**; Game Persistence **13/13**. |
| Real opt-in Direct Connect `Server/Scripts.Sample` cold/warm startup | **Both ready**; cold cache hits `0`, warm hits `3`; identical resolved fingerprint `2c0fd5baf2803b2d3d8984398b2c6b7300b04f8ecafdb933aee656803e2b372a`. The test used bounded `timeout`; its exit 124 follows the observed ready marker and is not a startup failure. |
| `env PATH="/home/nathan/.dotnet:$PATH" bash ./verify.sh --architecture-only` | **Pass**. |
| First `env PATH="/home/nathan/.dotnet:$PATH" bash ./verify.sh --configuration=Release` | **Fail**, exit 134 during Release Godot-test build at Network `GenerateDepsFile`/`System.IO.File.Create`; the emitted output did not expose the underlying exception. The exact standalone build passed immediately afterward, with one transient `MSB3026` retry saying a Network output DLL was in use. This suggests file contention but does **not** establish its cause. `/tmp` and the repository volume had ample free space. This failed attempt is retained as part of the result, not hidden by the rerun. |
| Second identical canonical Release command, no source changes or competing benchmark build | **PASS uninterrupted, exit 0**, through P2 focused suites, native Godot acceptance, Single Player/Online/Complete exports, F1.12 dogfood, contentless Online/native-bot controls, final composition parity and `git diff --check`. Verification scratch: `/tmp/ultrasim-repository-verification/394886`. |

The first canonical failure was transient on the same tree; the exact root cause remains unproven. The successful uninterrupted rerun supports P2 implementation acceptance, not a claim that arbitrary MSBuild environment contention has been repaired. The tool session output was not retained as a separate raw log; the commands, tested SHA, exit results and observed failure are recorded here.

## Scope and next work

P2 closes package identity compatibility **at admission**, bounded C# authoring, exact compile/load/runtime ownership, persistence participation, and Control Plane diagnostics. The dynamic compiler remains trusted server C#, not a sandbox or a hot-reload promise. DefinitionId/facet remaps and first-party Game-definition dogfooding are P4; polished control surfaces are P5; the Server Operator Product is P7. P3 is next but unstarted. PF6 is not ready solely by virtue of P2 closure.
