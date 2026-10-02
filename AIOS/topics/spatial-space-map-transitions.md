# Spatial-Space / Multi-Map Transition Foundation

Status: CANONICAL Game + Local foundation; Online/Remote product path incomplete

Primary evidence:
- [P9 spatial-space foundation](../../_Benchmarks/Baselines/framework-p9-spatial-space-foundation-2026-09-30.md)
- [Game World](../../Game/World/README.md)

Related:
- [WorldPack composition direction](../../Game/World/README.md)
- [Cabin visual benchmark](../canonical/visual-art-lighting-and-cabin-benchmark.md)

## Core distinction

A WorldPack boundary is not a map transition.

~~~text
same SpatialSpaceId
    different WorldPackCoord
        = ordinary structural streaming/storage boundary

different SpatialSpaceId
        = explicit spatial-world / map transition
~~~

WorldPack boundaries must remain semantically boring.

## Authoritative location

The current Game foundation represents one entity's spatial address as:

~~~text
WorldLocation
    SpatialSpaceId
    WorldPosition
~~~

Equal coordinates in different spaces do not meet.

The ECS component historically named `WorldPositionComponent` now persists that complete
location as schema version 2. Version-1 saves restore into `DefaultSpace`.

This is a compatibility name, not permission to treat position as space-independent.

## Current admitted-space model

One Game generation may currently admit up to eight prepared structural spaces.

That is a finite current-product bound, not a declared universal limit.

Each admitted space has its own prepared structural root. The Game authority can keep different
actors in different spaces simultaneously.

Space-aware behavior now includes the accepted Game/Local paths for:

- spatial index membership;
- public spatial publication;
- collision / occupancy;
- movement support;
- interaction range;
- ground-item/container spatial checks;
- spatial Chat;
- persistence validation.

## Actor transition

The canonical cross-space operation validates the destination before changing the actor's address.

Conceptually:

~~~text
current actor
    ↓
validate destination space exists
    ↓
validate support / collision / occupancy
    ↓
commit one WorldLocation replacement
    ↓
spatial publication/index reconcile
~~~

The transition does not recreate:

- ECS actor identity;
- persistent actor identity;
- participant/session;
- Inventory;
- Equipment.

Teleport content names semantic destination space + position. It does not name a client scene,
WorldPack filename, or renderer resource.

Same-space relocation remains a separate ordinary operation.

## Persistence

Save data now includes the current SpatialSpaceId with position.

A staged restore into an unadmitted space rejects before replacing the live generation.

Version-1 persisted position data is explicitly interpreted as `DefaultSpace`.

The accepted Local proof saves/reloads while the actor occupies the two-room Cabin space.

## Local product proof

The normal Single Player Concept Demo admits:

~~~text
Space 1
    Classic Concept Demo / Cabin

Space 2
    two-room Cabin Visual Benchmark v1
~~~

A normal Game telepad near the Classic Cabin travels to Space 2 and a return telepad travels back.

The accepted native journey performs:

~~~text
Classic
    → Benchmark
    → Classic
    → Benchmark
    → Classic
~~~

while preserving actor/session/Inventory truth.

Local client presentation changes its structural/semantic world binding and explicitly retires
the previous native world renderer. Old-space semantic chunks and native render resources must not
survive the transition.

This is now the preferred hands-on path for visual review; the older
`--p9-two-room-benchmark` launch control remains a bounded visual-laboratory automation path rather
than the normal way a player enters the benchmark.

## Multi-player isolation already proven inside one authority

The integrated Game acceptance proves two participants may occupy different admitted spaces
simultaneously.

Spatial interactions/publication are space-aware, so coordinates alone cannot make actors in
different maps nearby.

This prevents a server-global `CurrentMap` implementation from becoming the architecture.

## Explicit Online/Remote gap

Production Online map travel is not implemented yet.

The current hosted/Remote product still assumes one admitted structural space in:

- authenticated structural manifest advertisement;
- owner-state spatial lifecycle;
- client structural authorization/binding;
- map-discontinuity retirement;
- remote structural acquisition.

The new Local pads therefore are not exposed by the Online/Remote product.

Completing Remote map travel requires at least:

~~~text
space-aware structural advertisement/authorization
    ↓
authoritative owner-state spatial discontinuity
    ↓
retire old-space AOI/publication/interpolation debt
    ↓
acquire/validate destination structural composition
    ↓
switch client semantic/render roots
    ↓
resume owner/public state in destination space
~~~

Two exported network clients in different spaces must be proven independently.

Do not enable the pads remotely before these semantics exist.

## Streaming / cache implication

Local currently prepares both structural roots when the generation starts.

Therefore the accepted Local transition proves semantic/native retirement and rebind, but it does
not prove cold on-demand structural acquisition or warm network-return behavior.

The eventual Remote implementation is the better place to measure:

- cold first destination acquisition;
- cached/warm return;
- content/CAS reuse;
- old-space lease retirement;
- bounded prefetch policy.

Do not infer from Local's fast transition that every map should be permanently GPU/native resident.

## P9 relationship

The multi-map foundation was discovered while replacing a P9 launch-only benchmark path with an
actual player journey.

It is useful beyond P9 and should not be defined as visual-only infrastructure.

P9 visual closure still depends on human evaluation of the Cabin art/lighting benchmark.
Online map travel is a separate product gap that must be resolved before broader live-test readiness,
but it is not itself a subjective visual-quality criterion.
