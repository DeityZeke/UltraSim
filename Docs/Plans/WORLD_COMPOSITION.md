# UltraSim World / WorldPack Structural Composition

**Status:** canonical World/Terrain direction; W1-W6 closed, W7 active/open. W8-W10 remain planned.
**Accepted:** 2026-09-23.
**Current implementation:** W5 proves connected two-Pack authority in a dedicated fixture. W5R moves the actual Concept Demo Game authority and Local client to declared Packs `(0,0)` and `(1,0)` under one completely validated manifest. The frozen combined Pack remains solely for pre-W7 remote structural delivery and as a historical save identity. New Concept Demo saves use `worldview/v2`; historical Cabin keeps `worldview/v1`. Neither WorldPack/v1 nor WorldStructuralManifest/v1 bytes changed.
**Related:** [TERRAIN.md](TERRAIN.md), [ASSET_STREAMING.md](ASSET_STREAMING.md), [../README.md](../README.md), [../../Game/World/README.md](../../Game/World/README.md).

## 1. Core model

An UltraSim World is not one WorldPack.

A World is one coherent spatial/simulation identity backed by zero or more independently authored WorldPack structural shards.

```text
Realm / Save / World
    ↓
SpatialSpaceId
    ↓
World structural runtime composition
    ↓
immutable WorldStructuralManifest
    ↓
WorldPackCoord(s)
    ↓
global SpatialChunkKey(s)
    ↓
terrain cells / statics / Multis / sparse surfaces
```

The primary invariant is:

> **World identity is independent from WorldPack storage identity.**

Crossing a WorldPack boundary is not a map transition, teleport, loading screen, session transition, SpatialSpace transition, or simulation discontinuity. It only changes which structural artifact backs the relevant global world coordinates.

## 2. Fixed WorldPack spatial extent

Each WorldPack owns one fixed address envelope:

```text
256 × 256 SpatialChunks

SpatialChunk:
    16 m × 16 m

WorldPack envelope:
    4096 m × 4096 m
    4.096 km × 4.096 km
```

A WorldPack may contain anywhere from one authored chunk through all 65,536 local chunk positions, subject to its other bounded admission limits. Pack existence never implies dense allocation of all 65,536 chunks.

The Pack envelope is a storage/content boundary only. It is not a gameplay, ownership, region, physics, navigation, renderer, ECS, AOI, or durable-identity boundary.

## 3. Canonical centered Pack grid

The Pack grid is globally deterministic and centered around world origin. Pack (0,0) owns global chunk coordinates -128 through 127 on each horizontal axis.

```text
Pack (0,0):
    ChunkX = -128 .. 127
    ChunkZ = -128 .. 127

Pack (1,0):
    ChunkX = 128 .. 383

Pack (-1,0):
    ChunkX = -384 .. -129
```

The exact mapping is:

```text
biasedX = ChunkX + 128
biasedZ = ChunkZ + 128

PackX = floor-div(biasedX, 256)
PackZ = floor-div(biasedZ, 256)

LocalX = floor-mod(biasedX, 256)
LocalZ = floor-mod(biasedZ, 256)

ChunkX = PackX * 256 + LocalX - 128
ChunkZ = PackZ * 256 + LocalZ - 128
```

All arithmetic must preserve the canonical floor-division semantics already used by Spatial, including negative coordinates and checked overflow behavior.

W1 implements this mapping in `Game/World/WorldPackAddressing.cs`: `WorldPackCoord`
is an 8-byte signed X/Z value, `WorldPackLocalChunkCoord` is a 2-byte bounded
X/Z value, and `WorldPackAddressing.ToPackLocal` / `TryToGlobal` provide the
canonical conversion. `Contains` tests Pack ownership without changing
`SpatialChunkKey` or incorporating `SpatialSpaceId`. The bias and division use
widened `long` arithmetic and `SpatialMath.FloorDivRem`. Every signed-32-bit
global chunk maps forward; the inverse rejects arbitrary Pack/local addresses
outside signed-32-bit global SpatialChunk coordinates. The extreme Pack windows
are consequently partial at the representable Spatial boundary.

