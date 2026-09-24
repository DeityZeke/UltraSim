# Game world

Terrain T0.0 established the game-owned integer position/profile layer over the
dependency-neutral [Spatial contracts](../../Kernels/Spatial/README.md). T0.1
adds authoritative ECS position and generation-owned dynamic membership; T0.2–T0.5
add the dense base terrain, immutable pack, base queries/mesh payload, and packed
frozen-static/static-Multi content. T0.6 adds explicit dynamic public eligibility,
packed per-chunk current state, and bounded observer-independent dirty history.
T0.7 adds exact chunk-interest rings and a bounded packet-free local streaming
oracle over that publication state. It is accepted as a reference consumer;
T0.8's production client cache is separately owned under `Client/Core` and does
not make the oracle part of client storage. T1.0 adds explicit base-surface
presence/intentional void; T1.1 adds immutable sparse terrain shells and bounded
multi-surface column reads without turning terrain into ECS entities or voxels.

## Canonical WorldPack composition direction

The current runtime still prepares one exact WorldPack plus one WorldMutableOverlay. That is the
accepted implementation baseline, not the final World-size model.

The canonical forward composition is specified in
[Docs/Plans/WORLD_COMPOSITION.md](../../Docs/Plans/WORLD_COMPOSITION.md):

```text
World / Save
    ↓
SpatialSpaceId
    ↓
World structural runtime composition
    ↓
immutable WorldStructuralManifest
    ↓
zero or many WorldPackCoord shards
    ↓
global SpatialChunkKeys
```

One WorldPack is a fixed 256×256 SpatialChunk / 4.096×4.096 km structural address shard. The
globally centered Pack mapping makes Pack (0,0) own chunk coordinates -128..127 on each axis.
WorldPack boundaries do not change World/SpatialSpace identity and must remain semantically boring.

W1 is closed and W2 is next. `WorldPackAddressing.ToPackLocal` partitions every
signed-32-bit global `SpatialChunkCoord` into an 8-byte `WorldPackCoord` and a
2-byte bounded `WorldPackLocalChunkCoord`. `TryToGlobal` rejects an arbitrary
Pack/local pair if either reconstructed global axis is outside signed 32-bit
range; the first/last representable Pack windows are partial. The constants
are 256 chunks and 4,096 m per Pack edge, centered with a 128-chunk bias.
This is only a World-owned address value: `SpatialSpaceId`, global positions,
WorldPack/v1 bytes, one-Pack runtime behavior, and the existing Pack byte cap
are unchanged. Pack-bound validation and the 512 MiB cap belong to W2.

The future World layer distinguishes authoritatively unauthored Pack, manifest-known but
unprepared Pack, missing chunk inside a prepared Pack, intentional Absent terrain cell, and
present structural surface. Known-but-unprepared is residency state and must never be reported as
a hard World boundary.

Runtime publication eventually lifts WorldPreparedView into one atomic PreparedWorldComposition
above per-Pack immutable base + mutable overlay lineages. WorldPack/v1 remains the initial artifact
format; W1-W10 generalize composition around it rather than rewriting it prematurely.

An entity is current in dynamic-public projection only while its valid
`WorldPublicComponent` has `WorldPublicStateFlags.Visible`. Clearing `Visible`
retires that public lifetime without removing authoritative identity or the
component; setting it again begins a fresh generational public lifetime. I0.12
uses this for the same persistent item moving between public World placement and
owner-private Inventory/equipment. `GroundItem` is valid only with `Visible` and
distinguishes an independently selectable world pickup from visible equipped
presentation. Position/component presence alone never publishes a hidden
contained item.

The frozen first profile is:

```text
authoritative position quantum      1 mm
world chunk edge               16,000 mm
terrain cell edge                 500 mm
terrain cells/chunk edge           32
placement snap policy             100 mm
```

## Base-surface topology

Terrain T1.0 makes dense base-surface existence independent from material and
height. Each resident chunk has a canonical 1,024-bit presence plane: a clear bit
is intentional void, not material zero or missing content. All-present WorldPacks
omit the optional `TerrainPresence/v1` section and retain their exact historical
bytes/fingerprint; affected packs store one 128-byte plane per sorted chunk.

