# Terrain / World T0: spatial-seam, chunk-native world implementation plan

Status: active forward plan. T0.0 through T0.7 are accepted/frozen as of
2026-08-21 after T0.7 consumer hardening. T0.8 implementation is complete and
stopped for checkpoint review; it is not yet accepted/frozen and T0.9 has not
started.
Revised from the
accepted Network N0 baseline, later spatial-seam decisions, and V1/transitional
spatial-publication archaeology.
Network N0 is accepted and frozen as the current remote-authority reference. T0 is the next major implementation milestone. Network N1 is deliberately deferred until T0 has proved the world/spatial/publication contracts that N1 can consume.
T0 is not merely a terrain renderer. It establishes the spatial substrate for the persistent world: deterministic integer coordinates, a canonical coarse spatial partition, packed terrain and statics, incremental dynamic membership, chunk-associated public publication state, bounded dirty history, local streaming, Godot rendering, and the seams required by persistence and later replication profiles.
The motivating observation is measured rather than speculative. Network V3 Dense-400 is efficient once recipient/entity relationships are established, while sparse movement is limited by changing and retiering those relationships. The rejected N0.11 exact-AOI experiment removed 31.55% of 4K maintained relations and 40.36% of WorldState records, yet doubled interest-update p95 because precise recipient/entity membership reconciliation cost more than the coherent work it removed.
T0 therefore introduces a maintained coarse spatial index so entity spatial work is paid once when authoritative spatial state changes instead of being rediscovered independently by every observer. The game profile uses 16 m spatial chunks, but terrain, publication, AOI subscriptions, client semantic residency, rendering, physics, navigation, and other domains keep private representations and private local slot identities. They may share only canonical spatial identity/geometry where useful.
The independent Spatial seam is a **contract/read vocabulary, not a data-moving intermediary**. It must not become an event bus, mailbox, publication thread, generic chunk framework, or mandatory dependency of otherwise independent kernels. Concrete bridges may query optimized game/world spatial and publication stores directly, including borrowed spans/read views, when that is the correct hot path.
Recent archaeology of the V1-to-V2 transition reinforces the intended hot-path shape: movement/component writes may occur several times inside one authority step, but component-scoped dirty capture deduplicates the entity, a barrier/stage completion establishes that the final authoritative value is readable, and Spatial consumes that entity once. The old event-like mechanism is therefore retained only as a **completion/readiness semantic**, not as a per-movement message bus. T0.1 must preserve that property without relying on destructive-drain ordering between consumers.
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

THE DENSE BASE TERRAIN SURFACE IS A 2D HEIGHTFIELD.

BASE TERRAIN IS NOT THE PERMANENT MAXIMUM TOPOLOGY OF WORLD TERRAIN.

WORLD TERRAIN IS NOT PERMANENTLY LIMITED TO ONE SURFACE PER X/Z.

TERRAIN CHUNKS ARE WORLD-DATA PARTITIONS, NOT ECS STORAGE CHUNKS.

FUTURE AUTHORED CLIFF, LEDGE, OVERHANG, CAVE, TUNNEL, OR UNDERGROUND
FLOOR/CEILING SURFACES MAY USE SPARSE ADDITIONAL WORLD-TERRAIN GEOMETRY AND
SHARE A SPATIAL CHUNK KEY WITHOUT SHARING THE BASE HEIGHT PLANE.

ADDITIONAL TERRAIN SURFACES ARE WORLD DATA, NOT ECS ENTITIES BY DEFAULT.

SUPPORTING MULTIPLE TERRAIN SURFACES DOES NOT REQUIRE VOXEL TERRAIN.

MULTIS AND STATICS ARE NOT THE REQUIRED CATCH-ALL REPRESENTATION FOR
NON-HEIGHTFIELD GEOLOGY.

TERRAIN, PUBLICATION, AOI, CLIENT, RENDER, PHYSICS, AND NAV CHUNKS/REGIONS MAY DIFFER IN GRANULARITY LATER.

NO AUTHORITATIVE FLOAT POSITION IS REQUIRED.

NEGATIVE COORDINATES HAVE ONE CANONICAL FLOOR-DIVISION RULE.

GLOBAL X/Z RECONSTRUCTION IS CHECKED INT64; DISTANCE DELTAS AND SQUARED SUMS PROMOTE TO INT128 BEFORE SUBTRACTION.

FLOAT IMPORT REJECTS NONFINITE/OUT-OF-PROFILE VALUES AND QUANTIZES NEAREST MILLIMETER, MIDPOINT AWAY FROM ZERO.

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

LOGICAL MULTI STRUCTURE GROUPING IS SEPARATE FROM GODOT MULTIMESH GROUPING.

A MULTI IS LOGICAL STRUCTURE/PLACEMENT GROUPING; IT IS NOT PROPERTY OWNERSHIP.

WORLD REGIONS ARE DURABLE SPATIAL-POLICY METADATA, NOT WORLD STORAGE
PARTITIONS OR ECS ENTITIES BY DEFAULT.

PROPERTY, PROPERTY UNIT, WORLD REGION, MULTI, TERRAIN, AND EDIT-LEASE IDENTITIES
ARE DISTINCT EVEN WHEN THEIR GEOMETRY OVERLAPS.

FUTURE PROPERTY/ROOM REGIONS ARE TRUE 3D VOLUME SETS; LAND OWNERSHIP AND POLICY
ARE NOT DEFINED BY THE 16 M WORLD-CHUNK GRID.

A LOCATION MAY MATCH SEVERAL SEMANTIC REGIONS. CONCRETE POLICY DOMAINS RESOLVE
THE RELEVANT MEMBERS; THERE IS NO REQUIRED UNIVERSAL WINNING REGION.

TERRAIN EDITING AND MULTI/STATIC AUTHORING HAVE SEPARATE SEMANTIC AND
REVERSIBILITY DOMAINS.

TERRAIN EDITS ARE PRIVATE CANDIDATES UNTIL ONE COMPLETE SERVER-VALIDATED
TERRAIN COMMIT SUCCEEDS.

A TERRAIN COMMIT IS ALL-OR-NOTHING ACROSS ITS DIRTY REGION.

EDIT-REGION OWNERSHIP IS EXCLUSIVE; SPATIAL CHUNKS ARE LOCKING/STORAGE UNITS,
NOT AUTOMATIC PROPERTY-OWNERSHIP PARCELS.

SURROUNDING PUBLISHED WORLD DATA MAY BE READ-ONLY EDIT CONTEXT WITHOUT JOINING
THE EDIT CLAIM.

TERRAIN CONTENT REVISION IS SEPARATE FROM DYNAMIC PUBLICATION REVISION.

HARDENING PRESERVES LOGICAL MULTI GROUPING; FREEZING LOWERS WORLD CONTENT TO A
CHEAPER REPRESENTATION.

UNFREEZE IS NOT UNDO. HARDEN IS NOT FREEZE.

STRUCTURE-DESIGN VISIBILITY AND CONSTRUCTION REALIZATION ARE SEPARATE POLICIES.

TERRAIN MODIFICATION REMAINS PRIVATE/ATOMIC EVEN WHEN STRUCTURE CONSTRUCTION IS
PUBLIC/LIVE.

AN APPROVED STRUCTURE MAY COMPLETE IMMEDIATELY OR PROGRESS THROUGH AN
AUTHORITATIVE CONSTRUCTION PROJECT.

PLAYER, NPC, COMMUNITY, QUEST, AND ADMIN CONSTRUCTION CONVERGE ON THE SAME
VALIDATED WORLD-CONSTRUCTION SEMANTICS WHEN THEIR REQUIREMENTS MATCH.

AI/SETTLEMENT PLANNING MAY REQUEST CONSTRUCTION; IT MAY NOT BYPASS VALIDATION OR
WRITE FROZEN WORLD DATA DIRECTLY.

LOAD/PREPARE MAY BE ASYNCHRONOUS.

AUTHORITY/SPATIAL/PUBLICATION COMMIT IS OWNER-BOUND AND TRANSACTIONAL.

CANONICAL SPATIAL MAINTENANCE RUNS AFTER THE COMPLETE ECS GRAPH AND POST-GRAPH STRUCTURAL BARRIER, ONCE PER AUTHORITATIVE STEP.

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

DERIVED SPATIAL MEMBERSHIP CAPACITY IS GUARANTEED FROM THE ADMITTED ECS ENTITY CAPACITY; ROUTINE MOVEMENT DOES NOT FAIL AFTER POSITION COMMITS.

CURRENT PUBLIC STATE MAY COMMIT EVEN WHEN BOUNDED HISTORY INVALIDATES; HISTORY PRESSURE REQUIRES A SNAPSHOT AND NEVER BLOCKS AUTHORITY.

NO HOT-PATH OBJECT GRAPH IS CREATED PER CELL, STATIC, ENTITY, CHUNK SUBSCRIPTION, OR RECIPIENT.
```

These rules survive changes to mesh generation, file compression, renderer, network codec, future publication aggregation, world size, and eventual server partitioning. The fixed 16 m / 0.5 m / 0.1 m / 1 mm game profile may be reopened only by concrete evidence that it fails a required correctness, capacity, or whole-pipeline performance gate; it is no longer a routine T0.2 search parameter.

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
    dense base: 32 × 32 material cells + 33 × 33 height lattice
    optional future sparse structural surface geometry
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
A `Multi` remains the UO-like logical structure/placement grouping. It is
separate from property ownership, spatial-policy regions, spatial partitioning,
and Godot `MultiMesh` batching.

## Scope and deliberate non-goals

T0 includes the minimum world machinery needed to make the spatial partition real, prove a useful compatibility seam, and prove that chunk-associated public state can become a later replication/streaming unit. It does not attempt to finish every world system.
T0 includes:

- dependency-neutral Spatial value/geometry contracts and canonical math;
- the fixed game spatial profile: 16 m chunks, 0.5 m terrain cells, 0.1 m placement snap policy, 1 mm dynamic position quantum;
- deterministic world/chunk/local coordinate math with correct negative-coordinate behavior;
- a compact terrain height/material representation;
- an extensible world-data seam for later sparse structural surfaces without
  changing the dense base heightfield;
- a deterministic runtime `WorldPack` format and reader;
- UO-like frozen static placements and logical Multi structure composition,
  without conflating it with future property ownership;
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
- a production terrain/world editor or parallel multi-user editor UI;
- player housing/property ownership and private/public customization UI;
- a runtime `WorldRegionStore`, property/unit/lease model, or generalized region
  policy framework;
- staged construction gameplay, community construction events, or autonomous
  settlement planning;
- a UO-, Sims-, Valheim-, or Banished-specific authority model;
- a general multi-surface cliff/cave editor;
- procedural infinite terrain;
- voxel terrain or arbitrary volumetric caves/destruction (sparse authored
  non-heightfield cliff/cave surfaces remain permitted world data);
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
        WorldSpatialIndex (T0.1)
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

The implementation order follows actual dependencies; an early gate may not
require disposable scaffolding from a later checkpoint:

```
T0.0   Spatial contracts + coordinate/value machine
T0.1   authoritative WorldPosition + WorldSpatialIndex
T0.2   terrain profile validation + height encoding
T0.3   sectioned WorldPack container + terrain source
T0.4   authoritative base-terrain query + deterministic base mesh payload
T0.5   frozen statics + Multi structure compilation
T0.6   ChunkPublicationStore + bounded history
T0.7   interest/subscription oracle
T0.8   client residency + production Godot renderer/streaming
T0.9   persistence + durable mutable-overlay seam
T0.10  playable whole-pipeline acceptance
```

T0.4 proves a payload, not a renderer. T0.5 proves packed compilation, not live
streaming. T0.8 is the first production Godot/streaming gate, and T0.10 owns final
whole-pipeline validation of the fixed 16 m geometry.

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

The concrete game/world layer owns values that are not generic Spatial seam
vocabulary. T0.0 freezes only the coordinate/profile values it can prove:

```
WorldLocalPosition
WorldPosition / WorldLocation
TerrainCellCoord
```

`WorldPackFingerprint` arrives with the T0.3 container and publication epoch/
identity/revision values arrive with T0.6. They remain game/world values but are
not fabricated at T0.0 merely to reserve names.

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
The complete admitted X/Z domain is therefore:

```
SpatialChunkCoord.X/Z       int32
game local X/Z              uint16, canonical 0..15,999
reconstructed global X/Z    int64
game authoritative Y        int32 millimeters
distance delta/squared sum  Int128
```

Geometry edge/quantum values are positive `int32`. Reconstruction uses checked
`Int64` arithmetic. Distance code promotes each operand to `Int128` **before**
subtraction; subtraction of two otherwise legal `Int64` values may itself
overflow `Int64`.
World calculations use integer millimeters or explicitly defined terrain height units. Godot converts to float only at presentation boundaries and may rebase a render origin without changing authority.
The placement grid and terrain grid never constrain dynamic entity positions:

```
terrain cell boundary    500 mm
placement snap           100 mm
dynamic position           1 mm
```

An item authored at `4.2 m, 7.7 m` may later become dynamic at `4.237 m, 7.684 m`. A character can stand anywhere on the 1 mm lattice over a terrain cell.
If a floating source value is imported, reject NaN, either infinity, scaled
overflow, X/Z outside the representable world profile, and Y outside `int32`.
Conversion is round-to-nearest millimeter with midpoint **away from zero**
(`12.0357283 m -> 12.036 m`, `-0.0005 m -> -1 mm`). Never clamp or saturate
invalid authority input, and never let C# truncation toward zero define world
semantics accidentally.

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
- `Int128` promotion before distance subtraction and squared accumulation;
- overflow/rejection before mutation;
- NaN/infinity/scaled-overflow/out-of-profile float import rejection;
- positive and negative midpoint-away-from-zero float quantization;
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

**T0.0 acceptance checkpoint (2026-08-20): accepted and frozen.**
`Kernels/Spatial` now owns only the dependency-neutral values/math above;
`Game/World` owns the fixed profile and 16-byte `WorldPosition`. Fifteen permanent
tests pass in checked Debug and Release, including 100,000 deterministic wide
round trips, signed boundary/corner cases, `Int128` distance, float rejection,
and warmed 0 B allocation. The 31×500,000-operation Release observation records:

```
normalize global -> chunk/local       7.556 ns mean / 7.600 ns p95 / 0 B
reconstruct global position           3.421 ns mean / 3.443 ns p95 / 0 B
chunk key hash + order                5.765 ns mean / 5.803 ns p95 / 0 B
chunk bounds + distance + overlap    55.695 ns mean / 56.517 ns p95 / 0 B
terrain cell from local               2.869 ns mean / 2.896 ns p95 / 0 B
```

The immutable machine-readable evidence is
[`terrain-t0.0-spatial-contracts-2026-08-19.json`](../../_Benchmarks/Baselines/terrain-t0.0-spatial-contracts-2026-08-19.json).
No T0.1 index/component/lifecycle integration or later Terrain machinery is part
of this checkpoint.

## T0.1 — authoritative position and maintained dynamic membership

T0.1 immediately follows the independent value seam and introduces the concrete coarse spatial acceleration structure the game has been missing. It has no dependency on terrain packs, statics, publication, Network, or Godot. Dynamic actors/items remain authoritative ECS entities.
Do **not** implement one universal `WorldChunkStore` that owns terrain, publication, AOI, and rendering. Compose separate bounded stores keyed by the same `SpatialChunkKey` where appropriate:

```
GameWorldState
    TerrainChunkStore          immutable/prepared terrain + frozen statics
    WorldSpatialIndex         coarse dynamic home-chunk membership
    ChunkPublicationStore     T0.6 public projection/history
