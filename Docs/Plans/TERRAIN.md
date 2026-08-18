# Terrain / World T0: chunk-native world implementation plan

Status: active forward plan. Network N0 is accepted and frozen as the current
remote-authority reference. T0 is the next major implementation milestone.
Network N1 is deliberately deferred until T0 has proved the world/chunk contracts
that N1 will consume.

T0 is not merely a terrain renderer. It establishes the spatial substrate for the
persistent world: deterministic coordinates, chunk identity, packed terrain and
static data, dynamic entity membership, chunk-local publication state, bounded
dirty history, local streaming, Godot rendering, and the seams required by
persistence and later Network N1.

The motivating observation is now measured rather than speculative. Network V3
Dense-400 is efficient once recipient/entity relationships are established, while
sparse movement is limited by changing and retiering those relationships. The
rejected N0.11 exact-AOI experiment removed 31.55% of 4K maintained relations and
40.36% of WorldState records, yet doubled interest-update p95 because precise
recipient/entity membership reconciliation cost more than the work it removed.
T0 therefore makes spatial chunks the primary maintained world partition so a
future network recipient can subscribe to a small set of chunk publications
rather than maintain a large recipient/entity relation graph.

The first complete Terrain milestone is **T0 — Chunk-Native Playable World**:

> Start the existing authoritative Runtime and game composition, enter a
> deterministic persistent world, move across chunk boundaries, stream packed
> terrain and UO-like statics into Godot, enter a small building, interact with
> dynamic world objects, mutate authoritative/persistent state, disconnect or
> rebuild the local projection, and reproduce the same visible world without
> making renderer nodes, network relations, or chunk-local slots authoritative.

At the end of T0, return to [`NETWORK.md`](NETWORK.md) and implement N1 against
these accepted chunk/publication contracts. Do not redesign Network AOI again
before the world substrate exists.

## Frozen principles

```text
WORLD SPACE IS INTEGER AND DETERMINISTIC.
CHUNK IDENTITY IS A VALUE.
CHUNK-LOCAL SLOT IDENTITY IS NEVER PERSISTENT IDENTITY.
ECS ENTITIES REMAIN AUTHORITATIVE SIMULATION ENTITIES.
CHUNK MEMBERSHIP/PUBLICATION IS A DERIVED SPATIAL ACCELERATOR.
BASE TERRAIN AND FROZEN STATICS ARE WORLD DATA, NOT ECS ENTITIES BY DEFAULT.

TERRAIN CHUNKS ARE 2D WORLD PARTITIONS, NOT ECS STORAGE CHUNKS.
CHUNK SIZE IS A COLD WORLD-FORMAT DECISION.
CHUNK SIZE IS MEASURED, NOT GUESSED.
NEGATIVE COORDINATES HAVE ONE CANONICAL FLOOR-DIVISION RULE.
NO AUTHORITATIVE FLOAT POSITION IS REQUIRED.

ONE CHUNK OWNS ONE PACKED VIEW OF ITS PUBLIC SPATIAL STATE.
PACK ON PUBLICATION, NOT PER RECIPIENT.
SNAPSHOTS ARE COMPLETE AT A CHUNK REVISION.
LATEST STATE COALESCES.
EVENTS RETAIN FACTS.
DIRTY HISTORY IS BOUNDED.
HISTORY MISS FALLS BACK TO A SNAPSHOT.

AOI QUERIES CHUNKS, NOT MEMBERS WITHIN A DISTANCE CIRCLE.
AOI TIER CHANGES QUERY FREQUENCY, NOT THE TYPE OF WORLD STATE.
RING 0 IS THE LOCAL CHUNK SET, NOT AN ENTITY-BY-ENTITY IMMEDIATE SET.
CLIENT PRESENTATION MAY CULL MORE PRECISELY THAN SERVER CHUNK ADMISSION.
PRIVATE OR SECRET STATE NEVER ENTERS A PUBLIC CHUNK PAGE.

STATIC VISUAL SCALE DOES NOT REQUIRE ONE NODE PER STATIC.
FROZEN HETEROGENEOUS GEOMETRY MAY BE BAKED; REPEATED IDENTICAL GEOMETRY MAY BE INSTANCED.
LOGICAL MULTI/PROPERTY GROUPING IS SEPARATE FROM GODOT MULTIMESH GROUPING.

LOAD/PREPARE MAY BE ASYNCHRONOUS.
AUTHORITY/PUBLICATION COMMIT IS OWNER-BOUND AND TRANSACTIONAL.
RUNTIME MEMORY IS BOUNDED.
CAPACITY FAILURE IS EXPLICIT.
NO HOT-PATH OBJECT GRAPH IS CREATED PER CELL, STATIC, ENTITY, OR RECIPIENT.
```

These rules survive changes to mesh generation, file compression, renderer,
network codec, chunk size, world size, and eventual server partitioning.

## Terminology

