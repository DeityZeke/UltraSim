# Terrain / World T0: spatial-seam, chunk-native world implementation plan

Status: active forward plan. Revised 2026-08-19 from the accepted Network N0 baseline, the later spatial-seam design decisions, and the V1/transitional spatial-publication archaeology.
Network N0 is accepted and frozen as the current remote-authority reference. T0 is the next major implementation milestone. Network N1 is deliberately deferred until T0 has proved the world/spatial/publication contracts that N1 can consume.
T0 is not merely a terrain renderer. It establishes the spatial substrate for the persistent world: deterministic integer coordinates, a canonical coarse spatial partition, packed terrain and statics, incremental dynamic membership, chunk-associated public publication state, bounded dirty history, local streaming, Godot rendering, and the seams required by persistence and later replication profiles.
The motivating observation is measured rather than speculative. Network V3 Dense-400 is efficient once recipient/entity relationships are established, while sparse movement is limited by changing and retiering those relationships. The rejected N0.11 exact-AOI experiment removed 31.55% of 4K maintained relations and 40.36% of WorldState records, yet doubled interest-update p95 because precise recipient/entity membership reconciliation cost more than the coherent work it removed.
T0 therefore introduces a maintained coarse spatial index so entity spatial work is paid once when authoritative spatial state changes instead of being rediscovered independently by every observer. The game profile uses 16 m spatial chunks, but terrain, publication, AOI subscriptions, client semantic residency, rendering, physics, navigation, and other domains keep private representations and private local slot identities. They may share only canonical spatial identity/geometry where useful.
The independent Spatial seam is a **contract/read vocabulary, not a data-moving intermediary**. It must not become an event bus, mailbox, publication thread, generic chunk framework, or mandatory dependency of otherwise independent kernels. Concrete bridges may query optimized game/world spatial and publication stores directly, including borrowed spans/read views, when that is the correct hot path.
Recent archaeology of the V1-to-V2 transition reinforces the intended hot-path shape: movement/component writes may occur several times inside one authority step, but component-scoped dirty capture deduplicates the entity, a barrier/stage completion establishes that the final authoritative value is readable, and Spatial consumes that entity once. The old event-like mechanism is therefore retained only as a **completion/readiness semantic**, not as a per-movement message bus. T0.5 must preserve that property without relying on destructive-drain ordering between consumers.
The first complete Terrain milestone is **T0 — Chunk-Native Playable World**:

> Start the existing authoritative Runtime and game composition, enter a deterministic persistent world, move across spatial chunk boundaries, stream packed terrain and UO-like statics into Godot, enter a small building, interact with dynamic world objects, mutate authoritative/persistent state, retire or rebuild the local projection, and reproduce the same visible world without making renderer nodes, network relations, compatibility-seam objects, or subsystem-local slots authoritative.

At the end of T0, return to [`NETWORK.md`](NETWORK.md) and implement/measure the N1 **ChunkPublication** replication profile against these accepted contracts. N1 is an additional concrete profile beside the accepted **EntityRelation** profile; T0 does not promise that N1 will delete or retire N0's relation machinery. Do not redesign Network AOI again before the world substrate exists.

## Frozen principles

```
WORLD SPACE IS INTEGER AND DETERMINISTIC.

THE GAME WORLD POSITION QUANTUM IS 1 MM.

THE GAME SPATIAL CHUNK EDGE IS 16 M / 16,000 MM.

THE TERRAIN CELL EDGE IS 0.5 M / 500 MM.

THE AUTHORING / ITEM PLACEMENT SNAP IS 0.1 M / 100 MM AND IS POLICY, NOT WORLD IDENTITY.

TERRAIN CELLS DO NOT DEFINE ENTITY POSITION GRANULARITY.

PLACEMENT SNAP DOES NOT DEFINE WORLD POSITION GRANULARITY.

DYNAMIC ENTITIES ARE NOT SNAPPED TO TERRAIN CELLS OR THE AUTHORING GRID.

SPATIAL IDENTITY IS A VALUE.

SUBSYSTEM-LOCAL SLOT IDENTITY IS NEVER PERSISTENT IDENTITY.

SYSTEMS MAY SHARE SPATIAL IDENTITY AND GEOMETRY; THEY DO NOT SHARE MUTABLE CHUNK STORAGE.

THE SPATIAL CONTRACTS SEAM DEFINES MEANING; IT DOES NOT MOVE, QUEUE, OR COPY WORLD STATE.

THE SPATIAL SEAM IS NOT AN EVENT BUS, MAILBOX, MANAGER, OR PUBLICATION THREAD.

INDEPENDENT KERNELS REMAIN USABLE WITHOUT A MANDATORY SPATIAL DEPENDENCY.

CONCRETE BRIDGES MAY USE CONCRETE HOT READ APIS WHEN PERFORMANCE REQUIRES IT.

CROSSING THE SPATIAL SEAM IS A ZERO/NEAR-ZERO-COPY READ OF COMMITTED INDEXED STATE, NOT AN ASYNC PIPELINE.

SPATIAL CENTRALIZES COARSE SPATIAL TRUTH AND INCREMENTAL MEMBERSHIP, NOT EVERY SUBSYSTEM'S SPATIAL POLICY.

A WORLD SPATIAL CHUNK DOES NOT KNOW WHO IS WATCHING IT.

ECS ENTITIES REMAIN AUTHORITATIVE SIMULATION ENTITIES.

COARSE SPATIAL MEMBERSHIP IS A DERIVED ACCELERATOR.

BASE TERRAIN AND FROZEN STATICS ARE WORLD DATA, NOT ECS ENTITIES BY DEFAULT.

TERRAIN CHUNKS ARE 2D HEIGHTFIELD/STATIC PARTITIONS, NOT ECS STORAGE CHUNKS.

TERRAIN, PUBLICATION, AOI, CLIENT, RENDER, PHYSICS, AND NAV CHUNKS/REGIONS MAY DIFFER IN GRANULARITY LATER.

NO AUTHORITATIVE FLOAT POSITION IS REQUIRED.

NEGATIVE COORDINATES HAVE ONE CANONICAL FLOOR-DIVISION RULE.

PUBLICATION MEANS COMMITTING A REVISION/ROOT/VISIBLE STATE; IT DOES NOT MEAN COPYING A GIANT SNAPSHOT EVERY TICK.

ONE PUBLICATION CHUNK OWNS ONE CURRENT PACKED VIEW OF ITS PUBLIC SPATIAL STATE.

PACK/PROJECT PUBLIC STATE ONCE PER COMMITTED CHANGE, NOT ONCE PER RECIPIENT.

SNAPSHOTS ARE COMPLETE AT A PUBLICATION REVISION.

LATEST STATE COALESCES.

EVENTS RETAIN FACTS.

DIRTY HISTORY IS BOUNDED.

HISTORY MISS FALLS BACK TO A SNAPSHOT.

AOI QUERIES COARSE SPATIAL CHUNKS, NOT MEMBERS WITHIN A DISTANCE CIRCLE.

AOI TIER CHANGES QUERY FREQUENCY/PRIORITY, NOT THE TYPE OF WORLD STATE.

RING 0 IS A LOCAL CHUNK SET, NOT AN ENTITY-BY-ENTITY IMMEDIATE SET.

CLIENT PRESENTATION MAY CULL MORE PRECISELY THAN SERVER CHUNK ADMISSION.

PRIVATE OR SECRET STATE NEVER ENTERS A BROADLY SHARED PUBLIC CHUNK PAGE.

STATIC VISUAL SCALE DOES NOT REQUIRE ONE NODE PER STATIC.

FROZEN HETEROGENEOUS GEOMETRY MAY BE BAKED; REPEATED IDENTICAL GEOMETRY MAY BE INSTANCED.

LOGICAL MULTI/PROPERTY GROUPING IS SEPARATE FROM GODOT MULTIMESH GROUPING.

LOAD/PREPARE MAY BE ASYNCHRONOUS.

AUTHORITY/SPATIAL/PUBLICATION COMMIT IS OWNER-BOUND AND TRANSACTIONAL.

AUTHORITATIVE STRUCTURAL MUTATION HAS ONE SANCTIONED COMMIT/SINK PATH; HOT-PATH CONVENIENCE MAY NOT BYPASS IT.

MULTIPLE WRITES TO ONE SPATIAL COMPONENT WITHIN ONE COMMITTED STEP COALESCE TO ONE CHANGED-ENTITY OBSERVATION OF THE FINAL VALUE.

SPATIAL MAINTENANCE CONSUMES A BARRIER-SCOPED COMMITTED CHANGE VIEW OR AN EQUIVALENT DEDUPLICATED CHANGE CAPTURE; IT DOES NOT REQUIRE A PER-MOVEMENT EVENT.

A BARRIER/EVENT NOTIFICATION MAY SIGNAL THAT CHANGES ARE READY; IT DOES NOT CARRY OR OWN THE MOVEMENT DATA.

A CHANGE SET NEEDED BY MULTIPLE DERIVED CONSUMERS MAY NOT DEPEND ON "COPY BEFORE SOMEONE ELSE DRAINS" ORDERING; MULTI-READER OR EXPLICITLY OWNED CHANGE-VIEW SEMANTICS ARE REQUIRED.

PUBLICATION MAY PREPARE/CACHE RECIPIENT-INDEPENDENT SEMANTIC BODIES ON COMMIT WHEN MEASUREMENT JUSTIFIES IT; FINAL CONNECTION PACKETS REMAIN NETWORK-OWNED.

PREPARED PUBLICATION IS BUILD-ONCE / FAN-OUT-MANY SEMANTIC WORK, NOT ONE FINAL PACKET PER RECIPIENT.

ASYNC PREPARATION/SEND MAY ONLY CROSS AN IMMUTABLE OR VERSION-STABLE OWNERSHIP BOUNDARY; IT MAY NOT READ LIVE MUTATING ECS/SPATIAL STORAGE.

RUNTIME MEMORY IS BOUNDED.

CAPACITY FAILURE IS EXPLICIT.

NO HOT-PATH OBJECT GRAPH IS CREATED PER CELL, STATIC, ENTITY, CHUNK SUBSCRIPTION, OR RECIPIENT.
```

