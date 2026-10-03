# Local hover presented-prefab census repair — 2026-10-03

Starting HEAD: `f6dbfd7ebd24d3b6dfd4bfd451f01deb39dc336d`.
Measured implementation HEAD: `f157f2a511ed0fc255cbb483817b053169e45972`
(`1e3995630247a3d4ceb9410d3d211954457075a8` implements the production repair;
`bdab3446` makes only the opt-in post-window hitch-study page selection robust;
`f157f2a5` makes hidden chunk instances ineligible for hover).
Native removal coverage was added in test-only commit
`b35872a630b015a6560927519e64a23b95b08400` after the performance runs; it does not
change the measured client product.
Release configuration, exported Linux Single Player product, Godot 4.7.1 Mono, .NET 8.0.31,
Ryzen 7 7800X3D / RTX 4070 Ti SUPER. Full canonical Release was **not** rerun for this
isolated repair; these results must not be described as a canonical Release pass.

## Cause and repair

The previous [profiler attribution](client-hitch-profiler-attribution-2026-10-03.md) found
89.7% and 91.5% of sampled allocation bytes under `LocalCabinClient.CountPresentedPrefab`.
Every physics-service hover check recursively called `Node.GetChildren()` over the World
renderer. Godot collection, enumerator, and wrapper allocations accumulated even while the
client and authority were idle. The hidden-HUD control allocated 145.91 MiB in 30 seconds and
showed matching GC-pause/frame-tail timing.

`GodotWorldRenderer` now records committed public instance counts by prefab during native
build, visibility changes, public refresh, asset rebuild, and retirement. The hot global lookup uses a `uint` key
and allocates zero bytes in a 10,000-lookup native test. A chunk-local count additionally
prevents a semantically resident sign from hovering after its own render chunk retires while
another sign remains rendered elsewhere. Semantic proximity and final Game interaction remain
separate. Invisible public records do not nominate hover. The renderer census counts actual
primitive/fallback and direct-RID groups, which node-name traversal could miss.

The repair did not change hover frequency, GC policy, content/authority semantics, cold entry,
vegetation, or the separate `CountMeshInstances` actor-presentation path.

## Matched exported controls

Actual 1920×1080 exported Single Player, Official art already installed, vegetation Off,
F11 Performance hidden during measurement, fixed idle position, 8-second warmup and 30-second
focused eligible window. Same 49 rendered / 81 resident chunks, 293 renderer nodes, 143
resources, two Detailed actors and zero pending render builds/retirements. The client and
packet-free Local authority share one process. No affinity pinning or isolated desktop load.
Values below are whole-process/whole-client outcomes, not cost attributed exclusively to hover.

| Control | Allocated MiB / 30 s | CPU ms / 30 s | GC pause ms; Gen0/1/2 | P99 / max frame ms | >33 / >50 / >100 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Pre-repair hidden, `b337def4` | 145.91 | 7,460 | 268.43; 5/5/2 | 8.360 / 117.367 | 3/3/2 |
| Repaired [run 1](client-hover-census-repair-2026-10-03/controls/final-visibility-1/idle.context.json) | 13.351 | 6,630 | 7.575; 1/0/0 | 8.254 / 15.247 | 0/0/0 |
| Repaired [run 2](client-hover-census-repair-2026-10-03/controls/final-visibility-2/idle.context.json) | 13.352 | 6,760 | 8.334; 1/0/0 | 7.976 / 13.128 | 0/0/0 |

The two valid post-repair runs saved about **132.56 MiB per 30 seconds** (about 90.8%, or
4.42 MiB/s) against the recorded baseline. Their GC pauses and worst frames were much lower;
two runs do not establish a global bound on future hitches. The average frame rate in the
repaired controls was 143.85 FPS, but this is not an isolated renderer-FPS or broad gameplay
claim. Raw frame intervals and context, including exact committed position and resource census,
are retained beside each linked row.

Earlier exploratory runs on the first two repair commits also clustered near 13.35 MiB, but
are not part of the final pinned pair. A control lost focus and was rejected. Its retry completed measurement but
failed during *post-window* diagnostic page construction; neither was rated. The opt-in
diagnostic was then corrected to select the Performance page explicitly without modifying the
measured window or its eligibility rule.

## Post-repair allocation profile

A separate 20-second `dotnet-trace 8.0.547301 --profile gc-verbose` run on the final
implementation yielded 91 allocation ticks, 10,208,944 sampled bytes, and zero missing stacks. The old
`CountPresentedPrefab → Node.GetChildren()` path is absent: that recursive method was removed
from production source and no `CountPresentedPrefab` frame appears in the allocation summary.
The trace's largest remaining UltraSim attribution is `CountMeshInstances` (5,185,600 sampled
bytes), followed by `UpdateHud` (2,665,000 sampled bytes). These are **sampled weights**, not
exact allocations or savings estimates, and neither path was modified here. The profiled
whole-path run itself remained valid (one Gen0 and no >50 ms frame),
but its timings are not a substitute for the unprofiled controls.

The reproducible raw EventPipe capture is retained as compressed
[`idle.nettrace.gz`](client-hover-census-repair-2026-10-03/controls/final-trace-idle/idle.nettrace.gz)
(SHA-256 `80f5632d881d1622615226e19f942aa7d65bc53d7cf1e28f9a0b848993a09f4d`).
Decompress it and run `_Benchmarks/Tools/ClientHitchTraceAnalyzer` to inspect allocation stacks.
The generated `.etlx` analysis cache is not source-controlled.

## Correctness and verification

- Release Godot application/test builds: passed with zero warnings/errors.
- Native `T010PlayableWorldAcceptance`: passed. Added initial counts, zero-byte hot lookup,
  chunk hide/show, public-record visibility/removal refresh, asset fallback/primitive refresh, render-chunk retirement and return,
  full disposal, and direct-RID census controls.
- Exported Single Player `--d0-3-smoke`: passed. Sign hover and selection work; outside-radius
  hover clears; a semantically resident but retired sign cannot hover/select; re-entry restores it.
- Exported `--d0-1-smoke` render-radius/residency and `--d0-4-smoke` fallback lab: passed.
- `_Tests/UltraSim.Client.Tests` Release: 37/37 passed.
- `./verify.sh --architecture-only`: passed all four checks.
- Canonical `./verify.sh --configuration=Release`: **not run**; no status change claimed.

`CountMeshInstances` is the next measured allocation candidate, but this checkpoint deliberately
leaves it open so the before/after evidence stays attributable. The separate approximately
2.3-second cold-entry path likewise remains open.