`ECS chunk` continues to mean the packed archetype storage unit owned by the ECS
kernel. This plan uses `WorldChunk` for the spatial X/Z partition. Code and docs
must not shorten both concepts to an ambiguous `Chunk` at API boundaries.

A `WorldChunk` has one canonical signed X/Z coordinate within one `WorldSpace`.
It may expose several independent publication layers:

- **Terrain surface** — height/material cells and immutable or rare terrain edits;
- **Frozen statics** — non-simulated placements and compiled Multi fragments;
- **Dynamic public state** — packed projections of ECS entities currently homed
  in the chunk;
- **Optional interior/decor layer** — data whose streaming policy differs from
  the structural shell;
- **Events** — not stored in latest-state dirty history; they retain their own
  ordered semantics.

`WorldChunkSlot` means a process-local resident-store slot and is never serialized,
sent, or used as durable identity.

`PublicationRevision` is chunk-local monotonic latest-state publication identity.
It is not authoritative simulation tick identity, although a publication records
the authoritative tick that produced it.

## Scope and deliberate non-goals

T0 includes the minimum world machinery needed to make chunking real and prove
that it can become the later replication/streaming unit. It does not attempt to
finish every world system.

T0 includes:

- deterministic world/chunk/local coordinate math;
- a compact terrain height/material representation;
- a deterministic runtime `WorldPack` format and reader;
- UO-like frozen static placements and logical Multi/property composition;
- chunk-local derived membership for dynamic ECS entities;
- a packed dynamic-publication page and bounded dirty-history contract;
- exact chunk-overlap interest queries for local/offline use;
- local snapshot/delta consumption as a semantic oracle for future N1;
- terrain/static client streaming and bounded resident caches;
- Godot terrain mesh and static batching without per-cell/per-static nodes;
- deterministic ground/static spatial queries sufficient for the T0 movement
  vertical;
- persistence seams for base-pack identity, dynamic ECS entities, and future
  mutable world overlays;
- one small playable woods/cabin vertical slice.

T0 does **not** require:

- Network N1 packet formats or recipient chunk cursors over UDP;
- a production world editor;
- procedural infinite terrain;
- voxel caves/destruction;
- final physics, navigation, pathfinding, water, weather, seasons, farming, or
  biome simulation;
- final occlusion/portal systems;
- final texture-atlas policy;
- one Godot `MultiMesh` for heterogeneous meshes (Godot `MultiMesh` repeats one
  mesh; heterogeneous frozen clutter is instead baked/combined or grouped into
  multiple compatible instance batches);
- arbitrary live terrain deformation until the immutable/base path is proved;
- server sharding or ownership transfer between processes.

Those capabilities must use the T0 spatial contracts later rather than delay T0.

## Ownership and dependency shape

The existing authority root remains unchanged:

```text
ServerRuntime
    -> ServerGeneration
        -> EcsWorld                         dynamic simulation truth
        -> WorldClock
        -> GameGenerationState
             -> World / spatial state       world pack + derived resident index
```

The first implementation belongs in the concrete game layer. Do **not** create a
generic terrain kernel merely because terrain is foundational. The first world is
this game's world and should earn generalization before moving downward.

Candidate project shape:

```text
Game/World
    coordinate/value types
    world-pack format + validated reader
    resident WorldChunk store
    terrain/static data
    dynamic membership/publication pages
    chunk query API

Game/Composition
    owns WorldState/WorldChunkStore in GameGenerationState
    binds dynamic ECS components into chunk publication after committed steps

Client/World                    split only when implementation size earns it
    retained resident chunk projection
    snapshot/delta apply
    render-ready immutable chunk payloads

Client/_Godot
    terrain mesh build/commit
    static batching / RenderingServer resources
    presentation culling / floating render origin

_Tools/WorldPack                only when authored-source compilation is needed
_Tests/UltraSim.Game.World.Tests
_Benchmarks/UltraSim.Game.WorldBenchmarks
```

If a separate `Client/World` project does not yet earn itself, begin with a small
client-world projection adjacent to the existing client core and split when the
dependency boundary becomes concrete. Do not introduce a service container or
world framework.

Network does not own `WorldChunkStore`. Persistence does not own it. Godot does
not own it. Bridges translate immutable/typed chunk outputs into their domains.

## T0.0 — executable contracts before terrain content

Build the value contracts and a tiny deterministic in-memory world before a file
format or renderer.

Required values:

```text
WorldSpaceId
WorldChunkCoord(X, Z)
WorldChunkKey(WorldSpaceId, X, Z)
WorldLocalPosition
WorldPosition / WorldLocation
TerrainCellCoord
WorldChunkRevision
WorldPackFingerprint
```

The first tests must prove:

- canonical floor division/remainder for positive and negative world positions;
- position normalization across every X/Z boundary direction;
- corner crossing and large-coordinate round trips;
- no alias between world-space identity and chunk identity;
- local-to-global conversion uses checked 64-bit intermediates;
- deterministic chunk AABB distance/intersection;
- canonical ordering/hash of chunk keys;
- malformed/out-of-range values reject before mutation.

