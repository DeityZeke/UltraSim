# Chunk / Terrain / Statics Implementation Guide (Working Spec)

Purpose: capture the current agreed architecture for chunking, terrain, frozen statics, chunk streaming, and chunk-driven simulation so implementation can proceed without losing design decisions.

Status: planning/spec document (pre-implementation for chunking in current island slice).

Important sequencing note:

- terrain/chunk work should follow the minimum authoritative physics seam
- see `Docs/AUTHORITATIVE_PHYSICS_ARCHITECTURE.md`
- terrain is treated as the first major gameplay/world-content system after the minimum client/server platform is coherent
- see `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`

---

## 1) Why This Exists

We now have a solid pre-AOI / pre-chunking network baseline:

- Dense/everyone-active synthetic profile (20 Hz movement + chat): ping SLO passes through ~56 CCU, fails at 64.
- More realistic synthetic profiles (10 Hz movement-only or ~40% active movers/chatters): ping SLO passes through ~80 CCU, fails at 96.

These results strongly indicate the next major scaling win is:

1. **Server spatial chunking + AOI interest filtering**
2. **Shared delta fanout optimization within chunk/interest groups**

This document defines how chunking should be structured so those wins do not conflict with later terrain, statics, render streaming, and world simulation goals.

---

## 2) Core Principles

### 2.1 Layered Chunking (not one monolithic "chunk system")

Chunking is split into three layers:

1. **Spatial Chunks (server runtime)**
   - Dynamic entity membership
   - AOI / interest filtering
   - Replication fanout grouping
   - Nearby runtime queries (chat/combat/awareness)

2. **Terrain Chunks (authoritative content)**
   - Terrain tile data
   - Frozen statics / deco / baked structures
   - Chunk overlays (player edits/building deltas)
   - Dirty/version tracking
   - Collision/nav rebuild triggers

3. **Render Chunks (client runtime)**
   - Sliding window around player/camera
   - Pooled render containers
   - Local-relative + world-absolute chunk mapping
   - Zone tiers (near/mid/far)

### 2.2 Chunk Identity != ECS Entity Identity

- The canonical identity of a chunk is **its chunk coordinate/key** (`ChunkCoord` / `ChunkLocation`).
- A chunk may also have a **runtime ECS proxy entity** (for simulation metadata), but that entity is not the canonical chunk ID.
- Chunk ECS proxy entities may be created/destroyed as chunks load/unload; chunk coordinates remain stable.

### 2.3 Frozen Statics Are Chunk Content by Default (not ECS Entities)

`/freeze`-style baked items/props/buildings should normally be stored as **chunk content records**, not ECS entities.

They still load:
- on the **server** for collision / LOS / nav / query purposes
- on the **client** for rendering

Promote to ECS only when runtime state/logic is needed (doors, containers, destructibles, harvestables, scripted interactives, etc.).

---

## 3) World Scale Alignment (Initial Defaults)

Use the design-doc terrain defaults as the initial chunk alignment:

- Tile size: `0.5m x 0.5m`
- Height step: `0.25m`
- Terrain chunk size: `32 x 32 tiles` = `16m x 16m`

### Initial Recommendation

- **Spatial chunk size (server runtime): 16m x 16m (XZ)** to match terrain chunks
- Y axis can be reserved for future vertical worlds, but Phase A can remain effectively XZ-first

Rationale:
- simple server/client chunk mapping
- simple chunk snapshot boundaries
- easy render streaming integration
- easier debugging

---

## 4) Coordinate and Identity Model

### 4.1 ChunkCoord (Phase A runtime)

For Phase A (AoI/replication), use a small XZ coordinate type:

- `ChunkCoord { int X, int Z }`
- Value type, deterministic equality/hash
- Helpers:
  - world -> chunk
  - chunk -> world bounds
  - enumerate neighbors/radius/ring

### 4.2 ChunkLocation (future/full world)

For later terrain/verticality/full systems, support a fuller coordinate key (like old `ChunkLocation`):

- `X`, `Z`, optional `Y`
- packed key optional (for persistence or compact indexing)

### 4.3 Identity Spaces (must remain separate)

Use separate identity spaces:

1. **Chunk coordinate/key**
   - canonical chunk identity

2. **Static content IDs**
   - chunk-local static index, or stable content ID (for frozen statics)

3. **Runtime ECS entity IDs**
   - players, NPCs, dynamic items, chunk proxy entities, promoted interactives

