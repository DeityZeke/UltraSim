# Renderer Impostor LOD / Far-Field Cards

Status: TENTATIVE renderer direction; exact shader, atlas format, and transition thresholds remain EXPLORING

Primary references:
- [Terrain / World plan](../../Docs/Plans/TERRAIN.md)
- [Godot client renderer](../../Client/Godot/README.md)
- [Definition / presentation / asset independence](definition-presentation-asset-independence.md)
- External reference: [Imposter Cards Nacho Time](https://superhivemarket.com/products/imposter-cards-nacho-time?search_id=47206348)
- External video reference: https://www.youtube.com/watch?v=qBPuZ-1StZc

Related topics:
- [Visual Art / Lighting / Cabin Benchmark](../canonical/visual-art-lighting-and-cabin-benchmark.md)
- [Movement / Camera / Control](../../Docs/Plans/MOVEMENT.md)

## Why this fits UltraSim

The current Godot renderer already realizes frozen/public compatible groups as chunk-local Godot MultiMeshes. One World/render chunk may contain multiple compatible groups because one MultiMesh repeats one mesh/material realization; heterogeneous objects are not forced into one MultiMesh.

A far-field impostor system therefore fits the existing renderer ownership model naturally:

~~~text
semantic/static records
    ↓
chunk-local renderer preparation
    ↓
near compatible MultiMesh group(s)
or
far impostor MultiMesh group(s)
~~~

The impostor is renderer-owned presentation. It does not change DefinitionId, collision, bounds, interaction semantics, Multi identity, persistence, or other Game truth.

## Candidate technique

The current preferred reference is the Nacho Time style rather than a conventional many-angle sprite-sheet impostor:

- one camera-facing plane;
- six orthogonal source captures: front/back/left/right/top/bottom;
- object-space / incoming-view vector reconstruction to create pseudo-3D perspective;
- shared atlas/material across many unique assets;
- normal information so distant proxies still respond to direct lighting;
- optional richer material maps and optional higher-fidelity multi-plane/depth variants where earned.

The attraction is not merely triangle reduction. The technique may reduce geometry, material/state changes, and submission cost simultaneously.

Do not freeze the Blender add-on itself as a runtime dependency. Blender or another offline tool may be an authoring/baking source, while UltraSim owns its runtime artifact/shader contract.

## Chunk/ring candidate

The game spatial profile is currently fixed at 16 m × 16 m World chunks. Renderer chunks are private and may aggregate/subdivide that grid later, but current Godot static realization is chunk-local.

A promising first policy is:

~~~text
player/home render chunk = n

n and n+1 ring
    real 3D realization

n+2 and farther
    impostor candidate
~~~

This is attractive because a ring-2 chunk is roughly one full 16 m chunk width beyond the nearest adjacent ring, putting the transition near the distance where the reference technique is reported to become difficult to notice.

However, this must not be frozen as a pure chunk-index rule.

If the observer is near the edge of the home 16 m chunk, an axis-aligned ring-2 chunk can begin only about 16 m from the observer. Therefore a claimed ~18 m visually-safe switch is not guaranteed by “ring 2” alone.

Preferred first implementation policy:

~~~text
chunk ring
    coarse candidate / streaming scope

minimum camera-to-render-chunk AABB distance
or object projected size
    actual LOD decision

hysteresis / overlap
    prevents popping around threshold
~~~

For example, n+2 may have both realizations prepared/available while the renderer keeps real 3D until the conservative distance threshold is crossed, then retires/fades to impostor. The exact threshold must be measured with UltraSim art and camera profiles rather than copied from the reference video.

## Do not conflate renderer LOD with server AOI tiers

The Game/Network Immediate/Near/Far interest tiers are semantic publication/subscription cadence policy. Current Game Near is much larger than the proposed impostor transition.

Renderer LOD is private client policy.

Use terms such as “real-3D render ring”, “impostor ring”, or “render LOD tier” rather than reusing authoritative Near/Far tier names.

Client rendering may cull/retier more precisely than server chunk admission.

## MultiMesh / atlas shape

A shared impostor atlas/material makes one or a small number of far impostor MultiMesh groups per render chunk plausible.

Do not join an entire Realm into one giant MultiMesh solely to chase one draw call. Spatially bounded groups preserve chunk streaming, frustum culling, visibility bounds, and retirement.

Candidate shape:

~~~text
RenderChunk
    terrain mesh
    near compatible static MultiMesh groups
    far impostor Atlas-A MultiMesh
    far impostor Atlas-B MultiMesh
    vegetation MultiMesh
~~~

The exact atlas grouping policy remains open and should be measured against draw count, VRAM, culling granularity, and rebuild cost.

## Large Multi / property aggregation

Far LOD may eventually aggregate a logical structure into one impostor realization:

~~~text
near cabin
    wall/floor/roof/deco pieces

far cabin
    one aggregate cabin impostor
    + separately meaningful dynamic pieces if needed
~~~

This is especially promising for buildings, tree clusters, ruins, rock formations, shelves/clutter groups, and other mostly-static authored Multis.

Dynamic/meaningfully mutable pieces must not be visually lied about. A door, destroyed wall, harvested tree, or persistent structural edit may remain separate, invalidate an aggregate, or require another explicit strategy.

## Camera interaction

This direction becomes more useful if OrbitCommand permits large zoom ranges.

As the camera zooms out, more objects become visible; renderer cost per object should decrease correspondingly:

~~~text
close
    fewer visible objects
    expensive per-object realization

far / simulation view
    many visible objects
    cheap impostor / aggregate realization
~~~

The candidate must be tested across the intended camera range, not only one fixed isometric view.

## First proof

Suggested representative objects:

- pine tree;
- irregular rock;
- rectangular prop/crate;
- full Cabin Multi.

Compare:

- full mesh;
- current compatible MultiMesh;
- single-card six-view impostor;
- optional higher-fidelity multi-plane/depth form where justified.

Measure:

- CPU submission time;
- GPU frame time;
- draw count;
- triangle count;
- atlas/VRAM footprint;
- culling/rebuild cost;
- visible transition distance and angle;
- lighting/shadow mismatch;
- orbit/zoom robustness.

The ~18 m “transition virtually disappears” observation from the reference is a useful hypothesis, not an UltraSim acceptance threshold until our own art/camera benchmark reproduces it.