### Chunk-relative position target

The preferred physical representation is normalized chunk-relative X/Z rather
than a large floating global vector. Initial chunk-edge candidates are 16 m,
32 m, and 64 m. All fit millimeter local coordinates in `ushort`:

```text
ChunkX            int32
ChunkZ            int32
YMillimeters      int32
LocalXMillimeters uint16
LocalZMillimeters uint16
                  -----
                  16-byte natural payload before optional WorldSpace identity
```

This is a target, not an excuse to force 64 m. T0 benchmarks choose the chunk
edge. If the selected edge exceeds 65,535 mm, the representation must be revisited
explicitly rather than silently truncating.

`WorldSpaceId` may be stored separately from hot per-entity position where one
world/facet is already implied by authority. Multiple facets do not justify
inflating every hot row before they exist.

Authoritative calculations use integer millimeters or explicitly defined height
units. Godot converts to float only at the presentation boundary and may rebase a
render origin without changing authoritative coordinates.

**T0.0 gate:** all coordinate tests pass in Debug/Release; warmed normalization
and chunk lookup allocate zero; no renderer/network dependency exists.

## T0.1 — choose the world-cell and chunk format

T0 uses a 2D heightfield surface plus statics, closer to UO than a voxel world.
The terrain surface is not represented as one ECS entity per tile.

The compact candidate layout is two planes:

```text
material[cellZ * CellsPerEdge + cellX]       ushort material/content id
height[vertexZ * (CellsPerEdge + 1) + vertexX]
                                              signed fixed-point height
```

The `(N+1) x (N+1)` height lattice gives every chunk complete edge vertices for
its `N x N` material cells. Neighboring chunks must agree on their shared edge;
the pack compiler validates that invariant. Runtime mesh generation therefore
does not need to borrow mutable neighbor memory merely to close a seam.

Terrain material behavior comes from compiled content whenever possible. Do not
store duplicated walkability/render/collision flags per cell if they are stable
properties of the material definition. Add another per-cell plane only when a
real feature proves that the value varies independently.

Height storage is a cold world-format decision. Benchmark/requirements compare:

- signed 16-bit fixed height with a world-declared height quantum;
- signed 32-bit millimeters if exact vertical millimeters are required.

A 16-bit height plane is preferred only if its range/resolution can be stated as
a permanent gameplay contract.

### Chunk-edge calibration

Run the same deterministic world through at least 16/32/64 m chunk-edge profiles.
The comparison records:

- terrain bytes/chunk and directory overhead;
- frozen-static count distribution and worst chunk;
- dynamic-entity count distribution and worst chunk;
- chunk crossings per moving entity per second;
- R0/R1/R2 chunk subscription counts;
- false-positive public entities delivered by chunk admission;
- mesh build time and emitted triangles;
- Godot resident RID/draw/batch counts;
- world-pack random read size/count;
- dynamic-page and dirty-history reserved bytes.

Do not pick the smallest chunk because it minimizes false positives. N0.11 already
proved that precision can cost more than extra coherent work. Pick the smallest
profile that wins the whole pipeline or the simplest profile inside measurement
noise.

**T0.1 gate:** one chunk edge, cell size, height encoding, and maximum supported
local coordinate are written into the world-format contract and benchmark artifact.

## T0.2 — immutable `WorldPack` and terrain source

Authoring data and runtime data are separate. T0 first compiles/generates a small
deterministic authored world into one canonical runtime pack.

Do not create one file per chunk. The first pack is a bounded random-access
container:

```text
WorldPackHeader
    magic
    format version
    WorldSpaceId
    chunk/cell geometry
    content/schema fingerprint
    chunk count
    directory location/length

ChunkDirectory[]               canonical sorted keys
    WorldChunkKey
    section offset/length
    section hash/check
    terrain/static counts
    optional layer offsets

Chunk sections
    terrain material plane
    terrain height lattice
    frozen static placements
    Multi fragments / layer metadata
```

The first format should be uncompressed unless I/O measurement proves compression
is needed. Compression complicates random access, corruption boundaries, scratch
memory, and zero-copy opportunities. Preserve section boundaries so compression
can be added later without changing semantic chunk identity.

Validation happens before publication:

- header/version/fingerprint;
- checked offsets and lengths;
- sorted unique chunk keys;
- per-section capacities;
- material/prefab IDs resolve through compiled content;
- shared terrain edges agree;
- static placements are inside their compiled chunk fragment bounds;
- hashes/checks match;
- no proportional allocation occurs before bounds are known.

A `WorldPack` is immutable after publication. Runtime mutable state is an overlay,
not an in-place rewrite of mapped/base bytes.

Initial loading may use ordinary bounded file reads. `MemoryMappedFile`, direct
`RandomAccess`, explicit pread/io_uring, compression, and async read batching are
later measured backend choices, not T0 assumptions.

