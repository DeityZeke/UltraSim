# Exported-client hitch profiler attribution — 2026-10-03

This is a **diagnosis, not an optimization or a new FPS rating**. It follows the
[initial hitch characterization](client-hitch-characterization-2026-10-02.md).
Starting HEAD was `c6c218b04495c7981a375b41abd1165c22f23e13`; the tested diagnostic
implementation is `b337def49435b547459cbd9f0fd34bd53ee5f514`. No gameplay, renderer,
content, vegetation, sampling definition, or GC policy was changed. The new opt-in
`--client-hitch-hide-debug` control leaves the F11 Performance page closed during measurement;
its export-only snapshot is constructed afterward. The `TraceEvent` allocation reader and
retained-.NET-perf-map CPU reader are under `_Benchmarks/Tools/ClientHitchTraceAnalyzer/`.

## Matched exported-product control

Actual Linux Single Player export, Official art already installed, vegetation Off, idle at the
same position, 49 rendered chunks, 81 resident chunks, 293 renderer nodes, 143 resources,
two Detailed actors, zero pending builds/retirements. Each run had eight seconds warmup and a
30-second focused eligible window at 1920×1080, Vsync Enabled. The client and its packet-free
Local authority shared one process. Ryzen 7 7800X3D, RTX 4070 Ti SUPER, Godot 4.7.1 Mono,
.NET 8.0.31, Linux 7.2.6-1-cachyos; no affinity pinning or isolated desktop workload.

| F11 Performance | Frames / eligible seconds | Allocated MiB | Process CPU ms | GC pause ms / Gen0,1,2 | P99 / max ms | >50 / >100 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Visible | 4,280 / 30.004 | 151.54 | 7,540 | 267.75 / 5,5,2 | 8.307 / 114.907 | 3 / 2 |
| Hidden | 4,281 / 30.005 | 145.91 | 7,460 | 268.43 / 5,5,2 | 8.360 / 117.367 | 3 / 2 |

The visible-page **configuration** allocated about **5.63 MiB more per 30 seconds** in this
final pair; the mode also changes which ordinary small diagnostic panel is visible, so this is
not an isolated byte count for one HUD method. Two earlier
alternating pairs on identical production source showed hidden 145.91–145.92 MiB versus visible
151.58–151.65 MiB, the same 5/5/2 collections, and similar approximately 114–119 ms maxima.
Those four exploratory JSON files stamped the *starting* HEAD even though the in-progress
diagnostic patch was present; they are not the provenance-correct raw baseline. The final
pair, exported and stamped after the diagnostic commit, is preserved under
[`client-hitch-profiler-attribution-2026-10-03/`](client-hitch-profiler-attribution-2026-10-03/).
The small difference does not explain the roughly **146 MiB per 30 seconds** remaining with
F11 hidden, or remove the large warmed-idle tails.

The hidden final run's >50 ms frames ended at approximately 2.36, 12.22 and 23.60 seconds
from measurement start (78.3, 117.4 and 103.4 ms). Its 4 Hz process-wide GC counter recorded
pause increments in the corresponding bins: 41.4, 109.1 and 95.1+22.8 ms. The visible final
run showed the same three-way alignment. This supports GC as a major cause of the recurrent
idle tail; 250 ms counter resolution cannot assign every millisecond of a frame to GC.

## Allocation-stack finding

Two separate hidden-page `dotnet-trace 8.0.547301 --profile gc-verbose` 20-second captures
were readable. The first recorded 839 allocation ticks / 89,308,144 **sampled** bytes, all
with stacks; 80,123,776 sampled bytes (89.7%) had
`LocalCabinClient.<CountPresentedPrefab>g__Count` as their first UltraSim frame. The second
recorded 944 ticks / 105,666,504 sampled bytes, all with stacks; 96,727,760 sampled bytes
(91.5%) had the same frame. These are sampled attribution weights, **not** exact process
allocation totals or predicted savings.

