# F1.1 unified content scopes and stores — 2026-09-19

F1.1 is closed on tested implementation commit
`17b273122e7aa4fa78e0bc188ae97541f6416663` (starting HEAD
`cac114652d94ae0ac75ef72f769df4a096c9dcd1`). The machine-readable companion is
`client-f1.1-unified-content-scopes-2026-09-19.json`.

This checkpoint generalized the existing persistent content store; it did not change Game
authority, remote application proof, required Concept Demo actor packages, presentation fallback,
or renderer/UI behavior.

## Scope and storage design

`ContentScopeKey` is a finite immutable identity:

- `ContentScopeKey.Local` is the single reserved `world.local` authorization scope;
- `ContentScopeKey.ForRealm(ApplicationRealmId)` retains the exact authenticated Realm identity;
- the default key is invalid, constructors are private, and no string or filesystem path can
  manufacture Local or a Realm scope.

Local and Realm scopes share the same install, delta planning, manifest/package open, activation,
catalog publication/open, CAS, lease, quota, reachability, and retirement implementation. Existing
Realm-shaped overloads are compatibility adapters into that implementation. Remote protocol and
application-proof structures remain Realm-shaped and explicitly reject Local activation plans.

The accepted storage layout is:

```text
root/
    blobs/sha256/<prefix>/<hash>.blob
    scopes/local/manifests/
    scopes/local/activations/
    realms/<ApplicationRealmId>/manifests/
    realms/<ApplicationRealmId>/activations/
    metadata/access/
    staging/
```

The existing Realm paths and version-1 Realm activation bytes are retained unchanged, so accepted
remote caches reopen without migration. Local uses a new typed path and an otherwise impossible
all-zero encoded realm pair. It is never interpreted as an `ApplicationRealmId`. Blobs remain
global and immutable.

`ContentTrustClass.ScopeAuthorizedPassive` is an exact numeric alias for the accepted passive
class. Local packages pass the same canonical passive validation as Realm packages; Local does not
bypass trust validation, and user packs/executable extensions remain inadmissible.

## Permanent acceptance evidence

The Client.Content suite passed all 30 tests. New scope acceptance proves:

| Contract | Result |
| --- | --- |
| default scope / default Realm | rejected |
| reserved Local identity | canonical `world.local` |
| Realm A / Realm B identities | distinct and exact |
| Local package visible through Realm A before Realm install | no; `MissingManifest` |
| Realm A package visible through Realm B before Realm B install | no; `MissingManifest` |
| Realm-only package visible through Local | no; `MissingManifest` |
| shared-package physical blobs after Local + Realm A + Realm B installs | one per hash |
| scope census after isolation fixture | 3 scopes, 1 Local manifest, 3 Realm manifests |
| reused blob installs | at least 2 |
| Local and Realm logical resolved entries | exact equality |
| Local and Realm resolved fingerprints | intentionally distinct authorization/result identities |

The isolation fixture also installed a second Realm-only package. Its final physical census was two
blobs total, with exactly one physical file for the hash shared by all three scopes.

The warm-reopen/retirement fixture closed and reopened the store, then reopened both Local and
Realm activation catalogs with the exact prior fingerprints. Retirement while the Local catalog
lease was live failed with `CacheBusy`. After lease release, Local retirement removed one manifest
and one activation, Realm remained authorized, the one shared blob remained openable, and live
blob/catalog pins returned to zero.

The BuiltInTestWorlds suite passed all eight tests. `CabinVariantsInstallReopenThroughWorldLocalStore`
installed the Cabin Replace and Add packages through `ContentScopeKey.Local`, published all three
Default/Replace/Add catalogs, compared exact logical entries with the frozen oracle, closed the
store, and reopened all three catalogs. Its active metadata census was one scope, two Local
manifests, three Local activations, zero Realm manifests/activations, and zero live catalog pins
after lease retirement.

The frozen remote Cabin identities did not move:

```text
base     573b0486d7914f7640b109fcae10e0712c033844ca2325db889e0443adcd7a31
default  633eed18a9856474e43c4a6fb61db0bd80fbade8a40f6d884728b29024afb503
replace  f1f223d3c1ccf94560b8fbbb4d50bf036a98b043cb117ced63257ac4ecdd7f44
add      919d13d6572bf0df807891783d3ea4e25dea16010717df8a2223081ab1df8564
```

## F1.0 semantic/lifecycle comparison

The canonical verifier repeated the F0.7 sequence:

```text
Local -> menu -> Local -> menu
      -> Remote A -> menu
      -> Remote B -> menu
      -> Remote A reconnect -> menu
      -> Local -> menu
```

It reported the same F1.0 guardrail headline:

```text
local_chunks=49/25
local_batches=64/7
remote_cold_bytes=7669834
remote_warm_reused=3/6
remote_resources=1/2213268
retirement=clean
```

All six lifecycle boundaries were clean. Remote A/B/A used fresh application-session epochs;
Remote A reconnect reused the remembered exact Realm profile. Contentless Online cold/warm,
exported SinglePlayer/Online/Complete, Complete hosting, and the mixed exported-client swarms all
passed. The expected architectural change is only scope-aware cache metadata/diagnostics and the
new standalone Local persistent-store acceptance. The shipped Local Concept Demo still uses its
current `res://Assets` path, so its renderer, UI, actor, and gameplay counts remain the F1.0 control.

## Environment and verification

- CachyOS Linux 7.2.6-1-cachyos, x86_64.
- AMD Ryzen 7 7800X3D, 16 logical CPUs.
- .NET SDK 8.0.131; canonical runtime 8.0.31; Godot 4.7.1 Mono.
- `./verify.sh --architecture-only`: pass (4/4).
- `dotnet run --project _Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj -c Release`:
  pass (30/30).
- `dotnet run --project _Tests/UltraSim.Client.BuiltInTestWorlds.Tests/UltraSim.Client.BuiltInTestWorlds.Tests.csproj -c Release`:
  pass (8/8).
- Network Application tests, including Local-plan remote-projection rejection: pass.
- `./verify.sh`: pass, ending in `PASS repository verification`; no required native/export gate was
  unavailable.

## Deliberately deferred

F1.2+ did not begin. Concept Demo actor Add/Replace packages remain Required, actor art did not
become optional, semantic activation/application proof did not change, no presentation precedence
or `PresentationGenerationId` behavior was added, Local X/Y assets remain export-packaged, and no
resource-pack, atlas, UI, camera, renderer, or Official-asset productization work was introduced.

The current one-writable-root store remains. A later read-only install-root overlay is not blocked
because authorization ownership is now a typed scope rather than a physical root, but the overlay
itself is not part of F1.1.

F1.1 has no open closure blocker. F1.2 is next/planned.