`ProbeBaseTerrainSurface` returns `Present`, `Absent`, or `Unavailable`. Only a
Present result carries effective height/material. The legacy height/material
`Try...` APIs return false for either non-present state; callers needing residency
semantics use the explicit probe. Mesh generation omits both triangles of absent
cells. Current terrain replacements preserve the authored topology plane rather
than silently filling holes. T1.1 additional shells remain independent immutable
WorldPack structure; mutable topology and support/gravity behavior remain later
World/Physics checkpoints.

## Sparse terrain shells

Terrain T1.1 adds optional `AdditionalTerrainSurfaces/v1`. BaseTerrain remains
the dense default sheet, not the universal lowest surface. Additional shells may
exist at any absolute elevation, overlap one X/Z column, and span chunks. Empty
vertical distance is ordinary space; there is no numbered upper/underground layer
and no voxel volume.

`WorldSurfaceId` names one logical authored shell within an exact WorldPack/content
lineage. The same ID may have one canonical fragment per chunk; descriptor order,
chunk-local fragments, cells, triangles, ECS entities, and renderer handles are
not durable identity. A fragment is a bounded 32-by-32-or-smaller sparse presence
patch with optional Top/Bottom q5 height/material planes. A two-sided patch has
an explicit side material and closed exposed edges. Reciprocal edge-continuation
bits join same-ID fragments across adjacent chunks only when face sets and exact
edge heights agree, preventing both cracks and false closure walls.

The section is absent when unused, preserving exact historical T0/T1.0 pack bytes
and fingerprints. Opening validates canonical chunk/ID order, offsets, counts,
presence, face/edge flags, materials, heights, Top-above-Bottom, and cross-chunk
continuations before publication. Default limits admit 65,536 fragments per pack
and 64 per chunk; consumers may select smaller bounds.

Bound/prepared views expose borrowed per-chunk fragments plus allocation-free
column queries. A complete column contains present BaseTerrain and every
intersecting Top/Bottom face sorted by sampled Y, while helper reads select the
highest support candidate at/below a threshold or lowest ceiling candidate
at/above it. A nearest-support helper selects by absolute elevation distance,
prefers the lower face on an exact distance tie, and uses identity only to make
an exact-height tie canonical. Identity never determines vertical order. These
are structural candidates; Physics P0.1c owns grounding, footprint, clearance,
and step interpretation.

The engine-neutral mesh builder emits deterministic top, bottom, and exposed
closure triangles into caller-owned spans using the same global parity as base
terrain. Existing `WorldMutableOverlay` material/height replacements cannot
mutate shell geometry or topology. Mutable shells, destruction/state overlays,
illusions/per-observer perception, editor rip/extrude, and gravity remain future
work.

## Complete-pack stream opening

