# F1.5 — User presentation overrides and safe presentation generations

Accepted 2026-09-22. Starting HEAD
`ff2f2cbfae1e721926de4b1eba0e7834b48402fb`; tested implementation commit
`ac8d3601e010bc44d5775c84e72cd2b4f79ca60f`. The final closure HEAD is the commit
that owns this evidence and is reported with the closure; a Git commit cannot embed its own hash.
F1.0–F1.5 are closed, F1.6 is next, and F1.6+ was not implemented.

## Ownership, identity, and storage

`ContentTrustClass.UserResourcePack` is now a supported but deliberately separate client-local
presentation trust path. A pack identity is the exact immutable pair
`UserResourcePackIdentity(ContentPackageId, ManifestHash)`. It is neither a `ContentScopeKey` nor an
`ApplicationRealmId`, and it never participates in Local/Realm activation, catalog resolution, or
application proof.

User manifests live under `resource-packs/manifests/<manifest-hash>.manifest`; the canonical bounded
enabled order lives at `resource-packs/selection/enabled.selection`. Payloads use the existing global
`blobs/sha256` CAS, so byte possession is shared while authorization remains separate. Pack discovery
does not enable anything. Only an explicit rooted local archive source can install a pack; no Game or
Network protocol message can install, select, or prioritize one.

The persisted order contains at most 16 exact identities, low to high priority. Duplicate identities
and duplicate package IDs are rejected, metadata is strict and canonical, missing/corrupt manifests
or blobs fail closed, and a corrupt selection never silently enables discovered files. An immutable
stack lease pins the exact selected manifests and distinct blobs. Enabled or live-leased packs cannot
retire; after disable and lease release, metadata may retire while ordinary reachability-aware cache
eviction decides when unreferenced CAS bytes are reclaimable.

## Validation policy

The dedicated `TryCreateUserResourcePack` path accepts only:

- trust class `UserResourcePack` (canonical numeric value 3);
- 1–16 files and 1–16 definitions;
- `ActorPresentation` definitions with `Replace` only;
- `ActorGltfBinary` files, each referenced by exactly one declared presentation leaf;
- at most 32 MiB per actor artifact and 128 MiB total, within the existing package bounds;
- exact passive GLB inspection plus the current 65-bone skeleton and Idle/Walk/Jump compatibility
  required by the actor consumer.

It rejects Add, Texture/Icon, WorldPack, UI definition/schema/action content, undeclared or unused
files, duplicate logical IDs, executable content, oversized input, unsafe external GLB dependencies,
and incompatible actor skeleton/clip candidates. Normal scope-authorized package creation and the
generic archive decoder do not admit this trust class; the user archive has an explicit decoder.

## Effective presentation and safe publication

`PresentationGeneration` now resolves the finite actor-presentation layer as:

```text
User (highest enabled compatible pack)
    -> authenticated Realm
    -> world.local
    -> Primitive
```

User definitions overlay the effective presentation view; they do not rewrite the lower catalog.
A missing target is a harmless no-op. A present but invalid/incompatible target rejects the entire
candidate, leaving the current generation active. The stable effective fingerprint is derived from
the resolved logical IDs and selected immutable file hashes (or Primitive marker), not filesystem
paths, process-local generation numbers, source labels, or ineffective packs. Consequently a no-op
pack or byte-identical source change does not churn native presentation.

Local and Remote clients watch the explicit selection revision and atomically build a complete
candidate. A valid enable, disable, reorder, or immutable update publishes a new generation; invalid
candidates retain the old one. Existing async Realm acquisition remains epoch/revision fenced, and
completion composes with the currently selected user stack rather than publishing a stale selection.
Old generations retain their catalog, pack, and blob leases until native consumers release them.

## Acceptance matrix

| Case | Result |
| --- | --- |
| A — Local, no user pack | PASS: accepted F1.4 Official Local behavior is unchanged |
| B — Local, user X enabled | PASS: User X wins; `world.local` continues to provide Y |
| C — Local, Official absent | PASS: User X plus Primitive Y; semantic catalog/world unchanged |
| D — Remote, Realm art present | PASS: User X wins over Realm; Realm continues to provide Y |
| E — disable | PASS: Realm/Local presentation returns in the same semantic session through a new safe generation |
| F — two packs target X | PASS: last/highest enabled pack wins; reversing order deterministically changes the winner/fingerprint |
| G — invalid pack | PASS: incompatible 65-bone/skeleton candidate rejected atomically; current generation remains active |
| H — update | PASS: v1 -> v2 is a new manifest/hash and generation; v1 remains valid while leased |
| I — uninstall while live | PASS: busy while enabled or leased; succeeds only after disable and lease release |
| J — restart | PASS: exact ordered selection reopens from persistent metadata/CAS; corruption and missing content fail closed |
| K — CAS reuse | PASS: identical X bytes installed through Local, Realm, and User occupy one physical blob |
| L — forbidden package | PASS: Add, Texture, WorldPack, UI and unsafe/oversized actor packages rejected |
| M — texture | DEFERRED honestly: no current common PresentationGeneration consumer owns a standalone logical texture/material leaf |