These rules survive changes to mesh generation, file compression, renderer, network codec, future publication aggregation, world size, and eventual server partitioning. The fixed 16 m / 0.5 m / 0.1 m / 1 mm game profile may be reopened only by concrete evidence that it fails a required correctness, capacity, or whole-pipeline performance gate; it is no longer a routine T0.1 search parameter.

## Terminology

`ECS chunk` continues to mean the packed archetype storage unit owned by the ECS kernel. This plan uses `WorldChunk` for the game's canonical 16 m × 16 m coarse X/Z world partition. Code and docs must not shorten both concepts to an ambiguous `Chunk` at public/API boundaries.

### Spatial contracts seam

`Spatial.Contracts` is the architectural name for the tiny independent interoperability vocabulary. The physical repository/project name should follow existing conventions (for example `Kernels/Spatial` / `UltraSim.Spatial`) but the dependency rule is more important than the folder name.
The seam initially contains only value identity/geometry and canonical math that are proven common. Candidate values include:

```
SpatialSpaceId
SpatialChunkCoord(X, Z)
SpatialChunkKey(SpatialSpaceId, X, Z)
SpatialChunkGeometry / profile
SpatialBounds
canonical floor division / normalization / overlap math
```

It owns **no** terrain, ECS access, entity lists, publication pages, dirty journals, subscriptions, render state, threads, queues, Godot types, packet types, or service container. Do not add generic `ISpatialProvider`, `IChunk<T>`, manager, observer, or plugin abstractions during T0 merely to make the seam look reusable. Interfaces are earned only when multiple real consumers have the same semantic shape.
The seam must not hard-code this game's 16 m terrain geometry as a universal law. Geometry/profile is explicit. This game's concrete world profile declares the fixed values below.

### Game world spatial profile

```
Authoritative position unit     1 mm
WorldChunk edge                16,000 mm = 16.0 m
Terrain cell edge                 500 mm = 0.5 m
Terrain cells / chunk edge          32
Terrain cells / chunk              1,024
Height vertices / chunk         33 × 33 = 1,089
Placement snap                    100 mm = 0.1 m
```

These are four independent granularities. Only the terrain plane is a dense grid. Placement snap is a tooling/gameplay policy. Dynamic movement is free on the 1 mm authoritative lattice.
A player or dynamic object may therefore stand at `12.036 m` even though that location is neither a 0.5 m terrain-cell boundary nor a 0.1 m placement snap. A source float such as `12.0357283 m`, if it ever crosses an authority import boundary, is explicitly and deterministically quantized to the nearest millimeter; implicit float casts are not authoritative semantics.

### Corresponding domain chunks are not one object

The same `SpatialChunkKey` may identify corresponding data in several domains, but each domain owns its representation and local slot identity:

```
World spatial chunk / WorldSpatialIndex
    coarse 16 m address + dynamic home membership

TerrainChunk
    32 × 32 material cells
    33 × 33 height lattice
    frozen statics / Multi fragments

PublicationChunk
    packed public dynamic rows
    publication revision
    bounded dirty history

RecipientChunkSubscription / AOI state
    SpatialChunkKey
    revision cursor
    cadence / phase
    replication-owned, never world-owned

ClientSemanticChunk
    retained semantic terrain/static/dynamic projection

RenderChunk / render region
    mesh/RID/MultiMesh/LOD/visibility state
    may aggregate or subdivide spatial chunks later

Physics/Nav/AI regions
    private domain-specific acceleration if/when earned
```

A terrain resident slot, spatial-index slot, publication row slot, client resident slot, render RID, ECS slot, network connection slot, or AOI subscription slot are all different process-local identities. No subsystem may assume their numeric values correspond.
`PublicationRevision` is chunk-local monotonic latest-state publication identity. It is not authoritative simulation tick identity, although a publication records the authoritative tick that produced it.
A `Multi` remains the UO-like logical property/structure grouping. It is separate from spatial partitioning and separate from Godot `MultiMesh` batching.

## Scope and deliberate non-goals

T0 includes the minimum world machinery needed to make the spatial partition real, prove a useful compatibility seam, and prove that chunk-associated public state can become a later replication/streaming unit. It does not attempt to finish every world system.
T0 includes:

- dependency-neutral Spatial value/geometry contracts and canonical math;
- the fixed game spatial profile: 16 m chunks, 0.5 m terrain cells, 0.1 m placement snap policy, 1 mm dynamic position quantum;
- deterministic world/chunk/local coordinate math with correct negative-coordinate behavior;
- a compact terrain height/material representation;
- a deterministic runtime `WorldPack` format and reader;
- UO-like frozen static placements and logical Multi/property composition;
- a concrete, game-owned `WorldSpatialIndex` for coarse dynamic home-chunk membership;
- incremental maintenance from a deduplicated, barrier-scoped ECS position/lifecycle change view rather than full-world spatial rescans or per-movement messages;
- explicit multi-reader/owned change-set semantics so Spatial, publication, persistence, or other derived consumers do not race over one destructive dirty drain;
- a separate packed `ChunkPublicationStore` and bounded dirty-history contract;
- a measured path for recipient-independent prepared publication bodies/caches, while keeping final connection framing/encryption out of T0;
- exact chunk-overlap interest queries for local/offline use;
- direct committed read views/borrowed packed spans where useful, without an intermediate compatibility DTO pipeline;
- local snapshot/delta consumption as a semantic oracle for future N1;
- terrain/static client streaming and bounded resident caches;
- a separate client semantic projection and render representation;
- Godot terrain mesh and static batching without per-cell/per-static nodes;
- deterministic ground/static spatial queries sufficient for the T0 movement vertical;
- persistence seams for base-pack identity, dynamic ECS entities, and future mutable world overlays;
- one small playable woods/cabin vertical slice.

T0 does **not** require:

- Network N1 packet formats or recipient chunk cursors over UDP;
- replacing/deleting the accepted N0 EntityRelation replication profile;
- making `Kernels/Network`, ECS, UI, or another independent kernel depend on Spatial;
- a generic spatial manager/framework, event bus, service container, or `IChunk<T>` hierarchy;
- final public package-manager/SDK packaging for optional Spatial adapters;
- a production world editor;
- procedural infinite terrain;
- voxel caves/destruction;
- final physics, navigation, pathfinding, water, weather, seasons, farming, or biome simulation;
- final occlusion/portal systems;
- final texture-atlas policy;
- one Godot `MultiMesh` for heterogeneous meshes (Godot `MultiMesh` repeats one mesh; heterogeneous frozen clutter is instead baked/combined or grouped into multiple compatible instance batches);
- arbitrary live terrain deformation until the immutable/base path is proved;
- server sharding or ownership transfer between processes;
- final extraction of EntityRelation out of Network or a generic replication-profile framework;
- dedicated async socket/send workers, connection-worker sharding, or task-per-packet networking;
- server-to-server publication/authority-transfer streams (the architecture should not forbid them later, but T0 does not design them).

Those capabilities must consume/adapt the accepted spatial semantics later rather than delay T0.

## Ownership and dependency shape

The existing authority root remains unchanged:

```
ServerRuntime
    -> ServerGeneration
        -> EcsWorld                         dynamic simulation truth
        -> WorldClock
        -> GameGenerationState
             -> GameWorldState
                  -> WorldPack / terrain stores
                  -> WorldSpatialIndex      derived coarse membership
                  -> ChunkPublicationStore  derived public projection/history
```