Representative cases:

```text
Chunk (0,0)       -> Pack (0,0),  Local (128,128)
Chunk (127,127)   -> Pack (0,0),  Local (255,255)
Chunk (128,0)     -> Pack (1,0),  Local (0,128)
Chunk (-128,0)    -> Pack (0,0),  Local (0,128)
Chunk (-129,0)    -> Pack (-1,0), Local (255,128)
Chunk (383,0)     -> Pack (1,0),  Local (255,128)
Chunk (384,0)     -> Pack (2,0),  Local (0,128)
```

The Pack grid is not configurable per World. A fixed centered mapping keeps Pack coordinates deterministic content/storage addresses without adding movable manifest-origin state.

## 4. Worlds have no intrinsic rectangular size

There is no permanent MapWidth or MapHeight that constrains World growth.

A World owns a sparse set of authored WorldPack coordinates. It may contain one Pack, a dense block of Packs, disconnected Pack clusters separated by enormous unauthored distances, or zero Packs.

Only authored structural content consumes Pack/chunk storage. Coordinate distance itself consumes no terrain storage.

## 5. Five distinct structural states

World queries and streaming must preserve five states. They must not be collapsed into a generic missing/unavailable result when the distinction is semantically relevant.

### 5.1 Authoritatively unauthored Pack

```text
WorldStructuralManifest has no entry for WorldPackCoord
```

Meaning: no authored structural World exists anywhere in that 4.096 km × 4.096 km address envelope. This is hard World truth.

### 5.2 Authored Pack known but not prepared/resident

```text
WorldStructuralManifest HAS Pack entry
but this process has not acquired/opened/prepared it
```

Meaning: structural content exists according to the authoritative composition, but bytes/prepared state are currently unavailable to this process. This is acquisition/residency state, not a World boundary.

A server or client may therefore know far more Packs than it currently keeps prepared in memory.

### 5.3 Prepared Pack, chunk unauthored

```text
Pack is prepared
SpatialChunkKey has no authored chunk
```

Meaning: this 16 m × 16 m area is authoritatively unauthored. This is a hard structural World boundary inside an existing Pack envelope.

### 5.4 Authored chunk, base cell Absent

The existing T1.0 semantics remain: the chunk is authored World space, but that base-surface cell intentionally does not exist. This is known physical void/hole, not missing data.

### 5.5 Base surface Present

The requested base-terrain cell is present and carries an authoritative height/material.
T1.1 additional surfaces are independent of this base-cell result.

The permanent distinction is therefore:

```text
NotAuthoredPack
!= KnownPackButNotPrepared
!= AuthoredPackMissingChunk
!= IntentionalVoid
!= Present
```

W6 exposes this hierarchy through `PreparedWorldComposition.TryProbeStructuralChunk`
(`NotAuthoredPack`, `KnownPackButNotPrepared`, `AuthoredPackMissingChunk`,
`PreparedChunk`) and `TryProbeBaseTerrainResolution` (the five detailed base-cell
outcomes). The first three classify Pack/chunk authorship and residency; the last
two classify base topology *inside* a prepared authored chunk. A wrong Space
rejects the chunk probe rather than fabricating an unauthored Pack. Expected
Pack fingerprints are returned only when the manifest declares the Pack.

An `IntentionalVoid` base cell can coexist with T1.1 additional surfaces in the
same vertical column, as the Concept Demo Sky Island does. Existing bool and
`BaseTerrainSurfaceStatus` APIs remain deliberate lossy projections: the first
three detailed states map to `Unavailable`, while `IntentionalVoid` maps to
`Absent`. A caller requiring the exact reason probes structural state first;
W6 does not force every typed query to return five states or trigger acquisition.

## 6. World creation and growth

Both creation styles are first-class:

- Pure void: create a valid World with zero WorldPacks and paint/create structural content where desired.
- Prefilled: create/fill ordinary Packs and chunks from authoring templates such as Ocean, Flat Grassland, Stone, Desert, Known Void, procedural seed/rule, or copied area.