A0.7 adds `WorldPack.TryOpen(Stream, ...)` without changing WorldPack identity or format. It reads
the exact remaining stream, validates the fixed header and configured pack bound before one
proportional World-owned byte allocation, handles partial/non-seekable reads, and rejects truncation
or trailing bytes. `TryOpenFile` delegates to the same path. Content may supply a verified CAS
stream, but World still owns canonical container validation, `WorldPackFingerprint`,
`WorldContentFingerprint`, and `WorldContentBinding`; cache possession alone is not World
publication authority.

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
payload/high-water and lookup/probe diagnostics, and allocates nothing during
ordinary movement. Linear-probe deletion closes the probe-chain gap: retired or
moved chunks do not leave historical tombstones that make later misses scan the
reserved table. Lookup sizing rejects an unrepresentable rounded capacity before
allocating any proportional index storage.

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
one bounded immutable prepared-publication store
```

These identities do not alias ECS handles, spatial membership slots, durable
identity, client slots, or Network replication identity. Movement retains one
public identity and publishes a source remove before a target present using
successive entity-public versions. Destruction/public-component removal emits a
tombstone; ECS slot reuse advances the public generation.

On authoritative mutation, each dirty chunk interval freezes one recipient-independent prepared
publication containing exact from/to revision coverage plus complete current positive records and
explicit removal records. Public `TryCopyDelta` reads only a contiguous chain of those immutable
publications from the caller's proven cursor; it no longer walks raw retained history or rereads
current positive rows per recipient. The retained history-derived reader is comparison-only for
friend tests/benchmarks.

Snapshots and coalesced prepared deltas write complete results into caller-owned spans.
Each cursor binds its epoch, chunk key, and chunk-local revision. An old,
wrong-key, future, foreign-epoch, or pre-recreation cursor requests a snapshot;
no-change cursors produce no rows. A `ChunkAbsent` snapshot is authoritative
empty state: consumers clear that page and retain no cursor. History pressure
never rejects a representable current page: an
oversized dirty commit advances the current revision/floor, clears incomplete
history/prepared coverage, and forces older consumers to snapshot. No recipient pins retained
source publications. Observer cursors and watcher
lists are deliberately absent.

For one reusable public Index, a higher Generation globally supersedes lower generations across all
chunk pages. Within one Index/Generation, the higher entity-public version wins. In one contiguous
cumulative prepared range, later complete positive state for the same identity may coalesce while
retaining the union of changed fields. A positive for another Index cannot discharge a retained
Remove. A new-generation Present for the exact reused Index can replace the prior generation's
Remove without allowing delayed old state to survive. Missing any prepared revision interval
requests a complete current-state Rebase; a higher revision alone is not coverage. Batches carry
no simulation tick: the game-world commit counter is generation-local and is not an authoritative
clock.

The prepared store is fixed-capacity by publication and record slots, epoch-fenced, and
owner-thread-bound. It contains no recipient, delivery, session, queue, proof, cursor, or retry
state. PF5-R2 records the selected architecture and measured reservation in
`_Benchmarks/Baselines/concept-demo-pf5-r2-shared-latest-state-publication-2026-09-14.md`.

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

## Chunk interest and local semantic streaming

`ChunkInterestQuery` intersects the first game's exact circular X/Z profile with
canonical 16 m chunk AABBs. Previous canonically sorted entries supply only
consumer-owned enter/leave hysteresis. Output is unique and key-ordered, each
chunk receives one nearest tier, radius contact is included, and undersized
destinations return an exact complete requirement with `Count = 0`.

The packet-free `LocalChunkStreamingOracle` is a reference/convergence consumer.
It owns fixed recipient-local
subscription/cadence/cursor state plus a private semantic projection. First
interest snapshots; retained interest queries bounded deltas; history loss
snapshots; leave clears only local state. Complete batches apply to a private
candidate before swapping live, and a new `PublicationEpoch` resets old local
identity/version floors. World chunks and publication pages retain no watcher
or recipient state.

The oracle's `MaximumPublicEntities`-sized live/candidate arrays are deliberately
simple proof storage, not the T0.8 production client representation. T0.8 owns
the one real bounded semantic resident cache and may reuse these proven
application rules without stacking another cache over the oracle. The oracle,
its evidence cadence/limits, present source location, and public CLR visibility
are not Game.Contracts, framework, mod, or compatibility promises.

For one public identity version, exact duplicates are idempotent. An equal
generation/version with a different operation, key, or current record fails
closed instead of becoming last-writer-wins. Resident semantic slots retain
their generation while subscribed and advance it when a different key reuses
the same local slot index. The `Subscriptions` span is borrowed only until the
next synchronization.

The current `1/2/4` cadence is a local evidence profile, not an N1 protocol
constant. Interested absent keys have no publication cursor, so this direct
oracle probes them again at their due cadence to discover later residency. That
explains the high `ChunkAbsentApplications` count; it does not require N1 to
transmit repeated empty snapshots. A future replication-owned reverse subscriber
index may notify recipients without adding watchers to world/publication state.

The 64-entity fixture selects 497 centered subscriptions. The hardened run
measures about 35.2 us for a fresh exact query, 37.4 us with retained hysteresis,
and 67.8 us for a no-change local synchronization, all at 0 B warmed allocation.
See the [T0.7 hardening evidence](../../_Benchmarks/Baselines/terrain-t0.7-chunk-interest-local-oracle-hardening-2026-08-21.json).

T0.8's production cache consumes these publication semantics without retaining
this oracle. Real snapshots require a valid chunk-bound cursor; equal revision
requires an identical post-supersession page. A default cursor is reserved for
empty initial immutable-base staging. `ChunkAbsent` application assumes the
supplying bridge already validated its current request lineage; world publication
still gains no observers/watchers and Client Core gains no Network sequencing.
The renderer separately maps client semantic handles onto private presentation
slots and refreshes dynamic public groups without rebuilding immutable terrain or
frozen structure.

The T0.7 `Immediate/Near/Far` radii remain game/server public-state admission and
freshness policy. They are not terrain draw distance, a graphics setting, or a
client render-window radius. A client structural window may retain immutable
terrain/statics well beyond dynamic public interest, or render less public state
than supplied, without changing authority. Camera facing is presentation-only
and never feeds this interest query as authorization.

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
main-chunk order. T1.0 adds optional `TerrainPresence/v1`; T1.1 adds optional
`AdditionalTerrainSurfaces/v1` with its own canonical chunk range table. The
frozen top-level 48-byte terrain chunk row and accepted base sections remain
unchanged.

The sorted chunk directory is an authored key set, not a promise that its global bounding box is
fully populated. D0.2 exercises this canonical form with two distant 9×9 clusters in one space and
no rows for the intervening geography. A missing key therefore means outside authored world;
authored-but-nonresident data and an authored resident chunk containing T1.0 intentional void remain
separate conditions owned by their respective consumers.

The additional-shell schema keeps surface topology, material, effective physical
behavior, observer perception, and mutable Game state distinct. An absent/`None`
surface is topological absence, not a transparent or non-colliding material.
T1.1 supplies stable pack-lineage World-owned identity instead of using chunk
rows, triangle indices, render handles, or ECS entities. A future durable overlay
may retain divergence such as damaged/destroyed state against that identity while
Game derives separate physical, presentation, occlusion, and navigation
projections. The current overlay does not yet implement mutable shell state or
observer-specific perception.

Opening is bounded by `WorldPackLimits` before proportional allocation. The
reader validates geometry, content fingerprint, all checked offsets/counts,
canonical ordering, section and pack hashes, material/height payloads, and
reconstructed base/shell seams before publishing immutable owned bytes. Buffered file and
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
`GameWorldState`. T0.9 resolves the immutable base plus a validated durable
replacement overlay first without changing callers:

```text
BoundWorldPack
    what this immutable content artifact contains