```

T0.1 itself focuses on `WorldSpatialIndex`. Publication mechanics are T0.6.
The game owns one persistable ECS component wrapper rather than making the generic
Spatial value an ECS type:

```csharp
WorldPosition
    // game/world value; no ECS interface

WorldPositionComponent : IComponent<WorldPositionComponent>
    WorldPosition Value
```

`WorldPositionComponent` is canonical authority for every dynamic entity that
physically occupies world space. Its presence is sufficient to opt into coarse
spatial membership; do not add a redundant spatial tag. Inventory-contained,
accounting, and other abstract entities need not carry it. Public visibility is
a separate later projection policy: Position does not imply inclusion in a broad
public chunk page.
The spatial index is bounded and data-oriented:

```
WorldSpatialIndex
    fixed chunk metadata slots
    bounded SpatialChunkKey -> spatial-index-slot lookup
    free-slot stack/ring
    one private home/membership record per admitted ECS entity slot
    entity-index -> generation-validated home record
    explicit ReservedBytes / high-water diagnostics
```

The reverse map is a private optimization of this game-owned store, never part of
`Spatial.Contracts`. A home record is relinked when its entity moves; it is not
allocated again:

```
HomeRecord[EntityCapacity]
    EntityGeneration
    SpatialChunkSlot
    Previous/NextMembershipLink (or equivalent)
    Flags
```

The chunk table admits at least the theoretical maximum number of occupied
dynamic chunks: no more than the number of spatial entities, therefore no more
than ECS entity capacity. Empty chunk slots return to the free pool. Generation
construction rejects inconsistent limits. If guaranteed insertion later fails,
that is a terminal invariant/configuration fault—not a routine movement result
and not a reason to roll back an already committed Position.

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
        -> complete ECS graph and post-graph structural barrier finish
        -> CommittedChangeView<Position> becomes readable
        -> WorldSpatialIndex processes each unique changed entity once
```

Canonical maintenance is deliberately **not** the existing in-graph
`SystemStage.SpatialIndex`. Later Combat, AI, WorldSim, teleports, corrections,
or Cleanup may legitimately write Position, and structural mutation is not final
until the post-graph barrier. The frozen one-step sequence is:

```
EcsWorld.Tick(step)
    -> complete Input..Cleanup system graph
    -> complete post-graph structural barrier
    -> return

game/world post-step commit
    -> consume final Position + lifecycle changes
    -> commit WorldSpatialIndex
    -> T0.6 later commits publication

Clock.AdvanceSimulationTick()
```

This executes after **every** authoritative `TickSimulationStep()`, including
each step of a host call that catches up through several fixed steps. The stage
named `SpatialIndex` may later earn a separate in-graph broadphase for Perception;
it is not canonical full-step membership truth.

The change view is a **read contract**, not a queue of copied positions. ECS remains the source of current Position. A V1-style completion event/hook is acceptable only as the control-flow signal that the view is ready; it should not allocate or carry one event per movement.

If current ECS dirty APIs are destructive-drain-only and multiple legitimate consumers need the same barrier change set, do not encode correctness as:

```
Spatial must CopyDirty before Network/Persistence DrainDirty
```

Prefer existing independent changed-row cursors/views when they already allow several consumers to observe the same logical changes at different cadences without destructively draining one another. Otherwise provide a bounded multi-reader barrier view/epoch, give one owner responsibility for deriving all downstream facts from one read, or introduce another equally explicit bounded ownership scheme. The exact API is implementation-time, but **consumer ordering around one destructive dirty set is not the contract**. A cursor/epoch used for Spatial advances only after the corresponding spatial/publication operation has committed successfully; failure may not acknowledge away unapplied changes. Lifecycle create/destroy/add/remove facts need an equally explicit incremental source; do not infer every lifecycle transition by rescanning the whole world.

Lifecycle retention is required by every runtime profile that enables Spatial,
including Local; it is no longer a Network-only reservation. The initial existing
4,096-entity game profile should reserve 8,192 lifecycle records, while larger
profiles declare evidence-backed values explicitly. A cursor overrun remains a
recoverable `ResyncRequired` fact:

```
stop derived commit
    -> invalidate/discard derived Spatial candidate
    -> full-scan current committed WorldPositionComponent truth
    -> rebuild complete WorldSpatialIndex privately
    -> T0.6 also rebuilds publication and advances its epoch
    -> only after success capture fresh lifecycle/Position cursors "from now"
    -> atomically publish rebuilt derived state
```

This exceptional scan is allowed at the owner step boundary. Failure leaves the
previous committed derived root intact when possible; inability to represent the
admitted authoritative world faults generation preparation/operation explicitly.

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
    unlink/relink the existing generation-validated membership record
    create destination chunk metadata only from guaranteed chunk capacity
    release source chunk metadata if it becomes empty
    expose one canonical migration fact to publication/composition

created spatial entity
    insert into canonical home chunk

removed/destroyed/nonspatial entity
    remove from prior home chunk
```

This is the core scaling rule:

> Pay the entity's coarse spatial membership cost once when its authoritative location changes, rather than rediscovering candidate membership independently for every observer.

If 100,000 entities exist and only 8,000 positions changed, spatial examines the changed set rather than scanning 100,000. If only 300 of those cross a 16 m boundary, only those 300 require source/destination membership mutation; the remaining position changes do not churn coarse membership. If one of those 8,000 entities was written ten times before commit, it still contributes one changed-entity entry and one final-position read.
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
**T0.1 gate:** millions of deterministic same-chunk moves and boundary crossings preserve exact membership, zero duplicate/lost entities, zero warmed owner-thread allocation, successful rebuild after generation replacement/lifecycle overrun, explicit failure-during-rebuild behavior, create/move/destroy and Position remove/re-add/index-reuse regressions, no proportional full-ECS scan on ordinary ticks, repeated same-entity writes within one barrier coalesce to one spatial maintenance observation of the final value, no correctness dependency on destructive dirty-drain ordering between consumers, and a measured direct committed read path.

**T0.1 acceptance checkpoint (2026-08-20): accepted and frozen after
saturated-capacity hardening.**
`WorldPositionComponent` is persisted as a flattened 16-byte primitive payload.
`GameWorldState` owns independent exact Position/lifecycle cursors and a fixed
intrusive `WorldSpatialIndex`; Runtime invokes it through the domain-neutral
`IServerGenerationStepParticipant` after every complete ECS step and before clock
advancement. Both Local and Networked game profiles reserve 8,192 lifecycle
facts. Lifecycle overwrite privately rebuilds from current ECS truth, failed
rebuild leaves the old derived root intact, and prepared generation publication
rebuilds a fresh index before root swap.

Twenty-five checked Release tests cover final-value coalescing, same-home motion,
negative crossings, create/destroy, Position remove/re-add, ECS slot reuse,
lifecycle overrun, failed rebuild, generation replacement, and one million
warmed moves at 0 B. They also prove both legal full-capacity migrations:
singleton source into an already-resident destination collapses the source chunk,
while singleton source into a new destination re-keys its existing chunk slot.
A deterministic reference model checks create, move, merge, removal, and
generational slot reuse after every operation across 20,000 bounded churn
operations. The split Release observation records:

```text
post-step observation, no change          35.245 ns mean / 35.656 ns p95 / 0 B
changed Position, same home              137.001 ns mean / 137.651 ns p95 / 0 B
changed Position, cross chunk            186.664 ns mean / 189.594 ns p95 / 0 B
create+insert+destroy+remove pair        579.302 ns mean / 582.691 ns p95 / 0 B
full rebuild, 4,096 entities             380.498 us mean / 390.995 us p95
                                               606,984 B/rebuild (cold recovery)
copy 1,024 members from committed chunk    2.041 us mean /   2.058 us p95 / 0 B
```

The immutable evidence is
[`terrain-t0.1-world-spatial-index-2026-08-20.json`](../../_Benchmarks/Baselines/terrain-t0.1-world-spatial-index-2026-08-20.json).
No terrain height/pack, statics, public chunk projection/history, rendering, or
Network N1 work is part of this checkpoint.

## T0.2 — validate the fixed terrain profile and choose height encoding

T0.2 no longer chooses among 16/32/64 m chunk sizes. The game spatial profile is already fixed at 16 m with 0.5 m terrain cells. T0.2 validates the consequences of that geometry and chooses the remaining physical terrain encoding decisions, especially terrain height representation.
T0's base terrain surface is one dense 2D heightfield using that fixed 16 m /
0.5 m geometry, closer to UO than a voxel world. T0.2 freezes the physical
encoding of this common base surface; it does not freeze all future world
terrain topology to one heightfield or one surface per X/Z. The base surface is
not represented as one ECS entity per tile. Future
authored chunks may add sparse structural surface geometry for cliffs, ledges,
overhangs, caves, tunnels, or other places where multiple/non-heightfield
surfaces occupy one X/Z footprint. Those surfaces remain WorldPack/world data by
default rather than ECS entities, but their exact representation is deliberately
unfrozen.

For example, one X/Z footprint may eventually contain a mountain surface at
`+42 m`, a cave ceiling at `+31 m`, and a cave floor at `+27 m`. The dense base
heightfield remains the cheap common case; it is not required to encode all
three surfaces.

The base-surface layout is two dense planes per 16 m `TerrainChunk`:

```
material[cellZ * 32 + cellX]              ushort material/content id
heightBase                                int32 absolute millimetres
height[vertexZ * 33 + vertexX]            ushort fixed-point offset