Never overload ECS entity IDs to act as chunk IDs or static record IDs.

---

## 5) Spatial Chunks (Server Runtime / AOI)

This is the **first implementation target** because it directly addresses current replication fanout costs.

### 5.1 Responsibilities

- Track which dynamic ECS entities are in which chunks
- Provide chunk/radius-based runtime queries
- Track per-connection visible/subscribed chunks
- Drive replication interest filtering
- Support explicit AOI enter/leave behavior

### 5.2 Data Structures (Phase A)

Recommended minimal structure:

- `Dictionary<ChunkCoord, SpatialChunk>`
- `Dictionary<EntityPacked, ChunkCoord>` or `SpatialChunkOwner` component fast path
- `Dictionary<ConnectionId, ConnectionInterestState>`

`SpatialChunk` (minimal):
- membership set/list of dynamic entity IDs
- membership version / dirty flag
- optional counters (players, npcs, items)

Keep this single-threaded initially (server tick is deterministic and single-threaded).

### 5.3 Components

Minimal runtime component for dynamic entities:

- `SpatialChunkOwner`
  - current chunk coord
  - optionally previous chunk coord (if useful for transitions)

This should be updated by movement/teleport systems through normal world mutation pathways.

### 5.4 Chunk Membership Updates

When a dynamic entity moves:

1. Compute new chunk from world position
2. Compare with `SpatialChunkOwner`
3. If changed:
   - remove from old `SpatialChunk`
   - add to new `SpatialChunk`
   - update `SpatialChunkOwner`
   - mark old/new chunk membership dirty

Fast path is required; avoid "scan all chunks to find entity" in steady state.

### 5.5 Query Patterns (avoid global scans)

Preferred patterns:

- **Neighbor/radius chunk iteration**
  - compute chunk bounds from center/radius
  - loop `for cx in min..max`, `for cz in min..max`
  - `TryGetValue` chunk
  - optional per-entity distance filter

Avoid:
- iterating every chunk to answer local queries

### 5.6 AOI / ConnectionInterestState

Each connection should track:

- current center chunk (usually player-controlled entity chunk)
- subscribed chunk set (AOI)
- previous subscribed chunk set (for diffing enter/leave)
- optionally AOI radius configuration

Per tick (or on chunk movement):

1. recompute desired subscribed chunks
2. diff vs current
3. emit ordered enter/leave actions
4. update subscription state

### 5.7 AOI Enter/Leave Semantics

Rules:

- Enter/leave events must be explicit and deterministic
- Order should be stable within a tick (e.g., sorted chunk order and stable entity order)
- Leaving AOI must produce despawns or equivalent state invalidation on client

You already have server despawn messaging support; use it in Phase A.

---

## 6) Replication and Fanout with Chunking

### 6.1 Current Problem (Pre-AOI)

Current island slice baseline shows a fanout/replication knee:
- high correction throughput plateaus
- ping p95 rises sharply as CCU increases

Without chunking/AOI, changed entities are effectively replicated too broadly.

### 6.2 Phase A Goal

Replace broad interest checks with chunk-based interest filtering:

- entity changed
- look up entity chunk
- replicate only to connections subscribed to that chunk (or equivalent AOI set)

### 6.3 Execution Pattern (Desired)

1. Simulation mutates entities
2. Replication pipeline gathers changed entities
3. Interest filter checks chunk membership against connection AOI
4. Only interested recipients receive deltas
5. Existing replication budgets/overflow/debt logic still applies

### 6.4 Shared Fanout (Phase D / later optimization)

After AOI is working:

- build shared deltas per chunk/interest group where possible
- avoid redundant filtering/serialization for clients in same chunk group
- keep per-connection customization minimal (lane trimming, identity exceptions only)

Do **not** optimize this before AOI is in place.

---

## 7) Terrain Chunks (Authoritative World Content)

Terrain chunks are the canonical storage unit for world terrain and frozen content.

### 7.1 Responsibilities

- Store terrain tile grid
- Store frozen static/deco/building records
- Store overlays (player edits/building changes)
- Track dirty/versioning
- Trigger/rebuild derived artifacts (collision, nav, meshes)

### 7.2 Chunk Content Layers (Recommended)

Each chunk should conceptually contain:

1. **Base Terrain Data**
   - tile heights/materials/flags

2. **StaticChunkData (frozen content)**
   - baked props/deco/buildings
   - static collision descriptors
   - render instance records

