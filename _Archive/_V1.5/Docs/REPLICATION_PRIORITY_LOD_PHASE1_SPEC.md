# Replication Priority LOD (Phase 1) - Working Spec

Purpose: define a minimal server-side replication LOD layer that runs on top of AOI/chunk filtering and reduces update frequency for lower-priority visible entities without changing client render LOD yet.

## Implementation Status

- [x] Phase 1: `PassesPriorityLodGate()` — ring-distance cadence check (phase-based, no per-entity state map needed)
- [x] Phase 1: Ring 0 (same chunk, every tick) / Ring 1 (1–3 chunks, every 2 ticks) / Ring 2 (4+ chunks, every 3 ticks)
- [x] Phase 1: Despawn bypass — gate only applies in recipient selector; despawn path is separate
- [x] Phase 1: LOD telemetry counters (`lod_checks`, `lod_sent`, `lod_gated`, `lod_near`, `lod_ring1`, `lod_ring2`)
- [x] Phase 1: Enabled by default (`EnablePlayerPriorityLod = true`)
- [ ] Phase 2: 5-tier visual band cadence (Immediate/Near/Mid/Far/Very Far)
- [ ] Phase 2: Per-tier cadence options in `ServerRuntimeOptions`
- [ ] Phase 2: Fairness/aging promotion for long-deferred entities
- [ ] Phase 2: Payload detail LOD (coarser codec per tier)
- [ ] Phase 2: Semantic overrides (self, combat target, recently spawned)

## Goals

- Improve hotspot/dense-chunk behavior (current hotspot baseline: `300` pass / `400` fail) without reducing AOI visibility.
- Preserve responsiveness for nearby/self entities.
- Keep implementation compatible with the current `NetworkReplicationPipeline` interest-filter callback.
- Add telemetry so gains and regressions are measurable.

## Non-Goals (Phase 1)

- No protocol changes.
- No schema/payload compaction.
- No client render LOD changes.
- No frustum-based replication decisions.
- No per-entity semantic scoring beyond distance/ring.

## Conceptual Model

AOI answers:
- "Should this connection receive updates for this entity at all?"

Priority LOD answers:
- "How often should this connection receive updates for this visible entity?"

Phase 1 applies a cadence gate after AOI visibility:
- visible entities remain visible
- lower-priority entities are sent less frequently

## Phase 1 Lane Model (Minimal)

Server-side lanes are based on **chunk-ring distance** from the recipient's center chunk:

- `Near` (ring `0`, same chunk as recipient center)
  - cadence: every tick (`1`)
- `Outer` (ring `>=1`, still inside AOI)
  - cadence: configurable (`2+` ticks)

This is intentionally simple because current server AOI radius is small (`radius=1` by default).

## Initial Rules

- Apply only after AOI interest passes.
- If entity chunk cannot be resolved, do not gate (send).
- Cadence gate is per `(connection, entity)`.
- Spawn/enter-AOI behavior remains immediate:
  - AOI enter already marks entity dirty and should bypass starvation issues naturally for moving entities.
- Despawns are never cadence-gated.

## Data Needed (Per Connection)

In the connection AOI state:
- `lastSentTickByEntity` map (network entity id -> last sent replication tick)

Lifecycle:
- remove entries on AOI leave
- clear on disconnect

## Telemetry (Phase 1)

Add debug telemetry counters to evaluate behavior:

- `lod_checks`
- `lod_sent`
- `lod_gated`
- `lod_near_sent`
- `lod_outer_sent`
- `lod_outer_cadence`

These should be reported alongside existing AOI metrics in debug server telemetry.

## Validation Plan

1. Functional correctness
   - No despawn regressions
   - No ghost entities
   - No client disconnect regressions

2. Performance checks
   - Compare hotspot profile before/after enabling LOD
   - Compare spread/mixed profile to ensure no unexpected regression

3. Telemetry checks
   - `lod_gated > 0` in dense scenarios
   - `lod_near_sent` remains dominant for local/self activity

## Expected Effect

- Largest benefit in hotspot/dense-chunk cases where many entities are visible to the same recipients.
- Modest or negligible effect in already-sparse AoI scenarios (where AOI has already cut most fanout).