material cells                            32 × 32 = 1,024
height lattice                            33 × 33 = 1,089
```

Ignoring headers and statics, this is approximately:

```
materials: 1,024 × ushort                   ~= 2.00 KiB
based 16-bit heights: int32 + 1,089 × ushort ~= 2.13 KiB
raw terrain foundation                       ~= 4.13 KiB / chunk
```

The `(N+1) × (N+1)` height lattice gives every terrain chunk complete edge vertices for its `N × N` material cells. Neighboring chunks must agree on their shared edge; the pack compiler validates that invariant. Runtime mesh generation therefore does not need to borrow mutable neighbor memory merely to close a seam.
Terrain material behavior comes from compiled content whenever possible. Do not store duplicated walkability/render/collision flags per cell if they are stable properties of the material definition. Add another per-cell plane only when a real feature proves that the value varies independently.

### Terrain height encoding

Horizontal/world position quantum is already 1 mm. Terrain height encoding is intentionally separate.
Benchmark/requirements compare at least:

- signed 16-bit global fixed height as a range-control candidate;
- unsigned 16-bit fixed offsets from a per-chunk int32 base;
- signed 32-bit millimeters if exact vertical millimeters are required.

A 16-bit height plane is preferred only if its absolute range, per-chunk relief,
and resolution can be stated as permanent gameplay/content contracts. Terrain
sampling converts `BaseHeightMillimeters + HeightOffsetUnits * Quantum` to
authoritative Y millimeters deterministically. Dynamic `YMillimeters` may remain
int32 regardless of terrain storage width.

### Fixed-profile validation

Run representative deterministic content through the accepted 16 m / 0.5 m profile and record:

- terrain bytes/chunk and directory overhead;
- representative authored-source height/material distributions;
- candidate height quantum/range error and rejection cases;
- shared-edge duplication cost and exact-equality requirements;
- theoretical terrain vertex/cell/triangle bounds;
- malformed/out-of-range source rejection and compiler scratch high-water.

This checkpoint validates the physical terrain encoding it can actually observe;
it does not claim publication, AOI, streaming, or Godot evidence before those
systems exist. Final whole-pipeline validation of the fixed geometry belongs to
T0.10. Do **not** rerun a 16/32/64 m search by default. Reopen chunk edge or 0.5 m cell size only if concrete evidence shows a correctness/capacity/performance failure that cannot be solved more locally, and document the rejection/evidence explicitly.
**T0.2 gate:** the fixed 16 m chunk edge, 0.5 m terrain cell, 1 mm dynamic quantum, selected terrain height encoding/quantum, maximum supported local coordinates, and resulting representation evidence are written into the world-format contract and benchmark artifact.

**T0.2 checkpoint (2026-08-20): accepted and frozen after encoding and topology
contract hardening.** The accepted `TerrainHeights/v1`
representation is one signed int32 absolute base per chunk followed by 1,089
unsigned 16-bit offsets with a permanent 5 mm quantum. Authored millimetres
quantize to the canonical absolute q5 lattice with at most 2 mm integer-input
error; the minimum quantized value becomes the plane base. A chunk may contain
at most 327,675 mm of local vertical relief. Absolute quantized terrain covers
`-2,147,483,645..2,147,483,645` mm, so the packed range is no longer tied to
distance from world zero. Out-of-domain absolute values or excessive local
relief reject transactionally rather than clamp or partially write. Dynamic
authoritative Y remains signed int32 millimetres and is independent of the
terrain lattice.

The 327,675 mm relief bound belongs to one encoded dense surface plane in one
chunk. It is not a global vertical-world limit and does not constrain the format
of future sparse structural surfaces. Those later surfaces might prove to be
secondary masked heightfield patches, compact authored triangle/surface patches,
or another bounded structural representation; T0.2 deliberately chooses none.

The hardening matrix repeats the deterministic woods/coast/ridge fixture in
three independent elevation datums spanning `-1,500,000..1,500,000` mm: 192
chunks and 209,088 stored height samples. The previous global signed q10 control
rejects 139,392 samples across 128 chunks because its absolute range remains
`-327,680..327,670` mm. Both based q5 and based q10 reject no chunks. Based q5
wins because it retains the same two bytes per vertex and four-byte base as q10
while reducing mean error to 1.204 mm and p95/max error to 2 mm. Exact int32
millimetres increase the foundation to 6,404 bytes without an observed T0 need
for exact per-vertex millimetres. This fixture is synthetic evidence only;
production world content has not yet been authored, and T0.10 retains final
whole-pipeline validation.

The frozen per-chunk foundation is:

```text
materials       32 x 32 x ushort        2,048 B
height base     int32 absolute mm            4 B
height offsets  33 x 33 x ushort        2,178 B
height plane                             2,182 B
foundation                               4,230 B (4.131 KiB)
triangles       2 x 1,024               2,048
triangle indices                         6,144
duplicated positive-edge vertices           65 / 130 B per standalone chunk
terrain vertex local X/Z                0..16,000 mm inclusive
dynamic entity local X/Z                0..15,999 mm inclusive
```

All 11,088 reconstructed adjacent-edge comparisons across the three fixtures
were exact, including 330 neighboring pairs with different plane bases. Packed
offsets need not be equal across a seam; reconstructed canonical absolute
heights for adjacent pieces of the same logical base surface must be. The
fixtures stored 209,088 height vertices versus 198,147
unique global vertices, making finite-grid duplication 10,941 vertices / 21,882
bytes. One reusable authored-height/material/encoded-height scratch set is 8,586
bytes; timed encoding allocates 0 B. Exact T0.3 directory layout remains
deliberately unfrozen. The four-byte base is charged to the height plane. T0.2
records a 48-byte candidate minimum (`SpatialChunkKey` plus two offset/length
references) solely as directory-accounting input; T0.3 owns the sectioned
container decision.

The Release observation records:

```text
encode based uint16 / 5 mm  5.391 ns/vertex mean / 5.999 ns p95 / 0 B
decode based uint16 / 5 mm  1.420 ns/vertex mean / 1.468 ns p95 / 0 B
copy exact int32 mm         0.036 ns/vertex mean / 0.037 ns p95 / 0 B
```

The candidate matrix, source/material distribution, scratch accounting, exact
edge census, geometry bounds, and timings are preserved in
[`terrain-t0.2-height-encoding-2026-08-20.json`](../../_Benchmarks/Baselines/terrain-t0.2-height-encoding-2026-08-20.json).
No `WorldPack`, terrain source/loader, runtime terrain store, query, mesh,
publication, Network, or Godot work is part of T0.2.

T0.2 freezes the dense **base-terrain** encoding only. It does not establish a
permanent invariant that one X/Z coordinate has exactly one world surface.

## T0.3 — immutable `WorldPack` and terrain source

Authoring data and runtime data are separate. T0 first compiles/generates a small
deterministic authored world into one canonical runtime pack.
Do not create one file per chunk. The first pack is a bounded random-access
container:

```

WorldPackHeader
    magic
    container version
    SpatialSpaceId
    chunk/cell geometry
    content/schema fingerprint
    chunk count
    chunk-directory offset/length
    section-directory offset/length

ChunkDirectory[]               canonical sorted keys
    SpatialChunkKey
    TerrainMaterials/v1 offset / length / count
    TerrainHeights/v1 offset / length / count

SectionDirectory[]
    section kind
    section schema version
    offset
    length
    count
    hash/check

T0.3 required sections
    TerrainMaterials/v1
    TerrainHeights/v1

later sections using the same container
    additional structural-terrain surface section
                              future name/version/schema deliberately unfrozen
    FrozenStatics/v1           introduced by T0.5
    MultiFragments/v1          introduced by T0.5

```

T0.3 freezes the **sectioned container**, not the later static/Multi row schema.
Do not finalize a future record layout merely so the first terrain-only pack can
write an empty section with a misleading permanent version.
The frozen 48-byte top-level chunk row remains terrain-specific and does not grow
`StaticOffset`, `StaticCount`, Multi, or later-domain fields. Future sections own
their section-local chunk range/index metadata and relate it to canonical
`SpatialChunkKey`/main-directory order without changing this row:

```text
SHARED SPATIAL CHUNK IDENTITY != SHARED STORAGE LAYOUT
```

`TerrainMaterials/v1` and `TerrainHeights/v1` describe only the dense base
surface. The container must admit a future structural-surface section without
changing either base section, but T0.3 must not invent that future section's
name, version, or row schema.

The first format should be uncompressed unless I/O measurement proves compression
is needed. Compression complicates random access, corruption boundaries, scratch
memory, and zero-copy opportunities. Preserve section boundaries so compression
can be added later without changing semantic chunk identity.
Validation happens before publication:

- header/version/fingerprint;

- checked offsets and lengths;

- sorted unique chunk keys;

- per-section capacities;

- structural numeric IDs are well formed and within declared pack bounds;

- shared terrain edges agree;

- static placements are inside their compiled chunk fragment bounds;

- hashes/checks match;

- no proportional allocation occurs before bounds are known.

A `WorldPack` is immutable after publication. Runtime mutable state is an overlay,
not an in-place rewrite of mapped/base bytes.
`Game/World` owns structural validation only: offsets/lengths, section versions,
hashes, geometry, ordering, duplicate identities, local ranges, and shared-edge
consistency. It must not depend on `Game/Composition`.

During private generation preparation, Composition supplies a cold
`WorldContentBinding` compiled from `GameContent`: pack material/static/prefab IDs
to validated compact runtime definitions/handles. The pack content fingerprint
must match. Resolution occurs once before candidate publication; hot world queries
use the bound numeric tables rather than callbacks into Composition:

```
Game/World  -X-> Game/Composition

Game/Composition
    -> Game/World
    -> GameContent
    -> builds WorldContentBinding
```

Shared low-level numeric game identities, if needed, belong in `Game/Contracts`
rather than creating a reverse Composition dependency.
Initial loading may use ordinary bounded file reads. `MemoryMappedFile`, direct
`RandomAccess`, explicit pread/io\_uring, compression, and async read batching are
later measured backend choices, not T0 assumptions.
**T0.3 gate:** deterministic build produces byte-identical packs; malformed packs
reject transactionally; random chunk read has bounded scratch/allocation; one pack
reopens and hashes identically across supported platforms.

**T0.3 implementation checkpoint (2026-08-20): complete and stopped for
review.** `WorldPack/v1` is an uncompressed canonical little-endian container.
The frozen physical rows are:

```text
header                              160 B
chunk-directory row                  48 B
section-directory row                48 B

chunk row:
    SpatialChunkKey                  16 B
    material offset/length/count     16 B
    height offset/length/count       16 B

section row:
    kind/version                      8 B
    offset/length/count              24 B
    SHA-256 section digest prefix    16 B
```

The header fixes geometry, space, content fingerprint, bounded directory/payload
ranges, and a full 32-byte SHA-256 pack fingerprint. The pack fingerprint hashes
the complete canonical artifact with its own digest field zeroed. Chunk keys are
strictly sorted and unique. Sections are strictly kind-ordered and contiguous,
and required `TerrainMaterials/v1` and `TerrainHeights/v1` references must match
their exact per-chunk counts and lengths. Unknown future section kinds remain a
container possibility; the T0.3 compiler emits no speculative surface/static
schema.

Open reads only the fixed header before applying `WorldPackLimits`. Proportional
storage is allocated only after declared byte/chunk/section bounds are admitted.
The owned bytes become immutable, full and per-section hashes validate before
publication, all structural IDs and based heights are checked, and reconstructed
shared edges are revalidated. Random reads binary-search the packed directory and
return a borrowed `WorldTerrainChunkView`; no parsed object/cell graph is retained.
The buffered file source uses the same validator and rejects trailing/truncated
files.

Composition supplies a cold `WorldContentBinding`. Every material ID is resolved
against that binding and the content fingerprint must match before a
`BoundWorldPack` can enter `GameWorldState`. The default composition remains pack
optional; supplying a pack makes binding part of private generation construction,
so failure occurs before generation publication. Hot reads retain compact numeric
material IDs and never callback into Composition.

The current concrete game is single-space: dynamic membership uses
`GameWorldState.DefaultSpace`, and a bound base pack must carry that same
`SpatialSpaceId`. A correctly fingerprinted pack from another space rejects
during private generation construction before publication. This does not create a
generic multi-space/facet resolver; a real second space must first earn that
representation.

`GameContent.WorldFingerprint` must advance whenever the semantic meaning of any
packed numeric world-content identity changes incompatibly, even if the numeric
ID itself remains admitted. This governance applies to material/static/prefab and
future packed content meanings. T0 does not create an automatic fingerprint
framework.

The preserved 16 x 16 synthetic base-terrain pack contains 256 chunks:

```text
terrain payload                    1,082,880 B
header + directories                  12,544 B
complete artifact                 1,095,424 B

canonical compile mean / p95       1.929 / 1.966 ms
owned-memory open mean / p95       1.999 / 2.024 ms
buffered-file reopen mean / p95    2.022 / 2.041 ms
random chunk/value read mean/p95  74.704 / 76.044 ns / 0 B
copy complete base chunk mean/p95  3.593 / 3.613 us / 0 B
```

Forward and reverse insertion order produced byte-identical artifacts and the
same fingerprint. Corruption, wrong content, unsupported required schema,
unsorted keys, broken seams, invalid material/height planes, trailing/truncated
files, and preallocation limit attacks have permanent regressions. The evidence
is preserved in
[`terrain-t0.3-worldpack-2026-08-20.json`](../../_Benchmarks/Baselines/terrain-t0.3-worldpack-2026-08-20.json).
No runtime terrain sampling, mesh payload, statics/Multi rows, sparse additional
surface schema, publication, client residency, Network, or Godot work is part of
T0.3.

## T0.4 — terrain queries and deterministic mesh generation

Server/gameplay code must not depend on a Godot collision mesh to know the ground.
The world layer provides deterministic integer queries directly over chunk data:

```

TrySampleBaseTerrainHeight(location)

TryResolveBaseTerrainMaterial(location)

