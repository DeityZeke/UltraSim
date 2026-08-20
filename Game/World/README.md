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
chunk slots return to the free pool. At full capacity, a singleton source moving
to an existing chunk collapses into that destination; moving to a new chunk
re-keys the source slot in place. The table therefore admits the theoretical
maximum of one occupied chunk per admitted entity, reports reserved
payload/high-water diagnostics, and allocates nothing during ordinary movement.

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

## Terrain foundation format

T0.2 freezes the physical terrain planes without introducing a loader or mutable
terrain store:

```text
material cells       32 x 32 x ushort       2,048 B
height vertices      33 x 33 x TerrainHeight 2,178 B
foundation                                     4,226 B
height quantum                                  10 mm
decoded range                   -327,680..327,670 mm
```

`TerrainHeight` is a two-byte signed value. Authored integer millimetres quantize
nearest with midpoint away from zero and reject outside the decoded range.
`TerrainHeightEncoding` preflights a complete 1,089-vertex plane before writing,
so malformed input cannot partially publish a destination. Its ordinary encode
and decode paths are allocation-free.

`TerrainVertexCoord` admits `0..32` on each axis. The positive edge at 32 maps to
16,000 mm and is the duplicated immutable border shared with the neighboring
chunk; dynamic position locals remain half-open at `0..15,999`. Compiled shared
edge units must be bit-identical. Exact pack headers, directories, sections,
hashes, and loading remain T0.3 work.