The terrain/world implementation remains concrete game code. Do **not** create a generic terrain kernel merely because terrain is foundational.
The **Spatial contracts seam is the exception only in the narrow sense that it is intentionally dependency-neutral vocabulary**, not a generic world engine. Its job is to make compatible systems agree on spatial identity/geometry without forcing them to share storage or implementation.
Desired dependency shape:

```
                         independent mechanisms/contracts

          ECS          Network          UI          Spatial.Contracts
           |              |              |                 |
           |              |              |                 |
           +--------------+--------------+-----------------+
                                  |
                           composition / bridges
                                  |
                 +----------------+----------------+
                 |                                 |
                 v                                 v
            Game / World                    Game / Replication
                 |                          knows Network +
                 |                          concrete world/publication
                 v                                 |
            Client / World ------------------------+
                 |
                 v
            Client / _Godot
```

Permanent direction rules:

```
Kernels/Network  -X-> Spatial.Contracts
Kernels/Network  -X-> Game/World
Kernels/Network  -X-> ECS
Kernels/Network  -X-> Godot

Spatial.Contracts -X-> Network
Spatial.Contracts -X-> ECS
Spatial.Contracts -X-> Game/World
Spatial.Contracts -X-> Godot
```

A future optional `Network.Spatial` adapter/package may depend on both Network and Spatial, but the raw Network kernel remains directly usable without it. Likewise, an external engine may adapt its own world to Spatial without taking this game's terrain system.
For this game, concrete bridge code is **allowed** to know both `WorldSpatialIndex`/`ChunkPublicationStore` and Network. Compatibility is semantic; the hot path does not have to pay for generic runtime polymorphism.
Candidate project shape:

```
Kernels/Spatial or equivalent dependency-neutral contracts project
    spatial identity/value types
    geometry/profile value
    canonical floor/overlap math
    NO storage / managers / queues / consumers

Game/World
    Spatial/
        fixed game profile
        optimized WorldPosition / WorldLocalPosition
        WorldSpatialIndex (T0.5)
    Terrain/
        world-pack format + validated reader
        TerrainChunk storage
        statics / Multi fragments
    Publication/
        ChunkPublicationStore (T0.6)
        bounded dirty history

Game/Composition
    owns GameWorldState in GameGenerationState
    consumes committed ECS spatial/lifecycle changes
    updates spatial membership/publication at owner barrier

Game/Replication
    concrete replication bridge/profiles
    may directly borrow committed world/publication views
    Network kernel stays unaware of Spatial/Terrain/ECS

Client/World
    retained ClientSemanticChunk projection
    snapshot/delta apply
    render-ready immutable payloads

Client/_Godot
    private RenderChunk/render-region state
    terrain mesh build/commit
    static batching / RenderingServer resources
    presentation culling / floating render origin

_Tools/WorldPack                only when authored-source compilation is needed
_Tests/UltraSim.Game.World.Tests
_Benchmarks/UltraSim.Game.WorldBenchmarks
```

If a new Spatial contracts assembly is not compatible with current repository/project rules, preserve the same dependency-neutral boundary in the smallest repository-consistent form and document the deviation. Do not solve project layout uncertainty by putting spatial contracts inside Network.
Network does not own world spatial membership or publication storage. Persistence does not own it. Godot does not own it. `Spatial.Contracts` does not own it. Bridges translate or directly read immutable/committed typed outputs according to their domain.

## T0.0 — executable contracts before terrain content

T0.0 proves the dependency shape and coordinate/value machine before terrain files, resident stores, publication machinery, or rendering. **Stop at this gate.**

### T0.0A — dependency-neutral Spatial contracts

Create the smallest repository-consistent Spatial contracts/value boundary. It should contain only the common identity/geometry semantics that are already concrete enough to freeze:

```
SpatialSpaceId
SpatialChunkCoord(X, Z)
SpatialChunkKey(SpatialSpaceId, X, Z)
SpatialChunkGeometry / SpatialProfile geometry fields
SpatialBounds / chunk AABB value required by overlap tests
canonical floor-division / remainder helpers
canonical chunk-bound construction / overlap math
```

Requirements:

- zero dependency on Network, ECS, Game/World implementation, Client, ServerRuntime, Persistence, or Godot;
- no storage manager;
- no entity membership;
- no publication state;
- no queues/events/threads;
- no service container;
- no `IChunk<T>` or generic provider framework;
- no interface/virtual hot-path abstraction without two concrete consumers proving the exact same shape;
- geometry must be explicit rather than globally hard-wired to 16 m so another adapter can expose another chunk profile later.

### T0.0B — fixed game world spatial profile

The concrete game/world layer also owns the values that are not generic Spatial seam vocabulary:

```
WorldLocalPosition
WorldPosition / WorldLocation
TerrainCellCoord
PublicationRevision
WorldPackFingerprint
```

These may use the shared spatial identity/geometry values, but they are game/world contracts rather than reasons to enlarge `Spatial.Contracts`.
The concrete game world freezes these inputs:

```
UnitsPerMeter                  = 1,000
AuthoritativePositionQuantum  = 1 unit = 1 mm
WorldChunkEdgeUnits           = 16,000 mm
TerrainCellEdgeUnits          =    500 mm
TerrainCellsPerChunkEdge      =     32
PlacementSnapUnits            =    100 mm
```

`TerrainCellEdgeUnits` and `PlacementSnapUnits` are game/world policy. They do not need to live in the generic Spatial contracts assembly unless another real consumer proves that they are part of the shared seam.
The preferred authoritative dynamic position representation is normalized chunk-relative X/Z rather than a large floating global vector:

```
ChunkX            int32
ChunkZ            int32
YMillimeters      int32
LocalXMillimeters uint16   // canonical 0..15,999
LocalZMillimeters uint16   // canonical 0..15,999
                  -----
                  16-byte natural payload before optional space identity
```

`SpatialSpaceId` may be stored separately from hot per-entity positions when one world/facet is implied by authority. Multiple future facets do not justify inflating every hot row before they exist.
World calculations use integer millimeters or explicitly defined terrain height units. Godot converts to float only at presentation boundaries and may rebase a render origin without changing authority.
The placement grid and terrain grid never constrain dynamic entity positions:

```
terrain cell boundary    500 mm
placement snap           100 mm
dynamic position           1 mm
```

An item authored at `4.2 m, 7.7 m` may later become dynamic at `4.237 m, 7.684 m`. A character can stand anywhere on the 1 mm lattice over a terrain cell.
If a floating source value is imported, conversion must use one explicit deterministic quantization rule. The intended semantic is nearest millimeter (for example `12.0357283 m -> 12.036 m`), with a documented midpoint rule and symmetric negative-coordinate tests. Do not let C# truncation toward zero define world semantics accidentally.

### T0.0C — correctness tests

Permanent tests must cover at minimum:

- positive X/Z normalization;
- negative X/Z normalization using mathematical floor division;
- exact boundaries at `... -16,000, -1, 0, 15,999, 16,000 ...` mm;
- all four X/Z crossing directions;
- corner crossings;
- large positive and negative chunk coordinates;
- local -> global -> local round trips;
- checked 64-bit intermediates for reconstruction and bounds;
- overflow/rejection before mutation;
- canonical `SpatialChunkKey` ordering;
- deterministic hash/equality;
- no alias between space identity and chunk coordinate identity;
- deterministic half-open chunk AABB construction;
- deterministic AABB intersection/distance semantics required by later AOI;
- malformed/default/out-of-range local values reject rather than normalize silently;
- terrain-cell mapping uses `500 mm` cells and yields `0..31` within a chunk;
- placement snap is tested as an explicit policy helper if implemented, never as a `WorldPosition` invariant.

Use property/exhaustive boundary testing where useful rather than a few hand-picked examples.

### T0.0D — performance/data tests

Keep T0.0 close to the metal:

- readonly/value structs where appropriate;
- numeric fields;
- no strings in hot coordinate identity;
- no object graph;
- no allocation during ordinary normalization/query;
- no LINQ in protected hot loops;
- no virtual/interface dispatch added merely for abstraction;
- no bespoke native allocator unless measurement earns it.

Benchmark at minimum:

- normalize global integer position -> `SpatialChunkKey` + local position;
- reconstruct global position;
- `SpatialChunkKey` hash/equality/ordering where applicable;
- chunk AABB construction/intersection/distance;
- terrain-cell lookup from local millimeters.

Record mean/tail where meaningful and warmed managed allocation.
**T0.0 gate:**

```
all coordinate/value tests pass Debug and Release;
16 m / 0.5 m / 0.1 m / 1 mm game profile is encoded explicitly;
negative-coordinate floor semantics are canonical;
warmed normalization/chunk lookup allocate zero;
checked arithmetic is used where required;
Spatial contracts have no Network/ECS/Game implementation/Godot dependency;
Kernels/Network has no Spatial dependency;
no terrain file format, resident index, publication store, renderer, or N1 work has started.
```

Do not proceed into T0.1 without a new checkpoint/review.

## T0.1 — validate the fixed terrain profile and choose height encoding

