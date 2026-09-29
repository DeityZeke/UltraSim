# P4 — Engine Contracts and typed Definitions

Status: **P4 CLOSED for its scoped architecture and verification criteria; P5 NEXT.** This does **not** close the separately reproduced warm Official-content native Finalizer crash or make PF6 ready.

## Lineage and environment

- Starting clean `main`: `46dfbf1648eb8e01f574a7dde1f40c7d108bd93e` (P3/AIOS closure).
- Implementation: `0ebd4c883390a834aa474ecf05a1afa5b46bad0c`.
- Exact Single Player/Complete export-order assertion correction: `9c0ca15b3393a3d1dcb2cad8f2993e55adbbd9cd`.
- Exact headless bot closure correction and **canonically tested implementation**: `137289c9c195a42f78555b72d5c61d1515b1c6b9`.
- Evidence/documentation is a later commit; the implementation SHA above is the full-Release tested source tree.
- Release, .NET SDK 8.0.131, `Microsoft.NETCore.App` and isolated `Microsoft.AspNetCore.App` 8.0.31, Godot 4.7.1 Mono, CachyOS Linux 7.2.6 x86-64. The system ASP.NET runtime was missing; a matching distro package was unpacked under `/tmp` and placed on `PATH`/`DOTNET_ROOT` for verification. No system runtime installation or production source workaround was made.

## Implemented boundary

`Framework.Definitions` owns canonical stable IDs, package/capability/facet/tag identities, a bounded cold `DefinitionComposer`, immutable catalogs, generation-fenced dense handles, typed facet/tag tables, single-hop same-owner aliases, and deterministic cold diagnostics. Maximums are 4,096 Definitions, 64 facet families, 32 facets/Definition, 256 tags, and 8,192 memberships. Duplicate or conflicting contribution, missing required capability, ambiguous/chain alias, and malformed identity reject before publication. Optional tags to absent Definitions remain diagnostic-only unless explicitly required.

Separate Items, Combat, Skills and Presentation Contracts own their facet types and builder extensions. No Inventory↔Combat Core lateral reference was introduced. The first-party Iron Sword uses the public composer with seven facets—Item, Inventory, Equipment, Combat, Durability, Skills, Presentation—and two tags. Current Game derives its existing prefab/inventory realization from the typed values. Skills meaning remains authored while its optional runtime Core is unselected. `StableDefinitionId` is not `PrefabKey`; the currently equal text is not an identity equivalence rule.

The public `SemanticPresentationReference` is domain + Definition + role, with bounded fallback dimensions in the representative public descriptor. It contains no private Combat/Durability/Skills facts or file path. `ClientPrimitivePresentation` consumes this descriptor without optional art. The existing ECS item-instance save still persists its distinct prefab provenance and rolled state; a Framework save-set test persists a first-party Iron Sword **Definition reference** by stable ID and resolves it after fresh composition to a different dense handle. Generic alias tests prove old-to-current identity resolution and fail-closed duplicate/missing cases. A payload-schema migration beyond identity remap remains explicit work, never implied by aliasing.

The compiled `Server/Scripts.P4` proof loads two Contracts packages, an optional Combat Core and a Definition script through Server Authoring. Removing Core leaves the Combat facet data and stable identity valid but marks runtime behavior inactive; an explicitly required variant rejects before authority. Built-in Game and compiled scripts use the same public `DefinitionComposer` API. P4 does not add a global Definition service locator, a new wire format, Inventory portraits, or later P5+ surfaces.

## Controls and results

| Control | Result |
|---|---|
| New Definition tests | Pass: stable/dense identity, typed contributions and tags, multi-package set membership, aliases/conflicts/bounds, optional/required Core, save-set, warmed zero allocation |
| Warmed lookup sample (200,000 iterations) | stable→handle 81.2 ns, typed facet 7.1 ns, dense tag 4.7 ns on one unprofiled run; diagnostic micro-samples, not product p95 |
| Game Inventory | 36/36, including actual Iron Sword seven-facet binding and fresh-composition stable-ID save-set |
| Server Authoring | Pass, including compiled P4 Contracts/Core/Definitions removal/required-Core proof |
| RuntimeHost / Framework, Server and Game Persistence | 15/15 RuntimeHost; Framework, Server save-set and 13/13 Game Persistence pass |
| Client Core, Content, UI, World and existing Inventory behavior | Focused Client Core 36/36; broader suites and native routes passed in final canonical run |
| Architecture-only | Pass |
| Focused PF1 exported bot/native swarm after closure correction | Pass |
| Final `./verify.sh --configuration=Release` on `137289c9` | **Uninterrupted PASS**: physical Single Player/Online/Complete exports, contentless Online, F1.12 local and mixed dogfood, native Godot, Direct Connect, bot/impairment, final edition/composition parity |
| Main worktree at evidence drafting | Clean; only this evidence/doc update pending |

Reproduction: `./verify.sh --architecture-only`; `dotnet run --project _Tests/UltraSim.Framework.Definitions.Tests/UltraSim.Framework.Definitions.Tests.csproj -c Release`; `dotnet run --project _Tests/UltraSim.Server.Authoring.Tests/UltraSim.Server.Authoring.Tests.csproj -c Release`; `dotnet run --project _Tests/UltraSim.Game.Inventory.Tests/UltraSim.Game.Inventory.Tests.csproj -c Release`; `./verify.sh --configuration=Release` (requires .NET 8 ASP.NET runtime and repository Godot 4.7.1 Mono configuration).

## Adverse-run preservation and open product finding

The first canonical attempt stopped before Server Hosting because this machine lacked `Microsoft.AspNetCore.App` 8. After isolating the runtime, an attempt found the Single Player exact assembly-order assertion; that P4 verifier expectation was corrected. A later attempt stopped in the existing F1.8A 50 ms/jitter/loss motion impairment case; a focused Network Application rerun passed all tests, and no motion assertion was altered. The next attempt reached warm Official-content export and exited 139. A subsequent attempt passed exports and mixed lifecycle but found the bot dependency expectation; that P4 closure list was corrected and focused PF1 passed. The final implementation received one uninterrupted canonical Release pass. Failed attempts were not reclassified as passes.

The warm native failure is **confirmed and independent of P4 introduction**: the exact `46dfbf16` pre-P4 export and `9c0ca15b` P4 export each produced 1 SIGSEGV in 10 independent warm Official Single Player launches after a cold fill. The canonical P4 attempt also crashed in that warm stage. Coredumps `581402`, `581878` (P4) and `584354` (pre-P4) show `.NET Finalizer`, `pthread_mutex_lock`, and the same Godot executable offset `+0xb55d75`. `/tmp` had 14 GiB free and memory 17 GiB available at inspection. This does not prove the remaining finalizable type or repair it. It reopens native-lifetime assurance for the product; no exception suppression, sleep, forced GC, or verifier weakening was used. P4's architectural closure must not be read as PF6 readiness.

P5 is the next roadmap checkpoint. P6 portrait realization, general Definition wire disclosure, art/resource-pack changes, and UI/control-surface work were not started here.