3. **Overlay Data**
   - player edits/building placements/removals
   - chunk-level modifications over base content

4. **Derived Runtime Artifacts** (cache/runtime, not authoritative)
   - collision cache
   - navmesh data
   - greedy mesh / render batches
   - replication snapshot caches

### 7.3 Dirty and Versioning

Track versions/dirty flags separately for:

- terrain data
- statics/deco
- overlay/building data
- derived collision/nav
- render mesh cache

This allows incremental rebuilds and incremental chunk streaming/snapshots.

### 7.4 Client -> Server Authority Path (Chunk-Affecting Actions)

Chunking also needs an explicit write/update path for player-initiated world changes. This is mostly a **Phase C** concern, but the integration point should be documented now.

Examples:
- terrain edit
- building placement/removal
- static promotion (e.g., door interaction, harvest node activation)
- overlay modification

Required flow (authoritative server path):

1. Client submits command/input packet through the normal network command path
2. Server validates/authenticates/authorizes action (range, permissions, tool/state, anti-cheat constraints)
3. Action is handed to the server command pipeline / ECS-safe mutation point (barrier-safe handoff)
4. Server mutates authoritative chunk data (`TerrainChunkData`, `StaticChunkData`, `ChunkOverlayData`) and/or promotes a static to ECS
5. Server marks appropriate dirty/version flags:
   - terrain/static/overlay dirty
   - derived collision/nav/render dirty (as needed)
   - replication snapshot/cache dirty (as needed)
6. Server schedules/rebuilds derived artifacts (immediate or deferred budgeted rebuild)
7. Server replicates resulting world changes to interested clients (chunk snapshot delta, entity delta, or both)

Design rule:
- **Clients never directly mutate chunk state.**
- Clients submit requests/commands; the server applies authoritative chunk changes.

---

## 8) Frozen Statics and Interactive Statics

This section covers `/freeze`-style "file statics" and how they relate to chunking and ECS.

### 8.1 Frozen Statics (default path)

Examples:
- rocks
- walls
- baked furniture
- non-interactive trees
- decorative clutter
- frozen buildings

Storage:
- `StaticChunkData` records inside terrain chunk content

Loaded on server for:
- collision
- LOS checks
- nav obstruction
- spatial queries

Loaded on client for:
- rendering
- optional local collision/trace support

### 8.2 They Are Not ECS Entities by Default

Why:
- too many objects
- no independent runtime behavior
- unnecessary ECS iteration/memory overhead
- persistence is easier as chunk content records

### 8.3 Promotion to ECS (when needed)

Promote a static content record to an ECS entity when it requires runtime state/logic:

- door open/closed/locked
- container inventory
- destructible object damage state
- harvest node depletion/regrowth
- scripted triggers
- ownership/permissions with active logic

Promotion flow (conceptual):

1. Chunk static record exists (authoritative content)
2. Runtime event marks it interactive/changed
3. Spawn ECS entity proxy with state/components
4. Chunk overlay records "hidden/replaced" state for original static record
5. Replicate ECS runtime state as needed

The static content record keeps its content identity; the runtime entity gets its own ECS identity.

---

## 9) Chunk ECS Proxy Entities (Simulation Metadata)

Chunks themselves can and should support simulation metadata, but via a **hybrid model**.

### 9.1 Why Chunk ECS Proxies Are Useful

Chunk ECS entities allow chunk-level simulation values to participate in systems naturally:

- safety rating
- comfort value
- biome tags (`ForestZone`, etc.)
- town/faction ownership
- crime pressure / danger heat
- local activity heat
- NPC simulation cadence band

### 9.2 What a Chunk ECS Entity Is

- A runtime proxy representing a loaded/active chunk
- Tied to a stable `ChunkCoord` (stored as component data)
- Not the canonical chunk identity

Chunk ECS entities may be loaded/unloaded as chunk streaming changes.

### 9.3 What Should NOT Live on Chunk ECS Entities

Avoid placing large bulk data directly in chunk ECS components:

- terrain tile arrays
- full static instance lists
- big mesh buffers
- large collision blobs

Those should remain in manager-owned chunk content/runtime caches.

### 9.4 Good Chunk Proxy Components (Examples, Future / Design Intent)

Important scope note:

- For **Phase A (server spatial chunking + AOI)**, chunk ECS proxy entities are **not required**.
- If chunk proxies are introduced early, the only likely-required components are:
  - `ChunkCoordComponent`
  - `ChunkLoadedState`
