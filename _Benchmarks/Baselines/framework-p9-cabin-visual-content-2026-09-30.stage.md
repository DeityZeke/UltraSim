# P9 Cabin visual/content stage — not P9 closure

Status: **P9 ACTIVE**. Starting HEAD `00ad7073688b2d8c379da5636fa14013c70a1e85`; implementation commit `c067c30d2b25a981db484a07995cbc28d1ebf30f`. P1–P8 remain closed. P10/P11, Gate 1 and PF6 were not started.

This is a mechanically verified visual slice, not acceptance of the canonical two-room Cabin benchmark. Classic Cabin remains the authoritative 8 m × 6 m, one-room shell in `GamePlayableWorld.AddCabin`. It has no second room or interior doorway. The current P9 windowed wall art is used on both side walls, so it does not satisfy the Room A no-window / Room B one-window distinction. The interior still exposes ground rather than a designed floor. Inventing an apparently walkable second room only in presentation would contradict the structural/collision invariant. A separately versioned authored WorldPack, or an explicit change to the P9 scope, is required before closure.

## Implemented slice

- Passive, bounded `.usmesh` WorldPresentation admission: finite triangle attributes and material slots, with no Game collision, support, interaction or identity meaning.
- Separate optional `world.local` package `official.world-local.cabin-p9` with 11 Blender-authored meshes, five project-generated painted textures, and six material definitions. Archive SHA-256 `8b9ac85c948d1005c1b030721d063ac4d061c0d81b46a3610e4a09f59fbdab95`; 12,071,802 bytes; manifest `8f1a92abaf6628a254321b0c95465beeba7d540f944d33636760d8bf5c0c6951`; activation plan `ffe83afa4bcfb0d592b8c2a014ec4ab7a887fc8d9d478449a247cfc93e729c51`.
- SinglePlayer/Complete ship the archive externally; Online does not. User → Realm → `world.local` → Primitive resolution remains in the existing catalog/generation path. The A0.1 Default/Replace/Add oracle and the preexisting Official actor archive are unchanged.
- Shared Local/Remote world resolver realizes the exact mesh/material set and explicitly retires Godot-native wrappers. Same-session Primitive captures remove the optional visual layer while retaining semantic world truth.
- Product capture adds higher/lower/window-side views and finite mood controls. The existing vegetation setting remains device-owned, persisted, and default **Off**. The On control still reads as thin debug-like spikes in the captured gameplay view; no default promotion was earned.

## Reproduction and environment

Linux `7.2.6-1-cachyos`, AMD Ryzen 7 7800X3D, NVIDIA GeForce RTX 4070 Ti SUPER, Godot 4.7.1 Mono, OpenGL Compatibility, X11, .NET SDK 8.0.131 with local .NET/ASP.NET 8.0.31 runtime for the verifier. Blender 5.2.2 was used for source authoring/export; Blender is not a product runtime requirement. Source and generated artifacts are under `Client/PresentationSource/Cabin/` and `Client/PresentationAssets/Cabin/`. Painted PNGs were generated for this project using OpenAI image generation in new-image mode; provenance and prompt direction are in the source README. No third-party art was introduced.

Focused commands:

```sh
dotnet run --project _Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj -c Release
./verify.sh --architecture-only
env PATH="$PWD/.output/f112-r3-runtime:$PATH" ./verify.sh --f1-12-mixed-only --configuration=Release
env PATH="$PWD/.output/f112-r3-runtime:$PATH" ./verify.sh --configuration=Release
```

The exact-commit visual run used `Client/Godot/export-client.sh SinglePlayer
"$PWD/.output/p9/single-player-c067/UltraSimSinglePlayer.x86_64"`, then launched that physical
binary with `--f1-12-cabin-preview --p9-cabin-extended-capture`,
`--f1-12-head=c067c30d2b25a981db484a07995cbc28d1ebf30f`, unique save/evidence roots and
the same `--content-cache` for cold/warm. Off used `--p9-no-vegetation`; the On control used
`--f1-11-vegetation`. The captured JSON contains each image name, dimensions, pixel/PNG hashes,
source head, catalog/presentation fingerprints and semantic hash.

The native F1.11 renderer-stress test was run on Godot's Debug test assembly. It realized all 11 optional meshes, then proved held strong `Mesh` and `Material` wrappers had `NativeInstance == 0` after resolver retirement. Content tests passed 39/39, including bounded admission, Local/Realm isolation, User material override, warm reopen, missing-art fallback, and invalid mesh rejection. The focused mixed-lifecycle verifier passed after its F1.0 exact persistent-cache census was updated from the old two-package layout to the observed three-package P9 layout. It still requires zero live pins at the Remote handoff, exact cold acquisition, warm reuse, fresh lineage and final UI/resource retirement.