```

`QueryStaticBounds(aabb, destination)` joins the world query surface at T0.5,
after frozen static rows and their bounds actually exist. T0.4 does not fabricate
a static store or placeholder query merely to reserve that name.

T0 does not need a general physics engine. It needs enough authoritative ground
and static occupancy information to move a character through the cabin slice.
The base-height sampler is explicitly a base-heightfield convenience query; it
must not become the permanent definition of "the one ground height" at X/Z.
Later movement/collision requirements may earn a bounded world-surface query over
an X/Z footprint plus vertical interval so several floors, ceilings, ledges, or
vertical/non-heightfield surfaces can coexist. That API and its sparse geometry
encoding remain deliberately unfrozen until a real workload proves them.

Conceptually, later work may need a shape such as
`QueryWorldSurfaces(xz, verticalInterval, destination)`, but that spelling is not
an accepted API. `TrySampleBaseTerrainHeight` is a base-heightfield query. It is
not the permanent general definition of "what can I stand on?"

The terrain surface is two deterministic planar triangles per 500 mm cell—not a
bilinear four-vertex height function. The diagonal is selected from **global**
terrain-cell parity:

```
(globalCellX XOR globalCellZ) even: NW -> SE
odd:                                NE -> SW
```

Authoritative height sampling, generated render mesh, and generated collision
payload use the same triangle. Sampling performs triangle-plane/barycentric
interpolation with checked integer intermediates and signed nearest division,
ties away from zero; it never relies on C# truncation. Exact +500 mm cell edges
and +16,000 mm chunk edges belong to the next half-open cell/chunk.

The duplicated 33×33 border values in adjacent immutable chunks represent one
global base-surface terrain-vertex identity. Pack compilation requires bit-exact
shared base vertices for adjacent pieces of that logical surface. T0.9 mutable
height overlays are keyed by global terrain-vertex
coordinate and invalidate every adjacent chunk representation containing that
vertex, rather than mutating two independent border copies.

### Mesh contract

Mesh generation consumes one immutable root terrain chunk plus the bounded
resolved semantic neighbor halo required by incident edge/corner triangles, and
emits a bounded render payload. For the current base grid this dependency is at
most the surrounding 3 x 3 chunk neighborhood. Neighbor presentation residency
is irrelevant as long as immutable semantic data can be resolved. The server
never builds this mesh. This payload is specifically
the deterministic base-heightfield mesh. If additional structural terrain is
later implemented, it owns its own deterministic query/mesh representation while
sharing canonical world coordinates and spatial chunk identity.
Requirements:

- shared chunk edges produce bit-identical world-space seam positions;

- normals/tangents and triangle diagonals use the same deterministic global
  border/surface contract as authoritative sampling;

- no crack appears if adjacent chunks become resident in either order;

- flat/coplanar cells with compatible material may be greedily merged;

- material boundaries, height discontinuities, and required collision boundaries

  stop a merge;

- output is indexed and contiguous; no node/object per tile;

- absent neighbor presentation never changes authoritative terrain.

A terrain replacement invalidates the changed root mesh and only the bounded
adjacent mesh frames whose incident triangles read that chunk. This is local
neighbor invalidation, not an unbounded dependency graph.

If greedy merging costs more CPU than the vertex/triangle reduction earns for
real content, retain the simpler mesh. The benchmark records both mesh build time
and Godot render cost; triangle count alone is not an acceptance metric.
**T0.4 gate:** seam torture tests, deterministic mesh-payload hash, bounded output,
and a warmed mesh-build allocation profile. Live Godot rendering/streaming is not
required until T0.8.

**T0.4 implementation checkpoint (2026-08-20): accepted/frozen after focused
hardening.** `BoundWorldPack` now exposes allocation-free base-surface height and
material queries. The location's canonical half-open `WorldPosition` chooses the
chunk/cell. Height sampling reads the four packed q5 vertices and evaluates the
one triangle selected by global terrain-cell parity. Interpolation uses checked
integer plane numerators and signed nearest division with midpoint away from zero;
the caller's Y is irrelevant. Exact `+500 mm` cell and `+16,000 mm` chunk edges
therefore resolve through the next canonical cell/chunk, including negative
coordinates.

Those `BoundWorldPack` methods answer what one immutable content artifact
contains. Gameplay/current-authority callers use the equivalent concrete
`GameWorldState` routes. They delegate to `BaseWorld` in T0.4 and provide the
single place where a later durable replacement/overlay may shadow the base pack;
no generic terrain-provider interface is introduced.

The engine-neutral mesh builder retains the simple fixed grid because neither
real authored content nor T0.8 Godot evidence has earned greedy merging:

```text
TerrainBaseMeshVertex                    36 B
    chunk-local X/Z + absolute Y         12 B
    exact unnormalized integer normal    12 B
    exact unnormalized integer tangent   12 B

vertices                              1,089 / 39,204 B
triangle indices (ushort)             6,144 / 12,288 B
per-triangle material IDs (ushort)    2,048 /  4,096 B
complete caller-owned payload                  55,588 B
```

The 36-byte row is the current repository-internal, engine-neutral
mesh-preparation payload and remains regression-tested for this benchmark. It is
not a `WorldPack` or persistence schema, Network ABI, Game.Contracts ABI, mod API,
or long-term presentation compatibility promise. T0.8 may change presentation
packing, split streams, or normalized attributes when real Godot evidence earns
it without reopening base-terrain authority or `WorldPack/v1`.

The builder preflights every caller span and chunk identity before writing. It
emits upward canonical winding and the same global-parity diagonal used by the
authoritative sampler. Each vertex frame accumulates every incident triangle
from the immutable base surface, including neighboring packed chunks, so
independently built adjacent meshes reproduce the same absolute edge position,
normal, and tangent even when residency/build order differs. A later terrain
replacement must invalidate every mesh whose shared frame depends on it; missing
presentation residency never changes the calculation.

Permanent tests cover both diagonal halves, diagonal boundaries, positive and
negative signed half-millimetre rounding, exact negative/cross-chunk vertices,
material ownership, undersized/overlapping destination transactionality,
maximum-relief checked arithmetic, canonical indices, query/mesh height agreement,
tangent/normal orthogonality, deterministic rebuild, and all 33 frames along an
independently generated chunk seam. Four independently built meshes meeting at a
negative-coordinate four-way chunk corner also reproduce one exact global
position, normal, and tangent, directly covering the diagonal-neighbor halo. The canonical
nonlinear synthetic payload hash is
`943EC206C09A4E539F252C8B117C926B19FFC0E422185152F3662CBD7D5C1B6D`.

The preserved 16 x 16 / 256-chunk Release observation records:

```text
sample base height mean / p95          153.593 / 154.952 ns / 0 B
resolve base material mean / p95        75.995 /  76.740 ns / 0 B
build complete fixed-grid mesh mean    632.054 us
build complete fixed-grid mesh p95     635.291 us / 0 B
exact adjacent seam frames              33 / 33
```

The fixture is deterministic synthetic nonlinear q5 base terrain, not production
content or a Godot renderer measurement. Evidence is preserved in
[`terrain-t0.4-query-mesh-2026-08-20.json`](../../_Benchmarks/Baselines/terrain-t0.4-query-mesh-2026-08-20.json).
No static-bounds query, greedy mesh, additional structural surface, collision
engine, client residency, live Godot resource, publication, or Network work is
part of T0.4.

## T0.5 — UO-like statics and `Multi` structure compilation

Terrain and non-simulated world decoration must not become one ECS entity per
placement merely to render or stream it.

### Frozen static placement

A runtime static row is packed data, conceptually:

```

Prefab/StaticDefinitionId

chunk-local X/Z

height/Y

orientation/variant

required stable WorldStaticId

optional render/collision flags that are truly instance-varying

```

The exact field widths are chosen from content limits. Content IDs remain numeric;
Godot resource paths/text names are presentation metadata, never server hot data.
`WorldStaticId` is a 64-bit identity unique within the
`(WorldPackFingerprint, SpatialSpaceId)` domain. Durable removal/replacement
overlays refer to that ID plus the required pack fingerprint. Renderer instance
indices may map back to it but never become authority.
Authoring/building tools may snap placements to the 100 mm grid, but the runtime format must not infer that all world objects are permanently restricted to that lattice. Dynamic or physically moved objects use the 1 mm authoritative position quantum.
T0.5 freezes static placement orientation as engine-neutral yaw-only
`WorldYaw`: one unsigned 16-bit turn around +Y. `0` preserves canonical authored
facing; `16384`, `32768`, and `49152` are quarter, half, and three-quarter
turns. Increasing values use right-handed rotation, so a quarter turn maps local
+Z to +X and local +X to -Z; `65535` lies immediately before wrap to `0`.
Independent static and already-resolved Multi-piece orientations are
world-placement yaw, while the logical Multi root retains its own root yaw. This
does not establish pitch/roll or a moving local-coordinate-frame contract; a
future concrete use that needs those transforms must earn another representation.
Placements in an immutable pack use a CSR-style layout owned by
`FrozenStatics/v1`: a section-local chunk range/index maps canonical main chunk
index or `SpatialChunkKey` to `StaticOffset + StaticCount` in packed static rows.
The frozen 48-byte `WorldPack/v1` top-level terrain chunk row does not grow.
T0.5 chooses the exact section-local representation from its workload evidence.
Do not allocate one `List<Static>` for every chunk during normal read-only use.

### Logical `Multi` structure identity

A `Multi` is the UO-like logical grouping: a building, static placed structure, shelf set,
or authored collection of placements with one root identity/transform and many
local pieces.
Authoring keeps the logical Multi intact. Runtime compilation may split its frozen
render/collision pieces into the `WorldChunk`s they overlap so each chunk can load
independently. Every fragment retains the logical Multi/instance identity needed
for inspection, rebuilding, or future editing.
That `MultiInstanceId` is stable in the same pack/world domain; chunk-fragment and
packed row slots are not its identity.
T0.5 guarantees attribution and stable logical identity across fragmentation. It
does not promise a dedicated bounded/O(1) `MultiInstanceId -> all fragments`
reverse index. A future whole-structure editing/removal workload may earn a
section-local reverse index or another prepared representation.
`MultiFragments/v1` likewise owns its section-local chunk range/index metadata;
it does not add fields to the frozen top-level terrain chunk row.
T0.5 proves static placed Multi structures only. It does not establish moving
local-coordinate frames, ships, vehicles, or moving aggregates. A future moving
system may reuse authored structure definitions only when a concrete workload
proves that relationship.
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

### Future region/property seam — preserved, not implemented by T0.5

Historical RunUO `Region`, Regions-in-a-Box, Town Houses, and Rental Contract
implementations provide useful requirements without providing an implementation
shape to copy. They demonstrate unions of true 3D volumes, nested semantic
areas, authoring visualization, property applied over existing world geometry,
and vertically bounded rental subspaces. UltraSim preserves those capabilities
while rejecting a giant callback-driven `Region` base class, one universal flag
bag, and one priority winner for every gameplay decision.

The identities remain independent:

```text
SpatialChunkKey
    coarse storage, streaming, publication, and AOI identity

WorldRegionId
    durable spatial-policy identity

PropertyId
    durable ownership/economic/build-entitlement identity

PropertyUnitId
    stable assignable/rentable subspace identity

MultiInstanceId
    logical authored structure/group identity

EditSessionId / EditRegionLease
    transient authoring session and mutation-ownership identity

ConstructionProjectId
    authoritative incomplete-construction identity
```

These names are conceptual vocabulary used to preserve semantic separation.
Except for identities already implemented by a concrete accepted checkpoint,
future names in this section are not accepted public type names, APIs, schemas,
or storage layouts.

There is no numeric correspondence between them. A property may reference one
or more Multis, already-frozen world geometry, region definitions, or a
combination. A town building may therefore become a property without respawning
or reclassifying its geometry:

```text
PROPERTY != MULTI
WORLD REGION != SPATIAL CHUNK
PROPERTY UNIT != CURRENT OCCUPANT
```

A future `WorldRegion` is durable world-policy metadata. A bounded union of
integer-millimeter 3D AABBs is the current evidence-backed first candidate when a
real region consumer arrives, rather than an implicitly full-height 2D
footprint; the exact region-volume encoding remains unfrozen. One logical region
may span many chunks or contain disjoint volumes; one chunk may intersect many regions. A private
read-optimized index may compile chunk-to-candidate-region references before
exact 3D containment, but it is not `WorldSpatialIndex` and boundaries may occur
inside a chunk.

A point may simultaneously participate in several semantic regions:

```text
Britain
Market District
Drunken Badger Hotel
Room 203
Harvest Festival
```

Concrete gameplay domains resolve the relevant bounded region set. Room tenancy
may control occupancy/storage while hotel ownership controls structural changes,
town regions control law, and an event region controls event behavior. Do not
encode those unrelated answers through one global priority number or subtract a
child room from every parent semantic region.

A future property is ownership, economics, and build entitlement rather than a
geometry container. A `PropertyUnit` is a stable subspace such as a hotel room,
apartment, market stall, workshop, field, guild room, or NPC quarters. A
separate occupancy/lease relationship associates that unit with its current
holder, so changing tenants does not destroy/recreate the room identity. Player,
NPC, household, guild, business, or settlement holders may eventually share the
same relationship semantics; the fundamental geometry must not embed `PlayerId`.

Parent property rights remain applicable inside child units. Child occupancy
does not grant structural remodeling, terrain excavation, sale of the parent,
or other landlord authority unless a concrete policy explicitly grants it.
Property units may later reserve bounded shares of parent resources—storage,
decoration, merchant, resident, or utility capacity—when a real consumer earns
those budgets. T0 adds no generic quota system.

Region/property definitions may accompany a private structure candidate and
publish transactionally after validation. Editor boxes, colors, and gizmos are
presentation, not authority. Pure region-definition editing owns the region
identity/revision; it does not automatically acquire every terrain backing chunk
it overlaps. Terrain edits acquire terrain chunk leases, structure/property
edits acquire their own ownership, and a combined transaction acquires only the
domains it actually mutates.

This seam also leaves room for future semantic spaces used by simulation—rooms,
shops, work zones, pastures, districts, and similar concepts—without turning
static policy volumes into ECS entities. Concrete consumers must first earn any
policy channels or runtime store. T0.5 implements only packed statics and Multi
structure identity.

### Authoring and reversibility vocabulary

Terrain editing and static/Multi authoring do not share one representation or
one reversibility vocabulary. Preserve these distinct operations:

```text
Harden
    loose modular authored placements -> logical Multi

    Preserves higher-level grouping and identity. A hardened cabin remains
    "Cabin / Multi N".