- The list below is **future-facing design intent** for later NPC/world-sim systems. Do not pre-build all of it before concrete consuming systems exist.

Potential chunk-level components:

- `ChunkCoordComponent`
- `ChunkLoadedState`
- `ChunkSafety`
- `ChunkComfort`
- `ChunkBiomeTag`
- `ChunkTownOwnership`
- `ChunkActivityHeat`
- `ChunkRiskHeat`
- `ChunkSimCadenceBand`
- `ChunkDirtyFlags`
- `ChunkVersionSummary`

Implement incrementally, only when concrete systems require them.

---

## 10) Client Render Chunks (Sliding Window)

Render chunks are client runtime artifacts for streaming and presentation.

### 10.1 Responsibilities

- Maintain a sliding chunk window around the player/camera
- Pool/reuse render chunk containers
- Map local-relative render slots to world-absolute chunk coords
- Organize near/mid/far render tiers
- Resolve chunk/static visual content via client content resolver / pack stack (logical IDs -> engine assets)

### 10.2 Dual Coordinate Model (keep this)

Each render chunk should track:

- **Relative/local slot coordinate** (render space)
- **Absolute world chunk coordinate** (server chunk coordinate)

This supports:
- precision-friendly rendering
- stable chunk identity for streaming/replication matching

### 10.3 Zone/Tier Rendering (Near/Mid/Far)

Keep the old concept:

- near: interactive/full fidelity
- mid: batched visuals
- far: low-res/billboards/impostors

This is a rendering concern layered on top of chunk streaming, not a replacement for chunk streaming.

---

## 11) Physics / Collision / LOS / Navigation

Chunking affects gameplay correctness, not just rendering.

### 11.1 Server Must Load Static Collision

Frozen statics and terrain must be present on the server for:
- collision
- line of sight
- pathfinding/nav
- projectile traces

This does **not** imply ECS entities for each static object.

### 11.2 Derived Artifacts

Store derived artifacts per chunk (runtime caches):

- collision shapes / collision acceleration structures
- navmesh data (possibly multiple agent classes)
- LOS/occlusion helpers (optional later)

Mark them dirty and rebuild when terrain/static/overlay changes affect them.

### 11.3 Greedy Meshing

Greedy-meshed frozen statics (and maybe some non-frozen grouped content later) should be treated as:

- chunk-level render/cache artifacts
- versioned and rebuildable
- not chunk identity
- not per-object ECS state

---

## 12) NPC / District / World-Sim Integration

Chunking supports the living-world simulation model directly.

### 12.1 Chunks vs Districts

- **Chunks**: fine-grained runtime spatial units (queries, AOI, streaming)
- **Districts/Regions**: coarse simulation/social/economic units (NPC locality, risk modifiers, faction control, world events)

Districts should be modeled as a layer **above** chunks:
- a district contains many chunks
- district systems aggregate or influence chunk-level values

### 12.2 NPC Locality and Cadence

From NPC/time design:
- most NPCs are local to one district or adjacent districts
- NPC evaluation cadence is not every tick

Chunking implication:
- nearby/visible chunks -> higher simulation cadence
- distant chunks -> reduced cadence / coarser updates
- district-level events can modify chunk-level heat/risk/safety values

This is future work, but chunk architecture should not block it.

---

## 13) Persistence and File Format Guidance (Initial)

Exact file format can evolve, but the split should remain stable.

### 13.1 Recommended Conceptual Files/Records

Per chunk (or chunk database entry), store:

- `TerrainChunkData`
- `StaticChunkData`
- `ChunkOverlayData`
- version metadata / checksums

### 13.2 Runtime-Only Data (Do Not Persist as Authoritative)

Do not treat these as canonical persisted data:

- greedy mesh output
- navmesh (unless you intentionally cache it)
- collision broadphase caches
- transient replication caches

These are rebuildable/derived.

### 13.3 Static Content IDs

Give frozen statics stable IDs within chunk content:

- chunk-local index is acceptable
- globally stable content IDs are optional if needed later

This is required for overlay operations (hide/replace/promote static X).

---

## 14) Implementation Roadmap (Recommended)

Execution note (2026-03-05):
- keep current network optimization/hardening work active
- build out the client skeleton/foundation before committing to terrain/statics-heavy client implementation
- treat Phase C terrain/statics integration as intentionally deferred until client foundation is in place

## Phase A - Server Spatial Chunking + AOI (Do First)

Goal: move replication cost from "send to almost everyone" toward "send to nearby/interested clients only".

