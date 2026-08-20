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
material cells       32 x 32 x ushort             2,048 B
height base          int32 absolute millimetres       4 B
height offsets       33 x 33 x TerrainHeightOffset 2,178 B
height plane                                         2,182 B
foundation                                           4,230 B
height quantum                                           5 mm
local relief span                                 327,675 mm
absolute q5 range              -2,147,483,645..2,147,483,645 mm
```

`TerrainHeightOffset` is a two-byte unsigned value relative to one validated
`TerrainHeightPlaneBase`. Authored integer millimetres first quantize to one
canonical absolute q5 lattice; the minimum becomes the base. A plane rejects if
its absolute values cannot be represented or its local relief exceeds 327,675
mm.
`TerrainHeightEncoding` preflights a complete 1,089-vertex plane before writing,
so malformed input cannot partially publish a destination. Its ordinary encode
and decode paths are allocation-free.

`TerrainVertexCoord` admits `0..32` on each axis. The positive edge at 32 maps to
16,000 mm and is the duplicated immutable border shared with the neighboring
chunk; dynamic position locals remain half-open at `0..15,999`. Neighboring
packed offsets may differ because bases differ, but reconstructed absolute shared
edge heights must be exact.

## Immutable WorldPack

T0.3 freezes `WorldPack/v1` as a canonical uncompressed little-endian sectioned
container. It owns a 160-byte header, sorted 48-byte chunk rows, versioned
48-byte section rows, full-pack SHA-256 identity, and per-section hash checks.
The required sections are the dense base `TerrainMaterials/v1` and
`TerrainHeights/v1`; a future sparse structural-surface section can be added
without changing either base section, but no such row schema exists yet.

Opening is bounded by `WorldPackLimits` before proportional allocation. The
reader validates geometry, content fingerprint, all checked offsets/counts,
canonical ordering, section and pack hashes, material/height payloads, and
reconstructed seams before publishing immutable owned bytes. Buffered file and
owned-memory inputs use the same validator. `TryGetBaseTerrain` returns a
borrowed, allocation-free `WorldTerrainChunkView` rather than constructing a
cell or chunk object graph.

`WorldContentBinding` is supplied cold by Composition. A pack becomes a
`BoundWorldPack` only after its content fingerprint and every structural
material ID resolve. An optional bound pack is then generation-owned through
`GameWorldState`; default worlds may still run without one until the T0 vertical
supplies authored content.

## Base-terrain queries and mesh payload

T0.4 adds authoritative queries directly over a `BoundWorldPack`:

```text
TrySampleBaseTerrainHeight
TryResolveBaseTerrainMaterial
```

Sampling uses canonical half-open `WorldPosition` ownership, global terrain-cell
parity, the exact triangle plane represented by the packed q5 vertices, and
signed nearest integer division with midpoint away from zero. It is explicitly a
base-heightfield query, not a future general multi-surface ground resolver.

`TryBuildBaseTerrainMesh` fills caller-owned fixed spans with 1,089
`TerrainBaseMeshVertex` values, 6,144 ushort indices, and 2,048 triangle material
IDs. Vertices retain integer local X/Z, absolute Y, and exact unnormalized normal
and tangent directions. Frames include incident triangles from immutable packed
neighbor chunks, so adjacent chunks independently reproduce identical seam
frames regardless of presentation residency. Output capacity and target identity
are validated before any write, and the warmed query/build paths allocate 0 B.

The simple fixed grid is deliberate. Greedy geometry merging, float/Godot upload
formats, live collision resources, static bounds, and sparse additional terrain
surfaces remain later measured contracts.