Break
    logical Multi -> editable constituent authored pieces

Freeze
    static-like/structural content -> cheapest appropriate frozen world form

    This is representation lowering, not grouping. It may deliberately discard
    authoring structure that the frozen runtime representation does not need.
    T0.5 Multi fragments still retain their required logical Multi identity;
    deeper lowering may discard only provenance that no admitted runtime or
    editing contract requires.

Unfreeze
    frozen static-like data -> editable proxy placements

    This materializes something editable; it does not promise to reconstruct
    the original class, Multi, transaction, or high-level design unless separate
    provenance was retained.

Undo
    reverse a retained editor transaction
```

Therefore:

```text
Harden != Freeze
Unfreeze != Undo
```

Do not reuse `Freeze`/`Unfreeze` as ordinary terrain-editing verbs. Base terrain
is already close to its runtime form, so normal terrain authoring uses `Edit`,
`Save`/`Commit`, `Discard`, and editor-history `Undo`. A terrain Save validates
and publishes a complete private candidate; it is not another lowering command.

The conceptual structure-lowering flow is:

```text
Loose modular pieces
        |
      Harden
        v
      Multi
        |
      Freeze
        v
Frozen/packed world representation

Break:     Multi -> pieces
Unfreeze:  frozen statics -> editable proxies
Undo:      separate retained authoring-transaction history
```

### Future structure design and construction

Structure authoring may eventually support two visibility policies without
changing the underlying authoritative structure representation:

```text
PrivateDraft
    published structure + any separately owned property/region metadata
        -> private parallel design candidate
        -> validate complete candidate
        -> atomic structure publication

PublicLive
    validate one permitted structural operation
        -> commit it immediately
        -> nearby players may observe construction/design evolving
```

Terrain changes remain private and all-or-nothing even when structural work uses
`PublicLive`. Structure UI, placement granularity, and authority are also
separate concerns. UO-style coarse prefabs, Sims-style wall/room/floor tools, and
Valheim-style beam/socket placement should be possible consumers of the same
validated structure semantics. Do not make `HouseCell[,]`, one snap lattice, or
unconstrained transforms a permanent authority assumption merely because one UI
prefers them. Placement granularity remains content/tool/policy.

Structure design is distinct from its realization:

```text
Draft
   -> ApprovedBlueprint
        -> CompleteImmediately
        -> ConstructionProject
             -> resources + labor/work + authored stages
             -> CompletedStructure
                  -> Multi / packed fragments / appropriate frozen lowering
```

A blueprint describes what should be built; it is not a `Multi` and does not
claim that the structure already exists. A `ConstructionProject` is an
authoritative incomplete world structure, not merely a presentation timer. Its
future concrete schema may include target design, phase, delivered/required
resources, performed/required labor, and current structural realization.
Materials and labor are independent; `materials complete, labor incomplete` is
a valid state. A hammer/work action advances the approved project rather than
granting arbitrary geometry mutation.

Stage count and piece granularity are content-defined. A simple project might
show only foundation/frame/completed, while richer content may show survey
stakes, stockpiles, scaffolding, partial floors/walls, roof, windows, and finish.
Construction-only bookkeeping should disappear when complete unless gameplay
explicitly retains history; the finished result returns to ordinary
`CompletedStructure`/Multi/frozen world representation.

The initiating actor is orchestration, authorization, and planning—not a second
construction representation:

```text
player / property owner / guild / quest / community / NPC settlement / admin
                              |
                              v
                     ApprovedBlueprint
                              |
                              v
                    ConstructionProject
                       /             \
                  players             NPCs
                       \             /
                    resources + labor
                              |
                              v
                     CompletedStructure
```

An admin-prepared town tavern, bridge, dock, guild hall, wall repair, temple,
festival structure, mine entrance, or siege defense can therefore become public
gameplay rather than teleporting directly into existence. Autonomous settlement
planning follows the same path: needs select a suitable blueprint/site, then
ordinary hauling and labor jobs realize it. AI may request and work on a project;
it does not operate the player UI, bypass validation, or write frozen rows.
Construction labor may compete with harvesting, food production, repair,
crafting, or guarding, allowing overbuilding to have simulation consequences.

Current construction state is coalescible latest state. Contribution credit,
quest facts, achievements, and town history are separate retained Events when
needed:

```text
LATEST STATE COALESCES.
EVENTS RETAIN FACTS.
```

These are future semantic seams, not a T0 construction checkpoint. Do not build
`IWorldEditor`, `IBuildMode`, `IConstructionStrategy`,
`ITerrainLayerProvider`, `IGenericEditableChunk`, or a contributor/plugin
framework from this description. Concrete consumers must first prove reusable
mechanisms.

### Render-ready static batching contract (production implementation at T0.8)

T0.5 compiles deterministic compatible render groups; T0.8 consumes them in the
production renderer. Do not create one `MeshInstance3D` node per static. The
client groups compatible placements by render key and uses
`RenderingServer`/`MultiMesh` or combined meshes.
Important distinction:

- one `MultiMesh` repeats one mesh with many transforms;

- many identical books/trees/rocks can therefore batch very well;

- a shelf containing a book, globe, rune, statuette, and ring cannot become one

  heterogeneous Godot `MultiMesh` merely because they share an atlas;

- heterogeneous frozen clutter may instead be compiled into one combined mesh for

  the shelf/Multi/chunk, or several compatible `MultiMesh` groups.

Picking/interaction may map render-instance indices back to logical static or
interactive identities. Presentation batching never changes authority.
The asset pipeline should preserve reuse tricks such as statuettes/clockwork
figures using the same creature mesh at another scale/animation state. That is a
content/render decision built on numeric prefab identity, not a special terrain
entity type.
**T0.5 gate:** a cabin/Multi structure compiles deterministically across chunk boundaries,
uses packed/CSR-style static and Multi-fragment representation, preserves stable
logical identities, and emits bounded render-ready grouping evidence. Production
streaming and the no-node-per-static Godot proof arrive at T0.8.
T0.5 does not require structure customization UI, construction projects, resource
delivery/labor simulation, community building, or autonomous settlement planning;
it only preserves representations that those future workflows can validate and
lower into.

### T0.5 accepted/frozen — 2026-08-20

T0.5 was accepted/frozen after the no-base query-validation and orientation
contract hardening. The following implementation evidence is retained as the
checkpoint record; T0.6 subsequently builds publication without changing these
packed static/Multi contracts.
The frozen `WorldPack/v1` header and 48-byte main chunk row remain unchanged.
Terrain-only packs retain their original two-section canonical byte layout;
structure data adds optional section-local layouts only when present:

```text
FrozenStatics/v1
    32-byte header
    8-byte range / canonical main chunk
    56-byte packed static row

MultiFragments/v1
    40-byte header
    8-byte range / canonical main chunk
    40-byte logical instance row
    56-byte packed fragment row
```

The implementation freezes `WorldStaticDefinitionId`, stable 64-bit
`WorldStaticId`, stable 64-bit `MultiInstanceId`, and stable per-Multi
`MultiPieceId`. Canonical compilation sorts chunk-local rows by compatible render
key and stable identity. A Multi root remains independently searchable while its
pieces are compiled into their home chunks. Renderer grouping never becomes
authority.

The persisted `WorldYaw` field is yaw-only unsigned full-turn orientation:
`0/16384/32768/49152` mean canonical/quarter/half/three-quarter turn, increasing
right-handed around +Y. The packed field remains exactly two bytes. Static rows
and already-resolved Multi fragments store world-placement yaw; the logical
Multi root separately stores root yaw. Pitch, roll, and moving frames remain
outside T0.5.

Opening validates optional section hashes, bounds/counts, canonical CSR ranges,
row ordering, stable-ID uniqueness, Multi piece totals, roots, flags, local
coordinates, and exact half-open integer-mm bounds before publication.
`WorldContentBinding` additionally admits every referenced static definition.
Normal use returns borrowed packed views; it does not create a list/object graph
per chunk.

`TryQueryStaticBounds` is now the first exact static-bounds query. Placement
bounds are limited to a one-chunk home halo, queries admit an explicit maximum
candidate-chunk budget, and an undersized output span receives no partial writes.
`BoundWorldPack` answers immutable-artifact truth while `GameWorldState` is the
authoritative current-world route retained for T0.9 overlays.
The +/-1 home-chunk halo is a compact `FrozenStatics/v1` and static
`MultiFragments/v1` representation limit, not a universal maximum object size.
Larger structures may use multiple pieces/statics or a later representation
earned by a concrete workload.

`WorldStaticRenderKey` and `WorldStaticRenderGroup` are internal deterministic
packed-row preparation hints. They are not the final Godot batching/material
contract, a renderer ABI, a `Game.Contracts` API, or a mod compatibility promise.
T0.8 may filter non-renderable rows and coalesce or further subdivide these groups
from measured presentation requirements without changing static authority.

The permanent T0.0–T0.5 world suite contains 52 tests. It covers canonical
insertion-order-independent bytes, cross-chunk Multi identity, allocation-free
packed reads/grouping, transactional exact bounds queries, malformed optional
sections, invalid identities/placements/capacity, content binding, and generation
publication.

Hardened synthetic checkpoint evidence:

```text
chunks                                      256
independent frozen statics                8,192
logical static Multis                        64
Multi fragments                             512
complete pack bytes                   1,589,672

random static/fragment read mean/p95   168.476 /   170.081 ns / 0 B
copy compatible render groups          1.968 /     1.979 us / 0 B
sparse exact bounds query (max 1 hit)  23.248 /    23.527 us / 0 B
dense 2x2 bounds query (max 173 hits)  39.030 /    39.264 us / 0 B
```

Both bounds workloads include the admitted neighboring home-chunk halo and
two-pass transactional output, not a single-row lookup. The dense observation
adds result-write scaling across a two-by-two exact footprint; it does not change
or optimize the query algorithm. These are current exact-query costs, not a
collision-engine SLO. The fixture is synthetic and is not production woods/cabin
content or Godot evidence. The new machine-readable hardening observation is
[`terrain-t0.5-frozen-structures-hardened-2026-08-20.json`](../../_Benchmarks/Baselines/terrain-t0.5-frozen-structures-hardened-2026-08-20.json);
the earlier checkpoint artifact remains immutable historical evidence.

The checkpoint does not add dynamic/interactable statics, mutable overlays,
publication/history, client residency, Godot nodes/resources, property/region/
construction systems, sparse additional terrain surfaces, or moving Multis.


## T0.6 — packed chunk publication and bounded dirty history

T0.6 builds the game-owned public semantic projection that later replication profiles may consume. It is **not** part of `Spatial.Contracts` and it is not Network-owned.
`ChunkPublicationStore` is separate mutable storage keyed by `SpatialChunkKey`. Each resident publication chunk owns **one current packed public view** of its dynamic public state plus bounded revision/history metadata.
Inclusion is controlled by an explicit game-owned public-projection eligibility
contract. It is not inferred merely from `WorldPositionComponent` or exclusively
from prefab type.
Publication has its own identities:

```
PublicationEpoch            ulong, nonzero

WorldPublicEntityId
    Index                    uint
    Generation               uint, nonzero

EntityPublicVersion         ulong, nonzero monotonic within epoch/identity
ChunkPublicationRevision    ulong, nonzero chunk-local monotonic
```

`WorldPublicEntityId` is stable across chunk movement for one public lifetime in
one `PublicationEpoch`. It is not ECS identity, durable identity, Network's
`ReplicationEntityId`, or a row slot. A private ECS-index mapping is allowed.
Generation replacement and any in-generation full publication rebuild that makes
old cursors/identities incomparable advance `PublicationEpoch`. Exhaustion never
wraps silently; establish a new epoch or fault before wrap.
The initial row schema should be the smallest public locomotion/world vertical, for example numeric identity/lifetime, prefab/presentation identity, chunk-local transform, and minimal public simulation flags. It is game-owned projection data, not a serialization of arbitrary ECS components.
Store it SoA-first where fields are commonly traversed independently:

```
WorldPublicEntityIndex[]
WorldPublicEntityGeneration[]
EntityPublicVersion[]
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

