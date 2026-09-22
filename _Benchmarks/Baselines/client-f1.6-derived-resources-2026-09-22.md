# F1.6 — Derived resources and measured atlas pipeline

Accepted 2026-09-22. The expected starting revision in the task was
`254e06402df7ffabbe17204b97c7d07ea8886d01`; the resolved clean starting HEAD was
`b7dc0e00f8d6cfb2ad27c8036cfb31cf810dc1e3`. The intervening merge changed AIOS
documentation, not client content/presentation semantics. The tested production tree is
`f8220e4b225b0a1a5af355ea90f742dc6747dce3`. The final closure HEAD is the commit that owns this
evidence and is reported in the handoff because a commit cannot embed its own hash.

F1.0–F1.6 are closed, F1.7 is next, and F1.7+ was not implemented.

## Logical presentation boundary

`PresentationGeneration` remains immutable, process-local, reference counted, and lease-safe, but
its earned finite view now contains ActorPresentation, Texture, and Material leaves. Texture
selection is engine-neutral and retains the exact authorized blob:

```text
User -> authenticated Realm -> world.local
```

A missing logical/native texture continues to use the F1.3 renderer fallback; Primitive is not
invented as a texture catalog entry. A resolved material retains the lower selected material
definition and scalar values, while its logical albedo ID is resolved independently through the
effective texture view. Thus replacing `cabin:texture/wall` changes its selected bytes without
changing `cabin:material/wall`, roughness, metallic, emission, world structure, collision, or Game
truth.

The stable presentation fingerprint now canonically includes effective actor, texture, and
material results. Texture resource identity uses logical ID, passive file kind, and exact file hash;
material resource identity uses logical ID, definition hash, and effective texture fingerprint. It
does not include a process-local generation ID, filesystem path, source label, atlas coordinate, or
an ineffective pack. A real wall override changes the fingerprint; disabling restores the exact
lower fingerprint; an ineffective pack causes no fingerprint or derived-resource churn.

## User texture policy

The dedicated UserResourcePack path now accepts exactly:

- ActorPresentation Replace backed by a bounded compatible GLB; and
- Texture Replace backed by a strictly inspected PNG or WebP.

Material, Icon, UI, WorldPack, collision, scripts, DLLs, shaders, executable extensions, and all
semantic structural content remain rejected. Raster admission is limited to 16 MiB per user file,
16 files/definitions, 128 MiB per pack, 16,384 pixels per dimension, and a 256 MiB maximum decoded
RGBA envelope. The renderer rechecks decoded dimensions and byte count. Blob possession still does
not grant pack, scope, or activation authorization.

The deterministic Cabin user fixture replaces `cabin:texture/wall` with unmistakably distinct
passive image bytes. The Local product smoke retained semantic hash
`ClientSemanticHash(Low=7521387191213801372,High=9420329567225766878)` while transitioning:

| State | Stable presentation fingerprint | Wall derived key |
| --- | --- | --- |
| lower world.local | `0e807a405fc9fa59b0e533b47be67f69236c77e6b4b8fbcdb849d1898850d490` | `e924ffa55ed4806886dcda7c156575e7183067a4ebe8ba171dda553a697d7da2` |
| User wall | `84574d678d7612e97877816f69fb394fc5e7b662cb77153a2f7c75b2d5024e55` | `01c06c64850b5d7195376bcb9722f08cc001432dfd88968f8135005aa820776d` |
| disabled/restored | exact lower value | exact lower value |

The smoke built two initial Local textures, one new User wall artifact, then reopened the retained
lower artifact on disable (`builds=2/3/3/3`, `diskHits=1`). No semantic reset occurred.

## Godot derived store

`GodotDerivedResourceStore` owns Godot-native texture/material realization on the engine thread.
Source presentation leases flow into native leases; old native objects stay valid across generation
changes and cannot be disposed underneath consumers. Candidate construction completes before the
Local resolver switches source generations. The store refuses retirement while texture or material
leases remain live.

Persistent texture artifacts are disposable, renderer-specific RGBA data under:

```text
<writable-root>/Derived/Godot/<platform-profile>/format-1/policy-N/
    textures/<hash-prefix>/<derived-key>.rgba
```

They are not AssetBlob/CAS truth and never sit beside read-only Official archives. Deleting
`Derived/Godot` only causes regeneration from an authorized source blob.

The texture/material key is SHA-256 over the canonical domain, resource kind, stable per-resource
presentation fingerprint, backend `godot-4.7`, derived format version, policy version, validated
platform profile, import policy `rgba8-srgb-no-mip-v1`, and finite atlas family. It deliberately
excludes `ContentCatalogGenerationId` and `PresentationGenerationId`. Artifact headers additionally
authenticate the stored key, source file hash, dimensions, byte count, and RGBA payload hash.
Writes use an exclusive temporary file, flush, and atomic replacement.

Missing, truncated, corrupt, wrong-version, wrong-key, deleted, and policy-incompatible artifacts
all failed closed and rebuilt from verified source. Reparse-point/path escape attempts were
rejected. A fresh process-local generation with the same stable resource result reused the same
derived key and disk artifact.

## Measured standalone and atlas candidate

One Release/headless T0.10 diagnostic run on the tested tree produced:

| Case | Build/load | Managed allocation | Persistent/pixel bytes | Native resources |
| --- | ---: | ---: | ---: | --- |
| former direct Cabin construction | 2.357950 ms | 10,848 B | N/A | direct Godot path |
| derived standalone, cold | 0.678724 ms | 149,392 B | 248 B | 2 textures, 2 materials |
| derived standalone, warm | 0.013850 ms | 4,688 B | 248 B reused | same logical result |
| diagnostic WorldOpaque atlas build | 0.096010 ms | not separately isolated | 8 B page | 2 inputs, 1 page |

These are bounded diagnostic construction measurements, not frame-time or product-throughput
claims. Cold allocation rises because validated decode, hashed persistent serialization, and native
publication are included; warm reopen is both faster and lower-allocation. The former and new paths
produced exact logical source pixels.

The finite atlas candidate accepts 1–16 ordered, opaque RGBA8 WorldOpaque textures with the same
backend/profile/import/atlas policy. It owns exact source leases, emits one deterministic horizontal
page and exact UV scale/offset regions, and bounds the result by the same raster envelope. The Cabin
case packed two 1x1 textures into one 2x1 page with 100% pixel occupancy.

**Production decision: reject atlas for this workload; select Standalone.** Headless Godot did not
provide reliable GPU draw-call, texture-binding, or GPU-frame counters, and this candidate did not
reduce the observed material or draw-unit structure. One page alone does not merge heterogeneous
meshes or make them one MultiMesh. The atlas remains diagnostic evidence, not semantic content and
not selected production policy.

## F1.5 comparison and semantics

Relative to the immutable F1.5 baseline, expected changes are Texture Replace admission, logical
Texture/Material leaves, generalized stable presentation identity, writable Godot-derived files and
native leases, and bounded atlas diagnostics. User Actor replacement remains intact. Unexpected
dimensions remained stable: semantic activation/application proof, Game/Inventory/Chat/UI authority,
actor identity, Realm/Local authorization, Network policy, edition closure, Contentless Online, and
F0.7 retirement.

The real Local presentation package adds three blobs and one Local manifest/activation to the
F1.0-at-open cache census (now five blobs, two Local manifests, two Local activations). Realm counts
remain zero and live pins retire to zero. The repository F1.0 evidence predicate was updated to
recognize this intentional persistent-content delta; lifecycle expectations were not weakened.

Game manifest v31 and Network transport revision 2 are unchanged. No remote protocol, authority,
activation, proof, AOI, replication, or participant policy changed.

## Verification and reproducibility

Host: CachyOS Linux `7.2.6-1-cachyos` x86_64; AMD Ryzen 7 7800X3D (8 cores / 16 logical
processors); .NET SDK 8.0.131, runtime 8.0.31; Godot
`4.7.1.stable.mono.official.a13da4feb`.

Commands included:

```sh
dotnet run --project _Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.Client.BuiltInTestWorlds.Tests/UltraSim.Client.BuiltInTestWorlds.Tests.csproj -c Release
godot --headless --path _Tests/UltraSim.S0.Godot.Tests --scene res://T010PlayableWorldAcceptance.tscn
godot --headless --path Client/Godot -- --f1-6-derived-smoke --expect-edition=Complete \
  --content-cache=<fresh>/content --derived-cache=<fresh>/derived
./verify.sh --architecture-only
./verify.sh
```

Client.Content passed 36/36; BuiltInTestWorlds passed 9/9; native T0.10 passed all prior actor/world
checks plus cold/warm/restart/corruption/version/key/deletion/policy/path/lifetime/atlas controls; and
the fresh actual-product F1.6 Local smoke passed User enable, semantic invariance, exact disable
restore, ineffective-pack no-op, and cache reuse. Architecture verification passed.

One uninterrupted final canonical Release run on `f8220e4b225b0a1a5af355ea90f742dc6747dce3`
exited zero with `PASS repository verification`. It covered all managed suites, native T0.10,
SinglePlayer/Online/Complete exports, Official installed/absent/restored, Contentless Online
cold/warm/profile behavior, Complete-host exported clients, native/bot and accounting swarms, F0.7
mixed lifecycle, F1.0 guardrails, composition parity, and `git diff --check`.

Non-authoritative attempts are recorded rather than discarded: the first implementation run exposed
the stale F1.0 cache-count predicate and led only to the evidence-boundary correction in
`f8220e4b...`; one later attempt encountered shared-authority convergence while stale DirectConnect
processes from the prior interrupted run remained, after which the exact M0.0 gate passed; and one
attempt hit the known unrelated HttpListener shutdown stress flake, after which its exact hosting
suite passed 23/23. None is counted as the final canonical pass.

Implementation commits:

- `9452fcf91c5f7c903addbca46d922eb78d61a257` — implement F1.6 logical and derived presentation resources;
- `f8220e4b225b0a1a5af355ea90f742dc6747dce3` — align the F1.0 lifecycle evidence predicate with the intentional Local package.

Deliberately deferred to F1.7 or later: Gameplay Frame/SubViewport composition, gump/window manager,
Context/Tooltip rerouting, camera/view-envelope changes, prediction/reconciliation, direct
RenderingServer/RID rewrite, generalized atlas/texture-array policy, mesh compiler/batching, actor
native-import migration, audio/shader compilation, material/icon/UI user replacement, and
vegetation.