**T0.2 gate:** deterministic build produces byte-identical packs; malformed packs
reject transactionally; random chunk read has bounded scratch/allocation; one pack
reopens and hashes identically across supported platforms.

## T0.3 — terrain queries and deterministic mesh generation

Server/gameplay code must not depend on a Godot collision mesh to know the ground.
The world layer provides deterministic integer queries directly over chunk data:

```text
TrySampleTerrainHeight(location)
TryResolveTerrainMaterial(location)
QueryStaticBounds(aabb, destination)
```

T0 does not need a general physics engine. It needs enough authoritative ground
and static occupancy information to move a character through the cabin slice.

### Mesh contract

Mesh generation consumes an immutable terrain chunk snapshot and emits a bounded
render payload. The server never builds this mesh.

Requirements:

- shared chunk edges produce bit-identical world-space seam positions;
- normals/tangents use a deterministic border contract;
- no crack appears if adjacent chunks become resident in either order;
- flat/coplanar cells with compatible material may be greedily merged;
- material boundaries, height discontinuities, and required collision boundaries
  stop a merge;
- output is indexed and contiguous; no node/object per tile;
- absent neighbor presentation never changes authoritative terrain.

If greedy merging costs more CPU than the vertex/triangle reduction earns for
real content, retain the simpler mesh. The benchmark records both mesh build time
and Godot render cost; triangle count alone is not an acceptance metric.

**T0.3 gate:** seam torture tests, deterministic mesh hash, bounded output, warmed
mesh-build allocation profile, and a visible multi-chunk terrain window in Godot.

## T0.4 — UO-like statics, properties, and `Multi`

Terrain and non-simulated world decoration must not become one ECS entity per
placement merely to render or stream it.

### Frozen static placement

A runtime static row is packed data, conceptually:

```text
Prefab/StaticDefinitionId
chunk-local X/Z
height/Y
orientation/variant
optional stable static-instance identity
optional render/collision flags that are truly instance-varying
```

The exact field widths are chosen from content limits. Content IDs remain numeric;
Godot resource paths/text names are presentation metadata, never server hot data.

Placements in an immutable pack use a CSR-style layout: one chunk directory row
stores `StaticOffset + StaticCount` into packed global/static section arrays. Do
not allocate one `List<Static>` for every chunk during normal read-only use.

### Logical Multi/property

A `Multi` is the UO-like logical grouping: a building, structure, ship, shelf set,
or authored collection of placements with one root identity/transform and many
local pieces.

Authoring keeps the logical Multi intact. Runtime compilation may split its frozen
render/collision pieces into the `WorldChunk`s they overlap so each chunk can load
independently. Every fragment retains the logical Multi/instance identity needed
for inspection, rebuilding, or future editing.

This enables the desired building split:

```text
structural shell
    terrain/foundation/floors/walls/roof/frame

interior/decor layer
    furniture, clutter, nonessential decorative statics

interactive/dynamic overlay
    doors with mutable state, containers, characters, movable items, NPCs
```

A door may visually align with a static building but remains dynamic if gameplay
state changes. A chest's frozen body may eventually be batched visually while its
interaction/inventory identity remains authoritative elsewhere. Do not erase game
identity merely to earn a render batch.

### Godot static batching

Do not create one `MeshInstance3D` node per static. The client groups compatible
placements by render key and uses `RenderingServer`/`MultiMesh` or combined meshes.

Important distinction:

- one `MultiMesh` repeats one mesh with many transforms;
- many identical books/trees/rocks can therefore batch very well;
- a shelf containing a book, globe, rune, statuette, and ring cannot become one
  heterogeneous Godot `MultiMesh` merely because they share an atlas;
- heterogeneous frozen clutter may instead be compiled into one combined mesh for
  the shelf/property/chunk, or several compatible `MultiMesh` groups.

Picking/interaction may map render-instance indices back to logical static or
interactive identities. Presentation batching never changes authority.

The asset pipeline should preserve reuse tricks such as statuettes/clockwork
figures using the same creature mesh at another scale/animation state. That is a
content/render decision built on numeric prefab identity, not a special terrain
entity type.

**T0.4 gate:** a cabin/property crosses chunk boundaries, streams correctly from
all directions, uses no node-per-static representation, and preserves logical
Multi identity through chunk compilation.

## T0.5 — resident `WorldChunkStore` and dynamic entity membership

Dynamic actors/items continue to live in ECS. T0 adds the spatial index the world
has been missing.

The resident store is bounded and data-oriented:

```text
WorldChunkStore
    fixed metadata slots
    bounded key -> resident-slot lookup
    free-slot stack/ring
    terrain/static immutable views or owned prepared data
    dynamic-publication page per resident slot
    bounded dirty-history storage
```

Avoid:

- object-per-chunk graphs on the hot path;
- `Dictionary<Chunk, List<Entity>>`;
- per-entity heap nodes;
- allocation on ordinary movement;
- persisting resident slot numbers.

A small managed dictionary may be acceptable as an initial cold resident-key map
if its scale is explicit and movement does not mutate/allocate it. If lookup cost
appears in profiles, replace it with a fixed open-addressed table. Do not build a
custom hash table before measurement simply for aesthetic purity.

### Dynamic membership

Introduce one concrete world-position component for spatial dynamic entities.
After the ECS tick commits, changed position/lifecycle data updates the derived
`WorldChunkStore`:

```text
same home chunk
    update packed row in place

crossed chunk boundary
    remove old chunk publication row
    insert/update new chunk publication row

created spatial entity
    insert into home chunk

destroyed/nonspatial entity
    remove from prior home chunk
```

Membership update must be transactional across source/destination capacity. A
failed destination preflight may not remove the source first.

Chunk publication rows are not ECS authority. Restore/generation replacement may
rebuild them from complete committed ECS truth just as Inventory rebuilds its
derived index. A `WorldChunkSlot` or dynamic row slot never appears in persistence.

One entity has one canonical home chunk based on normalized position. Visual
bounds may overlap neighboring chunks, but authoritative dynamic membership does
not duplicate merely because a model is large. Special large-world objects earn a
separate coverage/index mechanism when needed.

**T0.5 gate:** millions of deterministic same-chunk moves and boundary crossings
preserve exact membership, zero duplicate/lost entities, explicit capacity failure,
zero warmed owner-thread allocation, and successful rebuild after generation
replacement.

## T0.6 — packed chunk publication and bounded dirty history

This is the key T0 seam for future Network N1.

Each resident chunk owns **one current packed public view** of its dynamic state.
The initial row schema should be the smallest public locomotion/world vertical,
for example numeric identity/lifetime, prefab/presentation identity, chunk-local
transform, and minimal public simulation flags. It is game-owned projection data,
not a serialization of arbitrary ECS components.

Store it SoA-first where fields are commonly traversed independently:

```text
EntityIndex[]
EntityGeneration[]
PrefabId[]
LocalX[]
LocalZ[]
Y[]
Orientation[]
PublicStateFlags[]
LastChangedRevision[]
...
```

Metadata for a chunk/page may remain a small AoS row. Large columns are contiguous.
Do not put boxed components, object references, delegates, strings, or Godot types
in the publication page.

### Publication revision

After one committed authority step:

- if no public row changed, the chunk revision does not advance;
- if one or more public rows changed, advance the chunk publication revision once;
- all dirty facts produced by that committed publication name that revision;
- publish the complete current SoA values before making the new revision visible.

This yields a stable local semantic query:

```text
first observation
    -> complete chunk snapshot @ revision N

cursor N, current N
    -> NoChanges

cursor N, history still covers N+1..M
    -> coalesced delta to current revision M

cursor older than retained history floor
    -> SnapshotRequired
```

History is a fixed ring. It never grows because a recipient is slow.

### Dirty-history semantics

The journal records **which public identities/fields became dirty**, while the SoA
page remains the source of latest coalescible values. A client that skips 20
position writes normally receives the current position once, not 20 intermediate
positions.

Removal/leave facts need enough identity/lifetime information to survive row-slot
reuse. Never rely on a dirty journal entry that points only at a slot which may
have been recycled for another entity.

The exact journal encoding is a measured T0.6 decision. Candidate designs include:

- fixed `(revision, identity, field-mask, current-slot/op)` entries with query-time
  dedup scratch;
- per-revision dirty bitsets plus a separate bounded tombstone identity ring;
- another fixed representation that proves lower CPU/bytes without pinning row
  reuse to the slowest reader.

Reject any design that needs a per-recipient dirty structure inside the chunk.
Recipients will own cursors later; chunks own publication/history once.

Non-coalescible gameplay facts remain Event semantics. Do not put attacks, chat,
sounds-that-must-occur, inventory transactions, or other ordered facts into a
latest-state dirty ring merely because the ring exists.

### Snapshot atomicity

A snapshot contains a complete current public scope for that chunk at one revision.
Apply stages and validates before replacing the client's corresponding chunk page.
A delayed pre-snapshot delta cannot resurrect state older than the committed
snapshot floor. T0 proves this locally before N1 has a packet format.

**T0.6 gate:** deterministic snapshot/delta oracle, history wrap -> snapshot
fallback, slot-reuse/tombstone tests, cross-chunk migration ordering tests, warmed
zero-allocation publication/query, and bounded memory independent of observer
lifetime.

## T0.7 — chunk interest rings and local streaming oracle

AOI becomes a query over `WorldChunk` AABBs.

A recipient/observer has an authoritative spatial anchor. The query returns a
small canonical set of chunk keys plus a relative cadence tier. It does not ask
each chunk which members are within radius X.

### Ring 0