`ChunkPublicationCursor` is self-identifying: it binds `PublicationEpoch`,
`SpatialChunkKey`, and the chunk-local revision. Revisions from different chunks
are incomparable, and a cursor presented for another key always requests a
snapshot rather than participating in numeric revision comparison. If bounded
metadata pressure evicts an empty chunk, a later recreation starts with its
first new global revision as `HistoryFloor`; cursors from the evicted incarnation
therefore cannot observe a partial new delta.

Snapshot recovery defines absence explicitly:

```text
SnapshotRequired
    -> request complete snapshot

snapshot returns ChunkAbsent
    -> authoritative semantic result is empty
    -> clear the retained page for that key
    -> retain no delta cursor until the key is published again
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

One dirty commit larger than the fixed history representation does **not** reject
the new current public page. If revision 10 commits an oversized change:

```
CurrentRevision = 11
HistoryFloor     = 11
dirty ring       = reset/empty
HistoryInvalidations++
```

Consumers below 11 receive `SnapshotRequired`; a consumer at 11 is current, and
a later small 11→12 commit may again be retained normally. Never keep an
incomplete suffix while claiming the commit is reconstructable. Failure to
represent current packed state is a configuration/invariant fault; failure to
retain deltas is ordinary bounded-cache pressure and never blocks world authority.
Record invalidation count and largest dirty commit.

### Cross-chunk migration ordering

A dynamic entity migration updates two coarse/publication addresses but preserves
one `WorldPublicEntityId`. Chunk revisions are deliberately incomparable, so
`EntityPublicVersion` supplies cross-chunk order. From canonical version 40:

```
source Chunk A: remove  E @ entity version 41
target Chunk B: present E @ entity version 42
canonical current version = 42
```

The client compares the two lifetime domains in order. For the same public Index,
a higher Generation supersedes every lower Generation, including rows retained
in another chunk page. For the same Index and Generation, a higher
`EntityPublicVersion` supersedes lower versions. A delayed A/41 may clean an
obsolete source page, but cannot erase/supersede B/42. A new-generation Present
may subsume the prior generation's coalesced Remove. Transient rows in multiple
chunk-local pages are acceptable under reorder; the canonical/presentation
resolver applies the generation-then-version rule globally. Destruction or
becoming non-public consumes the next version so delayed older presents cannot
resurrect the identity.

### Snapshot atomicity

A snapshot contains a complete current public scope for that publication chunk at one revision. Apply stages and validates before replacing the client's corresponding semantic chunk page. A delayed pre-snapshot delta cannot resurrect state older than the committed snapshot floor. T0 proves this locally before N1 has a packet format.
**T0.6 gate:** deterministic snapshot/delta oracle, history wrap -> snapshot fallback, slot-reuse/tombstone tests, cross-chunk migration ordering tests, warmed zero-allocation publication/query, direct-read encoding/copy benchmark, eager-vs-on-demand prepared-publication evidence including wasted-work/cache-hit accounting, and bounded memory independent of observer lifetime.

### T0.6 acceptance record — 2026-08-21

T0.6 is accepted/frozen after checkpoint hardening. The accepted implementation
does not depend on T0.7 consumer state.
The implementation adds an explicit persisted `WorldPublicComponent`; Position
or prefab provenance alone never opts an entity into broad public projection.
`GameWorldState` consumes Position, public-component, and lifecycle changes at
the existing post-ECS-step boundary, reads final current truth once per staged
entity, and commits a separate generation-owned `ChunkPublicationStore` before
acknowledging its cursors.

The store uses separate nonzero `PublicationEpoch`, generational
`WorldPublicEntityId`, monotonic entity-public version, and chunk publication
revision domains. Current rows are fixed-capacity SoA pages keyed by
`SpatialChunkKey`; a global bounded intrusive history pool records complete
identity/version operations without multiplying history by observer or resident
chunk. Public identities remain stable across movement. One crossing publishes
the source remove and target present at successive entity-public versions;
ECS-slot reuse advances the public generation. Empty source chunks retain their
tombstone history until bounded chunk/history pressure requires an explicit
snapshot fallback.

Snapshot and delta copies are complete-or-fail into caller-owned spans. Snapshot
rows and coalesced delta rows are sorted by public identity with allocation-free
bounded heapsort; the canonical engine-neutral snapshot body is written only
from that sorted snapshot. The borrowed committed page view remains the cheaper
direct SoA path and is invalid after the next publication mutation or generation
replacement. Oversized dirty commits publish current state, clear incomplete
history, advance the affected floors, and increment invalidation diagnostics
rather than rejecting authority.

The hardened permanent T0.0–T0.6 world suite contains 61 tests. T0.6 coverage
proves:

- explicit public eligibility and final-value observation after ten same-step
  writes;
- Snapshot / NoChanges / coalesced Delta behavior and exact field masks;
- source remove before target present, stable movement identity/version order,
  generation-fenced ECS-slot reuse, global cross-page generation supersession,
  and retained empty-source tombstones;
- chunk-bound cursor rejection, empty-chunk eviction/recreation floors, and
  authoritative `ChunkAbsent` reset semantics;
- history overflow -> `SnapshotRequired` while the new packed current state
  remains available;
- overlap-safe transactional canonical encoding, direct committed page
  iteration, and 0 B
  across 100,000 combined update/snapshot/encode/read cycles.
- a deterministic 20,000-operation reference-model oracle spanning lifecycle
  reuse, eligibility changes, repeated writes, moves, descriptor changes,
  history pressure, metadata eviction, stale cursors, and client convergence;
- failed rebuild retains both the old spatial index and publication
  store/epoch/current semantics.

T0.6 intentionally carries no simulation tick in its batch or semantic body.
`GameWorldState`'s commit count is generation-local and is not
`WorldClock.SimulationTick`; no current consumer has earned a host-neutral time
anchor or a Game/World dependency on Server.Runtime.

The accepted-candidate synthetic benchmark uses a 1,024-entity fixture with
eight publication chunks and 128 public rows in the measured chunk. With tiered
compilation and ReadyToRun disabled, all warmed observations allocate 0 B:

```text
post-step observation, no change       83.159 ns mean /   83.505 ns p95
public update, same chunk             404.388 ns mean /  405.089 ns p95
public migration, cross chunk         574.739 ns mean /  576.659 ns p95
update + one-record delta copy         539.126 ns mean /  541.484 ns p95
iterate prepared pages, 128 rows       203.482 ns mean /  204.322 ns p95
copy canonical snapshot, 128 rows        3.850 us mean /    3.860 us p95
copy + encode snapshot, 128 rows         4.796 us mean /    4.845 us p95
```

The hardened store reserves 1,336,128 bytes for that 1,032-slot admitted profile,
including current pages, identity maps, query/planning scratch, and bounded
history. A separate cold construction at 100,000 admitted ECS/publication slots
records 123,810,048 bytes (about 1.153 GiB per million admitted slots by linear
extrapolation). This is deterministic reserved payload, not per-observer memory;
the observation records the cost without selecting a different representation.

The policy census dirties 256 chunks. Under 256 requests concentrated on 64
unique chunks, unconditional eager construction builds 256 bodies and wastes
192 (75%); on-demand construction builds 64 and records 192 cache-hit
opportunities. Under two full passes over all chunks, both policies construct
256 bodies and on-demand records 256 hit opportunities on the second pass.
T0.6 therefore selects **on-demand recipient-independent semantic construction
over always-current prepared pages**, with no intentional wait. It does not add
a retained production byte cache before T0.7/N1 supplies a real consumer and
retention policy. This request-distribution census rejects unconditional eager
serialization without claiming that a future Ring-0-aware cache or hybrid policy
must lose once real consumers exist. It also does not add Network framing, MTU, reliability,
connection, encryption, or recipient state.

The original implementation-stop observation remains immutable at
[`terrain-t0.6-chunk-publication-2026-08-20.json`](../../_Benchmarks/Baselines/terrain-t0.6-chunk-publication-2026-08-20.json).
The current cursor/lineage/codec/capacity-hardened observation is
[`terrain-t0.6-chunk-publication-hardening-2026-08-20.json`](../../_Benchmarks/Baselines/terrain-t0.6-chunk-publication-hardening-2026-08-20.json);
it supplements rather than rewrites the historical candidate.
The fixture is synthetic public locomotion state, not production content,
client streaming, Network N1, or Godot evidence.

## T0.7 — chunk interest rings and local streaming oracle

AOI becomes a query over canonical 16 m world-spatial chunk AABBs. The **world spatial system answers which chunks overlap an interest shape; it does not own observer/subscription state.**
A recipient/observer has an authoritative spatial anchor. The query returns a small canonical set of `SpatialChunkKey`s plus a relative cadence tier. It does not ask each chunk which members are within radius X.
The first game profile uses circular X/Z interest measured by exact point-to-AABB
squared distance, with radius contact included. Hysteresis belongs to consumer
subscription reconciliation—not `WorldSpatialIndex`:

```
Immediate / R0    enter <=   5,000 mm    leave >   5,500 mm
Near              enter <=  96,000 mm    leave >  96,500 mm
Far               enter <= 192,000 mm    leave > 192,500 mm
```

Promotion tests the smaller tier's enter radius; demotion waits until its leave
radius is exceeded. Each key appears exactly once at its nearest retained tier,
in canonical `SpatialChunkKey` order. Hash iteration order is never semantic.

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

All first bounded authoritative query APIs are complete-or-explicit:

```
Success
    Count = complete result count

DestinationTooSmall
    Count = 0
    RequiredCount = exact complete count
    destination contents are unspecified and unusable

InvalidInput
    no output/mutation
