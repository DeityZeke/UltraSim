# Vegetation Scatter v2

Status: TENTATIVE product presentation policy / mechanically accepted focused implementation; canonical Release gate open

Primary evidence:
- [Vegetation scatter v2 evidence](../../_Benchmarks/Baselines/client-vegetation-scatter-v2-2026-10-02.md)
- [F1.11 original vegetation evidence](../../_Benchmarks/Baselines/client-f1.11-renderer-stress-vegetation-2026-09-25.md)

## Current implementation

The opt-in presentation-only grass prototype now uses a deterministic, space-aware 2 m density
field and wider per-patch scatter rather than near-uniform patch occupancy.

Current v2 characteristics:

~~~text
density field
    deterministic
    space-aware
    2 m scale

per selected patch
    12 varied blades
    wider physical spread

per active grass chunk
    one bounded MultiMesh

hard bound
    maximum 1,024 patches/chunk
~~~

Source/void filtering, settings ownership and explicit native retirement remain unchanged.

The vegetation remains presentation-only. It does not change Game terrain, collision, movement,
resource gathering, regrowth or authoritative vegetation truth.

## Focused work-count result

In the accepted native radius-3 fixture over the same 49 chunks:

~~~text
v1 patch instances      50,175
v2 patch instances      19,350   (-61.4%)

v1 logical blades      401,400
v2 logical blades      232,200

v1 submitted vertices 4,816,800
v2 submitted vertices 2,786,400 (-42.2%)
~~~

This is a reduction in prepared/rendered work counts, not an FPS, GPU-time or hardware-target claim.

## Visual result

The v2 distribution produces visibly clustered grass with meaningful exposed ground between
clusters. This is a clear improvement over the prior near-uniform spike carpet.

Human review still considers the blade-level realization prototype-grade. Remaining visual issues
include:

- very upright / needle-like blade silhouettes;
- dark repeated strokes at ordinary gameplay distance;
- limited local blade lean/shape variation;
- some clusters still reading as renderer bundles rather than natural vegetation.

Do not hide those issues merely by making the material more transparent. Distribution is improved;
blade realization remains a separate visual tuning problem.

## Product default

Vegetation remains **default Off**.

The setting is discoverable under Advanced / Experimental Vegetation and takes effect on the next
world entry.

Default Off is still the evidence-backed decision because v2 is useful for testing but is not yet
the intended finished vegetation presentation.

## Canonical verification status

The implementation commit is `99a1a53a8f125edf4a6e4026ac8eed59bbed81bc`.

Focused native, exported Cabin, Online export, PF1 and PF2 controls passed independently.

Four unchanged-source attempts at the full canonical Release verifier stopped at different W7,
Online-export or PF1 swarm gates. Those focused gates subsequently passed. No network/test gate was
weakened and there is **no uninterrupted canonical Release pass for this implementation yet**.

Therefore this vegetation work must not be represented as P9 closure or a canonical-green product
tree until the full gate completes uninterrupted.

## Physics finding discovered during this work

A separate +100 mm static-platform probe exposed an open Simple Physics grounded-step/contact gap.

The current P9 Cabin uses a flush floor as valid authored content, but that does not repair or close
the generic raised-platform behavior.

Permanent Physics work should retain a fixture covering at least:

~~~text
terrain → +100 mm platform → terrain
both directions
straight + diagonal
small + large movement increments
repeated crossings
step-limit boundary
real wall / low ceiling / narrow support controls
~~~

Do not shrink actor collision, allow solid penetration or special-case Cabin content to make the
probe green.

## Online multi-space relationship

This vegetation pass does not start or complete Online multi-space travel.

The Game/Local multi-map foundation remains accepted separately. Production Remote/Online still
needs a space-aware structural and owner-state lifecycle plus a real two-client different-space
acceptance.

## Promotion criteria

Do not promote vegetation v2 to the default presentation until evidence establishes both:

1. visual quality appropriate for normal representative play; and
2. an uninterrupted canonical Release pass on the selected implementation tree.

Additional GPU/frame-time evidence may inform tuning but is not required merely to preserve the
current default-Off prototype.