Ring 0 is the local chunk set. A point has one canonical home chunk under half-open
coordinate ownership, but the **interest footprint** may intersect neighboring
chunk AABBs. In ordinary interior movement this is one chunk; near one boundary it
is commonly two; near a corner it may be four. The whole selected chunk is R0.

There is no R0 entity-by-entity radius filter.

### Near/Far rings

For outer rings, compare the observer footprint/radius to chunk AABBs. A chunk is
assigned the nearest applicable tier once:

```text
R0 / Immediate    query every eligible immediate cadence
Near              same publication, slower query cadence
Far               same publication, slower query cadence
Outside           no subscription/query
```

Changing Near -> Far changes only subscription cadence/priority. It does **not**
retier every entity in that chunk or create another semantic WorldState type.

The client receives public chunk state and performs precise presentation culling,
LOD, occlusion, and camera selection. Sending some public entities that are beyond
the exact visual circle is an intentional coherence-for-CPU trade.

This rule is only for public state. Hidden/invisible/GM/private inventory/quest
secrets and owner-private state must use separate authorized streams and never be
placed in a broadly shared public chunk snapshot.

### Local oracle

Before Network N1, build an in-process subscriber that owns only:

```text
WorldChunkKey
last applied publication revision
cadence/phase
resident client-chunk slot
```

New subscription -> snapshot. Existing subscription -> delta query. History miss
-> snapshot. Leaving scope -> retire presentation chunk without mutating server
world state.

This is the chunk equivalent of OfflineDirect: it proves semantics without packet
noise.

**T0.7 gate:** boundary/corner movement changes only the expected chunk
subscriptions, same-chunk movement creates no membership churn, tier changes do
not rewrite member state, and local snapshot/delta application converges exactly.

## T0.8 — client resident cache and Godot world renderer

The client owns a bounded retained world projection separate from Godot nodes/RIDs.
Suggested cold state machine:

```text
Absent
Requested/Preparing
Staged
Resident
Retiring
```

Local/offline T0 may prepare directly from `WorldPack`; N1 later supplies the same
semantic snapshot/delta data remotely.

A client resident chunk stores typed/packed semantic data and render-resource
handles. It does not expose server ECS entities or mutable server chunk memory.

### Godot terrain

Use a small number of renderer objects per resident chunk, preferably direct
`RenderingServer` resources where that materially reduces scene-tree overhead.
Do not instantiate one node per terrain cell.

### Godot statics

Group frozen statics by compatible render key. Prefer:

- combined/baked mesh for immutable heterogeneous structure/clutter where rebuild
  frequency is negligible;
- `MultiMesh` for repeated identical meshes/material-compatible variants;
- individual dynamic render objects only for genuinely dynamic/interactively
  animated state that cannot be represented in a batch.

The renderer owns a mapping from render instance/pick result back to logical world
identity when interaction requires it.

### Floating render origin

The client may maintain an origin chunk and render everything relative to it so
Godot floats stay numerically small. Rebase updates presentation transforms/RIDs;
it never changes authoritative `WorldPosition`, chunk revision, persistent
identity, or network meaning.

### Streaming budget

Mesh/static preparation may run on workers over immutable prepared chunk data.
Godot resource publication occurs on the permitted engine thread/boundary. Work is
budgeted so crossing a chunk boundary cannot synchronously build an unbounded
number of meshes in one frame.

**T0.8 gate:** bounded walk through a multi-chunk world with repeated enter/leave,
no resource leaks, no node explosion, deterministic client semantic hashes, and
recorded mesh/build/upload/frame-time tails.

## T0.9 — persistence and mutable-world seam

The immutable base `WorldPack` is content, not a gigantic save snapshot. Durable
save data references its world-pack/content fingerprint.

Dynamic entities remain in existing ECS persistence. After restore, the chunk
membership/publication index is rebuilt from staged ECS truth before the candidate
`GameGenerationState` publishes.

Runtime streaming state is never persisted:

```text
resident slot ids
cache LRU state
mesh/RID handles
publication query scratch
client cursors
```

### Mutable terrain/statics

Do not rewrite base-pack bytes in place. When terrain construction/destruction or
runtime static placement becomes real, persist a bounded/versioned **world overlay**
per changed chunk (or region pack) through the existing save-set transaction.

The first mutable overlay format must distinguish:

- terrain cell/height overrides;
- added/removed/replaced static placements;
- logical Multi instance additions/removals/variants;
- schema/content version.

A complete prepared chunk is `base + durable overlay`, validated before resident
publication. Compaction of long overlay histories is a persistence concern and
must be atomic.

T0 does not need a full building editor to prove this seam. One deterministic
static/terrain overlay mutation and restore is enough if implementation reaches
this checkpoint; otherwise preserve the contract for the first construction
feature.

**T0.9 gate:** save/restore reproduces the same world-pack identity, ECS dynamic
entities, derived chunk membership, and any implemented overlay mutation without
persisting runtime slots.

