# F1.8 — Declarative camera rig and canonical gameplay-view crop

Accepted 2026-09-23. Starting HEAD: `65bcb40ca74a489d5ec226accd624ea36ca87b67`.
The uninterrupted canonical Release verifier tested implementation HEAD
`860d6790724733a6e577934845c72e654e2d3f1a`. The final closure HEAD is the
subsequent evidence-only commit, reported in the handoff; a commit cannot embed its own hash.
F1.0–F1.8 are closed. F1.8A is next; F1.8A/F1.9 were not implemented.

## Implemented boundary

`ClientCameraRig` is one immutable engine-neutral fixed-unit value. It has finite
projection (Perspective/Orthographic), ControlledActor target, Immediate follow,
Locked/Free yaw and pitch, Disabled/Stepped zoom, bounded pivot and distance,
fixed-unit angles, look speed, canonical 16:9 vertical FOV or orthographic size.
Distances and size use millimeters; angles/FOV use tenths of degrees. Unknown or
unsupported values fail validation. The current three stock compositions are:

| Grammar | Rig | Default distance | Canonical vertical FOV | Yaw/pitch | Zoom |
| --- | --- | ---: | ---: | --- | --- |
| OrbitCommand | Orbit | 14 m | 70° | Free/Free | Stepped |
| ThirdPersonDirect | ThirdPerson | 8.5 m | 76° | Free/Free | Stepped |
| IndependentMoveLook | Independent | 14 m | 70° | Locked/Locked | Disabled |

All three use `GodotGameplayCameraRig`, including the programmatic orthographic
branch. The controller owns camera-local pose, lens, zoom, and movement basis;
Local and Remote supply their existing presented/accepted target pose. A named
user profile (maximum 16, name maximum 48 characters) composes grammar, rig,
and physical bindings through the same resolver. An exactly equivalent named
Orbit profile had the same effective rig/bindings; changing only its name did
not change behavior. No pan, snap, collision, obstruction fade, shoulder swap,
follow smoothing, prediction, or actor interpolation is claimed.

Settings format is version 4. Valid legacy camera choice, distance, FOV,
bindings, and per-realm overrides migrate into the new rig without resetting
the user's choices. The old camera enum remains only as a migration/preset
helper. CameraLook, CameraZoomIn, and CameraZoomOut are client-local semantic
actions, defaulting to middle-button drag and wheel up/down. They are not Game
commands. The remote action mapping is explicit, not a numeric enum cast.

The bounded remote profile advertisement now encodes the full canonical rig
(`RemoteClientCameraRig.EncodedSize = 54` bytes); the complete profile remains
bounded to 2,048 bytes. Its authenticated hash covers revision, grammar, rig,
and recommendable bindings. A material FOV change changes the hash and requires
the existing consent decision; exact reconnect reuses the decision. Game
manifest version is 32; Network transport revision remains 2. Profile names
are local metadata, not protocol semantics.

## Projection and native evidence

For a perspective rig, `Tv = tan(canonical vertical FOV / 2)` and
`Th = Tv × 16/9`. At output aspect `A ≤ 16/9`, the target frame retains `Tv`
and uses horizontal half-tangent `Tv × A`. At `A > 16/9`, it retains `Th` and
uses vertical half-tangent `Th/A`. Orthographic extent follows the same crop
rule. Display aspect can never expand either canonical bound for a given rig.

The stock Orbit 70° numeric controls are:

| Output aspect | Target vertical half-tangent | Target horizontal half-tangent | Effect |
| --- | ---: | ---: | --- |
| 16:9 | 0.7002075382 | 1.2448134013 | full canonical envelope |
| 4:3 | 0.7002075382 | 0.9336100509 | horizontal crop |
| 3:4 | 0.7002075382 | 0.5251556537 | stronger horizontal crop |
| 64:27 | 0.5251556537 | 1.2448134013 | vertical crop |

The orthographic 20 m control retains 20 m vertical at 4:3 (26.667 m
horizontal) and retains the canonical 35.556 m horizontal at 64:27 (15 m
vertical). Core controls established equal projection for 1920×1080,
1280×720, 640×360, and 3840×2160, plus equal 4:3 projection for 800×600
and 1600×1200. Resizing the application among 1920×1080, 2560×1440, and
3440×1440 while retaining selected 1920×1080 output left the target envelope
unchanged. Frame aspect changes crop projection without resetting camera pose,
distance, canonical FOV, render distance, AOI, or server disclosure.