The first trace's largest types beneath that frame were weak-reference dictionary nodes
(24.37 MB), `GetEnumerator<Godot.Node>` state (16.39 MB), `WeakReference<IDisposable>`
(13.19 MB), Godot array wrappers (10.54+9.15 MB), and `Godot.StringName` (6.49 MB).
The source path is concrete:
`LocalCabinClient._PhysicsProcess` → `UpdateWorldHover` →
`IsKnownPresentedPrefabNear(InformationalSign)` → `CountPresentedPrefab` → recursive
`Node.GetChildren()` over the renderer root. The recurrence is per physics service while the
pointer has a valid world point, even in a stationary world. The scan creates native-backed
managed collection/wrapper objects. `CountMeshInstances` under actor presentation is a smaller
secondary stack (3.30 MB sampled in the first trace); HUD text is about 2.02 MB sampled.

**Confirmed performance finding, high confidence:** ordinary Local hover polling, not the F11
page, dominates this capture's warmed-idle managed allocation. This is a source/stack finding,
not yet an accepted rewrite or proof that eliminating the scan would eliminate all GC pauses.
A focused follow-up should replace the per-service scene-tree existence scan with already
retained, correctly invalidated semantic/presentation knowledge, then prove exact hover and
selection behavior, wrapper retirement, and matched whole-path frame/GC results. Native wrapper
lifetime deserves review, but this capture alone does not establish a shutdown defect.

The first `gc-verbose` attempt on the earlier checkpoint with `dotnet-trace 10.0.745401`
produced an unreadable trace. Version 8.0.547301 produced two complete traces here; the
version difference is an observed workaround, not a proven root cause of the older trace damage.
The second trace's surrounding hitch-study run lost window focus at its end; its trace is used
for stack corroboration, **not** a whole-window performance rating.

## `perf` and cold entry

`perf record -F 997 -e cycles:u -g --call-graph dwarf,4096` captured 8,259 CPU samples with
zero lost samples on a separate cold Official-art entry. A retained .NET JIT perf map identified
the main client thread and `UltraSim Local` authority thread separately. In the broad
3–5-second recording interval covering entry, 747 samples belonged to the main thread and
302 to the local authority thread; tiered compilation and native crypto were also active.
These are **CPU samples**, not a frame-duration decomposition. The published Godot executable
lacks sufficient native symbols for a trustworthy native-function breakdown, and CPU sampling
does not measure time blocked on another thread or GPU. The perf run lost focus near the end,
so it is diagnostic only; its earlier opt-in entry clocks did complete: authority ready
657.0 ms, optional art 1,281.4 ms (presentation generation 397.6 ms, world asset resolver
680.2 ms), native projection 278.0 ms, initial convergence 100.2 ms, `StartFreshWorld`
2,323.1 ms. Those reproduce the prior independent unprofiled 2.34-second entry finding,
but profiler-perturbed times are not a new rating.

The valid managed CPU trace was also inspected. Its all-thread `topN` mixes sleeping worker
stacks, runtime trace initialization and the main thread, so those percentages are **not**
reported as a main-thread cost or savings estimate. The existing sparse clocks and earlier
main-thread managed sample are the stronger cold-entry attribution.

## Verification and remaining work

- Re-exported Single Player Release on the diagnostic commit; final visible/hidden runs exited
  successfully, with fixed actor position and renderer counts.
- Exported `--client-performance-smoke`: passed.
- `_Tests/UltraSim.Client.Tests` Release: 37/37 passed.
- `./verify.sh --architecture-only`: four checks passed.
- Full canonical Release was **not** rerun for diagnostic-only code. No prior variable gate is
  relabeled green. No optimization was adopted.

The exact 2,850.74 ms screenshot event still lacks a timestamp and remains unidentified.
Map travel, new-chunk streaming, Inventory, shader compilation and other rare >100 ms events
are not exonerated by this idle control. A future fix should treat cold-entry architecture and
warmed-idle allocation/GC as separate tasks. The raw `.nettrace`, `.perf.data`, and exploratory
runs remain locally under ignored `_Benchmarks/results/client-hitch-profilers-2026-10-02/`;
the small provenance-correct unprofiled pair is tracked beside this report.