```

An under-capacity call may finish a count pass to report `RequiredCount`; it is an
exceptional sizing path. Static collision/admission queries must never return a
silent partial result. A separate visitor/resumable cursor may be earned later by
measurement rather than making the initial API ambiguously partial.

### Domain-specific spatial refinements remain private

The canonical world partition being 16 m does not force every subsystem to use 16 m internal regions forever. A renderer may later batch four world chunks into a 32 m render region; navigation may use smaller tiles; physics may use finer broadphase cells. Those are private domain accelerators related by geometry/key mapping, not reasons to mutate the canonical world partition.
**T0.7 gate:** boundary/corner movement and 1 mm radius-boundary oscillation change only the expected hysteretic consumer chunk subscriptions, output is unique/canonically ordered, under-capacity queries report exact complete requirements without partial authority results, same-home-chunk movement creates no coarse membership churn, tier changes do not rewrite member state, world chunks contain no watcher lists, and local snapshot/delta application converges exactly.

Here "no coarse membership churn" means authoritative entity membership in
`WorldSpatialIndex` remains unchanged while the observer moves within its home
chunk. The exact circular fringe may legitimately add, remove, or retier a small
number of consumer subscriptions after movement exceeds the applicable
hysteresis band; that is consumer topology, not world membership.

### T0.7 accepted/frozen after hardening — 2026-08-21

T0.7 is accepted/frozen after its focused consumer hardening. It did not begin
T0.8, Godot residency/rendering, or Network N1.

`ChunkInterestQuery` is a stateless game-world query over canonical 16 m chunk
AABBs. It consumes a sorted previous subscription set only to apply
consumer-owned hysteresis, then emits each key once at its nearest retained tier
in canonical order. Radius contact is included, including contact with the
maximum face of a half-open ownership chunk. The normal path writes during one
complete count pass; an undersized destination publishes `Count = 0` and the
exact `RequiredCount`, making any provisional destination contents explicitly
unusable. Invalid profiles, spaces, or noncanonical previous sets produce no
output. A fixed 4,096-candidate guard bounds malformed/custom profiles; the
first game profile examines fewer than 1,000 candidates.

`LocalChunkStreamingOracle` is the packet-free semantic consumer. Its fixed
arrays own only recipient-local subscriptions, deterministic cadence phase,
generation-validated `ClientSemanticChunkSlot`, publication cursor/knowledge,
and a private semantic projection. New interest snapshots, retained interest
queries deltas at the selected cadence, history loss snapshots again, and leave
retires only local semantic state. A changed `PublicationEpoch` resets old local
generation/version floors before taking replacement snapshots. Snapshot and
delta batches apply to a private fixed-capacity candidate and swap live only
after complete application. `ChunkAbsent` establishes empty knowledge without
retaining a delta cursor; an already-empty absent result avoids copying the
candidate while preserving that semantic rule.

This oracle is intentionally a reference/convergence implementation, not the
T0.8 production client store. Its semantic and candidate arrays scale with the
source public-identity address capacity and are copied for transactional proof.
T0.8 owns the one real bounded client semantic resident cache; it may lift the
proven snapshot/delta/generation rules without layering another client cache over
the oracle. The oracle's current source location, CLR visibility, limits, and
evidence cadence are not Game.Contracts, mod, framework, or compatibility APIs.
The `Subscriptions` span is a borrow valid only until the next `Synchronize`.

The `1/2/4` Immediate/Near/Far step cadence used by this local fixture is an
explicit T0.7 evidence profile, not the frozen Network N1 scheduling policy.
Tier/phase/cursor state remains consumer-owned. Neither `WorldSpatialIndex` nor
`ChunkPublicationStore` gained watcher lists, recipient IDs, Network messages,
or per-observer histories/pages.

The permanent T0.0–T0.7 suite now contains 71 tests. New gates cover middle,
edge, corner, negative-coordinate and exact-contact geometry; 1 mm enter/leave
hysteresis; unique canonical output; exact capacity failure and invalid-input
transactionality; minimum/maximum addressable world coordinates; a 2,000-operation
reference-model anchor churn; stable and generation-fenced reused local slots;
fail-closed contradictory equal-version semantic input; snapshot, delta, history
fallback, cross-chunk movement, publication epoch replacement, leave/reset
convergence; a separate 5,000-operation 1/1/1-cadence consumer reference-model
churn; and 0 B warmed query/no-change streaming allocation.

The synthetic 64-public-entity local fixture produces 497 centered
subscriptions: 1 Immediate, 136 Near, and 360 Far. The selected boundary and
corner observations produce 503/2 Immediate and 488/4 Immediate respectively.
With tiered compilation and ReadyToRun disabled, all warmed observations allocate
0 B:

```text
exact query, no prior subscription          34.582 us mean / 35.106 us p95
exact query, retained hysteresis             37.081 us mean / 37.783 us p95
1 mm boundary oscillation                    36.799 us mean / 36.991 us p95
canonical chunk-boundary crossing            38.148 us mean / 38.443 us p95
local streaming, no semantic change          65.522 us mean / 65.628 us p95
local streaming, one public update           66.934 us mean / 67.057 us p95
local streaming, forced history fallback     68.572 us mean / 68.757 us p95
```

The 1,024-subscription/128-public-slot oracle reserves 237,056 bytes; its source
publication store reserves 139,776 bytes. This is one local semantic-oracle
observation over about 500 subscriptions, not a per-server-client memory policy
or an N1 fanout/packet benchmark. N1 may earn replication-owned reverse
tier/phase subscriber indexes rather than polling one such oracle independently
per remote client. T0.8 owns the real bounded client resident cache and Godot
resources.

Most no-change `SnapshotApplications` in this fixture are cheap due-cadence
probes of interested `ChunkAbsent` keys. Absence intentionally retains no
publication cursor, so the direct oracle must probe to discover later residency.
This is not a requirement that N1 transmit repeated empty snapshots: a future
replication-owned subscriber/reverse index may notify interested recipients when
an absent key becomes resident, while world/publication authority remains free
of watcher state.

The original implementation-stop evidence is
[`terrain-t0.7-chunk-interest-local-oracle-2026-08-21.json`](../../_Benchmarks/Baselines/terrain-t0.7-chunk-interest-local-oracle-2026-08-21.json).
The immutable post-hardening observation, which adds semantic admission checks
without replacing the original artifact, is
[`terrain-t0.7-chunk-interest-local-oracle-hardening-2026-08-21.json`](../../_Benchmarks/Baselines/terrain-t0.7-chunk-interest-local-oracle-hardening-2026-08-21.json):

```text
exact query, no prior subscription          35.222 us mean / 35.568 us p95
exact query, retained hysteresis             37.442 us mean / 37.821 us p95
1 mm boundary oscillation                    37.276 us mean / 37.545 us p95
canonical chunk-boundary crossing            38.794 us mean / 39.326 us p95
local streaming, no semantic change          67.813 us mean / 68.353 us p95
local streaming, one public update           70.734 us mean / 71.221 us p95
local streaming, forced history fallback     70.865 us mean / 71.148 us p95
```

All hardened observations remain 0 B warmed allocation. The run is a separate
longitudinal observation, not a paired claim that the semantic checks alone
caused the difference from the implementation-stop measurements.

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

### T0.8 implementation stop — 2026-08-21

T0.8 is implemented and stopped for checkpoint review. It does not begin T0.9,
mutable terrain overlays, Network N1, physics, navigation, or production content.

`ClientWorldSemanticCache` is the one production client semantic store. It does
not wrap or retain `LocalChunkStreamingOracle`. Its capacity is stated in client
resident chunks, per-chunk static/Multi/public rows, and retained public identity
floors; none are derived from the server's ECS/public slot address space. The
owner-thread state machine is now concrete:

```text
Absent -> Requested -> Staged -> Resident -> Retiring -> Absent
```

Requested pages copy immutable terrain, frozen statics, and static-Multi
fragments from one bound `WorldPack`; publication snapshots/deltas contribute
dynamic public records. Staging is complete-or-fail. Publishing installs one
generation-validated client-local chunk handle and deterministic semantic hash.
Retirement returns the local slot only after presentation has retired its
resources, and reuse increments the handle generation.

Public application retains the T0.7 oracle's accepted semantic rules without
its server-sized double-buffered arrays. One bounded client knowledge table
tracks the highest Generation/EntityPublicVersion per public Index. Higher
generation globally supersedes older chunk pages; delayed older state cannot
resurrect it. Equal generation/version is idempotent only for the exact same
operation, key, and record. Contradictions fail closed. Publication cursors are
chunk-bound, a source-epoch change requires an explicit cache reset, and a
`ChunkAbsent` result clears only the addressed resident public page while
retaining stale-delivery floors. Immutable base terrain/statics remain resident.

`GodotWorldRenderer` is a separate engine-thread presentation owner. It has its
own bounded build/retire queues and per-frame budget. One render chunk currently
owns one terrain `MeshInstance3D`, plus one `MultiMeshInstance3D` per compatible
frozen-static/static-Multi group and visible public-prefab group. It does not
create a node per terrain cell, static, Multi piece, or public entity. Asset
resolution is a narrow optional Godot-side seam; missing test assets use bounded
fallback meshes. A semantic chunk may be resident without a render chunk, and a
render chunk may be retained while hidden.

Terrain preparation consumes the accepted neighbor-aware base mesh payload and
creates one Godot mesh with material surfaces. The current internal arrays and
Godot resource packing remain presentation implementation, not WorldPack,
Game.Contracts, Network, persistence, mod, or renderer compatibility ABI.
Static render keys remain preparation hints: the Godot adapter filters
non-renderable rows and may later coalesce/subdivide groups from measured asset
compatibility.

`WorldRenderOrigin` rebases horizontal chunk origin plus presentation Y. Rebase
updates render-root transforms only; it leaves `WorldPosition`, chunk keys,
publication cursors, identities, resident semantic bytes, and semantic hashes
unchanged. Godot creation and retirement occur only during `ProcessFrame` and
respect the supplied build/retire counts; no ready work waits merely to form a
larger batch.

The permanent Client Core regression covers request/stage/publish/retire,
transactional capacity rejection, slot-generation reuse, request-order-
independent world hashes, immutable terrain/static/Multi copying, cross-chunk
public movement, delayed stale state, contradictory equal versions, generation
supersession, `ChunkAbsent`, and explicit publication-epoch replacement. The
native Godot headless gate performs three complete forward/reverse passes through
an eight-chunk world. It proves a three-chunk semantic window can have only one
render build completed under budget, keeps one resident render chunk hidden,
rebases without changing the semantic hash, groups eight statics per chunk into
one node, and retires to zero chunk resources/nodes beyond the renderer root.

The Release, tiering-disabled semantic-cache observation is
[`terrain-t0.8-client-semantic-cache-2026-08-21.json`](../../_Benchmarks/Baselines/terrain-t0.8-client-semantic-cache-2026-08-21.json):

```text
32-resident world semantic hash                 0.303 us mean / 0.363 us p95
borrow 32 resident semantic pages               0.948 us mean / 0.963 us p95
request + stage + publish + retire one chunk   13.415 us mean / 13.511 us p95
apply one public delta                           4.890 us mean / 4.923 us p95
```

All four warmed paths allocate 0 B. The 32-chunk evidence profile reserves
256,580 bytes. The intentionally broad default profile reserves 11,087,108
bytes for up to 512 resident chunks; this is client-local prepared semantic
capacity, not server-per-connection memory and not a production budget selected
by the small fixture.

The separate native observation is
[`terrain-t0.8-godot-world-streaming-2026-08-21.json`](../../_Benchmarks/Baselines/terrain-t0.8-godot-world-streaming-2026-08-21.json).
In the headless Debug/Mono gate, 34 builds (including one dynamic-public rebuild)
and 33 retirements produce a 2.946 ms mesh/resource-build p95 and 2.907 ms
budgeted-frame p95; maxima of 23.640 ms and 24.682 ms include first-use
JIT/resource cost. Scene-tree replace/commit is 440.85 us p95 in this short run.
These figures prove bounded native ownership and expose cold tails; they are not
GPU frame/render-quality or Release exported-game performance claims.

## T0.9 — persistence and mutable-world seam

The immutable base `WorldPack` is content, not a gigantic save snapshot. Durable
save data references its world-pack/content fingerprint.
Dynamic entities remain in existing ECS persistence. After restore, the chunk
membership/publication index is rebuilt from staged ECS truth before the candidate
`GameGenerationState` publishes.
T0.9 initially adds one `WorldSaveSetParticipant` beside ECS, Accounting, and
WorldClock. Its artifact contains approximately:

```
required WorldPackFingerprint
world/profile/schema version
SpatialSpaceId / durable world identity
mutable-overlay schema version
changed terrain/static/Multi overlay records
```

Immutable pack bytes are not saved. Fresh-host composition resolves the exact
fingerprint through its configured pack catalog. Missing pack, fingerprint,
geometry, schema, or content-binding mismatch fails private preparation; restore
never substitutes whichever pack happens to be installed.

Restore order is one owned candidate transaction:

```
resolve + validate immutable WorldPack
    -> restore staged ECS + clock/accounting
    -> apply durable overlay privately
    -> rebuild other derived game state
    -> rebuild WorldSpatialIndex from final staged Position truth
    -> rebuild ChunkPublicationStore when enabled
    -> capture fresh derived cursors / PublicationEpoch
    -> construct complete GameWorldState
    -> construct GameGenerationState
    -> atomically publish the whole generation
```

Upgrade the current lightweight staging record to an explicit owned staging
aggregate before this composition grows. Failed preparation disposes every
candidate-owned ECS/world-pack/overlay/spatial/publication/inventory/accounting
resource exactly once. `GameGenerationState` already owns `GameWorldState` as
one coherent world member. Future staging continues to preserve that root while
expanding private preparation to include any overlay/publication resources
required before whole-generation publication rather than publishing world roots
independently.
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
or replacement generation per changed chunk (or region pack) through the
existing save-set transaction.
The first mutable overlay format must distinguish:

- terrain cell/height overrides;

- added/removed/replaced static placements;

- logical Multi instance additions/removals/variants;

- schema/content version.

A complete prepared chunk is `base + durable overlay`, validated before resident
publication. Compaction of long overlay histories is a persistence concern and
must be atomic. A changed terrain chunk may shadow the base pack's dense terrain
record without modifying that immutable pack. Long-lived patch generations may
later compact into a newly built base pack through an atomic maintenance
transaction; the exact patch-file layout remains unfrozen until implementation
needs it.

Conceptually:

```text
immutable Base WorldPack
    + durable terrain/static/Multi replacement generations
    = current prepared world view
```

### Future private terrain-edit sessions

Terrain authoring uses a private parallel candidate over the current published
world. This is an architectural direction for later tooling, not a production
editor requirement in T0:

```text
Published World
      |
      | begin edit session
      v
Private Edit Instance
    immutable published-world read-through
        +
    private mutable terrain overlay
      |
 +----+----+
 |         |
Discard   Save
            |
         validate
         /      \
      reject   commit
        |        |
 retain edit    publish complete dirty terrain replacement
  session       revisions/chunks
```

The private instance shares immutable published data and owns only changed
candidate data; it does not clone the entire world. Intermediate sculpting,
painting, cave, or landscaping work is invisible to normal players. All terrain
changes—including future property terrain changes—remain private until one
complete server-authoritative Save succeeds.

The private `EditOverlay` is transient authoring state. It is not the durable
published overlay:

```text
private EditOverlay
      |
      | validated Save
      v
durable committed terrain/static/Multi overlay or replacement
      |
      v
published world generation/view
```

Do not persist preview meshes/RIDs, grey-overlay state, context-halo residency,
editor selections, or other presentation state.

### Edit-region claims, context, and shared borders

An edit session claims a bounded `EditRegion` expressed in `SpatialChunkKey`
backing units. An exclusive `EditRegionLease` coordinates concurrent authoring;
it is not durable terrain identity or a property parcel. Acquisition is atomic:

```text
request {A, B, C, D}

