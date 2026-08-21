# Game world

Terrain T0.0 established the game-owned integer position/profile layer over the
dependency-neutral [Spatial contracts](../../Kernels/Spatial/README.md). T0.1
adds authoritative ECS position and generation-owned dynamic membership; T0.2–T0.5
add the dense base terrain, immutable pack, base queries/mesh payload, and packed
frozen-static/static-Multi content. T0.6 adds explicit dynamic public eligibility,
packed per-chunk current state, and bounded observer-independent dirty history.

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

## Dynamic chunk publication

T0.6 keeps broad public projection explicit. `WorldPositionComponent` supplies
location but does not grant publication eligibility; a valid persisted
`WorldPublicComponent` supplies the compact public prefab, yaw, and state flags.
`GameWorldState` observes both components plus lifecycle at the same post-ECS
barrier used by spatial membership and commits only final current values.

`ChunkPublicationStore` is separate generation-owned game storage keyed by
`SpatialChunkKey`:

```text
fixed SoA current pages
separate PublicationEpoch
generational WorldPublicEntityId
monotonic entity-public versions
chunk publication revisions
one bounded global dirty-history pool
```

These identities do not alias ECS handles, spatial membership slots, durable
identity, client slots, or Network replication identity. Movement retains one
public identity and publishes a source remove before a target present using
successive entity-public versions. Destruction/public-component removal emits a
tombstone; ECS slot reuse advances the public generation.

Snapshots and coalesced deltas write complete results into caller-owned spans.
Each cursor binds its epoch, chunk key, and chunk-local revision. An old,
wrong-key, future, foreign-epoch, or pre-recreation cursor requests a snapshot;
no-change cursors produce no rows. A `ChunkAbsent` snapshot is authoritative
empty state: consumers clear that page and retain no cursor. History pressure
never rejects a representable current page: an
oversized dirty commit advances the current revision/floor, clears incomplete
history, and forces older consumers to snapshot. Observer cursors and watcher
lists are deliberately absent.

For one reusable public Index, a higher Generation globally supersedes lower
generations across all chunk pages. Within one Index/Generation, the higher
entity-public version wins. This permits a new-generation Present to coalesce an
older generation's Remove without allowing delayed old state to survive. Batches
carry no simulation tick: the game-world commit counter is generation-local and
is not an authoritative clock.

The cheapest local bridge can borrow committed read-only SoA pages until the
next mutation/replacement. Canonical semantic bytes instead use the sorted
caller-owned snapshot result. Synthetic policy evidence selected on-demand
semantic construction over always-current pages: sparse demand avoided 192 of
256 otherwise eager constructions, while full demand required the same 256
constructions either way. No retained encoded-body cache or Network packet
format is introduced before a concrete consumer earns its lifetime policy.

The hardened 1,024-entity/8-chunk benchmark records 0 B warmed allocation, about
83 ns for an unchanged post-step observation, 404 ns for a same-chunk public
update, 575 ns for a crossing, 203 ns to iterate prepared 128-row pages, 3.85 us
to copy a canonical 128-row snapshot, and 4.80 us to copy plus encode it. A cold
100,000-slot construction reserves 123,810,048 bytes. See the
[current T0.6 hardening evidence](../../_Benchmarks/Baselines/terrain-t0.6-chunk-publication-hardening-2026-08-20.json).

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
`TerrainHeights/v1`. T0.5 optionally adds `FrozenStatics/v1` and
`MultiFragments/v1`; each owns a section-local range table aligned to canonical
main-chunk order. The frozen top-level 48-byte terrain chunk row remains
unchanged. A future sparse additional structural-surface section can likewise be
added without changing any accepted section, but no such surface schema exists.

Opening is bounded by `WorldPackLimits` before proportional allocation. The
reader validates geometry, content fingerprint, all checked offsets/counts,
canonical ordering, section and pack hashes, material/height payloads, and
reconstructed seams before publishing immutable owned bytes. Buffered file and
owned-memory inputs use the same validator. `TryGetBaseTerrain` returns a
borrowed, allocation-free `WorldTerrainChunkView` rather than constructing a
cell or chunk object graph.

`WorldContentBinding` is supplied cold by Composition. A pack becomes a
`BoundWorldPack` only after its content fingerprint, every structural material
ID, and every frozen-static definition ID resolve. An optional bound pack is then generation-owned through
`GameWorldState`; the current single-space game additionally rejects a bound pack
whose `SpatialSpaceId` is not `GameWorldState.DefaultSpace` before generation
publication. Default worlds may still run without one until the T0 vertical
supplies authored content.

`GameContent.WorldFingerprint` is explicit governance for packed numeric world
content. It must change whenever the semantic meaning of an admitted material,
static, prefab, or other packed numeric identity changes incompatibly—even if the
numeric value itself remains present. T0 does not add an automatic fingerprint
framework.

## Base-terrain queries and mesh payload

