# Staged Local world entry — 2026-10-03

Starting clean HEAD: `6988139a0cbdcc3e4d93c60534e372f43c2add92`.
Tested implementation HEAD: `d7ff79a4d7c1fb172dae659afeb7c890710516ba`.
Implementation commits: `520c331f` (staged entry), `57c316bf` (modal Help and readable stages),
`9096e00e` (remove misleading viewport-only capture), `09b2ce5a` (same-session
post-Ready hover/selection acceptance), `d05f30f9` (successful entry, clean retirement,
same-process staged re-entry), `d7ff79a4` (distinguish successful Ready time from
cancel/failure terminal time in JSON). This report and its linked controls
are evidence, not a claim that full canonical Release was run.

## Product result

Normal Single Player Concept Demo Join now enters a dedicated, modal loading page. A finite
Client Core `ClientWorldEntryProgress` reports truthful stages, optional exact work units,
Cancel and terminal state; unknown work says “Working…” rather than displaying a fake percent.
The loading page remains a product lifecycle even if later optimizations make entry short.
The [actual exported-window capture](client-staged-world-entry-2026-10-03/loading-window-stage.png)
shows the content-acquisition stage with Cancel and no competing session Help overlay.

The Local authority factory and immutable verified presentation preparation run as owned
worker tasks. Actual task completion/result is observed even on cancellation; a late-created
session is disposed. Authority readiness is polled while frames continue. Godot material,
mesh, node and renderer work stays on the owner thread; optional meshes and initial chunks
are built in explicit units/frame slices. The optional world asset resolver no longer eagerly
realizes all eleven meshes in one constructor call during staged entry. Partial native and
content owners retire deterministically on cancellation/failure, without finalizer cleanup.

The Ready boundary requires the Local authority and initial view, correct spatial state and
semantic residency, initial native chunk projection with no pending initial builds/retirements,
controlled actor and public presentation convergence, plus the required UI/Inventory/profile
state. Gameplay physics/input remains disabled until this boundary; the shell then changes
from Loading to InSession. Ready does not require distant optional content to be fully loaded.
The old synchronous path remains only for existing direct acceptance fixtures.

## Exported before/after

Linux Release Single Player, Godot 4.7.1 Mono/OpenGL Compatibility, .NET 8,
Ryzen 7 7800X3D / RTX 4070 Ti SUPER; 1920×1080 output, vegetation Off. Both studies used
an already installed Official package/cache and a fresh product process/session, not a fresh
empty CAS. The before control is the [2026-10-02 hitch study](client-hitch-characterization-2026-10-02.md)
on `40c661a406a592e3787e7eeacbaea849bc773f40`; the after controls are
[raw JSON](client-staged-world-entry-2026-10-03/controls/) and two
[Official replicates](client-staged-world-entry-2026-10-03/replicate-02/official.json)
([third run](client-staged-world-entry-2026-10-03/replicate-03/official.json))
on the tested implementation.
The old wall number is its synchronous `StartLocalCabin` duration, while the new number is
start-to-Ready including frame-paced admission; these are close but not identical scopes.
Maxima are player-visible eligible wall-frame intervals, not GPU or exclusive CPU times.

| Presentation | Before synchronous entry wall | Before max frame | After start-to-Ready wall | After max frame | After >100 ms frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| Official, 3 runs | 2,330.0 ms | 2,339.298 ms | 2,280.684–2,508.573 ms | **79.668–131.343 ms** | 0, 0, 2 |
| Primitive, 1 run | 1,022.9 ms | 1,032.374 ms | 990.610 ms | **50.963 ms** | 0 |

The old 1–2+ second *blocked frame* is gone. Official total entry is not faster in this
control; the value of this first pass is continuing presentation and a real loading lifecycle.
The three Official runs respectively observed 131.343, 79.668 and 83.919 ms maxima.
The first had 242 eligible frames, 12 over 50 ms and two over 100 ms; its maximum first rose
to 100.794 ms in content preparation and then to 131.343 ms during mesh preparation. These
residual spikes are real and are not hidden by the loading screen. Primitive had 123 eligible
frames and one over 50 ms. This is not an established FPS or GPU rating. Stage durations in the
first Official run were approximately authority 616 ms, content 625 ms, materials 31 ms,
meshes 747 ms, native world 320 ms, warm presentation 108 ms and finalization 10 ms;
these are wall stage durations and include frame pacing.

Both exported after runs reached 49 rendered chunks and two actors. The same-session post-Ready
D0.3 sign-hover/selection/UI acceptance and D0.0 gameplay acceptance passed in Official and
Primitive modes (movement, pickup/equipment, Inventory and existing interaction controls).
The separate exported D0.5 guest admission
passed with the staged path, YBot body and Chat focus. UI modal/native session tests verify
loading blocks gameplay input before Ready.

## Cancellation, failure and retirement

Three exported cancellation points were accepted: authority startup (640 ms to menu),
mesh preparation (1,318 ms), and world projection (2,150 ms). Each returned to MainMenu,
left no active world/catalog child/gump/session and successfully retried to Ready. Their
maximum observed frame intervals were 203.693, 84.716 and 96.765 ms respectively; the
authority-cancel interval includes synchronous retirement and is a future pacing lead.
The injected PreparingContent failure reached Error in 662 ms, with clean transient residue.
Its required invocation includes `--client-entry-fail-stage=PreparingContent`; an earlier
invocation omitted that flag and is not an accepted failure control. The JSON controls retain
the exact tested SHA and per-stage timing. `TerminalWallMilliseconds` applies to every outcome;
`ReadyWallMilliseconds` is null for cancelled/failed entry. The separate `visual/official.json` belongs only
to the window screenshot run and is not used as the comparative frame rating.

Repeated enter/leave is covered by both Official and Primitive exported controls: after the
post-Ready gameplay journey, the first world retires to the exact baseline, then a second
staged entry reaches Ready and retires cleanly in the **same process**. The three
cancel-then-reenter controls add separate partial-entry coverage. Native tests retain strong
mesh/material wrappers across partial resolver disposal and require native instance retirement.
Persistent cache bytes are intentionally not required to become empty on session exit.

## Verification

- `dotnet run --project _Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj -c Release`:
  38/38 passed, including typed/monotone/bounded progress.
- `dotnet run --project _Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj -c Release`:
  41/41 passed, including worker-prepared generation ownership.
- `dotnet run --project _Tests/UltraSim.Client.Local.Tests/UltraSim.Client.Local.Tests.csproj -c Release`:
  25/25 passed. A concurrent-build XML copy retry warning occurred in an initial parallel run;
  an isolated rerun passed 25/25 without that warning.
- Native headless F111 renderer-stress, T010 playable-world and S08 UI/Help acceptance scenes:
  each passed with exit code zero on the final implementation (partial resolver/strong-wrapper,
  content, and modal Help checks respectively).
- `Client/Godot/export-client.sh SinglePlayer`: passed on tested implementation; all six
  SHA-bound exported controls passed with exit code zero when invoked with their required flags.
  Exported `--d0-5-smoke` passed after the final implementation commit.
- `./verify.sh --architecture-only`: all four checks passed after the report/docs were present.
  A preliminary post-doc invocation correctly rejected the report link before this file existed;
  that transient authoring error was corrected, not counted as a gate pass.

The full canonical `./verify.sh --configuration=Release` was **not run** for this focused
Local entry change. No focused result is relabeled a canonical Release pass. No change was
made to the hover census, `CountMeshInstances`, GC policy, vegetation, Game/Network protocols,
remote map travel or the old W7/PF1 variability. Remote destination loading and adaptive budgets
for much larger/slower worlds remain open.