T0.1 no longer chooses among 16/32/64 m chunk sizes. The game spatial profile is already fixed at 16 m with 0.5 m terrain cells. T0.1 validates the consequences of that geometry and chooses the remaining physical terrain encoding decisions, especially terrain height representation.
T0 uses a 2D heightfield surface plus statics, closer to UO than a voxel world. The terrain surface is not represented as one ECS entity per tile.
The baseline layout is two dense planes per 16 m `TerrainChunk`:

```
material[cellZ * 32 + cellX]              ushort material/content id
height[vertexZ * 33 + vertexX]            signed fixed-point height

material cells                            32 × 32 = 1,024
height lattice                            33 × 33 = 1,089
```

Ignoring headers and statics, this is approximately:

```
materials: 1,024 × ushort  ~= 2.00 KiB
16-bit heights: 1,089 × short ~= 2.13 KiB
raw terrain foundation ~= 4.13 KiB / chunk
```

The `(N+1) × (N+1)` height lattice gives every terrain chunk complete edge vertices for its `N × N` material cells. Neighboring chunks must agree on their shared edge; the pack compiler validates that invariant. Runtime mesh generation therefore does not need to borrow mutable neighbor memory merely to close a seam.
Terrain material behavior comes from compiled content whenever possible. Do not store duplicated walkability/render/collision flags per cell if they are stable properties of the material definition. Add another per-cell plane only when a real feature proves that the value varies independently.

### Terrain height encoding

Horizontal/world position quantum is already 1 mm. Terrain height encoding is intentionally separate.
Benchmark/requirements compare at least:

- signed 16-bit fixed height with a world-declared height quantum;
- signed 32-bit millimeters if exact vertical millimeters are required.

A 16-bit height plane is preferred only if its range/resolution can be stated as a permanent gameplay/content contract. Terrain sampling converts `HeightUnits -> authoritative Y millimeters` deterministically. Dynamic `YMillimeters` may remain int32 regardless of terrain storage width.

### Fixed-profile validation

Run representative deterministic content through the accepted 16 m / 0.5 m profile and record:

- terrain bytes/chunk and directory overhead;
- frozen-static count distribution and worst chunk;
- dynamic home-membership count distribution and worst chunk;
- chunk crossings per moving entity per second;
- R0/Near/Far chunk subscription counts;
- false-positive public entities delivered by whole-chunk admission;
- mesh build time and emitted triangles;
- Godot resident RID/draw/batch counts;
- world-pack random read size/count;
- spatial-index, dynamic-page, and dirty-history reserved bytes;
- high-water values for a dense town/property profile.

The point is to verify that the chosen geometry produces healthy whole-pipeline behavior and to size capacities. Do **not** rerun a 16/32/64 m search by default. Reopen chunk edge or 0.5 m cell size only if concrete evidence shows a correctness/capacity/performance failure that cannot be solved more locally, and document the rejection/evidence explicitly.
**T0.1 gate:** the fixed 16 m chunk edge, 0.5 m terrain cell, 1 mm dynamic quantum, selected terrain height encoding/quantum, maximum supported local coordinates, and resulting capacity evidence are written into the world-format contract and benchmark artifact.

## T0.2 — immutable `WorldPack` and terrain source

Authoring data and runtime data are separate. T0 first compiles/generates a small
deterministic authored world into one canonical runtime pack.
Do not create one file per chunk. The first pack is a bounded random-access
container:

```

WorldPackHeader

    magic

    format version

    SpatialSpaceId

    chunk/cell geometry

    content/schema fingerprint

    chunk count

    directory location/length

ChunkDirectory[]               canonical sorted keys

    SpatialChunkKey

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
`RandomAccess`, explicit pread/io\_uring, compression, and async read batching are
later measured backend choices, not T0 assumptions.
**T0.2 gate:** deterministic build produces byte-identical packs; malformed packs
reject transactionally; random chunk read has bounded scratch/allocation; one pack
reopens and hashes identically across supported platforms.

## T0.3 — terrain queries and deterministic mesh generation

Server/gameplay code must not depend on a Godot collision mesh to know the ground.
The world layer provides deterministic integer queries directly over chunk data:

```

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

```

Prefab/StaticDefinitionId

chunk-local X/Z

height/Y

orientation/variant

optional stable static-instance identity

optional render/collision flags that are truly instance-varying

```

The exact field widths are chosen from content limits. Content IDs remain numeric;
Godot resource paths/text names are presentation metadata, never server hot data.
Authoring/building tools may snap placements to the 100 mm grid, but the runtime format must not infer that all world objects are permanently restricted to that lattice. Dynamic or physically moved objects use the 1 mm authoritative position quantum.
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

```

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

## T0.5 — resident world stores and dynamic entity membership

T0.5 introduces the concrete coarse spatial acceleration structure the game has been missing. Dynamic actors/items remain authoritative ECS entities.
Do **not** implement one universal `WorldChunkStore` that owns terrain, publication, AOI, and rendering. Compose separate bounded stores keyed by the same `SpatialChunkKey` where appropriate:

```
GameWorldState
    TerrainChunkStore          immutable/prepared terrain + frozen statics
    WorldSpatialIndex         coarse dynamic home-chunk membership
    ChunkPublicationStore     T0.6 public projection/history
```

T0.5 itself focuses on `WorldSpatialIndex`. Publication mechanics are T0.6.
The spatial index is bounded and data-oriented:

```
WorldSpatialIndex
    fixed chunk metadata slots
    bounded SpatialChunkKey -> spatial-index-slot lookup
    free-slot stack/ring
    packed home-membership storage
    entity -> current home mapping sufficient for incremental updates/rebuild
    explicit ReservedBytes / high-water diagnostics
```

Avoid:

- object-per-chunk graphs on the hot path;
- `Dictionary<Chunk, List<Entity>>`;
- per-entity heap nodes;
- allocation on ordinary movement;
- a generic spatial event bus;
- a queue that another subsystem must wait on merely to read committed state;
- persisting spatial-index slot numbers.

A small managed dictionary may be acceptable as an initial **cold resident-key map** if its scale is explicit and movement does not allocate/mutate it proportionally. If lookup cost appears in profiles, replace it with a fixed open-addressed table. Do not build a custom hash table before measurement simply for aesthetic purity.

### Incremental ECS integration

Spatial must consume ECS change tracking/lifecycle facts, not ask the ECS to rediscover all spatial entities every tick.
The required semantic is **one unique entity per relevant committed change set, with the final authoritative component value read after the producing stage/barrier**. If one entity is moved/corrected several times before the barrier, Spatial must not process several movement messages; it observes that the position component changed and reads the final committed position once.

The preferred source is the current ECS's already-accepted **independent changed-row/cursor machinery** if it provides the required exact component observation and lifetime semantics; do not build a second tracker merely to give Spatial a differently named API. Where a barrier-scoped view is the natural surface, the conceptual shape is:

```
Movement / physics / gameplay writes Position
        -> Position dirty capture deduplicates Entity identity
        -> producing stage/barrier completes
        -> CommittedChangeView<Position> becomes readable
        -> WorldSpatialIndex processes each unique changed entity once
```

The change view is a **read contract**, not a queue of copied positions. ECS remains the source of current Position. A V1-style completion event/hook is acceptable only as the control-flow signal that the view is ready; it should not allocate or carry one event per movement.

If current ECS dirty APIs are destructive-drain-only and multiple legitimate consumers need the same barrier change set, do not encode correctness as:

```
Spatial must CopyDirty before Network/Persistence DrainDirty
```

Prefer existing independent changed-row cursors/views when they already allow several consumers to observe the same logical changes at different cadences without destructively draining one another. Otherwise provide a bounded multi-reader barrier view/epoch, give one owner responsibility for deriving all downstream facts from one read, or introduce another equally explicit bounded ownership scheme. The exact API is implementation-time, but **consumer ordering around one destructive dirty set is not the contract**. A cursor/epoch used for Spatial advances only after the corresponding spatial/publication operation has committed successfully; capacity/preflight failure may not acknowledge away unapplied changes. Lifecycle create/destroy/add/remove facts need an equally explicit incremental source when T0.5 requires them; do not infer every lifecycle transition by rescanning the whole world.

Structural authority remains barriered. Existing component-value writes may remain direct when the scheduler/owner contract makes them safe and they participate in dirty capture; do not create a second structural mutation path for Spatial as a performance shortcut.

Conceptual owner-barrier flow:

```
ECS committed spatial/lifecycle changes
    position changed
    entity created/destroyed
    spatial component added/removed
            |
            v
WorldSpatialIndex update once per changed entity
```

For each changed spatial entity:

```
same canonical home chunk
    membership topology does not change
    retain current spatial membership row/slot
    T0.6 may still update the public transform row

crossed chunk boundary
    preflight destination capacity
    remove source membership
    add destination membership
    expose one canonical migration fact to publication/composition

created spatial entity
    insert into canonical home chunk

