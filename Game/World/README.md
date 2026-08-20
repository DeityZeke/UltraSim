# Game world

Terrain T0.0 established the game-owned integer position/profile layer over the
dependency-neutral [Spatial contracts](../../Kernels/Spatial/README.md). T0.1
adds authoritative ECS position and generation-owned dynamic membership.

The frozen first profile is:

```text
authoritative position quantum      1 mm
world chunk edge               16,000 mm
terrain cell edge                 500 mm
terrain cells/chunk edge           32
placement snap policy             100 mm
```

`WorldPosition` is a 16-byte game value: signed 32-bit chunk X/Z, unsigned
canonical local X/Z in `0..15,999`, and signed 32-bit Y millimeters. It is not an
ECS component. `WorldPositionComponent` is the separate 16-byte persisted ECS
wrapper. Its fields are flattened primitives so generated persistence remains
canonical; internal spatial slots are derived and never durable.

Exact millimeter input normalizes with mathematical floor division. Floating
import rejects nonfinite/out-of-profile values and rounds to nearest millimeter,
midpoint away from zero. Terrain-cell and placement granularity never constrain
dynamic positions.

## Maintained dynamic membership

`GameWorldState` owns prepared Position lookup/baseline/change capabilities and
one `WorldSpatialIndex` for exactly one ECS generation. The index uses:

```text
one generation-validated intrusive home record / ECS entity slot
one fixed chunk-metadata slot / occupied dynamic chunk
one bounded open-addressed SpatialChunkKey lookup
one fixed free-chunk stack
```

Component presence opts an entity into membership. Same-chunk value changes do
not mutate topology; a crossing unlinks/relinks the existing home record. Empty
chunk slots return to the free pool. The table admits the theoretical maximum of
one occupied chunk per admitted entity, reports reserved payload/high-water
diagnostics, and allocates nothing during ordinary movement.

Runtime invokes the game generation through the domain-neutral
`IServerGenerationStepParticipant` after `EcsWorld.Tick()` has completed its
whole graph and structural barrier, and before the world clock advances. Spatial
consumes independent exact changed-row and lifecycle cursors, deduplicates by ECS
slot, then reads final current Position truth. Ten writes before the boundary are
therefore one observation.

Lifecycle history is enabled for Local and Networked Spatial profiles. If a
consumer falls behind the bounded ring, `GameWorldState` builds a fresh index
privately from current ECS truth and swaps only after success. Failed rebuilding
leaves the previously published derived index intact. Generation replacement
always constructs and rebuilds a fresh index; no membership handle crosses the
generation boundary.

Terrain packs, statics, chunk publication/history, client streaming, rendering,
and Network N1 are not part of T0.1.
