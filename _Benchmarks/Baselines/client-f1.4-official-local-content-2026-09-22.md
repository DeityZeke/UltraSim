# F1.4 — Official Local content productization

Accepted 2026-09-22. Starting HEAD
`132b070c5d04f3225ca78f5c124e6adc9e6d7035`; tested implementation commit
`576828eeceb8ccf5afcba2fce7ab4dc783261d2a`. The final closure HEAD is the commit
that owns this evidence and is reported with the closure; a Git commit cannot embed its own hash.
F1.0–F1.4 are closed, F1.5 is next, and F1.5+ was not implemented.

## Product boundary

Primitive presentation, the minimum shell/fonts, and code remain permanent client substrate.
Optional Official humanoid presentation is now one deterministic external package,
`Content/Official/ultrasim-official-actors-v1.uspkg`, installed and reopened as ordinary
`world.local` content. The old `res://Assets` mount, X/Y wrapper scenes, and direct Local actor
loader are gone. Every Godot export preset excludes `Assets/**`; physical Single Player and
Complete exports contain no X/Y GLB or wrapper-scene filenames in their embedded PCK/managed
resources. Online contains no Official package at all.

The exact package identity is:

| Field | Value |
| --- | --- |
| Package ID | `official.world-local.actor-presentations` |
| Archive | `ultrasim-official-actors-v1.uspkg` |
| Archive bytes | 4,723,829 |
| Archive SHA-256 | `03f77dd361bdf7c9edf564fc92c95ced31aa71378a5d14555cc670b1cda6c554` |
| Manifest hash | `e4b767436784db6c42447d4fbbcf58e0c653bd168e81582fa6f1d1962821aaad` |
| Activation plan hash | `e6fd8000f009acf952e5f024b28e3a130fd72085e042b97a70ef36bd02b5192a` |
| Scope | `ContentScopeKey.Local` (`world.local`) |
| Definitions | two `ActorPresentation` Add definitions: `actors/human-x.glb`, `actors/human-y.glb` |
| Payload bytes | 4,723,368 |

The sole canonical runtime-art sources remain
`Client/PresentationAssets/Avatars/x_bot/x_bot_animated.glb` (2,213,380 bytes,
SHA-256 `2704d59bee5f8046f335d7e4da05fb8f4f3c7c8f30cdf1e636c4de75ff3b5833`) and
`Client/PresentationAssets/Avatars/y_bot/y_bot_animated.glb` (2,509,988 bytes,
SHA-256 `553fb71ebe61caede890e582b93a37335ff554f1bb907c034658c5f434e6a567`).
The deterministic builder validates those exact identities and passive humanoid profiles before
canonical archive encoding. The Concept Demo server consumes the same archive when authoring Realm
packages; no Client.Content-to-Server dependency or third source copy was introduced.

Local realization changed from a Godot-only `res://Assets/*.tscn` shortcut to:

```text
read-only exact Official archive
    -> LocalContentPackageSource
    -> writable ContentCache/global CAS
    -> ContentScopeKey.Local activation/catalog lease
    -> PresentationGeneration
    -> GodotContentActorVisualResolver
    -> GodotActorPresenter
```

The archive path is discovery only, not trust. `OfficialLocalPresentation` owns the finite compiled
package, manifest, activation, file hashes, sizes, and logical IDs. A changed archive at the expected
path is rejected without publishing Local metadata. The installation directory needs only read and
execute permission; CAS, authorization metadata, and activations live in the writable user cache.

Distribution-source removal, authorization retirement, and blob eviction are deliberately distinct.
Once acquired, the exact persistently pinned Local activation reopens without the distribution
archive. Removing the archive does not silently revoke it. Explicit Local scope retirement removes
authorization metadata; ordinary reachability-aware eviction can reclaim bytes only after no scope
reference or live catalog/blob/native lease remains. F1.4 adds no live uninstall UI or pack manager.

## Local-content classification

| Class | Current F1.4 ownership |
| --- | --- |
| A — permanent renderer/bootstrap | Primitive descriptors/realizers, minimum shell resources and fonts, renderer/client code |
| B — Game/code authority | `LocalPlayableSession`, local authority, Physics, Inventory, Chat, simulation and admission/runtime assemblies |
| C — required Local semantic content | Concept Demo WorldPack/world truth remains produced and bound by Game/local-authority code; compiled Standard UI remains a client capability; required semantics are not fabricated by Primitive |
| D — optional Official presentation | X/Y canonical GLBs and their two actor-presentation definitions, now the removable external package |
| E — test-only fixture | `CabinContentOracle` Default/Replace/Add packages and literal acceptance variants |

Static/Multi/public geometry continues to realize disclosed semantic WorldPack facts through the
F1.3 bounded primitive/batch substrate. It is not a second high-quality art package. Fully
productizing the Local semantic WorldPack and informational UI would cross the remaining
world-authoring/capability seam and was deliberately not faked in F1.4. `Client.StandardUi` was not
misclassified as optional art.

## Physical acceptance matrix

The canonical verifier built and inspected actual Linux exports. Each exported executable was
73,630,168 bytes. Single Player and Complete placed the exact archive beside the executable; Online
reported zero bundled WorldPacks and zero bundled packages. The same Single Player executable was
copied unchanged for installed, absent, and restored-source runs; verifier hash equality passed.