## Phase 2 — Multi-Tier Visual Band LOD

### Design Intent

Replace the binary Near/Outer model with a 5-tier visual band system keyed on chunk-ring distance.
Each tier defines an update cadence target and a content scope — what entity types are relevant at that range.

Server tick rate: **16ms**. Cadences are specified in ticks; ms values are targets (not enforced to the ms).

### Tier Table (at 16ms tick rate)

| Tier | Chunk Ring Radius | Target Update | Cadence (ticks) | Approx Rate |
|------|:-----------------:|:-------------:|:---------------:|:-----------:|
| Immediate | 0 (same chunk) | 16ms | 1 | 62.5 Hz |
| Near | ±1–3 chunks | ~20ms | 2 | ~31 Hz |
| Mid | ±4–6 chunks | ~30ms | 2–3 | ~21–31 Hz |
| Far | ±7–9 chunks | ~40ms | 3 | ~21 Hz |
| Very Far | ±10–12 chunks | ~50ms | 4 | ~16 Hz |

> Exact chunk-radius boundaries will be tuned based on server density and CCU targets.
> At AoI radius=1 (3×3 chunk window), only Immediate + Near tiers are relevant today.
> Mid/Far/Very Far become active once AoI radius is expanded for open-world content.

### Content Scope Per Tier

| Tier | Content Types |
|------|--------------|
| Immediate | Full fidelity: position, rotation, animation state, effects, HP/stats, chat |
| Near | All entity types: players, NPCs, projectiles, interactables — high fidelity |
| Mid | Players, large NPCs, projectiles; drop minor visual effects; coarser position |
| Far | Players + large NPCs only; no projectile replication; reduce stat detail |
| Very Far | Players only; minimal state (position + orientation); no combat events |

### Per-Entity Semantic Overrides

Some entities ignore tier cadence gating regardless of distance:
- **Self** (the connection's own player entity): always Immediate
- **Combat target**: upgrade to Immediate or Near while in-combat with the receiver
- **Recently spawned**: force one immediate update on AOI enter (already handled by force-publish)
- **Projectiles / fast-movers**: may need sub-16ms extrapolation on client; clip to Near/Mid only

### Implementation Notes

- Implemented as a 5-way switch on `chunkRingDistance` in `PassesPriorityLodGate()`, replacing the current binary Near/Outer check
- Per-connection `lastSentTickByEntity` map already exists (Phase 1); no new per-entity state needed
- Cadence values exposed as `ServerRuntimeOptions` parameters for tuning:
  - `PlayerPriorityLodNearCadenceTicks` (currently used for Outer)
  - Add: `PlayerPriorityLodMidCadenceTicks`, `PlayerPriorityLodFarCadenceTicks`, `PlayerPriorityLodVeryFarCadenceTicks`
- Chunk-ring radius boundaries exposed as options: `PlayerPriorityLodNearMaxRing`, etc.
- Content-scope filtering (per tier, per entity type) is a separate feature built on top of cadence gating

### Interaction With Dense Observer Cadence

Dense observer cadence is a load-based throttle (fires when chunk subscriber count ≥ threshold).
Multi-tier LOD is a distance-based throttle. They are composable:
- LOD gate applies first (distance check)
- Dense cadence gate applies second (load check)
- A connection that passes both gates gets the update; failing either gate defers it

### Chunk & Tile Size Reference

- Tile size: **0.5m × 0.5m** (`WorldUnitsPerGrid = 0.5f`)
- Chunk size: **32 tiles × 0.5m/tile = 16m × 16m** (`PlayerChunkSizeGridUnits = 32`)
- AoI radius=1 → 3×3 chunk window → 48m × 48m visible area
- AoI radius=3 → 7×7 chunk window → 112m × 112m visible area (expected for Mid tier)

### Phase 2 Additional Items

- Fairness/aging promotion: long-deferred entities get one forced send after N*cadence ticks with no update
- Payload detail LOD: coarser codec selection (fewer bits per field) for Far/Very Far tiers
- Distance + relevance scoring: combat/target/ownership can promote entity to a closer tier temporarily

