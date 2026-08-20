# ECS Systems & SystemManager vNext -- Outline

This document defines the vNext system architecture and the SystemManager contract.
It is aligned with the PRD and the current ECS core (archetypes, component registry).
Focus: deterministic scheduling, zero per-tick allocations, and minimal runtime overhead.

---

## 1. Goals (hard requirements)

- **Hot path is allocation-free** (no `Type`, no LINQ, no boxing).
- **Read/Write sets are precomputed** (bitsets or `ComponentId` arrays).
- **Stage order is a hard barrier** (no cross-stage parallelism).
- **Structural changes are always queued** (outside of system read/write sets).
- **Settings are cold-path** (load once, optional runtime overrides with explicit rebuild).
- **Optional instrumentation** is compile-time gated and runtime-toggled; branch once per phase/system, not per entity.

---

## 2. Core Concepts

### 2.1 SystemDescriptor (static intent)

Immutable metadata that defines how a system should run.
This is compile-time intent and **must not change at runtime**.

Fields (recommended):

- `string Name`
- `SystemStage Stage`
- `int DefaultPeriodTicks`
- `int DefaultStartOffset`
- `ComponentMask Read` (bitset or sorted `ComponentId[]`)
- `ComponentMask Write` (bitset or sorted `ComponentId[]`)
- `int[] Requires` (system IDs, resolved at registration)
- `bool Manual` (optional)

### 2.2 SystemSchedule (runtime effective)

Derived from descriptor + settings (or explicit overrides). Safe to mutate.

Fields:

- `bool Enabled`
- `int PeriodTicks`
- `int StartOffset`
- `SystemStage Stage`
- `int Priority` (optional, within-stage ordering)

### 2.3 SystemId

Stable ID assigned at registration. Used for all indexing and dependency graphs.

```
public readonly struct SystemId : IEquatable<SystemId>
{
 public readonly int Value;
 public SystemId(int value) => Value = value;
 public bool Equals(SystemId other) => Value == other.Value;
 public override int GetHashCode() => Value;
}
```

### 2.4 ComponentMask (shared bitset)

Reusable bitset type for read/write sets and archetype signatures.

```
public readonly struct ComponentMask
{
 private readonly ulong[] _bits;

 public bool Intersects(in ComponentMask other) { /* ... */ }
 public bool IsSupersetOf(in ComponentMask other) { /* ... */ }
}
```

`ArchetypeSignature` can wrap this internally so conflict checks use the same logic.

### 2.5 SystemContext (hot-path input)

Lightweight context passed to `Update`.

```
public readonly ref struct SystemContext
{
 public readonly World World;
 public readonly uint Tick;
 public readonly float DeltaSeconds;
 public readonly IStructuralCommands Commands;
}
```

### 2.6 Due-ness Helper

Single, explicit helper used by the scheduler (inline in hot path).

```
public static class SystemScheduleUtil
{
 [MethodImpl(MethodImplOptions.AggressiveInlining)]
 public static bool IsDue(uint worldTick, int periodTicks, int startOffset)
 {
 if (periodTicks <= 0)
 return false;

 uint offset = (uint)startOffset;
 return (worldTick - offset) % (uint)periodTicks == 0;
 }
}
```

### 2.7 SystemBase (shared behavior)

Use a base class for shared lifecycle, timing, and settings glue.
Interfaces are optional for metadata enforcement.

Recommended shape:

```
public abstract class SystemBase
{
 public abstract SystemDescriptor Descriptor { get; }
 public virtual SystemSettings? Settings => null;

 public bool Enabled { get; internal set; } = true;

 public virtual void OnInitialize(World world) { }
 public abstract void Update(in SystemContext ctx);
 public virtual void OnShutdown(World world) { }
}
```

Optional: `static abstract SystemDescriptor Descriptor { get; }` on a generic base class
if you want compile-time enforcement.

---

## 3. Conflict Rules (explicit)

Define conflicts by component access:

- `Write intersects Write != empty` -> conflict
- `Write intersects Read != empty` -> conflict
- `Read intersects Read` -> OK

Structural changes are always queued and **not** part of read/write sets.

---

## 4. Scheduler Semantics

- **At most one run per system per tick.**
- **Due-ness is evaluated against `worldTick`.**
- **Stage order is strict**: systems in later stages never run before earlier stages.
- **Parallelism only within a stage**, and only for non-conflicting systems.
- **Not all systems run simultaneously**: only systems in the same conflict-free batch can run concurrently.
- **Due-ness formula**: `IsDue(worldTick, period, offset)` is evaluated per tick.
- **Structural barriers**: pre-barrier flushes external enqueues; post-barrier flushes system enqueues.

---

## 5. Settings and Rebuild Levels

Settings are cold-path. Changes trigger explicit rebuilds:

1) **Enabled flag only** -> no rebuild (flip flag).
2) **Period/Offset/Stage** -> rebuild schedule buckets (or targeted stage rebuild).
3) **Read/Write sets** -> full rebuild + re-batch (should be disallowed at runtime).

Settings should not mutate the descriptor; they only affect the schedule.

---

## 6. SystemManager Responsibilities

### 6.1 Registration

- Register systems once at startup (preferred).
- Resolve dependencies via `Requires[]` (system IDs, not `Type`).
- Build read/write conflict data once.
- Topologically sort per-stage systems based on `Requires[]`.

### 6.2 Scheduling

- Maintain per-stage system lists.
- Maintain due buckets by `(PeriodTicks, StartOffset)` or precomputed run tables.
- Zero allocation per tick: all lists are preallocated.
- Manual systems live in a separate list and are not part of stage scheduling.
- Optional phase staggering is implemented at schedule rebuild time (`EnablePhaseStaggering`, `MaxNonCriticalStagger`).
- Critical stages (Movement, Combat) remain unstaggered; non-critical stages can be spread by period.

