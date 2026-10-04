# Client Performance, GC Allocation Debt, and World Entry

Status: MIXED — hover allocation repair and staged Local entry ACCEPTED; residual GC/allocation debt and Remote destination loading OPEN

Primary references:
- [Initial hitch characterization](../../_Benchmarks/Baselines/client-hitch-characterization-2026-10-02.md)
- [Profiler attribution](../../_Benchmarks/Baselines/client-hitch-profiler-attribution-2026-10-03.md)
- [Local hover census repair](../../_Benchmarks/Baselines/client-hover-census-repair-2026-10-03.md)
- [Staged Local world-entry evidence](../../_Benchmarks/Baselines/client-staged-world-entry-2026-10-03.md)
- [Exported-client VSync pacing characterization](../../_Benchmarks/Baselines/client-vsync-pacing-2026-10-04.md)
- [Godot client](../../Client/Godot/README.md)

Related topics:
- [Godot / .NET native shutdown lifetime](godot-dotnet-native-shutdown-lifetime.md)
- [Definition / presentation / asset independence](definition-presentation-asset-independence.md)
- [Spatial-space / map transitions](spatial-space-map-transitions.md)

## Current understanding

UltraSim now has an exported-client wall-frame performance HUD and raw frame capture path. Frametime is the primary pacing metric; FPS is derived. The tooling retains real gameplay/content/render hitches and excludes focus/minimized gaps.

The exported HUD and JSON now also report effective Godot VSync mode, `Engine.MaxFps`, screen refresh, display server, rendering method/driver, and display adapter. A matched Official-art, vegetation-Off, warmed-idle Single Player control on 2026-10-04 found default VSync **Enabled** at 144.02 average FPS against a 143.83 Hz reported display. The same exported binary with Godot `--disable-vsync` reported **Disabled** and 1,256.31 average FPS, with CPU time rising from 2.10 to 10.32 seconds per ten-second wall window. Thus the ~144 FPS observation on this host was Godot presentation pacing, not a measured client throughput ceiling or an externally enforced 144 FPS cap. The host was KDE Wayland with Godot using X11/Xwayland and NVIDIA OpenGL Compatibility. This does not measure GPU time or display-delivered frames. No product default, OS setting, or sampler semantics changed; no setter fallback was needed. The linked report retains raw controls and the rejected initial over-capacity capture.

A warmed-idle investigation found a concrete managed-allocation/GC defect in Local sign hover. Physics-service hover repeatedly called `CountPresentedPrefab`, recursively traversing the Godot scene tree through `Node.GetChildren()`. Two hidden-HUD allocation traces attributed 89.7% and 91.5% of sampled allocation bytes to that path.

The accepted focused repair moved presented-prefab knowledge into a renderer-owned retained census. In matched exported hidden-HUD controls, allocation fell from 145.91 MiB/30 s to 13.351–13.352 MiB/30 s, GC pause from 268.43 ms to 7.575–8.334 ms, and >100 ms frames from two to zero. The old `CountPresentedPrefab -> GetChildren()` allocation stack disappeared. Full canonical Release was not rerun for that isolated repair, so canonical Release status is unchanged.

This establishes a durable performance-debt lesson: steady physics/render-adjacent paths should not rediscover retained native/presentation state through allocation-heavy scene-tree polling when the owning subsystem can retain and invalidate the needed fact directly.

## Remaining GC / allocation debt

The major warmed-idle hover allocator is repaired, but GC/allocation debt is not considered globally closed.

Measured remaining candidates include:

- `CountMeshInstances` under actor presentation, which still recursively enumerates Godot children and became the largest remaining UltraSim allocation attribution in the post-repair trace;
- ordinary HUD/status text and other smaller diagnostic allocations;
- any future hot-path managed/native-wrapper churn discovered by the same frame/GC tooling.

The post-repair idle controls were healthy enough that `CountMeshInstances` is currently recorded as measured hot-path hygiene debt rather than a proven user-visible stutter cause. It should not be bundled into unrelated work merely because it is now the tallest remaining allocation stack.

Do not infer savings directly from sampled allocation weights. Prefer matched exported before/after controls and prove the blamed stack disappears.

## Cold world-entry discontinuity and adopted repair

A separate issue was synchronous Local Concept Demo entry blocking presentation for seconds even when the warmed world itself was healthy.

Accepted characterization on the exported product found approximately:

- Official-art entry: ~2.3 s blocked frame;
- Primitive entry: ~1.0 s;
- Local authority create/readiness: ~0.63 s;
- optional presentation/art preparation: ~1.28 s;
- initial native world projection: ~0.27 s;
- initial presentation convergence: ~0.09 s.

The accepted Local repair retains a modal loading page until a real Ready boundary. Authority
construction and immutable content preparation run as owned worker work; Godot native realization
stays owner-thread and initial chunks are built in frame slices. A finite engine-neutral progress
model reports truthful stages and exact units only where known. Input/physics is not admitted until
authority, initial semantic/native working set, controlled actor, UI/Inventory and presentation
converge. Cancel/failure retires partial ownership. The loading lifecycle remains a product feature
even if future work makes entry shorter.

On exported Single Player controls, three Official entries were 2,281–2,509 ms start-to-Ready
with 79.668–131.343 ms maximum eligible frames (two >100 ms frames in one run); Primitive was
991 ms with a 50.963 ms maximum. The older
pre-repair blocked frames were 2,339.298 ms and 1,032.374 ms respectively. These comparisons
prove that the multi-second presentation block was removed, **not** that total entry became
faster: the new start-to-Ready scope includes asynchronous admission and frame pacing. Actual
loading-window evidence, post-Ready gameplay checks, cancellation at authority/mesh/world stages,
retry, failure retirement, native ownership and focused verification are in the linked report.
The full canonical Release verifier was not rerun for this focused checkpoint.

## Loading / warm-up boundary

The staged Local entry and its loading page are implemented product behavior. The progress
type is intentionally renderer-independent so future Remote destination acquisition can use
the same vocabulary; no Remote travel implementation is claimed.

The current Local product flow is:

```text
shell / prior world
    -> loading page
    -> prepare authority + content + presentation + world
    -> warm / converge required initial state
    -> drop player into the world
    -> enable control only after Ready
```

During loading, the UI surfaces truthful current work such as:

- loading meshes;
- loading textures/materials;
- loading map/structural data;
- building native map/world projection;
- applying presentation/profile state;
- waiting for authority/readiness;
- warming/converging required presentation.

Bounded mesh/chunk preparation exposes completed/total units. Unknown-duration work remains
indeterminate; no fake weighted percentage is used. The loading screen is deliberately retained
for short as well as long transitions.

The loading surface does not imply Game control is live before authoritative and presentation
admission. Cancel/failure deterministically retires partially prepared authority, leases, native
resources, renderer state and UI transition state rather than relying on finalizers.

## Open questions

- How should Local entry budgets adapt to slower hardware and much larger initial working sets?
- At what actor/load scale does the remaining `CountMeshInstances` allocator become user-visible enough to justify its own repair?
- How should the same loading-state model map onto future Remote multi-space travel without reconnecting or exposing a half-entered world?