either acquire A+B+C+D
or acquire none and report the conflicting active claim
```

Normal users may wait, choose/resize another region, request release, or leave.
Only explicit privileged policy may force-end a conflicting session. Exact
heartbeat, expiry, and administrative workflow remain future implementation
decisions.

The editor may display surrounding authoritative world data as a read-only
`ContextHalo` so elevations, walls, roads, cliffs, cave entrances, and adjacent
properties line up against the real published world:

```text
Edit Core       claimed chunks; mutable; normal presentation
Context Halo    published chunks; read-only; visually marked/desaturated
Outside         ordinary editor/world streaming policy
```

The grey/desaturated treatment is client presentation, not authority. Tools
must reject writes outside the claim.

The dense base heightfield duplicates positive-edge vertices, but each duplicate
represents one canonical global terrain vertex. The first edit-ownership rule is:

```text
A global terrain vertex is editable only when the session owns every chunk
representation affected by that vertex.

interior claimed vertex       editable
outer claimed-region border   pinned/read-only unless claim expands
shared edge                   requires both adjacent chunks
shared four-way corner        requires every applicable chunk
```

An editor may offer “expand edit region” at a protected boundary. This avoids
cross-session seam reconciliation and preserves exact reconstructed shared
edges. Future sparse additional surfaces define equivalent ownership at their
own topology boundaries when their representation is earned.

### Terrain Save is one authoritative multi-chunk transaction

The client may preview rich tools and candidate meshes, but it never uploads
trusted terrain bytes for blind installation. The server captures the base
terrain revisions/hashes used to create the session and validates/recompiles the
canonical candidate on Save:

```text
collect all dirty terrain chunks/surfaces
    -> verify authorization and exclusive claims
    -> verify captured base revisions/hashes remain current
    -> validate material/range/geometry/topology/shared-boundary contracts
    -> compile every complete candidate replacement privately

if any step fails:
    publish nothing
    retain the private edit session for correction/discard

if every step succeeds:
    atomically commit the whole dirty region
    advance affected terrain content revisions/hashes
    publish the new prepared terrain view
```

The lease prevents ordinary human conflicts. Revision/hash validation is the
correctness proof if some other authorized process replaced terrain beneath the
session.

Terrain content identity is independent from T0.6 dynamic publication identity:

```text
SpatialChunkKey
    terrain content revision/hash       immutable/replacement world-data domain
    ChunkPublicationRevision            dynamic public-entity projection domain
```

Do not overload `ChunkPublicationRevision` to version terrain and do not push
terrain blobs through `ChunkPublicationStore`. The two domains may share chunk
interest identity while retaining independent storage, lifetime, and revision
semantics. World chunks still own no observer lists.

Normal authoritative world mutation remains owner-bound. Private editing may
prepare candidate buffers and preview meshes away from the published authority
root, but accepted publication is an owner-bound transaction. A private edit
instance may never mutate published `WorldPack` bytes or live terrain storage.

### Verified client-local terrain residency

A client may satisfy terrain residency from an installed base `WorldPack` or
verified local terrain cache when its identity matches the authoritative required
version:

```text
authoritative server
    Chunk A terrain revision 72
    Chunk B terrain revision 19

client cache
    A revision 71 -> stale -> fetch verified replacement
    B revision 19 -> current hash matches -> read locally
```

The cache is never authority. Validity derives from the world/pack identity,
`SpatialChunkKey`, terrain revision, and content hash. Local modification cannot
change server collision/gameplay truth and may require replacement before the
client is considered current. Normal play should not retransmit unchanged base
terrain merely because dynamic publication also uses chunk identity.

### Housing/property terrain reuse

Future property terrain mode reuses this same private edit-session transaction.
A property grants authorization over a bounded area or volume; the edit session
claims whatever complete backing chunks are needed for safe compilation:

```text
property authorization boundary
    what the owner may modify

chunk claim/storage boundary
    which backing chunks must be exclusively edited and recompiled
```

A property may authorize part of a chunk while its active edit session
temporarily claims the whole backing chunk. Authorization may later include
height/depth/surface restrictions over a 3D build volume so landscaping,
basements, hobbit-hole excavation, and future cave/underground surfaces can share
the same validation path. World chunks do not become land-ownership parcels.
T0 does not design the full property system.

A pure future `WorldRegion` metadata edit is a different ownership domain from
terrain compilation and does not automatically take a terrain chunk lease merely
because its volume overlaps that chunk. A transaction that changes region,
property, structure, and/or terrain state must acquire and validate only the
specific authority domains it mutates.

Structure `PrivateDraft` may use the same published-world/private-candidate shape.
Structure `PublicLive` may instead commit each validated structural operation.
That choice never changes terrain: terrain modification remains private and
all-or-nothing.

T0 does not need a full building editor to prove this seam. One deterministic
static/terrain overlay mutation and restore is enough if implementation reaches
this checkpoint; otherwise preserve the contract for the first construction
feature.
**T0.9 gate:** save/restore reproduces the same world-pack identity, ECS dynamic
entities, derived chunk membership, and any implemented overlay mutation without
persisting runtime slots.
Private edit sessions, leases, housing, construction projects, client patch-cache
distribution, and multi-user editor UI are explicitly not added to this gate.

## T0.10 — playable woods/cabin vertical and acceptance

Build a deliberately small real world rather than another isolated microbenchmark.
This is a preauthored playable proof; it does not require terrain-editor,
housing-customization, staged-construction, community-project, or autonomous
settlement gameplay:

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

Future editing/construction systems add their own explicit proportional bounds
only when those systems exist:

```text
maximum concurrent edit sessions
maximum claimed chunks/edit session
maximum dirty terrain chunks/edit session
maximum private edit-overlay bytes
maximum construction projects
maximum pieces/stages/resources per construction project
```

These are not numerical T0 gates. Their values must come from the concrete
editor/construction workloads rather than speculative reservations.

Do not reserve worst-case dynamic/static arrays for every theoretical chunk in an unloaded world. Capacity distinguishes **world addressability** from **resident working set**. Large immutable pack sections may live on disk/mapped storage while bounded resident stores own hot mutable/publication state.
If one town chunk exceeds an admitted dynamic/static/publication capacity, report the exact high-water and choose a larger bounded capacity or an explicitly designed bounded overflow-page mechanism. Do not silently fall back to an unbounded list. Reopening the fixed 16 m world profile is a last resort requiring whole-pipeline evidence, not the first response to a local capacity miss.

## Concurrency and ownership

Authority-facing world mutation is owner-thread/barrier-bound. The spatial seam does not introduce another asynchronous scheduler/mailbox stage. Canonical maintenance occurs at the game-owned post-ECS-step boundary after the complete graph and structural barrier. A scheduler may still name an in-graph `SpatialIndex` stage for a narrower broadphase or fire one barrier-complete/readiness callback; neither replaces final-step membership and neither carries per-movement data.

The sanctioned-path rule from the earlier V1 rewrite applies here: a hot path may not bypass an established mutation/commit boundary merely because direct access is faster. If a sanctioned path is too expensive, optimize the path rather than creating a second authoritative writer.

The intended hot order is:

```
ECS complete Input..Cleanup system graph
    -> direct component writes are finalized under scheduler ownership; structural work commits at the post-graph authority barrier
    -> component-scoped changed identities are deduplicated/merged
    -> expose one committed barrier change view / equivalent owned change set
    -> consume changed position/lifecycle/public facts once
    -> relink pre-reserved WorldSpatialIndex membership/home records
    -> update/stage ChunkPublicationStore current rows + dirty facts
    -> commit publication revisions / committed read root
    -> gameplay / replication bridge / local client may borrow committed read views
    -> advance authoritative clock tick
```

This ordering is a semantic requirement, not an artificial wait. A consumer cannot safely read half-mutated authoritative state anyway.
`Spatial.Contracts` performs no publish/consume scheduling. There is no:

```
ECS -> copy to Spatial queue -> wait -> copy to Network
```

The concrete world stores are updated once, then consumers read the committed memory according to explicit ownership/lifetime rules.
Immutable/base terrain and frozen statics are even simpler: once their prepared resident lifetime is valid, readers do not need a per-tick dynamic publication barrier. Rare durable overlay mutations publish a new prepared/revisioned state only when they change.
File read, decompression if later added, validation, and mesh generation may occur on workers after ownership of immutable buffers is clear. A worker never mutates live owner-bound spatial/publication state.
Private editors may likewise prepare mutable candidates and presentation payloads
away from the published authority root under exclusive bounded edit claims. Only
the owner-bound world commit may install an accepted terrain/structure candidate.
Workers and previews operate over private or immutable buffers and never mutate
published `WorldPack` or live authoritative terrain storage.
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

N0 remains an adapter to its accepted narrower coordinate profile. Its
`ReplicationSpatialPoint` stores signed `int32` global X/Z millimeters. The
EntityRelation bridge converts canonical `WorldPosition` through checked global
reconstruction. A position outside N0's representable point range is explicitly
unrepresentable by that profile; it is never truncated or clamped, and frozen N0
is not widened merely because the canonical world address space is larger.

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
- logical Multi structure identity survives chunk splitting without being
  mistaken for property or region identity;
- terrain, spatial membership, publication, client semantic, AOI subscription, and render storage do not share mutable chunk objects or local slot identity;
- dynamic ECS entities rebuild into one exact canonical home-chunk spatial index;
- ordinary spatial maintenance consumes a deduplicated committed ECS change view/equivalent rather than proportionally rescanning every spatial entity;
- repeated same-entity movement writes within one barrier produce one downstream spatial maintenance observation of the final authoritative value;
- no spatial/publication correctness depends on one consumer copying dirty state before another consumer destructively drains it;
- same-home-chunk movement performs no coarse membership topology rebuild;
- source/destination crossing relinks one pre-reserved generation-validated
  membership record and cannot fail as routine movement after Position commits;
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

- exact encoding/query/mesh topology for future sparse additional structural
  terrain surfaces (the dense base q5 encoding is already frozen);
- exact physical project/package name for the tiny Spatial contracts boundary if repository conventions constrain it;
- whether any shared spatial query interface is ever justified beyond values/math (do not add one until multiple concrete consumers earn it);
- managed arrays versus aligned unmanaged slabs for spatial/publication hot storage;
- exact membership-link/occupied-chunk lookup representation inside the frozen
  entity-capacity-sized generation-validated home-record model;
- exact ECS changed-row cursor/barrier-view/lifecycle-observation API; the
  post-complete-graph per-authority-step commit and rebuild-on-overrun semantics
  are already frozen;
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
- mutable terrain/static overlay and replacement-generation format;
- edit-session heartbeat/expiry, administrative conflict policy, property-volume
  authorization, and client terrain-patch distribution;
- future `WorldRegionId`, `PropertyId`, `PropertyUnitId`, region-volume storage,
  bounded region candidate indexing, domain-specific policy resolution, and
  occupancy/lease schemas;
- concrete ConstructionProject schema, authored stage granularity, and
  contribution authorization;
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
global reconstruction       checked Int64
distance arithmetic         Int128 before subtraction
float import                nearest millimeter, midpoint away from zero
Spatial contracts are storage-free/dependency-neutral
Network kernel does not require Spatial
systems share spatial identity/geometry, not mutable chunk storage
canonical Spatial commit is post-complete-ECS-graph per simulation step
base terrain is one deterministic parity-diagonal triangle surface
world topology is not permanently limited to one surface per X/Z
terrain edits remain private candidates until an atomic validated commit
terrain content revision is separate from dynamic publication revision
Harden preserves Multi identity; Freeze lowers representation
Unfreeze is not Undo
Multi, Property, PropertyUnit, WorldRegion, terrain, and edit leases are distinct
WorldRegions are true 3D spatial-policy metadata, not chunk partitions
policy domains resolve a bounded applicable region set, not one universal winner
structure visibility policy is separate from construction realization
publication identity is game-owned and cross-chunk ordered by EntityPublicVersion
history pressure invalidates deltas but never rejects current public state
```

These decisions may change mechanism. They may not weaken the frozen contracts above without explicit evidence and documentation.

## Stop condition and next milestone

Do not continue polishing terrain after T0 merely because more world features are possible. Once the fixed world format, concrete spatial index, packed publication, direct committed read path, streaming client/renderer separation, bounded history, persistence seam, and woods/cabin vertical are accepted, **stop T0 and return to Network N1**.
N1's first job is not new transport research. It is to add the `ChunkPublication` replication profile above the unchanged Network kernel and test the original chunk-native AOI hypothesis against the frozen N0 sparse baseline:

> Maintain recipient-to-`SpatialChunkKey` cursors, snapshot on first interest, query bounded dirty history thereafter, let cadence control query frequency, reuse recipient-independent prepared semantic publication when T0.6 evidence supports it, let the game replication bridge feed the generic Network delivery path without teaching Network chunk/AOI semantics, and let the client perform fine public visibility.

The measurement question is not “can N1 replace N0 by design?” It is:

> **Does paying coarse spatial membership/public projection once per changed entity/chunk materially reduce sparse recipient work while preserving accepted semantics and dense behavior?**

If it does not, reject or limit the ChunkPublication profile with the same evidence discipline used throughout N0. If it does, make it the preferred path where it wins while retaining EntityRelation for exact/selective/targeted cases or as a valid alternative capability.