### 6.3 Execution

Per tick:

1) Build list of due systems for each stage.
2) For each stage:
 - Build conflict-free batches (cached where possible).
 - Execute batches using configured backend:
 - `Serial` (default)
 - `ParallelBatches` (persistent worker pool; systems in same non-conflicting batch run concurrently)
3) During system execution, world enables local-capture windows:
 - structural commands can enqueue into worker-local buffers,
 - dirty marks can write to serial/worker-local trackers.
4) End-of-systems merge happens before post-structural barrier apply.

### 6.4 Legacy reference (_OLD) and vNext adaptation

The _OLD scheduler had several useful patterns worth carrying forward:

- Deferred system operation queues (`register/unregister/enable/disable`) applied at safe points.
- Tick buckets by cadence (`TickRate -> List<System>`) with cached next-run timestamps.
- Conflict batches executed in parallel by a dedicated worker pool (`ManualWorkerPool`).
- Manual systems isolated from scheduled systems.
- Cached batch sets for repeated cadence groups.

vNext keeps descriptor/schedule semantics and now adopts these parts in a constrained way:

- Keep strict stage barriers and deterministic ordering.
- Add an execution strategy per stage or per system group:
 - `Serial` (implemented)
 - `ParallelBatches` (implemented via persistent worker pool)
 - `JobifiedSystem` (single system parallelizes its own entity/chunk work)
- Keep all conflict math precomputed; runtime should only do due checks and dispatch.
- Use a persistent worker pool interface (no `Task.Run` churn) for "hose the CPU" mode.
- Keep queue processing explicit and bounded (no hidden registration churn in hot loops).

Current status:

- `Serial` and `ParallelBatches` backends are implemented.
- Stage/dependency/conflict batch precomputation is implemented.
- Worker-local structural + dirty capture merge is implemented.
- Jobified per-system chunk backend remains optional future work.

---

## 7. Data Structures (suggested)

- `List<SystemBase>` for all systems (registration order).
- `SystemAccess[]` (cached read/write masks).
- `SystemSchedule[]` (runtime effective schedule).
- `StageBuckets[stage] -> Span<SystemId>` (precomputed order by stage)
- `DueBuffers[stage] -> Span<SystemId>` (reused each tick)
- `BatchCache[stage] -> List<List<SystemId>>` (precomputed conflict batches)

Prefer `SystemId` indexing everywhere for cache locality.

Example storage layout:

```
private SystemBase[] _systems;
private ComponentMask[] _readMasks;
private ComponentMask[] _writeMasks;
private SystemSchedule[] _schedules;
private SystemId[][] _stageSystems;
private SystemId[][] _stageBatches;
```

Prefer `int systemId` indexing everywhere for cache locality.

---

## 8. Minimal APIs

### 8.1 Registration

```
void Register(SystemBase system);
void RegisterAll(params SystemBase[] systems);
```

### 8.2 Lifecycle

```
void Initialize(World world);
void Shutdown(World world);
```

### 8.3 Tick

```
void Tick(World world, uint worldTick, float deltaSeconds);
```

### 8.4 Settings

```
void ApplySettings(SystemBase system); // sets schedule fields
void RebuildSchedules(); // explicit rebuild when needed
```

---

## 9. Integration Points

- **World** owns `SystemManager` and calls `Tick`.
- **SystemManager access is world-mediated**: registration/tick/manual-run and diagnostics route through world APIs.
- **Structural queues** are applied at explicit barriers (outside system update loops).
- **Public world structural APIs are deferred-only** (`CreateDefault`, `Instantiate*`, `Destroy`, `Add/Remove` enqueue and apply at barriers).
- **System structural writes** go through `SystemContext.Commands` (`IStructuralCommands`) and are capture-merged at barrier end.
- **Dirty tracking** supports world APIs for single, batch, and trusted-unique marks; capture mode is enabled during system tick.
- **Component registry** is sealed before systems register (read/write sets are stable).
- **Host injection** is through `IHost` (environment + logging), with `NoOpHost` default.
- **Barrier events** flow through `IWorldEventSink`:
 - barrier summary each pre/post flush,
 - optional created-entity batch per barrier.
- **Godot integration harness** (kept in `_tmp`) uses staged bootstrap + host/event sink injection, and runs macro visual benchmarks via:
 - `ECSVisualBenchmarkNode` (scenario orchestration),
 - `ECSMultiMeshBridge` (ECS -> `MultiMeshInstance3D` transform sync).
- **World lifecycle state** gates scheduler mutation:
 - `Created`: registration/config allowed
 - `Sealed`: registries and system layout frozen
 - `Initialized`: systems have run `OnInitialize`
 - `Running`: `Tick` active
 - `Shutdown`: no further enqueue/tick allowed

---

## 10. Non-Goals (for vNext)

- Dynamic component read/write mutation at runtime.
- Runtime reflection for dependency discovery.
- Per-system dynamic query caching (use direct archetype queries each update).

---

## 11. Summary

vNext systems should be:

- **Descriptor-driven** (static intent) + **Schedule-driven** (runtime effective).
- **Allocation-free** on tick.
- **Deterministic** in stage order and cadence.
- **Safe for parallelism** via explicit read/write sets.
- **Settings-aware** without contaminating hot paths.
- **Manual systems isolated** from scheduled stages.
- **Networking-ready** via deterministic stage execution, dirty drains, and barrier-flushed structural writes.