removed/destroyed/nonspatial entity
    remove from prior home chunk
```

This is the core scaling rule:

> Pay the entity's coarse spatial membership cost once when its authoritative location changes, rather than rediscovering candidate membership independently for every observer.

If 100,000 entities exist and only 8,000 positions changed, spatial examines the changed set rather than scanning 100,000. If only 300 of those cross a 16 m boundary, only those 300 require source/destination membership mutation; the remaining position changes do not churn coarse membership. If one of those 8,000 entities was written ten times before commit, it still contributes one changed-entity entry and one final-position read.
Membership update must be transactional across source/destination capacity. A failed destination preflight may not remove the source first.
Spatial membership is derived, not authority. Restore/generation replacement may rebuild the complete index from staged committed ECS truth. A spatial-index slot never appears in persistence, publication identity, network protocol, or renderer state.
One dynamic entity has one canonical home chunk based on normalized position. Visual bounds may overlap neighboring chunks, but authoritative coarse membership does not duplicate merely because a model is large. Special large-world objects earn a separate coverage/index mechanism when needed.

### Committed read boundary

The index may expose a concrete owner-safe/borrowed committed read view for game systems that need coarse membership or chunk lookup. The exact API is implementation-time, but it must not require:

```
Spatial DTO allocation
-> compatibility queue
-> consumer DTO allocation
```

Consumers must obey explicit lifetime/ownership rules and may not retain spans/references beyond the committed view's valid generation/barrier lifetime.
**T0.5 gate:** millions of deterministic same-chunk moves and boundary crossings preserve exact membership, zero duplicate/lost entities, explicit capacity failure, zero warmed owner-thread allocation, successful rebuild after generation replacement, no proportional full-ECS scan on ordinary ticks, repeated same-entity writes within one barrier coalesce to one spatial maintenance observation, no correctness dependency on destructive dirty-drain ordering between consumers, and a measured direct committed read path.

## T0.6 — packed chunk publication and bounded dirty history

T0.6 builds the game-owned public semantic projection that later replication profiles may consume. It is **not** part of `Spatial.Contracts` and it is not Network-owned.
`ChunkPublicationStore` is separate mutable storage keyed by `SpatialChunkKey`. Each resident publication chunk owns **one current packed public view** of its dynamic public state plus bounded revision/history metadata.
The initial row schema should be the smallest public locomotion/world vertical, for example numeric identity/lifetime, prefab/presentation identity, chunk-local transform, and minimal public simulation flags. It is game-owned projection data, not a serialization of arbitrary ECS components.
Store it SoA-first where fields are commonly traversed independently:

```
EntityIndex[]
EntityGeneration[]
PrefabId[]
LocalXMillimeters[]
LocalZMillimeters[]
YMillimeters[]
Orientation[]
PublicStateFlags[]
LastChangedRevision[]
...
```

Metadata for a publication chunk/page may remain a small AoS row. Large columns are contiguous. Do not put boxed components, object references, delegates, strings, Godot types, Network packet types, or seam/framework objects in the page.

### Publication means commit, not mailbox copying

After one committed authority step, world composition uses the already-known changed spatial/lifecycle/public-component set to update the existing packed pages. `Publish` means completing the staged updates and making a revision/root visible atomically; it does **not** mean creating a giant new snapshot or pushing a copy through `Spatial.Contracts` every tick.
Conceptually:

```
prepare/preflight changed pages
    -> write current packed values
    -> append bounded dirty facts/tombstones
    -> make complete page state visible
    -> advance visible PublicationRevision once if anything public changed
```

If no public row changed, the publication chunk revision does not advance.

### Direct committed reads

The hot bridge is allowed to query the concrete publication store directly after the owner commit. Preferred shapes are borrowed read-only spans/ranges or equivalent fixed/native views:

```
acquire committed publication view
    -> check current revision / history floor
    -> inspect bounded dirty identities/masks
    -> read current SoA columns directly
    -> encode/copy into the caller-owned destination
```

Avoid an intermediate pipeline such as:

```
PublicationChunk -> Spatial DTO -> Network DTO -> packet buffer
```

when the packet encoder can safely consume the committed packed values directly. Snapshot/delta APIs may still copy into caller-provided destinations when semantic isolation requires it; the rule is to avoid **unnecessary** per-recipient gathering/DTO construction.
Borrowed views have explicit lifetimes. A bridge may not retain a span/ref across a world-generation replacement, publication mutation boundary, or other lifetime invalidation.

### Prepared publication bodies: eager vs on-demand

T0.6 must leave room for the next optimization boundary discovered during N0/T0 design: **prepare recipient-independent semantic publication work when the chunk changes, so later consumers do not have to rediscover/gather/serialize the same chunk state on request**.

The important distinction is:

```
allowed / potentially useful
    Chunk revision N commits
        -> prepare/cache canonical semantic delta/body once
        -> many later consumers reuse that prepared body

not T0 ownership
    build final ConnectionId-specific packet
    patch per-connection sequence/reliability state
    encrypt for a connection
    enqueue/send socket datagram
```

Those final per-recipient operations remain Network work later.

Do not automatically rebuild a complete encoded snapshot for every small change. The current packed page is already the canonical complete state. Snapshot encoding may be lazy/cached by revision unless measurement proves incremental eager maintenance is cheaper. Dirty history should normally identify the identities/fields changed across a requested revision range, while the current page supplies the latest coalesced values.

Likewise, do not eagerly serialize dirty chunks nobody can consume merely because eager preparation sounds fast. Candidate policy is demand-aware preparation:

```
active/due local consumer or later subscriber cohort
    -> eager prepared body is eligible

no consumer / no due cadence
    -> commit page + history only; encode lazily if requested
```

T0.6 should benchmark at least synchronous eager preparation versus on-demand encoding over representative dirty/publication profiles. **Synchronous owner-bound preparation is the baseline implementation.** Async preparation is only a later experiment if encoding is material and a worker can consume immutable/version-stable captured state without copying enough data to erase the benefit.

A prepared body is a game/publication semantic artifact, not a Network packet format. Avoid baking connection framing, transport MTU, crypto, or reliability metadata into `ChunkPublicationStore` merely to increase cache hits.

### Publication revision

This yields a stable semantic query:

```
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

The journal records **which public identities/fields became dirty**, while the SoA page remains the source of latest coalescible values. A client that skips 20 position writes normally receives the current position once, not 20 intermediate positions.
Removal/leave facts need enough identity/lifetime information to survive row-slot reuse. Never rely on a dirty journal entry that points only at a slot which may have been recycled for another entity.
The exact journal encoding is a measured T0.6 decision. Candidate designs include:

- fixed `(revision, identity, field-mask, current-slot/op)` entries with query-time dedup scratch;
- per-revision dirty bitsets plus a separate bounded tombstone identity ring;
- another fixed representation that proves lower CPU/bytes without pinning row reuse to the slowest reader.

Reject any design that needs a per-recipient dirty structure inside the publication chunk. Recipients own cursors later; the publication store owns current state/history once.
Non-coalescible gameplay facts remain Event semantics. Do not put attacks, chat, sounds-that-must-occur, inventory transactions, or other ordered facts into a latest-state dirty ring merely because the ring exists.

### Cross-chunk migration ordering

A dynamic entity migration updates two coarse/publication addresses but preserves one canonical global identity/generation. Source removal and destination insertion/publication must have an ordering/floor contract strong enough that a delayed source fact cannot erase or resurrect newer destination state.

### Snapshot atomicity

A snapshot contains a complete current public scope for that publication chunk at one revision. Apply stages and validates before replacing the client's corresponding semantic chunk page. A delayed pre-snapshot delta cannot resurrect state older than the committed snapshot floor. T0 proves this locally before N1 has a packet format.
**T0.6 gate:** deterministic snapshot/delta oracle, history wrap -> snapshot fallback, slot-reuse/tombstone tests, cross-chunk migration ordering tests, warmed zero-allocation publication/query, direct-read encoding/copy benchmark, eager-vs-on-demand prepared-publication evidence including wasted-work/cache-hit accounting, and bounded memory independent of observer lifetime.

## T0.7 — chunk interest rings and local streaming oracle

AOI becomes a query over canonical 16 m world-spatial chunk AABBs. The **world spatial system answers which chunks overlap an interest shape; it does not own observer/subscription state.**
A recipient/observer has an authoritative spatial anchor. The query returns a small canonical set of `SpatialChunkKey`s plus a relative cadence tier. It does not ask each chunk which members are within radius X.

### Ring 0

A point has exactly one canonical home chunk under half-open coordinate ownership, but an immediate interest footprint may intersect neighboring chunk AABBs:

```
middle of a 16 m chunk      usually 1 R0 chunk
near one boundary           commonly 2
near a corner               up to 4
```