| Product / mode | Result |
| --- | --- |
| Single Player, clean Official install | PASS: 2 actors, 1 animated/content-backed, 1 primitive, 2 Local selections, 2 CAS blobs, 4 blob pins / 1 catalog pin |
| Single Player, warm reopen | PASS: exact persisted activation reopened; same presentation/game result |
| Single Player, Official physically absent and clean cache | PASS: same executable and semantic world; 2 actors, 0 animated, 2 primitive, 0 Local selections, 0 blobs/pins |
| Single Player, Official restored | PASS: same executable; high-quality Local presentation restored on the next activation |
| Complete, Local Official installed | PASS: same 2/1/1 actor result and 2 Local selections |
| Complete, Local Official absent | PASS: same executable; 2 primitive actors and no Local selections |
| Complete, hosted Local + two exported remotes | PASS: packet-free Local authority path survived; two remote products joined and retired cleanly |
| Online, no Official | PASS: physical contentless export; Realm content acquired normally; `official=Unavailable`, Realm selections 2, Local selections 0 |
| Online, Official available | PASS: Local activation installed/reopened but authenticated Realm presentation won (Realm 2, Local 0) |
| Online, changed server profile/reconnect | PASS: Local reopened, Realm still won, no refetch, no Local authority gained |

Realm -> `world.local` -> Primitive precedence therefore remains exact. Local possession does not
satisfy Realm authorization or the application proof. The Content acceptance installed the same two
blobs first through Local and then through an independent Realm: the global CAS stayed at two blobs,
the Realm install reported two reused blobs, and Local/Realm manifest metadata remained separate.
Realm could not open the Local manifest before its own authorization. A read-only source install and
a warm reopen with no source also passed.

## F1.3 and semantic comparison

The Local semantic catalog fingerprint remains
`633eed18a9856474e43c4a6fb61db0bd80fbade8a40f6d884728b29024afb503`.
Game manifest v31, Network transport revision 2, authority ownership, Inventory, Chat, interaction,
actor/public identity, remote application proof, and edition capability closure did not change.
F1.3 primitive-only remains 49 resident / 25 rendered chunks, 2/2 primitive actors,
64 static + 7 public + 71 fallback groups, and 177 nodes / 99 resources. The ordinary installed
F1.4 Local path adds the intended Local activation/CAS/pins and realizes the active human through
content; physical absence produces the same primitive behavior rather than a resolver-suppression
simulation.

F0.7 mixed lifecycle passed Local -> Local -> Remote A -> Remote B -> Remote A reconnect -> Local.
After an earlier Local session, the persistent cache intentionally contains exactly two blobs, one
Local manifest, and one Local activation at the first Remote open, with zero live blob/catalog pins.
All transient session presentation, native resources, profiles, and remote state still retire at
each boundary. Persistent cache contents are not misreported as lifecycle residue.

## Verification and reproducibility

Host: CachyOS Linux `7.2.6-1-cachyos` x86_64; AMD Ryzen 7 7800X3D (8 cores / 16 logical
processors); .NET SDK 8.0.131, runtime 8.0.31; Godot
`4.7.1.stable.mono.official.a13da4feb`.

Commands included:

```sh
./verify.sh --architecture-only
dotnet run --project _Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.Client.BuiltInTestWorlds.Tests/UltraSim.Client.BuiltInTestWorlds.Tests.csproj -c Release
# native T0.10 and product export/smoke controls through the maintained verifier
./verify.sh
```

Focused Client.Content passed 32/32; BuiltInTestWorlds passed 8/8; native T0.10 passed.
Architecture-only passed. One uninterrupted final canonical Release run at the tested implementation
commit exited zero with `PASS repository verification`, including physical Single Player/Online/
Complete exports, installed/absent/restored Official controls, contentless Online cold/warm/profile
controls, Complete hosting, PF1/PF2 mixed exported/headless swarms, F0.7 lifecycle, and final
`git diff --check`.

Earlier closure attempts exposed two verifier/build issues that were repaired rather than normalized:
edition-switched Godot exports reused a stale root NuGet restore graph, so the export script now
forces the exact edition/flavor restore; and the F1.0 lifecycle census incorrectly required an empty
cache after Local had legitimately installed Official content, so it now checks the exact persistent
Local metadata and zero live pins. One unrelated Server Hosting wrapper-ratio run failed amid host
noise and passed immediately in isolated repetition (23/23, ratio 0.989) and in the final canonical
run (ratio 1.002). A stale Godot process from a prior run was terminated before the authoritative
run. The previously documented shared-authority convergence failure did not recur.

The implementation commits are:

- `e81f050113858e2b978b36a20c22ed8ec7cd9e0a` — productize Official Local actor content;
- `e2905575e7d55756903bd8e425d5c5622f0b0cb6` — harden edition-switched Godot exports;
- `576828eeceb8ccf5afcba2fce7ab4dc783261d2a` — update lifecycle census for Official Local content.

No F1.5 user packs, package-manager UI, live enable/disable/update, arbitrary overrides, atlas/native
derived cache, Gameplay Frame, UI rework, camera work, prediction, RenderingServer rewrite, actor
batching, or vegetation work was started.