The deterministic content evidence identities are:

| Fixture | Exact identity |
| --- | --- |
| user pack A/v1 | `user.overlay@e985d8f39deca7392259031b86324ba6cfcc8ebc14d0bd7e646197a9573e35fd` |
| user pack B | `user.overlay.second@f2abe020bed3de0b4729ae5dbcb2cf115672c3a41ff27043ef83deee2c387130` |
| user pack A/v2 | `user.overlay@974961bfaea04df6e7e177c9040d77c14434f26aa54eb0eb4516ced362636025` |
| remote proof pack | `user.remote-proof-x@a7e621133486decb76148352732e1d1c9e22dd55f3eeb64230926c5b65729cc4` |

The actor override is an objectively distinct red-tinted, otherwise compatible X GLB. Native T0.10
proved User-over-Realm hot replacement, preserved the existing actor root instance and actor-local
animated/equipment/overhead state, reported exact User provenance, then disabled the pack and
restored the exact prior Realm file. The acceptance emitted
`user_hot_replace=true user_disable_restore=true`.

Texture support was not faked. Current Cabin materials/textures are renderer-constructed leaves, not
logical resources resolved by the common `PresentationGeneration`. Giving them user-pack identity
would require the F1.6 world-renderer/derived-resource seam (or a premature renderer rewrite). F1.5
therefore admits no Texture/Icon kind and makes no texture-pack support claim.

## Semantic and F1.4 comparison

The remote integration retained semantic plan
`e20924faa9e12645f713db43544eccd15adf00a5f77afe729046bdb90e612fa1`
and semantic catalog
`82781a0fed8a1d3ac43f4aeac3ce4c44d0ebb2de44d56295dccc7fd1b790ed69`
while enabling, no-op extending, and disabling the user stack. Application epoch, authoritative
world, and participant identity remained unchanged. Game manifest v31 and Network transport revision
2 did not change; no remote user-pack protocol exists.

Relative to the immutable F1.4 baseline, expected additions are resource-pack metadata/counts,
explicit selection revision/validity, User selections, pack/blob leases, and changed effective
presentation fingerprints/native actor resource identity while an override is active. Unexpected
dimensions remained stable: semantic activation/proof, Game outcomes, Inventory, Chat, UI authority,
world structure, Primitive substrate, edition closure, Contentless Online composition, and F0.7
retirement boundaries. Persistent pack metadata/CAS is correctly distinguished from zero live
session leases after retirement.

## Verification and reproducibility

Host: CachyOS Linux `7.2.6-1-cachyos` x86_64; AMD Ryzen 7 7800X3D (8 cores / 16 logical
processors); .NET SDK 8.0.131, runtime 8.0.31; Godot
`4.7.1.stable.mono.official.a13da4feb`.

Commands included:

```sh
./verify.sh --architecture-only
dotnet run --project _Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj -c Release
# native T0.10 and every product/export/lifecycle control through:
ULTRASIM_GODOT='/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' ./verify.sh
```

Client.Content passed 35/35, including the full resource-pack matrix. The full Network Application
suite passed with the F1.5 semantic-invariance control. Native T0.10 passed the hot replacement and
restore control. One uninterrupted final canonical Release run on the tested implementation commit
exited zero with `PASS repository verification`, including architecture, all managed suites,
SinglePlayer/Online/Complete physical exports, Official installed/absent/restored controls,
Contentless Online cold/warm/profile cases, native and bot impairment swarms, F0.7 mixed lifecycle,
and final composition parity.

Non-authoritative attempts are retained separately: one development Network Application run reached
an unrelated timing-sensitive shared-authority convergence failure, which passed in the isolated
three-test gate and the later full-suite run; a first canonical attempt used Godot 4.7.0 and stopped
because its matching export templates were absent; a second attempt on 4.7.1 encountered the known
unrelated HTTP shutdown stress race (the exact 23-test hosting suite passed immediately afterward);
and another 4.7.1 attempt hit a transient editor shutdown abort, whose exact command passed on
immediate isolation. None is counted as the final pass.

Implementation commit:

- `ac8d3601e010bc44d5775c84e72cd2b4f79ca60f` — implement F1.5 user presentation packs.

Deliberately deferred to F1.6 or later: logical texture/material presentation leaves and derived
renderer resources, atlases, package-manager UI, automatic directory scanning, server-controlled
packs, Gameplay Frame/UI rework, camera work, prediction, RenderingServer rewrite, actor batching,
and vegetation.