The whole selected chunk is R0. There is no R0 entity-by-entity radius filter.
A movement such as `12.035 m -> 12.036 m` changes neither home chunk nor coarse subscription topology. Even `12.0 m -> 15.9 m` stays in one home chunk. Only crossing the canonical `16.000 m` boundary changes home chunk.

### Near/Far rings

For outer rings, compare the observer footprint/radius to chunk AABBs. A chunk is assigned the nearest applicable tier once:

```
R0 / Immediate    query every eligible immediate cadence
Near              same publication semantics, slower query cadence
Far               same publication semantics, slower query cadence
Outside           no subscription/query
```

Changing Near -> Far changes only observer/subscription cadence/priority. It does **not** retier every entity in that chunk, rewrite member state, or mutate `WorldSpatialIndex`. It also does not create a second Near/Far copy of the chunk's semantic state: one `PublicationChunk` remains the source, while tier/cadence/phase belong to the consumer/subscription profile.
The client receives public chunk state and performs precise presentation culling, LOD, occlusion, and camera selection. Sending some public entities that are beyond an exact visual circle is an intentional coherence-for-CPU trade.
This rule is only for public state. Hidden/invisible/GM/private inventory/quest secrets and owner-private state must use separate authorized streams and never be placed in a broadly shared public chunk snapshot.

### AOI/subscription state is private to the consumer

For the local oracle, a subscription record may own only:

```
SpatialChunkKey
last applied PublicationRevision
cadence / phase
client semantic resident slot
```

A future Network N1 subscription may have an equivalent shape, but that record belongs to Game/Replication/session state. **A WorldChunk does not know who is watching it.** A later replication profile may maintain reverse per-publication-chunk subscriber buckets by tier/phase for fanout efficiency, but those are replication-owned indexes over subscribers, not world/spatial membership and not duplicated semantic chunk pages.
New subscription -> snapshot. Existing subscription -> delta query. History miss -> snapshot. Leaving scope -> retire the consumer's semantic/presentation chunk without mutating server world state.
This is the chunk equivalent of OfflineDirect: it proves semantics without packet noise and without forcing Network into the world implementation.

### Domain-specific spatial refinements remain private

The canonical world partition being 16 m does not force every subsystem to use 16 m internal regions forever. A renderer may later batch four world chunks into a 32 m render region; navigation may use smaller tiles; physics may use finer broadphase cells. Those are private domain accelerators related by geometry/key mapping, not reasons to mutate the canonical world partition.
**T0.7 gate:** boundary/corner movement changes only the expected consumer chunk subscriptions, same-home-chunk movement creates no coarse membership churn, tier changes do not rewrite member state, world chunks contain no watcher lists, and local snapshot/delta application converges exactly.

## T0.8 — client resident cache and Godot world renderer

The client owns a bounded retained **semantic world projection** separate from both server storage and Godot render resources.
Suggested semantic resident state machine:

```
Absent
Requested/Preparing
Staged
Resident
Retiring
```

Local/offline T0 may prepare terrain from `WorldPack` and public dynamic data from the local publication oracle; N1 later supplies equivalent semantic snapshot/delta data remotely.
A `ClientSemanticChunk` stores typed/packed semantic data. It does not expose server ECS entities, mutable server spatial/publication memory, or Godot nodes/RIDs.
Rendering owns a separate private representation:

```
ClientSemanticChunk keyed by SpatialChunkKey
        |
        v
RenderChunk / render region
    mesh RIDs
    MultiMeshes / combined meshes
    visibility / LOD / occlusion state
    origin-relative transforms
```

Semantic residency and render visibility are different facts. A semantic chunk may be resident while its render region is not visible this frame. Render regions may later aggregate/subdivide canonical world chunks when measurement earns it.

### Godot terrain

Use a small number of renderer objects per resident render region/chunk, preferably direct `RenderingServer` resources where that materially reduces scene-tree overhead. Do not instantiate one node per terrain cell.

### Godot statics

Group frozen statics by compatible render key. Prefer:

- combined/baked mesh for immutable heterogeneous structure/clutter where rebuild frequency is negligible;
- `MultiMesh` for repeated identical meshes/material-compatible variants;
- individual dynamic render objects only for genuinely dynamic/interactively animated state that cannot be represented in a batch.

The renderer owns a mapping from render instance/pick result back to logical world identity when interaction requires it. A render-instance index/RID never becomes persistent or authoritative identity.

### Floating render origin

The client may maintain an origin chunk and render everything relative to it so Godot floats stay numerically small. Rebase updates presentation transforms/RIDs; it never changes authoritative `WorldPosition`, `SpatialChunkKey`, publication revision, persistent identity, or network meaning.

### Streaming budget

Mesh/static preparation may run on workers over immutable prepared semantic data. Godot resource publication occurs on the permitted engine thread/boundary. Work is budgeted so crossing a spatial boundary cannot synchronously build an unbounded number of meshes in one frame.
**T0.8 gate:** bounded walk through a multi-chunk world with repeated enter/leave, separate semantic/render residency proven, no resource leaks, no node explosion, deterministic client semantic hashes, and recorded mesh/build/upload/frame-time tails.

## T0.9 — persistence and mutable-world seam

The immutable base `WorldPack` is content, not a gigantic save snapshot. Durable
save data references its world-pack/content fingerprint.
Dynamic entities remain in existing ECS persistence. After restore, the chunk
membership/publication index is rebuilt from staged ECS truth before the candidate
`GameGenerationState` publishes.
Runtime streaming state is never persisted:

```

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

```

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

```

terrain_flat

terrain_rough

chunk_boundary_walk

chunk_corner_walk

sparse_world_movement

packed_dense_town

static_heavy_property

multi_boundary

movement_multiwrite_coalesce

publication_idle

publication_dirty_2pct

publication_heavy

publication_prepared_vs_on_demand

publication_unwatched_dirty

publication_shared_hotspot

history_wrap_snapshot

client_stream_walk

Godot_static_heavy

```

Measure phases independently:

- ECS/barrier changed-set capture + dedupe;

- coordinate/membership update;

- dynamic page publication;

- optional prepared semantic-body build/cache;

- dirty-journal append/query/coalesce;

- snapshot copy/apply;

- world-pack read/validation;

- terrain mesh generation;

- static batch preparation;

- Godot resource publication;

- complete local streaming frame.

Permanent invariants:

- multiple writes to one spatial component inside one committed step collapse to one downstream changed-entity observation of the final value;

- no derived consumer depends on racing another consumer to a destructive dirty drain;

- unchanged chunks cost no semantic publication revision/work beyond bounded

  scheduler/query checks;

- same-chunk entity movement does not mutate recipient topology;

- chunk crossing performs bounded source/destination work;

- no warmed allocation in protected membership/publication/query paths;

- no per-cell or per-static scene-tree node scaling;

- history and caches remain fixed-capacity;

- the fixed 16 m / 0.5 m profile is validated by whole-pipeline timing/capacity evidence, not one counter;

- rejected experiments remain documented as negative evidence.

## Data-layout requirements

T0 should stay close to the metal without becoming unsafe-code theater.
Prefer:

- flat numeric structs;
- one canonical spatial identity rather than duplicated `TerrainPosition` / `NetworkPosition` / `RenderPosition` universes when the semantics are actually identical;
- game-specific compact `WorldPosition` for hot storage;
- SoA for large hot publication columns;
- CSR/offset+count for immutable variable-size static sections;
- fixed free lists and rings;
- canonical sorted directories;
- chunk-local compact coordinates;
- caller-provided `Span<T>` for bounded query output;
- barrier-scoped dense changed-entity spans/views rather than per-write event objects;
- borrowed `ReadOnlySpan<T>`/equivalent committed views where ownership permits direct reads;
- immutable prepared data across worker/owner boundaries;
- explicit `ReservedBytes` and high-water diagnostics for major stores.

Avoid:

- object-per-cell or object-per-static representations;
- nested managed collections proportional to theoretical world capacity;
- string/name lookups in hot world code;
- `IEnumerable<T>`/iterator allocation in protected hot spatial/publication paths;
- virtual/interface dispatch inside per-cell/per-row loops unless measurement proves it irrelevant;
- a generic `ISpatialProvider` hot abstraction when a concrete bridge can safely use a concrete optimized read API;
- compatibility DTO chains that merely copy already-canonical packed values;
- storing duplicate global coordinates when the chunk key already supplies the high bits;
- serializing raw in-memory structs as a wire/disk format;
- runtime format fields whose only consumer is Godot.

A single aligned unmanaged slab may eventually be the best storage for hot resident publication pages, but only introduce custom native ownership when the layout/benchmark justifies it. Contiguous managed numeric arrays are preferable to a bespoke allocator that makes correctness worse without measurable benefit.
The independent Spatial contracts package should remain tiny enough that it cannot become a throughput bottleneck by construction: it owns values/math, not runtime data movement.

## Capacity model

T0 names every proportional bound explicitly. At minimum:

```
maximum resident terrain chunks
maximum resident spatial-index chunks
maximum resident publication chunks
maximum resident client semantic chunks
maximum resident render regions/chunks
maximum terrain cells/chunk                 fixed 1,024 for game profile
maximum height vertices/chunk               fixed 1,089 for game profile
maximum frozen statics/terrain chunk
maximum Multi fragments/terrain chunk
maximum dynamic home members/spatial chunk
maximum dynamic public rows/publication chunk
maximum dirty-history entries/publication chunk
maximum prepared chunk loads
maximum mesh vertices/indices/render region
maximum render batches/render region
maximum static query output
maximum overlap-query chunk output
maximum world-overlay bytes/chunk           when implemented
```

Do not reserve worst-case dynamic/static arrays for every theoretical chunk in an unloaded world. Capacity distinguishes **world addressability** from **resident working set**. Large immutable pack sections may live on disk/mapped storage while bounded resident stores own hot mutable/publication state.
If one town chunk exceeds an admitted dynamic/static/publication capacity, report the exact high-water and choose a larger bounded capacity or an explicitly designed bounded overflow-page mechanism. Do not silently fall back to an unbounded list. Reopening the fixed 16 m world profile is a last resort requiring whole-pipeline evidence, not the first response to a local capacity miss.

## Concurrency and ownership

Authority-facing world mutation is owner-thread/barrier-bound. The spatial seam does not introduce another asynchronous scheduler/mailbox stage. A scheduler may still name an explicit `SpatialIndex`/post-movement stage or fire one barrier-complete/readiness callback; that is control-flow ordering, not per-movement data transport.

The sanctioned-path rule from the earlier V1 rewrite applies here: a hot path may not bypass an established mutation/commit boundary merely because direct access is faster. If a sanctioned path is too expensive, optimize the path rather than creating a second authoritative writer.

The intended hot order is:

```
ECS systems execute
    -> direct component writes are finalized under scheduler ownership; structural work commits at the authority barrier
    -> component-scoped changed identities are deduplicated/merged
    -> expose one committed barrier change view / equivalent owned change set
    -> consume changed position/lifecycle/public facts once
    -> preflight spatial/publication destination capacity
    -> update WorldSpatialIndex
    -> update/stage ChunkPublicationStore current rows + dirty facts
    -> commit publication revisions / committed read root
    -> gameplay / replication bridge / local client may borrow committed read views
```

This ordering is a semantic requirement, not an artificial wait. A consumer cannot safely read half-mutated authoritative state anyway.
`Spatial.Contracts` performs no publish/consume scheduling. There is no:

```
ECS -> copy to Spatial queue -> wait -> copy to Network
```

The concrete world stores are updated once, then consumers read the committed memory according to explicit ownership/lifetime rules.
Immutable/base terrain and frozen statics are even simpler: once their prepared resident lifetime is valid, readers do not need a per-tick dynamic publication barrier. Rare durable overlay mutations publish a new prepared/revisioned state only when they change.
File read, decompression if later added, validation, and mesh generation may occur on workers after ownership of immutable buffers is clear. A worker never mutates live owner-bound spatial/publication state.
Prepared ownership is single-use. If publication fails after trusted mutation begins unexpectedly, fault the owning world/runtime path under the same principle used elsewhere in UltraSim rather than pretending a partial world update can be rolled back.
Any borrowed span/ref/read view has an explicit validity scope. It cannot survive generation replacement, store compaction/reallocation, or a mutation boundary that invalidates the underlying storage. The same lifetime discipline applies to ECS changed-set views: a consumer may read the finalized barrier set during its declared epoch, but may not retain it after the owning world advances/recycles the capture storage.

## Interaction with existing Network N0

Do **not** delete or rewrite `GameInterestService` while T0 is under construction. N0 remains accepted executable evidence and the current remote client path.
T0 builds the world/spatial/publication semantics needed to evaluate an additional N1 profile. During T0:

- Network N0 can continue using current recipient/entity relations for existing tests;
- `Kernels/Network` remains a standalone reusable mechanism library with no Game/World/ECS/Spatial/Godot runtime dependency;
- local world streaming consumes concrete world/client semantics without UDP/session dependency;
- `Spatial.Contracts` receives no Network/session dependency;
- `ChunkPublicationStore` receives no Network/session dependency;
- Game/Replication may later depend on both Network and concrete world/publication code and use direct read APIs;
- benchmark evidence compares T0 spatial/publication maintenance to N0 relation maintenance only as attribution, not by mixing unfinished implementations.

The permanent replication profile model is:

```
EntityRelation profile
    accepted N0 maintained recipient/entity interest
    exact/selective relation semantics
    existing WorldState/baseline machinery
    retained as a production/reference capability

ChunkPublication profile
    future N1 recipient -> SpatialChunkKey subscriptions
    snapshot first
    bounded revision/dirty-history queries thereafter
    cadence changes query frequency
    client performs fine public visibility
```

They may coexist in one game/server. Owner/private state, Events, targeted/selective visibility, and broadly public spatial state are allowed to use different paths.

The longer-term architectural direction, to be tested only after a second real producer exists, is that **interest topology and semantic publication are replication-profile responsibilities while the Network kernel is a generic bounded delivery consumer**. In that end-state `EntityRelation` is not the ontology of Network; it is one supplier beside `ChunkPublication`. Both eventually reduce to something like shared/owned payload + recipient set + delivery semantics, after which Network owns connection/session validation, fanout, sequencing/reliability, framing, crypto, pacing/congestion, and transport.

Do **not** refactor N0 into that end-state during T0. The reason to wait is deliberate: once EntityRelation and ChunkPublication are both concrete, extract only the common output seam they actually prove. If the seam measurably slows EntityRelation, it is the wrong seam.

Profile selection should eventually be a positive composition-time slot such as `PublicWorldStateProfile = EntityRelation | ChunkPublication`, resolved once at startup, not a Network-internal `UseSpatial` branch or a pair of contradictory enable/disable flags. This does not replace separate owner/private/Event/Bulk paths.

Do not build a generic strategy/plugin framework just because two profiles exist. Prefer concrete implementations with a tiny cold selection/composition seam only when N1 actually needs it.

## Network N1 handoff contract

T0 is ready to hand back to Network only when the following are accepted APIs or executable contracts in the game/world/client layers:

```
SpatialChunkKey + explicit Spatial geometry/profile
normalized 1 mm WorldPosition
EnumerateInterestChunks(anchor/shape, destination) -> bounded keys + cadence tier
committed direct publication read view with explicit lifetime
CopyChunkSnapshot(key, destination) -> complete revisioned public scope
QueryChunkDelta(key, cursor, destination) -> NoChanges | Delta | SnapshotRequired
CurrentRevision / HistoryFloor
public identity/lifetime semantics across chunk migration
client atomic snapshot/delta apply
separate public vs owner/private/event state
bounded spatial/publication/client capacities
```

N1 then adds and measures a concrete **ChunkPublication** replication profile beside the accepted **EntityRelation** profile.
The likely hot recipient state is approximately:

```
SpatialChunkKey
LastAppliedRevision
Cadence
Phase
```

A new chunk subscription gets a snapshot. Existing subscriptions compare/query dirty history at their cadence. A history miss gets another snapshot. Near/Far changes query frequency/priority. The client handles fine visibility.
A useful historical idea to revisit without copying its old implementation is the V1-style **ring-as-routing-scope** trick: a recipient can associate one physical `SpatialChunkKey` with one relative tier/phase, while publication remains one semantic chunk page. A replication-owned reverse index may then fan a changed chunk to Immediate/Near/Far subscriber buckets without rebuilding recipient/entity relations or duplicating the chunk's semantic state. Prefer canonical structured keys/fields over opaque hashed identity when the underlying values must be inspected/debugged. A deterministic phase candidate is `Hash(SpatialChunkKey, ParticipantId, Cadence) % Cadence`, but N1 should freeze it only after benchmark/correctness evidence.
The game replication bridge may directly borrow the concrete committed `ChunkPublicationStore` view and encode into Network-owned packet buffers. If T0.6 proves prepared recipient-independent semantic bodies, N1 should be able to reuse those bodies instead of rebuilding chunk semantics on request. The goal is **build semantic chunk state once, fan it out many times**; a large publication may still become multiple MTU-sized datagrams, so "one packet" is not the semantic contract.

Packet framing, connection-specific sequence/reliability metadata, encryption, pacing/congestion, and transport remain per connection in `Kernels/Network`; semantic world gathering/public projection is game-owned and shared. The target Network hot path is therefore closer to "consume this prepared/shared body for this recipient set with these delivery semantics" than "discover which entities this connection should replicate."
No mandatory dependency is introduced from `Kernels/Network` to Spatial. A future optional reusable `Network.Spatial` adapter may depend on both only if a real common adapter shape is proven.
Entity migration between chunks must preserve global canonical identity/generation so delayed old-chunk removal/update cannot erase or resurrect newer state in the new chunk. N1 reuses the baseline/floor lessons already proved by N0 rather than inventing weaker semantics.
Private/owner state and retained Events remain separate streams.