Minimum deliverables:

- `ChunkCoord` (XZ)
- `SpatialChunk`
- `SpatialChunkIndex`
- `SpatialChunkOwner` component for dynamic entities (players first)
- per-connection `ConnectionInterestState`
- AOI interest check wired into replication interest filter
- explicit despawn on AOI leave
- metrics for AOI membership/fanout savings

Do not yet implement full terrain chunk streaming to get this win.

## Phase B - Client Render Chunk Sliding Window

Goal: map chunk subscriptions to client render chunk containers and start pooling.

Deliverables:

- render chunk pool
- local/global chunk mapping
- near/mid/far tier assignment
- basic chunk snapshot consumption (entities first if needed)
- content resolver integration for chunk visuals (no raw engine asset paths in network/core payloads)

## Phase C - Terrain/Statics Chunk Integration

Goal: chunked terrain + frozen static content + overlays become first-class.

Deliverables:

- terrain chunk content storage
- static chunk content records
- server collision/nav integration
- client render build path for chunk statics
- dirty/versioned rebuilds

## Phase D - Fanout and Streaming Optimization

Goal: reduce residual costs after AOI is in place.

Deliverables:

- shared chunk/group delta fanout
- lane-aware chunk batching
- distance/update-rate shaping
- chunk snapshot cache reuse

---

## 15) Testing and Validation Plan

Use existing harnesses and baselines.

### 15.1 Baseline Comparison

Re-run the same socket-ramp profiles from `Docs/NETWORK_BASELINE_2026-02-22.md` after each major change:

- dense/everyone-active
- movement-only 10 Hz
- mixed-activity (40%)

Track:
- max ping-pass CCU
- first ping-fail CCU
- throughput (corr/s, tx/rx)
- server CPU/RSS

### 15.2 New Tests Once AOI Exists

Add workload distributions:

- **Clustered hotspot**
  - many clients in same chunk neighborhood (worst-case AoI)

- **Spread-out population**
  - clients distributed across many chunk regions

- **Mixed**
  - hotspot + distributed population

This quantifies how much chunking helps in realistic vs pathological cases.

### 15.3 Metrics to Add (Chunking-specific)

Server-side:
- active chunks
- entities per chunk (avg/p95/max)
- subscribed chunks per connection (avg/p95/max)
- AOI enter/leave counts per tick
- pre-AOI candidate deltas vs post-AOI selected deltas
- fanout savings ratio
- chunk snapshot bytes sent

Client-side:
- loaded render chunks
- chunk load/unload counts
- chunk snapshot apply time
- render chunk pool hit/miss

---

## 16) Open Questions (Track, Do Not Block Phase A)

1. Exact AOI radius shape
   - square (chunk radius) vs circle filtering
2. Vertical chunking
   - when Y becomes necessary
3. Snapshot granularity
   - full chunk snapshot vs entity-only snapshot first
4. Chunk unload policy
   - active, warm-cache, cold
5. Terrain/build overlay storage format
6. Static content authoring pipeline and content IDs
7. Chunk ECS proxy lifecycle timing (when to introduce)

None of these block Phase A spatial chunking for AOI.

---

## 17) Practical Rules (Quick Reference)

- Implement **server spatial chunking + AOI first**.
- Keep chunking split into **spatial / terrain / render** layers.
- Use **chunk coordinates** as canonical chunk identity.
- Frozen statics are **chunk content records**, not default ECS entities.
- Promote statics to ECS only when runtime state/logic is needed.
- Chunk ECS entities are **runtime proxies**, not authoritative chunk identity.
- Avoid global chunk scans in hot paths.
- Re-run existing socket-ramp baselines after each milestone.

---

## 18) References / Related Docs

- `Docs/NETWORK_BASELINE_2026-02-22.md`
- `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
- `Docs/game_design_summary.md`
- `Docs/time_traversal_npc.md`
- `NETWORK_PRD.md`
- `NETWORK_IMPLEMENTATION_PLAN.md`

Historical reference (old implementation ideas):
- `_OLD/Server/ECS/Chunk/SimplifiedChunkManager.cs`
- `_OLD/Server/ECS/Chunk/SpatialChunk.cs`
- `_OLD/Client/ECS/Systems/RenderChunkManager.cs`
- `_OLD/Client/ECS/Components/RenderChunk.cs`
- `_OLD/Server/ECS/Components/Terrain/TerrainChunkComponent.cs`