## T0.10 — playable woods/cabin vertical and acceptance

Build a deliberately small real world rather than another isolated microbenchmark:

```text
woods / uneven terrain
    -> clearing
    -> cabin spanning one or more chunk boundaries
       structural shell
       door/interactable dynamic overlay
       interior/decor layer
       chest/container
       several frozen clutter/statue placements
    -> nearby dynamic NPC/animal or moving test actor
```

Acceptance flow:

1. start or restore the authoritative server generation;
2. enter outside the cabin;
3. stream/render only the bounded local chunk window;
4. move across terrain and chunk boundaries;
5. enter the cabin and admit its interior/decor stream;
6. interact with the door/chest or another authoritative object;
7. move/drop an item so authoritative/persistent state changes;
8. retire and rebuild the client world projection;
9. disconnect/reconnect locally where applicable;
10. restore a durable save and reproduce the same world state.

This is not a UI milestone. Add only enough UI to exercise the world action.

### T0 performance/evidence matrix

Every accepted benchmark records exact world geometry/profile, chunk edge, cell
size, resident counts, dynamic/static density, authority cadence, allocation
scope, and canonical state hashes.

Required profiles:

```text
terrain_flat
terrain_rough
chunk_boundary_walk
chunk_corner_walk
sparse_world_movement
packed_dense_town
static_heavy_property
multi_boundary
publication_idle
publication_dirty_2pct
publication_heavy
history_wrap_snapshot
client_stream_walk
Godot_static_heavy
```

Measure phases independently:

- coordinate/membership update;
- dynamic page publication;
- dirty-journal append/query/coalesce;
- snapshot copy/apply;
- world-pack read/validation;
- terrain mesh generation;
- static batch preparation;
- Godot resource publication;
- complete local streaming frame.

Permanent invariants:

- unchanged chunks cost no semantic publication revision/work beyond bounded
  scheduler/query checks;
- same-chunk entity movement does not mutate recipient topology;
- chunk crossing performs bounded source/destination work;
- no warmed allocation in protected membership/publication/query paths;
- no per-cell or per-static scene-tree node scaling;
- history and caches remain fixed-capacity;
- accepted chunk-size choice is based on whole-pipeline timing, not one counter;
- rejected experiments remain documented as negative evidence.

## Data-layout requirements

T0 should stay close to the metal without becoming unsafe-code theater.

Prefer:

- flat numeric structs;
- SoA for large hot publication columns;
- CSR/offset+count for immutable variable-size static sections;
- fixed free lists and rings;
- canonical sorted directories;
- chunk-local compact coordinates;
- caller-provided `Span<T>` for bounded query output;
- immutable prepared data across worker/owner boundaries;
- explicit `ReservedBytes` diagnostics for major stores.

Avoid:

- object-per-cell or object-per-static representations;
- nested managed collections proportional to theoretical world capacity;
- string/name lookups in hot world code;
- virtual/interface dispatch inside per-cell loops unless measurement proves it
  irrelevant;
- storing duplicate global coordinates when the chunk key already supplies the
  high bits;
- serializing raw in-memory structs as a wire/disk format;
- runtime format fields whose only consumer is Godot.

A single aligned unmanaged slab may eventually be the best storage for hot
resident publication pages, but only introduce custom native ownership when the
layout/benchmark justifies it. Contiguous managed numeric arrays are preferable
to a bespoke allocator that makes correctness worse without measurable benefit.

## Capacity model

T0 names every proportional bound explicitly. At minimum:

```text
maximum resident server chunks
maximum resident client chunks
maximum terrain cells/chunk                 derived from format
maximum frozen statics/chunk
maximum Multi fragments/chunk
maximum dynamic public rows/chunk
maximum dirty-history entries/chunk
maximum prepared chunk loads
maximum mesh vertices/indices/chunk
maximum render batches/chunk
maximum static query output
maximum world-overlay bytes/chunk           when implemented
```

Do not reserve worst-case dynamic/static arrays for every theoretical chunk in an
unloaded world. Capacity distinguishes **world addressability** from **resident
working set**. Large immutable pack sections may live on disk/mapped storage while
a bounded resident cache owns hot mutable/publication state.

If one town chunk exceeds an admitted dynamic/static capacity, report the exact
high-water and choose a new world profile or bounded overflow-page design. Do not
fall back to an unbounded list.

## Concurrency and ownership

Authority-facing world mutation is owner-thread/barrier-bound.

```text
ECS systems execute
    -> structural barrier commits
    -> replication/world capture observes complete state
    -> chunk membership/publication candidate is prepared
    -> owner publishes chunk revisions
```

File read, decompression if later added, validation, and mesh generation may occur
on workers after ownership of immutable buffers is clear. A worker never mutates
live `WorldChunkStore` state.

Prepared ownership is single-use. If publication fails after trusted mutation
begins unexpectedly, fault the owning world/runtime path under the same principle
used elsewhere in UltraSim rather than pretending a partial world update can be
rolled back.

