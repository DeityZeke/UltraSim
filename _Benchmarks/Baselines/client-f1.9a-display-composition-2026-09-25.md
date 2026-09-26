# F1.9A — display composition and real Gameplay Output (2026-09-25)

Starting HEAD: `9ab3030413898298f5f4a06c23157ebcc0722017`. Tested implementation
HEAD: `371ffc90978f69ddc2a94149b6343c669b59f1ec`. The separate evidence/status
commit is identified by the repository history after this artifact. No Game, Network,
WorldPack, or W8 semantics changed. F1.9B has not started.

## Implemented boundary

Settings schema v4 became v5. v1–v4 load with the deterministic Simple, Windowed,
16:9, 1920×1080, 100% default while preserving controls, camera, render distance,
named profiles, and server-scoped settings. Display is user-global, outside server
recommendations. Simple and Advanced are authoring modes over the same immutable
`ResolvedClientDisplayConfiguration` and native composition. Simple locks logical
Application, full Gameplay Frame, output, and internal 3D size together. Advanced
allows bounded custom dimensions (each ≤4096, each target ≤8,294,400 pixels),
Fill/Max Fit/Custom centered frames, exact frame/output aspect, and 50–100% internal
3D scale. Invalid persisted allocations fail closed through the settings loader.

The exact Simple presets are 16:9: 1280×720, 1600×900, 1920×1080, 2560×1440,
3840×2160; 16:10: 1280×800, 1440×900, 1680×1050, 1920×1200, 2560×1600;
4:3: 1024×768, 1280×960, 1600×1200, 1920×1440; 5:4: 1280×1024.
Changing aspect rebuilds the selectable resolution list. Advanced Custom does not
invent a named aspect for 1500×920. Returning to Simple ignores all retained
Advanced dimensions. The physical window uses Godot's aspect-preserving canvas
scaling; resizing or fullscreen does not change the selected logical resolution.
Windowed native size is fitted to the usable desktop without rewriting settings.

`ClientRoot/UILayers/GameplayOutput` is one `SubViewport` containing the same World
root for Local, Remote, and Complete. Its size is Gameplay Output resolution. A
frame-sized `TextureRect` presents that output in the application workspace; app
HUD, gumps, debug, and context/tooltip remain outside the 3D viewport. Godot's
`Viewport.Scaling3DMode=Bilinear` and `Scaling3DScale` apply the internal scale to
3D only; [Godot's resolution-scaling documentation](https://docs.godotengine.org/en/4.4/tutorials/3d/resolution_scaling.html)
specifies bilinear availability in all renderers, including this project's
Compatibility renderer. This is a property/native-resource assertion, not a GPU
pixel-timing claim. Native camera realization now uses Core's target crop directly,
not the temporary full-main-viewport lens compensation. Pointer coordinates map
application → frame-local → output before native camera ray nomination; Game keeps
semantic disclosure and interaction authority. Native Label3D actor labels remain
within the World output and scale with it.

| Acceptance | Application | Frame x,y,w,h | Output | Internal 3D |
|---|---:|---:|---:|---:|
| Simple 16:9 | 1920×1080 | 0,0,1920,1080 | 1920×1080 | 1920×1080 |
| Simple 4:3 | 1600×1200 | 0,0,1600,1200 | 1600×1200 | 1600×1200 |
| Advanced smaller output | 2560×1440 | 320,180,1920,1080 | 1280×720 | 1280×720 |
| Advanced 50% internal | 2560×1440 | 320,180,1920,1080 | 1920×1080 | 960×540 |
| Advanced custom 75% | 1500×920 | 0,0,1500,920 | 1500×920 | 1125×690 |
| Advanced exact Max Fit | 1500×920 | 6,41,1488,837 | 1280×720 | 960×540 |

All six passed native `SubViewport` size/scale, retained World/camera identity,
frame-to-output center mapping, camera projection/ray round-trip, and canonical
16:9 envelope bounds. Core tests prove 1500×920 and 3000×1840 share an exact
camera envelope, 960×540 and 1920×1080 share the 16:9 envelope, narrow aspects
crop horizontally, and a 1920×1080 Simple fullscreen selection remains logically
1920×1080. The headless environment does not control a physical 2560×1440 monitor;
that transition was tested at the resolved/native-property boundary, not by a
physical monitor capture. The existing F1.8/F1.8A camera, motion, and displayed-pose
picking regressions remained green.

## Native product and resource evidence

The Local product display smoke changed an active session among 1920×1080/100%,
1920×1080/50%, and 1280×720 output/100%, then restored the original display.
The session, semantic world, and single output viewport stayed owned; retirement
was clean. The exported Online W7 Far Gate and 127/128/127 seam journey ran with
output 1280×720 and internal 960×540, passed the exact scaled-output assertion,
and retained Pack acquisition/retirement. SinglePlayer, Online, and Complete
exports, Contentless Online, PF1/PF2 native swarms, F0.7 mixed lifecycle, and
F1.0 lifecycle guardrails passed. Protocol manifest remains v33, transport revision
2; WorldPack/v1, WorldStructuralManifest/v1, and worldview/v2 are unchanged.

One representative headless Local diagnostic run, with 177 renderer nodes and 99
renderer resources in all three modes, reported:

| Mode | Reconfigure | Godot process mean | Godot renderer-frame mean | RGBA lower-bound bytes |
|---|---:|---:|---:|---:|
| 1920×1080 output, 100% | 91 µs | 0.633 ms | 0.146 µs | 16,588,800 |
| 1920×1080 output, 50% | 27 µs | 0.633 ms | 0.137 µs | 10,368,000 |
| 1280×720 output, 100% | 45 µs | 2.172 ms | 0.128 µs | 7,372,800 |

These are smoke diagnostics, **not** rated GPU or scalable-renderer comparisons.
Headless GPU monitor bytes were zero/unavailable; the RGBA value is only an output
plus internal render-target lower-bound estimate. The noisy process means must
not be interpreted as a resolution-speed curve. F1.9B owns renderer scalability.

## Verification and environment

- `dotnet run --project _Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj -c Release`: passed, including schema migration, all preset families, arbitrary custom aspect, relock, camera bounds, and settings ownership.
- Godot 4.7.1 Mono native S0.8 headless acceptance: passed six F1.9A display cases and existing camera/ray/presentation controls.
- `./verify.sh --f1-7-only --configuration=Release`: passed, including active-session Local display changes and clean retirement.
- `./verify.sh --architecture-only`: passed on `371ffc90`.
- `./verify.sh --configuration=Release`: **one uninterrupted command passed on `371ffc90`**, ending `PASS repository verification`. The exporter internally retried one incomplete Complete managed export from clean generated state and then passed; this was an internal recovery, not a separate failed verifier run.

Host: CachyOS Linux 7.2.6, AMD Ryzen 7 7800X3D (8 cores/16 threads), 30 GiB RAM,
.NET SDK 8.0.131/runtime 8.0.31, Godot 4.7.1 Mono, Compatibility renderer. The
canonical run serialized its jobs. Test code changes are bounded to display
diagnostics/acceptance; no W8 or F1.9B implementation was introduced.