Prefill size is an authoring convenience, never a permanent World bound.

Extending a World adds new structural shards at new Pack coordinates. Existing entity coordinates, properties, regions, structures, history, persistence identity, and other Packs remain unchanged.

Disconnected authored areas within one SpatialSpaceId are legal. Ordinary movement cannot traverse unauthored space; teleportation may move an entity between distant valid authored areas.

## 7. World/save identity versus immutable structural content identity

Do not bake mutable World/save instance identity into immutable authored structural content merely because a World consumes it.

The target ownership split is:

```text
World / Save instance
    WorldId
    dynamic ECS/simulation state
    world time
    persistence lineage

World structural runtime state
    SpatialSpaceId
    WorldCompositionRevision
    StructuralManifestFingerprint
    PreparedWorldComposition
    save/runtime structural overlay lineage

Immutable WorldStructuralManifest
    format/version
    canonical centered Pack-grid contract
    spatial profile
    sorted WorldPackCoord -> WorldPackFingerprint entries
```

Two different saves/world instances may reference the same immutable authored structural manifest while retaining different dynamic ECS state, time, mutable terrain state, and runtime composition revision.

ManifestFingerprint identifies immutable authored composition content. WorldCompositionRevision identifies one runtime/save structural publication lineage. They are deliberately different concepts.

## 8. WorldStructuralManifest

The World-level manifest is canonical, deterministic, sparse, immutable, fingerprinted, and bounded.

Conceptually:

```text
WorldStructuralManifest
{
    FormatVersion
    SpatialProfile
    PackGrid = Centered256

    sorted entries:
        WorldPackCoord -> WorldPackFingerprint
}
```

The manifest knows all authored Packs in the immutable authored composition. It does not require all Pack bytes to be prepared/resident.

WorldPack entries are content identities, not runtime memory slots.

W3 implements this as `Game/World/WorldStructuralManifest.cs`. The v1 canonical
artifact is a 64-byte fixed header followed by sorted 40-byte entries. Its
header contains `USWMAN01`, version 1, profile 1, the current 16,000 mm chunk
edge, 256-chunk centered Pack edge, 128-chunk centering bias, entry count, and
the 32-byte SHA-256 fingerprint. The fingerprint covers every header field
except its own slot plus the complete entry table. Each entry is an 8-byte
signed `WorldPackCoord` and a 32-byte `WorldPackFingerprint`. The current
profile is fixed, not an arbitrary per-World grid language.

The manifest retains one private canonical byte array as its flat sorted
lookup table. `TryCreate` boundedly copies and sorts source entries, rejects
duplicate coordinates and invalid Pack fingerprints, then encodes canonical
bytes. `TryOpen` rejects malformed format/profile/order/hash/length before
copying and publishing untrusted bytes. `TryGetPack` performs allocation-free
binary search. Zero entries are valid. Repeated Pack fingerprints at distinct
coordinates are permitted at the index layer; W4 must prove each actual
opened artifact matches its entry and W2 spatial declaration.

`WorldStructuralManifestLimits.Default` admits at most 65,536 entries and
2,621,504 canonical bytes (64 + 65,536 × 40). Limits are caller admission
policy and are not serialized. `WorldStructuralManifestFingerprint` is a
32-byte SHA-256 content identity. `WorldCompositionRevision` is a separate
8-byte monotonic runtime/save publication value; W3 does not persist or
publish it. The manifest contains no `WorldId`, `SpatialSpaceId`, revision,
residency state, overlay, or Pack payload. A manifest entry is an authored
expectation, not a prepared or resident Pack.

## 9. PreparedWorldComposition and atomic publication

Runtime readers consume one immutable/prepared World-level composition snapshot above individual Packs.

```text
PreparedWorldComposition Revision N

    Pack A:
        immutable Base A
        Overlay A.12

    Pack B:
        immutable Base B
        Overlay B.8

    Pack C:
        immutable Base C
        Overlay C.2
```

A cross-Pack structural edit may prepare new per-Pack state independently, but publication is one World-level atomic swap:

```text
prepare A.13
prepare B.9
validate complete dirty region
validate cross-Pack seams and identities

publish PreparedWorldComposition Revision N+1
    A.13
    B.9
    C.2
```

Readers observe N or N+1. They never observe half of one structural transaction.

This extends the existing private-candidate/all-or-nothing terrain commit rule rather than replacing it.

## 10. Per-Pack mutable state

Immutable WorldPack base artifacts remain immutable.

The current shape:

```text
one BoundWorldPack
+ one WorldMutableOverlay
+ one WorldPreparedView
```

evolves toward:

```text
PreparedWorldComposition
    PreparedWorldPack A
        WorldPackCoord
        BoundWorldPack
        WorldMutableOverlay A

    PreparedWorldPack B
        WorldPackCoord
        BoundWorldPack
        WorldMutableOverlay B
```

A mutation in Pack B must not require rebuilding/replacing unrelated Pack A.

Editor/content authoring and runtime/save mutation remain distinguishable. Authoring produces new immutable Pack/manifest artifacts. Runtime terraforming persists save-owned structural composition/overlay state without silently rewriting installed Official/base content.

## 11. World-level durable structural identity

WorldPack boundaries must never become durable semantic identity boundaries.

A logical object may span several Packs, including terrain shells, mountains, properties, regions, Multis, giant structures, roads, and rivers.

The World-level composition validator owns uniqueness/continuation for identities intended to be World-scoped, including as they are earned:

```text
WorldSurfaceId
WorldStaticId
MultiInstanceId
MultiPieceId
future durable structural IDs
```

A Pack owns storage fragments. The World owns semantic identity.

CompositionRevision is not automatically part of those semantic IDs. Replacing a Pack or publishing a new World revision does not create a new semantic mountain/structure when the authored identity remains the same.

Current T1.1 WorldSurfaceId is Pack-lineage scoped because that is sufficient for the implemented one-Pack world. W5 does not impose global uniqueness on that ID, but cross-Pack additional-surface continuation still needs its own representation and validation before it can be claimed.

## 12. Cross-Pack seams

Adjacent authored chunks obey the exact same terrain-edge requirements regardless of Pack ownership.

A boundary such as Pack A local X=255 to Pack B local X=0 is an ordinary global SpatialChunk neighbor relation.

The World-level composition validator owns cross-artifact seam validation. No cracks, alternate parity, teleport, map transition, or different Physics semantics are permitted at Pack boundaries.

## 13. WorldPack/v1 remains the initial artifact format

Do not create WorldPack/v2 merely to compose multiple Packs.

WorldPack/v1 already contains global SpatialChunkKeys and useful immutable terrain/static/Multi/surface sections. Initially, opening/preparing a manifest entry proves that every contained global chunk maps to exactly that declared WorldPackCoord under the centered mapping.

```text
Manifest: PackCoord P -> fingerprint F
open F
validate every contained SpatialChunkKey maps to P
bind/publish
```

Because v1 stores global coordinates, the exact same Pack artifact cannot be mounted at an arbitrary second location. Copy/move authored geography is initially an authoring operation that remaps/recompiles content and produces a new fingerprint.

A location-independent reusable Pack format is a possible future WorldPack/v2 only if a concrete consumer earns it.

## 14. Capacity and byte ceiling

The fixed Pack envelope makes the existing 65,536 maximum chunk count meaningful:

```text
MaximumChunks = 65,536
```

The dense terrain foundation is about 4,230 bytes per chunk, so 65,536 dense chunks require about 264.4 MiB before directories, presence, statics, Multis, sparse surfaces, and other sections.

The initial hard Pack safety ceiling therefore becomes:

```text
MaximumPackBytes = 512 MiB
```

W2 enforces at most `WorldPackAddressing.MaximumChunksPerWorldPack = 256 × 256 =
65,536` in `WorldPackLimits`, while still permitting lower custom limits. The
default byte ceiling is 512 MiB; it is an additional total-artifact limit, not
a guarantee that all independent section maxima can coexist. A real flat
65,536-chunk Pack built and opened at 280,363,264 bytes (267.375 MiB), beyond
the old 256 MiB default.