If recipient-specific finalization/encryption/socket flush becomes the next measured limiter after shared publication, a later N1+ experiment may introduce a **bounded dedicated send/finalization path**: one socket owner or a small fixed worker set, immutable work handoff, fixed batch/ring capacity, explicit backpressure, and no Task-per-packet design. Report authority critical-path time, worker CPU, queue depth, and end-to-end publication latency separately so pipeline overlap is not mistaken for eliminated work. Do not build this merely because async exists.

The same flattened producer/delivery boundary also intentionally leaves room for a much later server-to-server publication or authority-transfer producer without teaching the Network kernel about server territories or world chunks. That is future architecture, not N1 scope.

### N1 performance hypothesis to test, not assume

Do not treat ChunkPublication as only a sparse-AOI optimization. Once semantic state is built per changed chunk and shared, dense and sparse may perform much more similarly than the EntityRelation topology suggests:

```
dense
    fewer/hotter publication chunks
    many subscribers consuming the same prepared semantic bodies
    high fanout and frequent R0 cadence

sparse
    more independently changing publication chunks
    fewer subscribers per chunk
    more Near/Far cadence suppression
```

The differentiator may therefore move from "number of recipient/entity relations" toward **number of changed publication chunks + due subscriber deliveries + unavoidable per-recipient finalization/send work**. Dense can even amortize semantic build better than sparse when many recipients share the same hot chunks.

N1 should measure, at minimum, the existing Dense-400 checkpoint plus candidate 500/600 dense points, and sparse 1024/2048/4096/5120-style points where bounded runtime permits. These are benchmark coordinates, not accepted capacity claims. Include cadence comparisons that preserve explicit tick meanings (for example the normal multi-tier cadence and an experimental `1/1/2`-tick Immediate/Near/Far shape) so the dense/sparse gap can be attributed to publication/fanout rather than hidden cadence differences.

Report separately:

- spatial/change maintenance;
- publication finalize + prepared semantic build/cache;
- subscription/cadence selection;
- recipient-specific framing/reliability work;
- encryption/finalization;
- transport batch/flush/send;
- total authority critical-path time;
- any worker CPU/queue latency if async work is later introduced.

A large total speedup is welcome, but the architectural success signature is more specific: spatial/relation-topology cost should collapse, semantic build should scale primarily with changed chunks rather than recipient/entity pairs, and the remaining slope should move toward genuinely unavoidable recipient/byte/send work.

If ChunkPublication materially improves sparse scaling while preserving semantics, it may become the preferred/default profile for broadly public spatial state. That result still does not require deleting the EntityRelation capability.

## T0 definition of done

T0 is accepted only when:

- the independent Spatial contracts/value boundary has no Network/ECS/Game implementation/Godot dependency and contains no runtime storage/publication manager;
- `Kernels/Network` remains fully usable with no Spatial dependency;
- world/chunk/local coordinates are canonical for negative and large positions;
- the 16 m chunk / 0.5 m terrain cell / 0.1 m placement snap / 1 mm dynamic position contracts are explicit and independently tested;
- terrain height encoding is documented by evidence;
- immutable world-pack validation is transactional and bounded;
- terrain and frozen statics stream without ECS/entity/node-per-placement explosion;
- logical Multi/property identity survives chunk splitting;
- terrain, spatial membership, publication, client semantic, AOI subscription, and render storage do not share mutable chunk objects or local slot identity;
- dynamic ECS entities rebuild into one exact canonical home-chunk spatial index;
- ordinary spatial maintenance consumes a deduplicated committed ECS change view/equivalent rather than proportionally rescanning every spatial entity;
- repeated same-entity movement writes within one barrier produce one downstream spatial maintenance observation of the final authoritative value;
- no spatial/publication correctness depends on one consumer copying dirty state before another consumer destructively drains it;
- same-home-chunk movement performs no coarse membership topology rebuild;
- source/destination chunk crossing is transactional and bounded;
- each publication chunk has one current packed public view and bounded history;
- publication commit means revision/root visibility, not a per-tick compatibility-copy pipeline;
- committed publication can be read/encoded through a measured zero/near-zero-copy concrete path with explicit lifetime rules;
- prepared recipient-independent publication, if retained, is evidenced against on-demand encoding and contains no connection-specific packet/crypto/reliability state;
- snapshot/delta/history-miss semantics converge under local loss/reorder-style tests;
- chunk interest tiers change consumer subscription cadence, not member semantic type;
- world spatial chunks contain no watcher/recipient lists;
- client precise visibility does not feed back into server public membership;
- client semantic residency is separate from render visibility/resource residency;
- private/secret data is structurally excluded from broad public chunk pages;
- terrain mesh seams are deterministic and static rendering is batch-oriented;
- resident terrain/spatial/publication/client/render memory and every hot proportional structure have named capacities;
- warmed membership/publication/query paths meet zero-allocation contracts;
- the woods/cabin vertical works through save/rebuild boundaries;
- current Network N0 remains green while T0 is introduced;
- T0 leaves N1 as an additional measured replication profile rather than silently deleting N0.

## Deliberate implementation-time decisions

These require measurements or real content rather than premature commitment:

- terrain height quantum/width and total supported vertical range;
- exact physical project/package name for the tiny Spatial contracts boundary if repository conventions constrain it;
- whether any shared spatial query interface is ever justified beyond values/math (do not add one until multiple concrete consumers earn it);
- managed arrays versus aligned unmanaged slabs for spatial/publication hot storage;
- exact coarse membership row layout and entity->home reverse mapping;
- exact ECS changed-row cursor/barrier-view/lifecycle-observation integration; reuse existing independent cursor machinery where it already satisfies the semantics rather than creating a duplicate tracker;
- dirty-history encoding and dedup strategy;
- whether prepared semantic publication bodies are eager, lazy, cohort-cached, or omitted based on evidence;
- whether snapshot encoding is cached per revision or built from the current packed page on demand;
- whether async publication preparation ever beats synchronous cache-local preparation after immutable/versioned ownership costs are included;
- dynamic rows/chunk and whether a bounded overflow page is ever required;
- exact committed read-view lifetime/API shape;
- whether future publication aggregation groups multiple 16 m world chunks after N1 measurement;
- exact Godot `RenderingServer` vs scene-node ownership split;
- render-region granularity, combined static mesh granularity, and `MultiMesh` grouping;
- runtime texture atlas policy and atlas dimensions;
- world-pack compression and I/O backend;
- mutable terrain/static overlay format;
- interior/portal/occlusion policy;
- later physics/nav/AI spatial subdivision or aggregation;
- whether server residency eventually unloads chunks containing dormant ECS entities or whether entity simulation partitioning becomes a later milestone;
- whether optional reusable adapters such as `Network.Spatial` earn a stable binary/API package after the game bridge proves their common semantics;
- whether, after ChunkPublication exists, EntityRelation should be extracted from Network as a sibling replication producer feeding a flattened generic delivery consumer;
- whether recipient finalization/encryption/send earns a dedicated bounded worker/socket-owner pipeline after publication/fanout profiling;
- whether a future inter-server publication/authority-transfer producer is ever required.

Already frozen unless concrete failure evidence reopens them:

```
game WorldChunk edge       16.0 m
game terrain cell           0.5 m
game placement snap         0.1 m policy
game dynamic quantum        1 mm
Spatial contracts are storage-free/dependency-neutral
Network kernel does not require Spatial
systems share spatial identity/geometry, not mutable chunk storage
```

These decisions may change mechanism. They may not weaken the frozen contracts above without explicit evidence and documentation.

## Stop condition and next milestone

Do not continue polishing terrain after T0 merely because more world features are possible. Once the fixed world format, concrete spatial index, packed publication, direct committed read path, streaming client/renderer separation, bounded history, persistence seam, and woods/cabin vertical are accepted, **stop T0 and return to Network N1**.
N1's first job is not new transport research. It is to add the `ChunkPublication` replication profile above the unchanged Network kernel and test the original chunk-native AOI hypothesis against the frozen N0 sparse baseline:

> Maintain recipient-to-`SpatialChunkKey` cursors, snapshot on first interest, query bounded dirty history thereafter, let cadence control query frequency, reuse recipient-independent prepared semantic publication when T0.6 evidence supports it, let the game replication bridge feed the generic Network delivery path without teaching Network chunk/AOI semantics, and let the client perform fine public visibility.

The measurement question is not “can N1 replace N0 by design?” It is:

> **Does paying coarse spatial membership/public projection once per changed entity/chunk materially reduce sparse recipient work while preserving accepted semantics and dense behavior?**

If it does not, reject or limit the ChunkPublication profile with the same evidence discipline used throughout N0. If it does, make it the preferred path where it wins while retaining EntityRelation for exact/selective/targeted cases or as a valid alternative capability.