GameWorldState
    what is authoritative in this game world now
```

`TryBuildBaseTerrainMesh` fills caller-owned fixed spans with 1,089
`TerrainBaseMeshVertex` values, 6,144 ushort indices, and 2,048 triangle material
IDs. Vertices retain integer local X/Z, absolute Y, and exact unnormalized normal
and tangent directions. Canonical indices satisfy
`cross(v1-v0, v2-v0)` toward the semantic front/outward side in UltraSim
coordinates; normals occupy that same hemisphere. Engine adapters own any
coordinate, handedness, winding, normal, or tangent conversion. The 36-byte row
is an internal mesh-preparation payload,
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
`BoundWorldPack`; authoritative gameplay routes through `GameWorldState`, whose
T0.9 prepared view can shadow complete terrain/static/Multi chunk content without
changing callers.
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

## Prepared mutable-world view

T0.9 keeps `WorldPack` immutable and layers one `WorldMutableOverlay` over an
exact `BoundWorldPack`. A changed chunk may completely replace its dense terrain,
independent static rows, and/or static-Multi fragment rows; logical Multi roots
may be replaced as one complete sorted set. Empty replacement arrays mean empty
authority rather than base fallback. Every changed chunk has its own monotonic
content revision and SHA-256 replacement hash, while the complete overlay has a
separate monotonic generation revision.

`WorldMutableOverlayBuilder` is cold transactional preparation. It validates
content identities, exact terrain seams, q5 height planes, one-chunk static
bounds halos, globally unique static/piece identity, root residency, piece
totals, and configured count/byte bounds before exposing immutable arrays.
Compact sorted immutable identity-owner indexes are built once during
`BoundWorldPack` content binding, so ordinary overlay validation is shaped by
retained replacement content rather than every immutable chunk/row. Complete
Multi-root replacement remains an explicit global transaction because it must
prove that no surviving base Multi is orphaned.
`GameWorldState.TryPublishOverlay` accepts only the next revision and swaps the
complete `WorldPreparedView` on its owner thread. It does not mutate pack bytes,
dynamic publication history, Spatial membership, client residency, or renderer
state.

A mutation-free fresh builder produces the canonical revision-0 empty overlay.
Canonical replacements identical to the base or prior generation do not invent
content revisions. Removing the final prior replacement may still publish a
valid next overlay revision with zero retained rows, and that state is preserved
by save/restore. Selected limits must bound inherited prior content as well as
new replacement calls. A chunk replacement hash is scoped beneath the exact base
pack fingerprint.

The prepared view is also a persistence and future-replication seam, not a
generic provider framework. The current save artifact references the exact pack
and carries complete overlay replacements. Client Core can stage from the same
resolved view and retain structural revision/hash independently from dynamic
public-entity publication cursors. Live changed-chunk delivery is deferred to
Network N1.