T0.4 keeps pure immutable-artifact queries on `BoundWorldPack`:

```text
TrySampleBaseTerrainHeight
TryResolveBaseTerrainMaterial
```

Sampling uses canonical half-open `WorldPosition` ownership, global terrain-cell
parity, the exact triangle plane represented by the packed q5 vertices, and
signed nearest integer division with midpoint away from zero. It is explicitly a
base-heightfield query, not a future general multi-surface ground resolver.

Gameplay/current-authority callers route the same operations through
`GameWorldState`. Today those methods delegate to `BaseWorld`; T0.9 may resolve a
durable replacement/overlay first without changing callers:

```text
BoundWorldPack
    what this immutable content artifact contains

GameWorldState
    what is authoritative in this game world now
```

`TryBuildBaseTerrainMesh` fills caller-owned fixed spans with 1,089
`TerrainBaseMeshVertex` values, 6,144 ushort indices, and 2,048 triangle material
IDs. Vertices retain integer local X/Z, absolute Y, and exact unnormalized normal
and tangent directions. The 36-byte row is an internal mesh-preparation payload,
not a pack/persistence/Network/Game.Contracts/mod ABI. Frames include incident
triangles from the bounded immutable packed-neighbor halo, so adjacent chunks
independently reproduce identical seam
frames regardless of presentation residency. Output capacity and target identity
are validated before any write, and the warmed query/build paths allocate 0 B.

The simple fixed grid is deliberate. Greedy geometry merging, float/Godot upload
formats, live collision resources, and sparse additional terrain
surfaces remain later measured contracts.

## Frozen statics and static placed Multis

T0.5 keeps immutable non-simulated world geometry out of ECS. The two optional
section layouts are:

```text
FrozenStatics/v1
    32-byte header
    8-byte range / canonical main chunk
    56-byte frozen-static rows

MultiFragments/v1
    40-byte header
    8-byte range / canonical main chunk
    40-byte logical Multi-instance rows
    56-byte chunk-local fragment rows
```

An independent row retains its stable `WorldStaticId`; every Multi fragment
retains `MultiInstanceId` plus its stable `MultiPieceId`. Those identities are
pack/world identities. Chunk indices, row offsets, render group offsets, and
future renderer instance indices are not authority. A logical static placed
Multi may cross chunk boundaries while retaining one root record and one piece
count. T0.5 does not establish moving local-coordinate frames, ships, vehicles,
or moving aggregates.

Static orientation is the 2-byte `WorldYaw` value: an unsigned full turn around
+Y. Zero preserves canonical authored facing; 16,384/32,768/49,152 are exact
quarter/half/three-quarter turns, increasing with right-handed rotation (local
+Z maps to +X at one quarter turn). It is yaw-only. Independent statics and
already-resolved fragments store world-placement yaw; a Multi root separately
retains root yaw. Pitch, roll, and moving frames require a later proven
representation.

The compiler sorts rows canonically by main chunk, compatible render key, and
stable identity. Borrowed per-chunk views decode directly from owned pack bytes
and can copy deterministic compatible render groups into caller spans. Normal
reads allocate no per-chunk lists or objects.
Those groups are internal packed-row preparation hints, not a final Godot
batch/material contract, renderer ABI, `Game.Contracts` API, or mod API. T0.8 may
filter, coalesce, or subdivide them from real presentation evidence.

`WorldStaticBounds` is an exact half-open integer-millimetre 3D bound. Frozen
rows are assigned one canonical home chunk and may extend only through its
bounded one-chunk halo. `TryQueryStaticBounds` expands the query's candidate
chunk range by that halo, rejects work beyond the caller's explicit candidate
budget, counts matches before writing, and therefore never partially publishes
an undersized destination. Pure artifact queries live on `WorldPack`/
`BoundWorldPack`; authoritative gameplay routes through `GameWorldState` so a
future durable overlay can shadow or augment the immutable base without changing
callers.
The halo is a compact v1 section representation limit, not a universal UltraSim
object-size limit. Larger structures can use multiple pieces/statics or another
later representation earned by a concrete workload. Stable Multi/piece identity
guarantees attribution across fragmentation; T0.5 does not provide or promise a
dedicated `MultiInstanceId -> all fragments` reverse index.

All optional section counts and offsets are validated against `WorldPackLimits`
before a pack is published. Canonical ranges, row ordering, stable-ID uniqueness,
logical Multi piece totals, root residency, flags, local coordinates, bounds,
hashes, and content definitions are fail-closed.

The hardened synthetic T0.5 evidence uses 256 chunks, 8,192 independent statics,
64 logical Multis, and 512 fragments in a 1,589,672-byte pack. Warmed packed
reads, render-group copies, a sparse exact query, and a 2x2-chunk dense query
returning as many as 173 records all allocate 0 B. The overlap fixtures
deliberately exercise the transactional halo scan and result-copy scaling; they
are not collision-engine or production-content throughput claims.