## Interaction with existing Network N0

Do **not** delete or rewrite `GameInterestService` while T0 is under construction.
N0 remains accepted executable evidence and the current remote client path.

T0 only builds the semantic chunk publication/oracle needed for N1. During T0:

- Network N0 can continue using current entity relations for existing tests;
- local world streaming consumes chunks directly;
- chunk publication receives no UDP/session dependency;
- benchmark evidence compares T0 chunk maintenance to N0 relation maintenance
  only as attribution, not by mixing implementations.

This separation prevents an unfinished terrain system from destabilizing the
accepted network baseline.

## Network N1 handoff contract

T0 is ready to hand back to Network only when the following are accepted APIs or
executable contracts:

```text
WorldChunkKey / normalized WorldPosition
ChunkInterestQuery(anchor, rings) -> bounded chunk keys + cadence tier
CopyChunkSnapshot(key, destination) -> complete revisioned public scope
QueryChunkDelta(key, cursor, destination) -> NoChanges | Delta | SnapshotRequired
CurrentRevision / HistoryFloor
public identity/lifetime semantics across chunk migration
client atomic snapshot/delta apply
separate public vs owner/private/event state
bounded resident and history capacities
```

N1 should then replace recipient/entity maintained AOI for public spatial state
with recipient/chunk subscription cursors where measurement proves the new path.
The likely hot recipient state becomes approximately:

```text
WorldChunkKey
LastAppliedRevision
Cadence
Phase
```

A new chunk subscription gets a snapshot. Existing subscriptions query dirty
history at their cadence. A history miss gets another snapshot. Near/Far changes
query frequency. The client handles fine visibility. Packet framing/encryption
remains per connection while semantic chunk packing is shared.

Entity migration between chunks must preserve global canonical identity/generation
so delayed old-chunk removal/update cannot erase or resurrect newer state in the
new chunk. N1 reuses the baseline/floor lessons already proved by N0 rather than
inventing weaker semantics.

Private/owner state and retained Events remain separate streams.

## T0 definition of done

T0 is accepted only when:

- world/chunk/local coordinates are canonical for negative and large positions;
- the selected chunk/cell/height format is documented by evidence;
- immutable world-pack validation is transactional and bounded;
- terrain and frozen statics stream without ECS/entity/node-per-placement
  explosion;
- logical Multi/property identity survives chunk splitting;
- dynamic ECS entities rebuild into one exact home-chunk index;
- same-chunk movement is a packed-row update, not AOI topology rebuild;
- source/destination chunk crossing is transactional and bounded;
- each chunk has one current packed public publication and bounded history;
- snapshot/delta/history-miss semantics converge under local loss/reorder-style
  tests;
- chunk interest tiers change subscription cadence, not member semantic type;
- client precise visibility does not feed back into server public membership;
- private/secret data is structurally excluded from public chunk pages;
- terrain mesh seams are deterministic and static rendering is batch-oriented;
- resident server/client memory and every hot proportional structure have named
  capacities;
- warmed membership/publication/query paths meet zero-allocation contracts;
- the woods/cabin vertical works through save/rebuild boundaries;
- current Network N0 remains green while T0 is introduced.

## Deliberate implementation-time decisions

These require measurements or real content rather than premature commitment:

- final 16/32/64 m chunk edge;
- terrain cell size and height quantum/width;
- managed arrays versus aligned unmanaged slabs for resident publication pages;
- dirty-history encoding and dedup strategy;
- dynamic rows/chunk and whether a bounded overflow page is ever required;
- exact Godot `RenderingServer` vs scene-node ownership split;
- combined static mesh granularity versus `MultiMesh` grouping;
- runtime texture atlas policy and atlas dimensions;
- world-pack compression and I/O backend;
- mutable terrain/static overlay format;
- interior/portal/occlusion policy;
- whether server residency eventually unloads chunks containing dormant ECS
  entities or whether entity simulation partitioning becomes a later milestone;
- whether another game's world needs any of this promoted into a reusable kernel.

They may change mechanism. They may not weaken the frozen contracts above.

## Stop condition and next milestone

Do not continue polishing terrain after T0 merely because more world features are
possible. Once the chunk format, packed publication, streaming renderer, bounded
history, persistence seam, and woods/cabin vertical are accepted, **stop T0 and
return to Network N1**.

N1's first job is not new transport research. It is to consume the accepted T0
chunk publication model and test the original chunk-native AOI hypothesis against
the frozen N0 sparse baseline:

> maintain recipient-to-chunk cursors, snapshot on first interest, query bounded
> dirty history thereafter, let cadence control query frequency, and let the
> client perform fine public visibility.

If that does not materially improve sparse scaling while preserving N0 semantics,
reject it with the same evidence discipline used throughout N0. If it does, make
chunk-native spatial replication the production N1 path and leave N0's
recipient/entity graph as historical/reference evidence.
