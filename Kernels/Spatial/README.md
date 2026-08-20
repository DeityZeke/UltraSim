# Spatial contracts

`UltraSim.Spatial` is the dependency-neutral integer identity and geometry seam
introduced by Terrain T0.0. It contains values and canonical math only:

- `SpatialSpaceId`, `SpatialChunkCoord`, and `SpatialChunkKey`;
- explicit positive `SpatialChunkGeometry`;
- half-open `SpatialBounds`;
- floor division/normalization and checked wide-distance math.

It owns no world state, entity membership, terrain, publication, AOI subscriber,
queue, thread, renderer, ECS, Network, or Godot integration. Independent kernels
remain usable without referencing it.

Chunk ownership is half-open. Geometry contact at an exclusive ownership edge
still has zero point-to-AABB distance for circular interest tests. Reconstruction
uses checked `Int64`; distances promote to `Int128` before subtraction.

The game-specific 16 m / 0.5 m / 0.1 m / 1 mm profile is deliberately owned by
[`Game/World`](../../Game/World/README.md), not this kernel.
