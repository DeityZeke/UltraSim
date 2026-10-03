# Exported-client hitch characterization — 2026-10-02

Diagnostic finding, not an optimization or a new client FPS rating. The original HUD image's
2,850.74 ms frame has no event timestamp; this study reproduces a **2.34-second cold-entry
frame of the same class**, but cannot prove that it was the image's exact event.

## Provenance and workload

- Starting HEAD: `5c018c29552367e465051f1e618741519b249b4e`. Diagnostic implementation and
  every accepted raw row: `40c661a406a592e3787e7eeacbaea849bc773f40`.
- Actual exported Linux Single Player, Godot 4.7.1 Mono / OpenGL Compatibility, .NET 8.0.131,
  Linux 7.2.6-1-cachyos, Ryzen 7 7800X3D (8 cores/16 threads), RTX 4070 Ti SUPER driver
  615.71.09. Window/output 1920×1080, Vsync Enabled, no frame cap. Affinity was not pinned.
- One exported process with its packet-free embedded Local authority; no concurrent benchmark
  job. This is player-visible wall frame interval, **not** GPU duration or client-only CPU.
  Process CPU includes authority and workers. Desktop activity/OS scheduling were not isolated.
- `idle`: eight-second warmup, then a 30-second eligible rolling frame window. `move`: same,
  with real X11 F2 then W held six seconds; actor moved from chunk (0,0) to (1,0). `cold`:
  start from the shell, arm the sampler after Reset, synchronously enter the same Concept Demo,
  then retain the first frame in a roughly 20-second window. “Cold” means a new session/native
  realization with the already installed Official content/cache, **not** an empty CAS or OS cache.
- Vegetation Off is explicitly `--p9-no-vegetation`; On is `--f1-11-vegetation`. The realized
  setting appears in every context and frame JSON. Detailed game timings were not enabled;
  a diagnostic 4 Hz process-wide GC counter timeline was used, with sparse opt-in entry clocks.

The six accepted rows have raw chronological microsecond intervals and context/GC timelines in
[`client-hitch-characterization-2026-10-02/`](client-hitch-characterization-2026-10-02/).
All six exported runs exited successfully. Each context stamps the tested implementation SHA.

| Scenario | Eligible frames / seconds | Mean / P99 / max frame ms | >100 ms | GC pause ms | Allocated MB | Process CPU ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Warm idle, vegetation Off | 4,282 / 30.00 | 7.007 / 8.243 / 112.006 | 2 | 264.8 | 159.0 | 7,600 |
| Warm idle, vegetation On | 4,277 / 30.01 | 7.016 / 8.278 / 118.658 | 3 | 329.8 | 173.1 | 7,740 |
| Real-input movement, Off | 4,279 / 30.00 | 7.011 / 8.737 / 115.979 | 1 | 266.0 | 157.8 | 7,460 |
| Cold entry, Official art, Off | 2,846 / 22.34 | 7.849 / 8.462 / **2,339.298** | 3 | 211.7 | 495.0 | 6,690 |
| Cold entry, Official art, On | 2,848 / 22.35 | 7.846 / 8.395 / **2,347.112** | 2 | 176.4 | 514.5 | 6,850 |
| Cold entry, Primitive art, Off | 2,854 / 21.03 | 7.369 / 13.354 / **1,032.374** | 2 | 167.2 | 135.2 | 5,710 |

The cold-window mean includes the entry stall; its P99 excludes that one extreme frame. The
primitive control changes presentation but retains the same authority world and session-entry
path. Neither the roughly 8 µs warm-idle mean difference nor the cold On/Off difference earns a
vegetation performance conclusion from one matched row. Vegetation is not the 2.34-second cause
in this control. No setting/default was changed.

## Attribution

The corrected cold capture measured `StartLocalCabin` at 2,330.0 ms and the next real frame
at 2,339.3 ms (Off). Reset originally erased the prior frame timestamp; the first exploratory
cold control therefore missed entry. The committed study waits for a real post-Reset frame and
keeps entry inside the bounded ring. That invalid exploratory row is **not** in the accepted table.

Sparse clocks in `LocalPlayClientRoot.StartLocalCabin`, `LocalCabinClient._Ready`,
`StartFreshWorld`, and `PrepareOptionalActorPresentation` give the following Off entry path:

| Synchronous operation | Wall ms |
| --- | ---: |
| Local authority create and `WaitUntilReady` | 632.7 |
| Optional art preparation | 1,280.7 |
| Native world projection | 270.8 |
| Initial presentation convergence | 88.0 |
| Remaining shell, scene, semantic and UI entry | about 57.7 |

Inside optional art preparation, `PresentationGeneration.CreateWithResources` took 386.5 ms,
and `new GodotCatalogWorldAssetResolver` took 689.8 ms. Its
`PrepareOptionalWorldMeshes` synchronously reads and realizes the eleven selected P9 world
meshes. Official Cabin catalog opening took 131.5 ms; the derived-store constructor itself took
only 1.1 ms. A separate, valid `dotnet-trace` sampled managed-stack profile on the same source
commit found the main thread in `StartFreshWorld` for about 2,307 sampled-wall ms,
`CreateWithResources` for 393 ms, the resolver constructor for 711 ms, and
`PrepareOptionalWorldMeshes` for 700 ms. These are corroborating **sampled stack residence**, not
exclusive CPU costs; all-thread `topN` percentages would mix waiting authority/worker/finalizer
threads and are not used as savings estimates. The local profiler trace is retained under ignored
`_Benchmarks/results/client-hitch-study-2026-10-02/cold-profile-pinned.nettrace` (7.3 MB), not
promoted as an immutable raw rating.

Primitive entry spent 0.7 ms in the skipped optional-art phase and 1,022.9 ms overall. The
Official-minus-Primitive entry difference was 1,307.1 ms in these matched controls. The
remaining approximately one second is **not** all rendering: about 633 ms is authority creation
and readiness, with a further approximately 260 ms native world projection. No work was moved
off-thread or suppressed for this investigation.

Warm-idle Off held the same player position, 49 rendered chunks, 293 renderer nodes, 143
renderer resources, and zero pending builds/retirements across its window. Its three >50 ms
frames were 76.6, 112.0 and 106.6 ms. The 4 Hz GC timeline advanced by 40.6, 104.1 and
120.1 ms in the corresponding 250 ms bins. This is strong temporal evidence that managed GC
accounts for much of the recurrent tail, **not** proof that every microsecond of each frame was
GC. It allocated 159.0 MB in about 31 wall seconds (roughly 5.1 MB/s). Equivalent periodic
spikes appeared in pilot idle runs and the final movement/vegetation controls. The allocation
call sites remain unproven: attempted `gc-verbose` EventPipe traces were unreadable despite
`Trace completed`, so no allocation-stack claim is made.

## Disposition and limitations

- Confirmed, high confidence, performance: synchronous cold Local world entry produces a
  multi-second rendered-frame interval. The dominant measured work is optional content
  composition/mesh realization, followed by authority readiness and initial projection. No
  repair is adopted without a safe transition/lifetime design and matched whole-path controls.
- Confirmed, high confidence, performance: roughly 100 ms tails recur in a static warmed world
  and temporally align with process-wide GC pauses. Which normal client/authority paths allocate
  the approximately 5 MB/s is open; an allocation-stack capture is the next narrow diagnostic.
- The exact event behind the original 2,850.74 ms screenshot is **not identified**; it may be a
  different entry, content or capture operation. No map-travel, Inventory, shader-compilation,
  fresh-CAS or deeper continuous-streaming control was completed here. The six-second movement
  control crossed one chunk, not a sustained new-terrain route.
- One attempted movement row was excluded because focus eligibility fell to 28.63 seconds;
  its raw failed evidence remains locally under ignored `_Benchmarks/results/`. An earlier
  synthetic-key attempt was rejected because it did not set Godot's physical-key state. The
  accepted movement row has 30.00 eligible seconds and a changed authoritative position.
- No renderer, content, Game, Network, vegetation, sampler definition or HUD UI optimization
  was made. The opt-in diagnostic harness and sparse entry clocks are the only source change.

Verification on the tested implementation: Single Player Release export passed; all six actual
exported study runs passed; exported `--client-performance-smoke` passed; 37/37 Client Core
tests passed; `./verify.sh --architecture-only` passed four checks. Full canonical Release was
not rerun for this diagnostic-only checkpoint, and no earlier variable gate is relabeled green.