An additive declared `WorldPackBuilder(..., WorldPackCoord)` rejects a chunk
outside its Pack window at insertion with `ChunkOutsidePackWindow`. An opened
v1 artifact can be checked against an external declaration through
`WorldPack.TryValidateSpatialWindow`, which walks its canonical chunk directory
and reports the first offending key. The declaration is not encoded in v1 or
its fingerprint. The legacy unaddressed builder remains available during the
one-Pack transition. The frozen combined Concept Demo Pack spans `(0,0)`/`(1,0)`
and cannot validate as one declared shard; W5R preserves its exact bytes for
pre-W7 remote delivery while independently compiling two authority Packs.

## 15. Pack and chunk composition operations

World-level structural composition supports transactional Add/Replace/Remove Pack operations.

Removing a Pack returns its complete address envelope to authoritatively unauthored World space and must fail until all required structural/runtime dependencies are validly handled.

Within a Pack, Create Chunk and Delete Chunk are different from Present/Absent cell topology edits. Deleting a chunk does not mean setting all 1,024 base cells to Absent.

## 16. Authoring UX

Ordinary terrain authoring should operate primarily in global World coordinates and hide Pack/chunk creation mechanics.

When a brush crosses into an unauthored Pack/chunk, the editor may create the required private candidate shards/chunks, stage all changes, validate the full dirty region, and commit atomically.

Pack/chunk boundaries remain visible in diagnostics/debug overlays and tooling where useful.

## 17. Streaming and residency

Server/client structural residency operates below World identity.

```text
player/AOI/structural interest
    ↓
global SpatialChunkKeys
    ↓
resolve WorldPackCoord through canonical mapping + manifest
    ↓
ensure required Pack artifact is acquired/prepared
    ↓
retain/publish required semantic chunks
    ↓
renderer/private consumers
```

The manifest may contain thousands of Packs while a server/client prepares only the active subset.

Pack boundaries may help content/cache locality, but they do not dictate render chunk, Physics partition, navigation partition, ECS chunk, or AOI region sizes.

## 18. Remote content delivery

For remote play the manifest becomes semantic admission-critical structural composition truth; every Pack's bytes do not.

Target flow:

```text
authenticate exact WorldStructuralManifest
    ↓
know complete sparse authored Pack index
    ↓
resolve starting/AOI SpatialChunkKeys
    ↓
determine owning Pack entries
    ↓
acquire/prepare only required Packs
    ↓
publish resident structural chunks
```

A client joining a 10,000-Pack World near Island A must not download unrelated continents before entering when policy permits demand-based structural streaming.

Manifest entry present + Pack bytes not resident means KnownPackButNotPrepared, never No Pack.

Changing one Pack should not require redownloading unrelated Pack artifacts.

## 19. Persistence

A save persists World identity, SpatialSpaceId, authored structural manifest identity, runtime composition revision/state, per-Pack base fingerprints, durable per-Pack mutable overlay/shard state, and dynamic simulation state.

Immutable base content remains content. Mutable authoritative terrain/topology changes remain durable save/runtime state.

A changed terrain shard does not turn the entire large World into one save blob.

## 20. Empty World and dynamic topology

A valid World may contain zero Packs and zero chunks. No dummy WorldPack is required to create or save an empty World.

Future topology editing may create/remove Packs, create/delete chunks, toggle Present/Absent cells, and add/remove sparse surfaces through private staged candidates and atomic World-composition publication.

Unavailable/unprepared content must never be treated as if mutable topology had already authored intentional void.

## 21. Regions and other spatial systems

Regions remain independent of WorldPack boundaries and may span chunks/Packs or contain disconnected volumes.

The same is true for properties, climate/biome policy, jurisdiction, visual environment profiles, lighting regions, renderer regions, navigation, simulation sectors, and future ownership systems.

SpatialSpaceId remains reserved for genuinely distinct spatial worlds/facets/dimensions. Exceeding one Pack does not create another SpatialSpaceId.

## 22. Current implementation evolution