F1.8 still renders with the application viewport and F1.7 masks. The native
controller explicitly uses Godot's vertical/Height aspect policy and applies
the temporary main-viewport compensation
`target vertical extent × application height / actual frame height`.
The target-frame projection remains a separate Core result for F1.9; the
integer max-fit frame can round by one pixel. Native headless acceptance
verified center and four near-edge pointer rays and round trips at 16:9,
4:3, 3:4, and 64:27, plus orthographic realization, locked axes, and a
same-frame rig/lens switch. Outside-frame world input remains rejected.
The native viewport for this headless check was 64×64; the frame aspect was
supplied by the realized layout. Its result was:

```text
F1.8 CAMERA RAYS PASS app=64x64 aspects=16:9,4:3,3:4,64:27 orthographic=pass locked=pass
```

The same-frame switch check caught a real presentation defect during
development: applying a new rig with unchanged frame geometry could preserve
the prior native lens. The controller now invalidates its cached layout when
the rig changes, and the regression checks Orbit→ThirdPerson FOV and
Orthographic→Perspective without requiring a resize.

## F1.0/F1.7 comparison and verification

The final full verifier retained the F1.0 resource and semantic guardrail:

```text
F1.0 CLIENT BASELINE PASS local_chunks=49/25 local_batches=64/7
remote_cold_bytes=733198 remote_warm_reused=1/3
remote_resources=1/2213268 retirement=clean
F0.7 MIXED SESSION LIFECYCLE PASS ... boundaries=clean
PASS F0.8_FinalCompositionParity exports=SinglePlayer/Online/Complete
```

Local and Remote native smokes validated their realized camera rig against
the resolved Core rig; Complete Local/Remote and SinglePlayer/Online export
composition passed. The intentional differences from F1.0/F1.7 are the
resolved camera representation, profile wire/hash (manifest v32), camera
semantic bindings, camera diagnostics, and the native compensated lens under
the existing masked Gameplay Frame. Semantic world fingerprint, authority,
Inventory, Chat, content activation, renderer residency, AOI, and F1.7
workspace/gump/lifecycle ownership remained unchanged. Camera crop is a
stock-client consistency policy, not an information-security boundary.

Host: CachyOS Linux `7.2.6-1-cachyos` x86_64; AMD Ryzen 7 7800X3D, 8 cores/
16 logical processors; .NET SDK 8.0.131/runtime 8.0.31; Godot
`4.7.1.stable.mono.official.a13da4feb`; Release configuration.

Commands and final results:

```sh
dotnet run --project _Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj -c Release  # 33/33 pass
dotnet run --project _Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj -c Release  # 180/180 pass
./verify.sh --architecture-only                        # pass
./verify.sh --f1-7-only --configuration=Release          # pass
./verify.sh --configuration=Release                      # PASS repository verification
```

The uninterrupted final canonical run on `860d679...` passed all managed
gates, native Godot acceptance, real Local/Remote journeys, SinglePlayer/
Online/Complete exports, cold/warm Contentless Online, Official content
installed/absent/restored, mixed swarms, F0.7 lifecycle, F1.0 guardrails,
edition parity, and `git diff --check`. During development, one M0.0 native
smoke attempt failed transiently; focused reruns and the final canonical
run passed. Separate development attempts also exposed stale manifest
fingerprint and camera-label assertions; these were updated for the deliberate
v32 profile change before the final pass.

Implementation commits: `7d16bacd666eb9f9577c3262073a01b4a61c20cd`,
`090a89d511532434ae5495d13419e5fb90240fa5`,
`1e3456a206e2f1102ee5775149deffec240ddb5c`,
`44ee67cb0a826dc741d4b56a27d871970a19acde`,
`860d6790724733a6e577934845c72e654e2d3f1a`.

F1.8A retains prediction, reconciliation, correction smoothing, and remote
actor interpolation. F1.9 retains a real Gameplay SubViewport, independent
render resolution, upscaling, and pixel/retro modes. None began in F1.8.