Preliminary full runs exposed two pre-P9 exact-count assumptions: one for Remote world resources, then the F1.0 cache census (five blobs/two Local manifests/two activations) after the mixed product journey itself passed. The new exact cache expectation is 21 blobs/three Local manifests/four activations; this is a content-store count change, not semantic permission to leave live leases. The corrected focused gate passes. The Remote assertion now distinguishes P9 Local materials from the existing Realm-selected wall. The first attempted canonical invocation also lacked the ASP.NET 8 runtime in the system dotnet root; the local .NET/ASP.NET 8.0.31 runtime supplied the environment prerequisite. Failed logs are retained separately from passing evidence.

Canonical Release on `c067c30d`: **PASS, uninterrupted**. This included the physical SinglePlayer/Online/Complete exports, F1.12 installed/absent/primitive product dogfood, mixed Local/Remote retirement, native Online/bot and accounting controls, audio, Contentless Online, and final composition parity. The final command returned exit code zero and ended with `PASS repository verification`. The exact-commit visual captures used a fresh SinglePlayer export with the same package hashes.

## Actual-product observations

The exact-commit 17-view native Cabin capture ran for installed P9 art and same-session Primitive fallback, with clean retirement. A separate On capture shows the existing grass treatment; Off remains the product default. Cold installed entry built seven derived textures in 96.507 ms cumulative texture-build time; warm entry built zero and read seven derived textures from disk in 19.488 ms cumulative load time (31,451,168 derived bytes read). These are preparation-stage accumulations, not frame-time p95 or visible-hitch measurements. The cold and warm source blobs are 21 / 16,792,396 bytes; both retire from 30 live blob pins and two catalog pins to zero. Native resolver evidence records 11 optional meshes, 32 owned mesh wrappers, 24 owned primitive/material wrappers, and eight derived material leases. This is owned-resource accounting, not GPU memory use.

The P9 installed capture and accepted F1.12 Cabin capture have identical BaseWorldFingerprint, WorldSemanticHash, 84 active rendered instances and 59 MultiMesh groups. The visual package changes presentation, not the authoritative world or publication identity. The cold/warm and On/Off raw captures include exact source/content hashes, native image hashes, semantic hash, world-resource and retirement snapshots. Visual quality, audio feel, and illumination credibility remain human-review observations; capture existence does not certify them.

Permanent exact-commit evidence: [cold capture JSON](framework-p9-cabin-visual-content-2026-09-30.stage/cold-evidence.json), [warm JSON](framework-p9-cabin-visual-content-2026-09-30.stage/warm-evidence.json), [vegetation-On JSON](framework-p9-cabin-visual-content-2026-09-30.stage/vegetation-on-evidence.json), [canonical Release log](framework-p9-cabin-visual-content-2026-09-30.stage/canonical-release.log), and [focused mixed log](framework-p9-cabin-visual-content-2026-09-30.stage/mixed-focused.log). The [native renderer raw rows](framework-p9-cabin-visual-content-2026-09-30.stage/native-renderer-evidence.json) were captured from the identical pre-commit implementation tree; the committed-tree canonical log independently records the F1.11 native pass. The two preliminary failed full-run logs are retained as [Remote count](framework-p9-cabin-visual-content-2026-09-30.stage/preliminary-remote-count-failure.log) and [F1.0 count](framework-p9-cabin-visual-content-2026-09-30.stage/preliminary-f10-count-failure.log) evidence. Representative actual-product images: [high exterior Off](framework-p9-cabin-visual-content-2026-09-30.stage/exterior-high-off.png), [lower exterior](framework-p9-cabin-visual-content-2026-09-30.stage/exterior-low-off.png), [doorway](framework-p9-cabin-visual-content-2026-09-30.stage/doorway-noon-off.png), [interior](framework-p9-cabin-visual-content-2026-09-30.stage/interior-noon-off.png), [window-side](framework-p9-cabin-visual-content-2026-09-30.stage/window-side-noon-off.png), [dusk](framework-p9-cabin-visual-content-2026-09-30.stage/exterior-dusk-off.png), [night](framework-p9-cabin-visual-content-2026-09-30.stage/exterior-night-off.png), [same-session Primitive](framework-p9-cabin-visual-content-2026-09-30.stage/primitive-same-session-exterior.png), and [vegetation On](framework-p9-cabin-visual-content-2026-09-30.stage/exterior-high-vegetation-on.png). All three final capture JSON files report `SourceHead=c067c30d2b25a981db484a07995cbc28d1ebf30f`.

## Closure gap

P9 is **not closed**. The requested Room B, interior doorway, differentiated window topology, blocker/light-path test, designed interior floor, and two-room lighting comparison are missing. A new authored two-room WorldPack would add *content* with its own structural truth rather than alter the accepted Classic Cabin oracle, but that authoring choice has not been made in this stage. No Game/World/Network/authority sources were changed, and no P10 work began.