Historical V3 compatibility path (still used by legacy one-Pack consumers):

```text
GameWorldState
    BaseWorld : BoundWorldPack
    PreparedWorld : WorldPreparedView

WorldPreparedView
    BaseWorld
    Overlay
```

The current canonical Game and Local structural path is already composed:

```text
GameWorldState
    PreparedComposition : PreparedWorldComposition

PreparedWorldComposition
    sparse Pack index / prepared subset

PreparedWorldPack
    WorldPackCoord
    WorldPreparedView (BoundWorldPack + WorldMutableOverlay)
```

The client semantic cache retains one canonical prepared structural source and may publish a
validated residency-only prepared subset without replacing live Pack views. Game rejects
conflicting prepared seams and durable IDs among present Packs. Remote Concept Demo activation
still transfers the historical combined Pack; W7 demand-driven Remote Pack preparation is not
complete. This section describes current ownership, not W7 closure or W8 edit publication.

Terrain/static/Multi/surface queries remain global SpatialChunkKey APIs and resolve the backing Pack internally. Ordinary consumers should not need to know which Pack owns a chunk.

## 23. Implementation sequence

### W1 — WorldPack coordinate primitive (CLOSED)

Add/test WorldPackCoord, centered global-chunk mapping, exact negative-coordinate cases, and inverse mapping. No runtime behavior change.

### W2 — explicit Pack spatial contract (CLOSED)

Freeze 256×256 chunks / 4096×4096 m, validate every WorldPack chunk belongs to its declared Pack window, and raise the Pack byte safety ceiling to 512 MiB with full-Pack evidence.

### W3 — immutable WorldStructuralManifest (CLOSED)

The canonical sparse Pack index, content-derived manifest fingerprint, fixed
format/spatial profile, and separate runtime `WorldCompositionRevision` value
are implemented. Empty, one-Pack, 10,000-Pack sparse, and 65,536-entry
manifests have permanent tests. This does not connect the manifest to the
one-Pack runtime or claim any Pack residency.

### W4 — PreparedWorldComposition (CLOSED)

`PreparedWorldComposition` is the canonical immutable runtime root. It retains one
`SpatialSpaceId`, valid `WorldCompositionRevision`, unchanged immutable
`WorldStructuralManifest`, and a private sorted array of `PreparedWorldPack`
address/view references. The prepared subset may be empty while the manifest
remains complete authored truth. Cold transactional construction checks duplicate
addresses, manifest membership/fingerprint, exact Space, W2 Pack window, and a
common effective Game content fingerprint. The per-Pack `WorldPreparedView`
continues to own the exact bound base plus independently revised overlay.

Global chunk queries resolve their Pack internally via the W1 address mapping.
Base terrain, neighbor-capable mesh source, vertical columns, frozen statics,
Multi fragments, additional surface bands, replacement identity, and bounded
static-bounds scans use the same accepted algorithms and borrowed views. A
representative 64-Pack warmed lookup/query loop allocated zero managed bytes.

`GameWorldState` can own either the new root or the legacy one-Pack view, never
both. Its structural query surface, including the vertical-column reads used
by Simple Physics, selects one root. `worldview/v1` explicitly refuses to
capture a canonical composition rather than silently dropping Packs. W4 does
not hot-swap compositions, acquire Packs, migrate persistence, or claim the
full W6 structural-state vocabulary. W5R subsequently migrated the product
authority while retaining the historical Pack only for remote delivery.

### W5 — multi-Pack authority/local proof (CLOSED)

The dedicated `(0,0)`/`(1,0)` fixture independently compiles two declared Packs around
global chunks 127/128. A complete prepared-composition validation requires every
manifest Pack, checks exact q5 base-terrain edges across Pack addresses, and rejects
duplicate WorldStaticId and MultiInstanceId ownership. Partial prepared subsets
remain valid for bounded runtime queries, but cannot make complete-world claims.

