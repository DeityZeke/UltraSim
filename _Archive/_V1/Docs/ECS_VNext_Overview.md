# ECS vNext Overview (Design Brief)

This document consolidates the agreed vNext ECS goals and core concepts. It is a
compact overview with a bit of detail, plus a checklist of optional improvements.
It also links and aligns with:

- `Docs/ECS_Layered_Architecture.md`
- `Docs/Tick_Based_Timing_Cadence_Guide.md`

---

## 1) Core Principles

- ECS is engine-agnostic and integrated via host injection.
- ECS is the source of truth; caches are derived and rebuildable.
- Structural changes are deferred via queues and applied at barriers.
- Component data mutation is direct only when single-threaded or scheduler-safe.
- Avoid boxing and unnecessary GC in hot paths.
- Server components are `unmanaged`. Client components prefer `unmanaged` and avoid engine refs.

---

## 2) Compact Overview (Core Concepts)

### Entity (handle only)
- Struct handle: `uint index`, `uint version`, `ulong packed`.
- Immutable handle in practice (readonly struct).
- Create: always in an archetype (never floating).
- Destroy: remove from archetype, bump version, push index to free list.
- IsAlive: index in range + version match + lookup valid.

Invariant: if `IsAlive` is true, lookup must point to the correct archetype+slot.

### Archetype (SoA storage)
- Dense `Entity[]` + per-component `T[]` (SoA).
- Only entities with the exact signature live here.
- `AddEntity`: append entity + default component rows.
- `RemoveAtSwap`: keep arrays dense, update lookup for swapped entity.

Invariant: all component arrays have the same length as entity array.

### Component (data-only)
- `struct`, no boxing, ideally `unmanaged`.
- Prefer readonly fields and overwrite semantics.
- Optional static abstract `Serialize/Deserialize` via a generic interface for type-local IO:
  - `IComponent<TSelf> where TSelf : unmanaged, IComponent<TSelf>`
  - `static abstract void Serialize(ref Writer, in TSelf value)`
  - `static abstract TSelf Deserialize(ref Reader, ushort version)`
- IDs can be recomputed for fresh start; persistence/load uses stable type identity + mapping.

### Archetype Signature
- Signature is a set of component type IDs stored as a bitset (`ulong[]`).
- Used for archetype lookup and query matching.
- Created from component IDs (not entity IDs).

### Queues (bulk processing)
- All structural changes use queues:
  - create/destroy entity
  - add/remove component
- Queues enable batch processing from 1 to 1M+ entities.
- Process order is deterministic (e.g., destroy → create → remove → add).

### Systems & Tick
- Systems mutate component data directly only when single-threaded or scheduler-safe.
- Structural changes always deferred and applied at a barrier.
- Optional post-systems hook can publish dirty batches/metrics.

### Dirty Tracking (optional but useful)
- Track dirty entities per component/system.
- Events carry entity IDs only; consumers pull current data from ECS.

---

## 3) Host Injection (Layered Architecture Alignment)

See `Docs/ECS_Layered_Architecture.md`.

Key rule: ECS defines the host interface; engines implement it.

Typical host surface:

- Runtime/context/environment
- Logging sink
- IO profile for persistence
- Root handle (optional)

---

## 4) Tick Timing & Scheduling

See `Docs/Tick_Based_Timing_Cadence_Guide.md`.

Key points:
- Fixed-step tick with accumulator and spiral-of-death guard.
- Multi-rate system cadence using tick periods.
- Phase staggering for load smoothing.
- Striping/amortization for massive populations.
- Deterministic per-entity timers with jitter for cascade prevention.
- Systems are scheduled by `worldTick`, not wall-clock time.

---

## 5) vNext Changes (Agreed / Candidates)

### Strongly-typed ComponentId (candidate)
- Add `readonly struct ComponentId` for API clarity and type safety.
- Use raw `int` internally for hot paths and bitsets.

### ArchetypeSignature naming
- Keep `ArchetypeSignature` as the canonical type name.
- Document that it is created from component IDs.

### Access safety in parallel execution
- If systems run in parallel, enforce declared read/write sets in scheduler.
- Direct component writes are safe only under scheduler guarantees.

### Optional future improvements (only if profiling demands)
- Chunked storage inside each archetype (SoA chunks).
- Component change/version tracking to reduce full scans.
- Query caching improvements tied to archetype creation.

---

## 6) Invariants Checklist

- Lookup always matches actual archetype + slot for alive entities.
- Archetype component arrays are aligned in length.
- Each unique signature maps to exactly one archetype instance.
- Signature matches the actual component set.
- Structural changes are applied only at barriers.
- Engine integration stays outside Module.ECS.

---

## 7) Decisions and Remaining Questions

- Component ID stabilization: **not required for fresh start**; only needed for persistence/load.
- Storage strategy:
  - Default to **List<T> SoA** for vNext.
  - Prefer **stable archetypes** with data-state flags to reduce structural churn.
  - Revisit **chunked storage** only if profiling shows List<T> growth or cache pressure.

## 8) Notes on Continuity

- Keep the parallel scheduler and most of the existing architecture.
- Focus changes on stability, API conformity, simplicity, and efficiency.

## 9) Risks and Mitigations

- Archetype churn: frequent add/remove causes migration cost.
  - Prefer stable archetypes with data-state flags where feasible.
- Too many tiny archetypes: fragmentation hurts cache and query costs.
  - Group similar entities; use flags instead of structural splits.
- Parallel contention: overlapping write sets reduce parallelism.
  - Keep write sets narrow; split systems if needed.

## 10) Spatial World Model (MMO context)

- Entities carry spatial components (Position, ChunkCoord, RegionId).
- Spatial indexing is derived from ECS data, not the source of truth.
- Movement updates spatial structures at a defined cadence (e.g., 30ms).
