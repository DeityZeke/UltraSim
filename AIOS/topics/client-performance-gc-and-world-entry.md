# Client Performance, GC Allocation Debt, and World Entry

Status: MIXED — accepted measured evidence and focused repair; residual GC/allocation debt OPEN; staged loading UX EXPLORING

Primary references:
- [Initial hitch characterization](../../_Benchmarks/Baselines/client-hitch-characterization-2026-10-02.md)
- [Profiler attribution](../../_Benchmarks/Baselines/client-hitch-profiler-attribution-2026-10-03.md)
- [Local hover census repair](../../_Benchmarks/Baselines/client-hover-census-repair-2026-10-03.md)
- [Godot client](../../Client/Godot/README.md)

Related topics:
- [Godot / .NET native shutdown lifetime](godot-dotnet-native-shutdown-lifetime.md)
- [Definition / presentation / asset independence](definition-presentation-asset-independence.md)
- [Spatial-space / map transitions](spatial-space-map-transitions.md)

## Current understanding

UltraSim now has an exported-client wall-frame performance HUD and raw frame capture path. Frametime is the primary pacing metric; FPS is derived. The tooling retains real gameplay/content/render hitches and excludes focus/minimized gaps.

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

## Cold world-entry discontinuity

A separate issue remains: synchronous Local Concept Demo entry can block presentation for roughly seconds even when the warmed world itself is healthy.

Accepted characterization on the exported product found approximately:

- Official-art entry: ~2.3 s blocked frame;
- Primitive entry: ~1.0 s;
- Local authority create/readiness: ~0.63 s;
- optional presentation/art preparation: ~1.28 s;
- initial native world projection: ~0.27 s;
- initial presentation convergence: ~0.09 s.

These are a separate lifecycle/loading problem from warmed-idle GC debt.

## Loading / warm-up UX under exploration

An explicit loading page/bar is an active direction worth retaining, but it is not yet a canonical implementation contract.

The useful product shape discussed is:

```text
shell / prior world
    -> loading page
    -> prepare authority + content + presentation + world
    -> warm / converge required initial state
    -> drop player into the world
    -> enable control only after Ready
```

During loading, the UI may surface truthful current work such as:

- loading meshes;
- loading textures/materials;
- loading map/structural data;
- building native map/world projection;
- applying presentation/profile state;
- waiting for authority/readiness;
- warming/converging required presentation.

Exact stage names, ordering, and whether a numeric percentage can be honest remain open. Prefer a truthful stage/progress model over a fake percentage. A loading screen is allowed to make a short 1–2 second transition legible today while remaining valid if content later makes the transition longer.

The loading surface must not imply that Game control is live before authoritative and presentation admission are complete. Back/cancel/failure should deterministically retire partially prepared authority, leases, native resources, renderer state, and UI transition state rather than relying on finalizers.

## Open questions

- Should entry progress be stage-only, weighted stage progress, or exact work-unit progress where bounded counts are available?
- Which preparation can safely run off the Godot owner thread, and which native realization must remain owner-thread/budgeted?
- Should presentation resource preparation be separated from the current resolver lifetime so lookup and eager realization are not one constructor responsibility?
- At what actor/load scale does the remaining `CountMeshInstances` allocator become user-visible enough to justify its own repair?
- How should the same loading-state model map onto future Remote multi-space travel without reconnecting or exposing a half-entered world?