Real Game control moves one persistent/public actor 127→128→127 with continuous
Simple Physics support, ordinary SpatialChunkKey migration, and source/target
ChunkPublication behavior. Client semantic residency and the Pack-blind Godot
renderer keep their instances while a presentation window straddles the seam.
`worldview/v2` records exact manifest identity, World-level composition revision,
and sorted durable per-Pack overlays; it resolves immutable manifest/Pack bytes
externally and privately validates the complete candidate before generation
publication. Legacy `worldview/v1` remains readable and writable for the
historical one-Pack path. Capture currently refuses a partial prepared runtime
instead of risking omission of durable overlay state; the v2 *format* does not
encode prepared residency. A later durable overlay catalog may relax that
runtime limitation without changing the format.

Multis are valid and globally collision-checked on both sides; one logical Multi
continuing across Packs is not represented by this v1 profile. Repeated
WorldSurfaceId is not globally rejected; additional-surface cross-Pack
continuation is not claimed. The immutable W5 fixture evidence remains under `_Benchmarks/Baselines`.

### W5R — Concept Demo migration (CLOSED)

One Game-owned authored source holds two unbound immutable declared Packs and a canonical manifest.
Pack `(0,0)` owns 117 authored chunks (Central plus Far-Island west); Pack `(1,0)` owns
45 (Far-Island east). `UltraSimGameComposition` binds both to admitted Game content
for each generation and validates the complete composition before publication.
Ordinary Single Player does not build the historical combined Pack. Local semantic
residency and Godot presentation query the canonical composition without Pack-specific
rendering branches. Standalone and Complete authority use the same root; their
pre-W7 remote clients still receive the byte-identical combined compatibility Pack.
The resulting 162-chunk terrain, static, Multi, and additional-surface semantics are
exactly compared against that historical artifact. Direct control crosses Far-Island
chunks 127/128 in both directions; local Far-Island save/load retains exact
position and manifest/revision identity.

No production path publishes nonempty mutable structural overlays today, but the
legacy format permits them. A pre-W5R Concept Demo `worldview/v1` save therefore
fails closed with an explicit compatibility error under canonical authority; it is
not silently rebased or stripped. Cabin v1 restore remains supported. W7 owns
remote manifest/Pack acquisition; W6 owns explicit structural absence vocabulary.

### W6 — explicit absence/residency states (CLOSED)

Canonical immutable composition probes now distinguish authored Pack absence,
manifest-known but unprepared Pack, missing chunk inside a prepared Pack, authored
base void, and present base cell. Game and composition-backed Client Core expose
the detailed status; legacy one-Pack sources do not invent manifest truth.
Warmed probes allocate zero; the exact Concept Demo Sky Island proves base void
and additional structural surfaces can coexist. W7 still owns acquisition.

### W7 — remote manifest and demand-based Pack acquisition

Authenticate exact structural composition, acquire/prepare required Packs on demand, and preserve client residency across Pack seams without expanding semantic disclosure.

The client-side prepared-subset publication seam and Content's independently verified passive
structural-manifest kind are implemented as W7 groundwork. Game protocol v33 now has bounded
structural control messages; the product bootstrap includes the exact manifest, independent Pack
packages can be fetched without another catalog activation, and the authenticated manifest is
World-validated with an explicit proof before guest admission. Product Remote presentation still
uses the historical combined Pack. Per-Pack grants/proofs, demand acquisition, bounded initial
semantic residency, delivery readiness, retirement, and seam acceptance remain open. This does
not close W7.

### W8 — atomic Add/Replace/Remove Pack composition

Publish complete prepared composition revisions atomically and prove cross-Pack edit all-or-nothing behavior.

### W9 — editor/private authoring candidate

Auto-create Packs/chunks, paint across boundaries, bulk-fill Packs, Delete Chunk, Delete Pack, and validate before publication.

### W10 — durable runtime mutable topology

Persist save-owned Present/Absent topology plus new/removed chunks and Packs when concrete gameplay requirements justify runtime structural mutation.

## 24. Permanent acceptance cases

At minimum permanently prove:

- Centered Pack mapping and inverse mapping around every zero/±128/±256 seam.
- Exact canonical negative-coordinate behavior and checked overflow.
- Legacy one-Pack fixture behaves identically through composition-aware APIs.
- A fully populated 256×256 Pack can compile/open within configured safety bounds.
- A sparse Pack stores primarily authored content rather than 65,536 empty rows.
- Connected terrain crosses a Pack boundary with exact reconstructed shared edges.
- Missing Pack, known-unprepared Pack, missing chunk, intentional void, and present surface are observably distinct.
- Distant Pack clusters coexist without allocation/storage proportional to coordinate distance.
- Adding a Pack changes no existing global coordinates or semantic identities.
- Validated Pack removal retires only that structural scope.
- World-level durable identities cannot collide merely because fragments live in different Packs.
- Local and Remote delivery of the same structural composition produce equivalent semantic World truth.
- Readers never observe a partial cross-Pack composition publication.

## 25. Permanent design rules

> **A WORLD IS NOT A WORLDPACK.**

> **A WORLDPACK IS A FIXED 256×256-CHUNK / 4.096×4.096-KM STRUCTURAL ADDRESS SHARD.**

> **PACK (0,0) IS CENTERED AROUND WORLD ORIGIN; PACK ADDRESSING IS GLOBALLY DETERMINISTIC.**

> **WORLD IDENTITY IS INDEPENDENT FROM IMMUTABLE STRUCTURAL-MANIFEST IDENTITY AND WORLDPACK STORAGE IDENTITY.**

> **A MANIFEST-KNOWN PACK THAT IS NOT PREPARED IS NOT MISSING WORLD.**

> **ONE SPATIAL SPACE MAY CONTAIN ZERO OR MANY WORLDPACKS UP TO EXPLICIT CAPACITY LIMITS.**

> **WORLDPACKS MAY BE FULL OR SPARSE.**

> **WORLD SIZE IS THE AUTHORED COMPOSITION, NOT A PERMANENT WIDTH/HEIGHT FIELD.**

> **PREFILL SIZE IS AN AUTHORING CONVENIENCE, NOT A WORLD BOUNDARY.**

> **WORLDS MAY GROW, SHRINK, AND CONTAIN DISCONNECTED AUTHORED CLUSTERS WITHOUT RELOCATING EXISTING COORDINATES.**

> **CROSSING A WORLDPACK BOUNDARY MUST BE SEMANTICALLY BORING.**

> **STORAGE PARTITIONS MUST NOT BECOME GAMEPLAY, REGION, PHYSICS, RENDERER, NETWORK-SESSION, AOI, OR DURABLE-IDENTITY BOUNDARIES.**

> **THE WORLD PUBLISHES ONE ATOMIC PREPARED COMPOSITION ABOVE PER-PACK BASE/OVERLAY LINEAGES.**

> **THE STRUCTURAL MANIFEST MAY KNOW FAR MORE PACKS THAN ARE CURRENTLY PREPARED/RESIDENT.**

> **WORLDPACK/V1 REMAINS VALID UNTIL A CONCRETE REQUIREMENT EARNS A FORMAT REVISION.**

> **STRUCTURAL EDITS ARE STAGED AND COMMIT ATOMICALLY.**

> **EMPTY, HANDCRAFTED, PREFILLED, CONTIGUOUS, SPARSE, AND DISCONNECTED WORLDS ARE ALL FIRST-CLASS.**

## 26. Result

This composition model scales from an empty handcrafted project, through a conventional ~4 km one-Pack World, through contiguous 10+ km Worlds, through very large sparse Worlds, to far-separated authored clusters connected only by teleportation without changing the coordinate, terrain, simulation, or client semantic model.

```text
World / Save identity
    ↓
SpatialSpaceId
    ↓
World structural runtime composition
    ↓
WorldStructuralManifest
    ↓
WorldPackCoord
    fixed 4096 m × 4096 m address shard
    ↓
SpatialChunk
    fixed 16 m × 16 m canonical spatial identity
    ↓
Terrain Cell
    0.5 m × 0.5 m base-surface cell

+ sparse additional structural surfaces at arbitrary elevation
```

Each storage level is independently sparse where appropriate, and none imposes an artificial final rectangular size on the World.
