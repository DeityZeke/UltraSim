# ECS vNext -- Product Requirements Document

A concise specification for the next-generation ECS architecture, covering core design, host integration, tick-based timing, and scalability patterns.

---

## 1. Core Principles

- **Engine-agnostic**: ECS defines host interfaces; engines implement them.
- **Source of truth**: ECS owns authoritative state; caches are derived and rebuildable.
- **Deferred structural changes**: Create/destroy/add/remove queued and applied at barriers.
- **Direct mutation**: Component data mutated directly only when single-threaded or when the scheduler enforces non-overlapping read/write sets. Unsafe otherwise.
- **Zero-allocation hot paths**: No boxing, minimal GC pressure in tick loops.
- **Sealed registries**: Component registration happens at startup and is sealed for lock-free reads.
- **Unmanaged components**: Server components are `unmanaged`. Client components prefer `unmanaged` and avoid engine references.
- **Optional instrumentation**: Stats/debug hooks are compile-time gated and runtime-toggled; no hot-path overhead when disabled.

**Implementation Notes (current code)**:
- The component registry/manager is sealed after registration (`ComponentRegistry.Seal`, `ComponentManager.Seal`) to enable lock-free reads.

### 1.1 Statistics Contract

ECS observability is a first-class requirement, with low-overhead counters/timers across all hot subsystems.

Required coverage:

- World/tick:
  - tick count, tick duration, system stage duration, structural barrier duration.
- Structural queue:
  - enqueued/applied create/spawn/destroy/add/remove counts, batch sizes, overflow/defer events.
- Entity/archetype:
  - live entities, archetype count, migration counts, reserve/grow counts.
- Systems/scheduler:
  - systems executed, execution mode (serial/parallel), worker utilization, stage barrier timings.
- Dirty tracking:
  - marks, dedupe hits, drained counts, dropped/deferred counts (if budgeted drains are used).

Operational requirements:

- Stats are runtime-toggleable; disabled mode keeps hot paths minimal.
- Counters used from concurrent paths must be thread-safe.
- Provide both cumulative totals and rolling windows (for example p50/p95) for profiling and soak runs.

---

## 2. Core Concepts

### 2.1 Entity

Struct handle only--no data storage.

```csharp
public readonly struct Entity : IEquatable<Entity>
{
 public readonly uint Index;
 public readonly uint Version;
 public readonly ulong Packed; // (Version << 32) | Index
}
```

- **Create**: Always into an archetype (never floating).
- **Destroy**: Remove from archetype, bump version, push index to free list.
- **IsAlive**: Index in range + version match + lookup valid.
- **IsValid**: `Version != 0` (zero is reserved for `Entity.Null`).

**Invariant**: If `IsAlive` returns true, lookup points to correct archetype + slot. `RemoveAtSwap` returns the moved entity so `EntityManager/World` can update the lookup.

**Implementation Notes (current code)**:
- Implemented in `ECS/Entity/Entity.cs` with `Packed` value, `IsValid()` (`Version != 0`), and `Entity.Null`.
- `IsAlive` and lookup validation are implemented in `ECS/Entity/EntityManager.cs`.
- `EntityManager` currently owns free-list allocation, versioning, and entity location mapping.

---

### 2.2 Archetype

Dense SoA storage for entities sharing the same component signature.

```
+-------------------------------------------------------------+
| Archetype (Signature: Position, Velocity) |
+-------------------------------------------------------------+
| Entity[] [E0] [E1] [E2] [E3] |
| Position[] [P0] [P1] [P2] [P3] |
| Velocity[] [V0] [V1] [V2] [V3] |
+-------------------------------------------------------------+
```

- **AddEntity**: Append entity + default component rows.
- **RemoveAtSwap**: Swap last into removed slot, update lookup for swapped entity.

**Invariant**: All component arrays have same length as entity array.

**Implementation note**: Storage is raw arrays (`Entity[]`, `T[]`) with manual growth and `Span<T>` access. Column lookup is O(1) via `ComponentId.Value -> column index` array.

**Implementation Notes (current code)**:
- Implemented in `ECS/Archetype/Archetype.cs` with raw arrays and `Span<T>` accessors.
- Columns are initialized via `Archetype.InitializeColumns` (called by `ArchetypeManager`).
- Bulk APIs exist for load/persistence paths: `Archetype.Reserve(int)` and `Archetype.AddEntityUninitialized(Entity)`.

---

### 2.3 Component (Option B -- constraint-first design)

Data-only structs registered with runtime-assigned type IDs (stable identity only required for persistence/network).

```csharp
// Constraint-only marker for ECS components -- never stored as an interface.
public interface IComponent<TSelf>
 where TSelf : unmanaged, IComponent<TSelf>
{ }

// Optional serialization support -- inherits base constraint
public interface ISerializableComponent<TSelf> : IComponent<TSelf>
 where TSelf : unmanaged, ISerializableComponent<TSelf>
{
 static abstract void Serialize(ref BitWriter writer, in TSelf value);
 static abstract TSelf Deserialize(ref BitReader reader, ushort version);
}

// Example: Position is serializable
public struct Position : ISerializableComponent<Position>
{
 public float X, Y, Z;

 public static void Serialize(ref BitWriter writer, in Position p)
 {
 writer.WriteSingle(p.X);
 writer.WriteSingle(p.Y);
 writer.WriteSingle(p.Z);
 }

 public static Position Deserialize(ref BitReader reader, ushort version)
 {
 return new Position
 {
 X = reader.ReadSingle(),
 Y = reader.ReadSingle(),
 Z = reader.ReadSingle()
 };
 }
}

// Example: Velocity is a component but not serialized (transient)
public struct Velocity : IComponent<Velocity>
{
 public float X, Y, Z;
}
```

**Design decisions**:

- `IComponent<TSelf>` enforces `unmanaged` via generic constraint -- no boxing is possible **unless a component is stored as an interface**, which is forbidden.
- `ISerializableComponent<TSelf>` is opt-in for components that need persistence/networking.
- Self-referential generic pattern enables static abstract methods without boxing.

**Interface usage rule (critical):**

- `IComponent<TSelf>` and `ISerializableComponent<TSelf>` are used **only as generic constraints**.
- **Never store** components as `IComponent<...>` or `ISerializableComponent<...>` variables, collections, or fields.
- All storage and iteration remains over concrete unmanaged `T` (e.g., `T[]`, `Span<T>`, SoA columns).

Guidelines:

- All **ECS-stored components** must implement `IComponent<TSelf>`.
- Only persistent/networked components implement `ISerializableComponent<TSelf>`.
- Prefer readonly fields with overwrite semantics.
- IDs can be recomputed at startup for fresh worlds; persistence uses stable type identity with mapping on load.

**Implementation Notes (current code)**:
- Implemented in `ECS/Component/IComponent.cs` and `ECS/Component/ISerializableComponent.cs` with generic constraints only.
- No `IComponent` marker interface; components are not stored as interface values.

---

### 2.4 ComponentId and ArchetypeSignature

**Core types**:

- `Entity` -- handle to an entity instance (index + version)
- `ComponentId` -- strongly-typed ID for a component type
- `ArchetypeSignature` -- bitset of component types (identifies an archetype)

#### ComponentId (strongly-typed)

```csharp
// Prevents accidentally mixing raw ints with component IDs
public readonly struct ComponentId : IEquatable<ComponentId>
{
 public readonly int Value;

 public ComponentId(int value) => Value = value;

 public bool Equals(ComponentId other) => Value == other.Value;
 public override bool Equals(object? obj) => obj is ComponentId id && Equals(id);
 public override int GetHashCode() => Value;

 public static bool operator ==(ComponentId left, ComponentId right) => left.Value == right.Value;
 public static bool operator !=(ComponentId left, ComponentId right) => left.Value != right.Value;

 public static readonly ComponentId Invalid = new(-1);
}
```

#### ArchetypeSignature (class, interned)

```csharp
// Class because:
// - Contains ulong[] (heap-allocated anyway)
// - Can intern for reference equality fast path
// - Can cache hash once
// - No accidental value-copy semantics
public sealed class ArchetypeSignature : IEquatable<ArchetypeSignature>
{
 private readonly ulong[] _bits;
 private readonly int _hashCode; // Cached at construction

 public ArchetypeSignature(ReadOnlySpan<ComponentId> componentIds)
 {
 int maxId = 0;
 foreach (var id in componentIds)
 maxId = Math.Max(maxId, id.Value);

 _bits = new ulong[(maxId >> 6) + 1];
 foreach (var id in componentIds)
 _bits[id.Value >> 6] |= 1UL << (id.Value & 63);

 _hashCode = ComputeHash();
 }

 public bool Has(ComponentId componentId) =>
 componentId.Value < (_bits.Length << 6) &&
 (_bits[componentId.Value >> 6] & (1UL << (componentId.Value & 63))) != 0;

 public bool IsSupersetOf(ArchetypeSignature other) { /* ... */ }

 public bool Equals(ArchetypeSignature? other) =>
 other is not null && _bits.AsSpan().SequenceEqual(other._bits);

 public override int GetHashCode() => _hashCode;
}
```

**Usage**:

- `ComponentId` identifies a component type; obtained from `ComponentRegistry.GetId<T>()`.
- `ArchetypeSignature` identifies an archetype; used for lookups and queries.
- Signatures are **interned** in `ArchetypeManager` for reference equality fast path.
- Archetypes are accessed by signature, not by numeric index.
- Signature inputs are normalized (sorted + de-duplicated) before creation; small lists use stackalloc, larger use ArrayPool.

**Invariants**:

- Each unique signature maps to exactly one archetype instance.
- Array size is based on the highest registered component type ID.
- Either register all component types at startup, or support resize when new types are registered.
- Signatures are immutable once created.

**Implementation Notes (current code)**:
- `ComponentId` is implemented in `ECS/Component/ComponentId.cs`.
- `ArchetypeSignature` lives in `ECS/Archetype/ArchetypeSignature.cs` with cached hash and `HasAll/HasAny`.
- `ArchetypeManager` normalizes component ID lists and builds signatures from a precomputed bitset.
- Component registration + column factories are in `ECS/Component/ComponentRegistry.cs` and `ECS/Component/ComponentManager.cs` (array-backed, sealed snapshots).

---

### 2.5 Structural Queues

All structural changes are queued and applied at barriers.

```csharp
public sealed class StructuralQueue
{
 // ArrayPool-backed queue buffers, write/read swapped at barrier apply.
 private readonly QueueBuffer<ArchetypeSignature> _createDefaultsWrite;
 private readonly QueueBuffer<ArchetypeSignature> _createDefaultsRead;
 private readonly QueueBuffer<PrefabSpawnRequest> _spawnsWrite;
 private readonly QueueBuffer<PrefabSpawnRequest> _spawnsRead;
 private readonly QueueBuffer<Entity> _destroysWrite;
 private readonly QueueBuffer<Entity> _destroysRead;

 public void Apply(
 World world,
 out int createsApplied,
 out int spawnsApplied,
 out int destroysApplied,
 out int removesApplied,
 out int addsApplied)
 {
 // Order: Destroy -> Spawn -> CreateDefault -> Remove -> Add
 // 1. Destroy first: avoids use-after-free and stale lookups
 // 2. Spawn second: prefab values are applied immediately after slot creation (within barrier apply)
 // 3. CreateDefault next: default rows for tools/tests or non-prefab use
 // 4. Remove before Add: allows remove+add on same entity deterministically
 }
}
```

**Semantics**:

- `CreateDefault` allocates an entity + archetype row and writes default component values during barrier apply.
- `Spawn` allocates an entity + archetype row and invokes a prefab apply delegate during barrier apply.
- `Destroy` removes the row via swap-remove and returns the entity to the pool.
- `Remove` migrates an entity to a new archetype without the component.
- `Add` migrates an entity to a new archetype with the component (or overwrites in-place if already present).
- Queue processing is deterministic by category and stable archetype grouping: per-archetype request order is preserved.
- Enqueue/apply uses a swap-buffer snapshot: lock scope is short, heavy apply runs outside the queue lock.
- In parallel system mode, worker-local structural capture buffers can be merged at barrier end.

**Invariant**: Structural changes only occur at explicit barriers, never mid-iteration.

**Implementation Notes (current code)**:
- Implemented in `ECS/StructuralQueue.cs` with `EnqueueCreateDefault`, `EnqueueSpawn`, `EnqueueDestroy`, `EnqueueAdd<T>`, and `EnqueueRemove<T>`.
- Spawn queues are archetype-grouped, reserve upfront, and support template-copy runs (`CopySlot`) for repeated prefab entries.
- Public world create/spawn APIs are deferred-only and enqueue structural work:
 - `World.CreateDefault(signature)`
 - `World.Instantiate<TPrefab>()`
 - `World.InstantiateTemplate<TPrefab>()`
 - `World.Instantiate(PrefabId)` / `World.Instantiate(string key)`
 - `World.Instantiate(signature, PrefabApply)`
- No public immediate structural mutation APIs are exposed; structural mutation entrypoints are queue-backed.

**World Structural API Contract (current code)**:

| API | Category | Behavior | Notes |
| --- | --- | --- | --- |
| `World.CreateDefault(ArchetypeSignature)` | Public | Deferred | Enqueues default-row creation. |
| `World.Instantiate<TPrefab>()` | Public | Deferred | Enqueues spawn using prefab apply delegate. |
| `World.InstantiateTemplate<TPrefab>()` | Public | Deferred | Explicit template-prefab enqueue path (`ITemplatePrefab<T>`). |
| `World.Instantiate(PrefabId)` | Public | Deferred | Enqueues prefab-id spawn. |
| `World.Instantiate(string key)` | Public | Deferred | Resolves key then enqueues prefab spawn. |
| `World.Instantiate(ArchetypeSignature, PrefabApply)` | Public | Deferred | Enqueues direct spawn apply delegate. |
| `World.EnqueueCreateDefault(...)` / `World.EnqueueSpawn(...)` | Public | Deferred | Low-level explicit queue wrappers. |
| `World.EnqueueDestroy(...)` / `World.EnqueueAdd<T>(...)` / `World.EnqueueRemove<T>(...)` | Public | Deferred | Structural mutation requests only. |
| `World.Entities` | Public | Read-only | Exposes `IEntityReadOnly` view only (`AliveCount`, `IsAlive`, `IsValid`, `Capacity`). |
- `World` is the sole structural mutation entrypoint; manager instances are not publicly exposed for mutation.
- Read/query/system inspection is provided through world facade APIs (for example: `AliveEntityCount`, `IsAlive`, `GetComponent`, `Query`, `SystemCount`, `SystemExecutionMode`, `RunManualSystem`).
- Prefab spawn uses `PrefabApply(Archetype archetype, int slot)` to write values.
- Prefab registry supports standard registration and resolved-column registration (`IResolvedPrefab<TSelf>` + `RegisterResolved<T>`), with per-archetype column index caching.
- Prefab registry also supports keyed runtime/data registration (`Register(key, ...)`, `RegisterResolved(key, ...)`) for reloadable prefab content.
- Prefab registry supports template-prefab registration (`RegisterTemplate(...)` / `ReloadTemplate(...)`) with optional per-instance apply callbacks.
- Keyed runtime refresh uses `Reload(...)` / `ReloadResolved(...)`; `UnregisterKey(...)` removes key lookup but keeps existing numeric prefab IDs valid.
- `DataPrefabLoader` supports JSON/data-driven prefab reload into `PrefabRegistry.ReloadResolved(...)`.
- Apply order: Destroy -> Spawn -> CreateDefault -> Remove -> Add.
- Spawn/CreateDefault apply uses stable archetype batching with `Reserve(count)` to reduce reallocation and improve locality.
- Template-prefab spawn path applies template once per run and clones rows via `Archetype.CopySlot`; optional per-instance apply runs after clone for dynamic fields (for example, randomized stat ranges).
- Queue enqueue/apply paths are lock-protected and safe for concurrent enqueueing.
- Dirty tracking hooks are invoked during structural apply for create/spawn/add/remove when enabled.
- Thread-local structural capture + barrier merge is implemented (`BeginLocalCapture` / `EndLocalCapture`).

---

## 3. Host Injection

ECS defines the host interface; engines implement it. No engine types inside Module.ECS.

### 3.1 Host Interface

```csharp
public interface IHost
{
 EnvironmentType Environment { get; }
 void Log(LogLevel level, string message);
}

public enum EnvironmentType { Server, Client, Hybrid }
```

### 3.2 Engine Implementation

```csharp
// Engine.Server
public sealed class ServerHost : IHost
{
 public EnvironmentType Environment => EnvironmentType.Server;
 public void Log(LogLevel level, string message) => ServerLogger.Write(level, message);
}

// Engine.Godot (client)
public sealed class GodotHost : IHost
{
 public EnvironmentType Environment => EnvironmentType.Client;
 public void Log(LogLevel level, string message) => GD.Print(message);
}
```

### 3.3 World Construction

```csharp
public sealed class World
{
 public IHost Host { get; }
 public IWorldEventSink EventSink { get; }

 public World(IHost host, IWorldEventSink sink, EcsOptions options = default)
 {
 Host = host ?? NoOpHost.Instance;
 EventSink = sink ?? NoOpWorldEventSink.Instance;
 }
}
```

**Implementation Notes (current code)**:
- Implemented in `ECS/World.cs` with no-op defaults:
 - `World(EcsOptions options = default)`
 - `World(IHost host, EcsOptions options = default)`
 - `World(IHost host, IWorldEventSink eventSink, EcsOptions options = default)`
- `NoOpHost` and `NoOpWorldEventSink` are provided for zero-friction local world construction.
- World exposes direct component/query APIs for gameplay and tools:
 - `GetComponent/TryGetComponent/GetComponentRef/HasComponent`
 - `TrySetComponent` and `SetOrAddComponent`
 - `Query<T>` and `Query<T1,T2>` archetype iteration helpers
- Systems enqueue structural changes through `SystemContext.Commands : IStructuralCommands`, which forwards into world-owned wrappers.

### 3.4 World Event Sink (barrier-level emission)

Event sink is intentionally batch-oriented to avoid per-entity event overhead in hot paths.

```csharp
public interface IWorldEventSink
{
 bool WantsEntityCreatedBatches { get; }
 void OnEntitiesCreated(StructuralBarrierPhase phase, ReadOnlySpan<Entity> entities);
 void OnStructuralBarrierApplied(in StructuralBarrierEvent value);
}
```

**Implementation Notes (current code)**:
- `World.ProcessStructuralQueues(...)` emits:
 - one created-entity batch (if sink opts in),
 - one structural barrier summary event (`created/spawned/destroyed/removed/added` counts).
- Created entities are buffered per barrier using pooled arrays (`ArrayPool<Entity>`).

### 3.5 Godot bootstrap and log routing (_tmp harness)

Godot-specific runtime integration stays in `_tmp/` so ECS core remains engine-agnostic:

- `ECSNode` performs staged deferred startup (`Configure -> Initialize -> Start`) before enabling tick.
- `ECSNode` injects Godot-backed `IHost` and `IWorldEventSink`.
- ECS logs route through host injection (`EcsLog -> World.FlushLogs -> IHost.Log`).
- Optional console capture (`Console.Out/Error -> EcsLog`) is enabled only in the Godot harness.
- `ECSLogConsoleBridge` surfaces host logs + barrier/batch events in Godot console/UI for live diagnostics.

---

## 4. Dependency Rules

Dependencies flow **downward only**. No upward or sideways coupling.

```
SCRIPTS (content: items, mobs, spells)
 ->
SYSTEMS (game logic: combat, skills, crafting)
 ->
ENGINES (integration: NetworkedWorld, GameClient)
 ->
MODULES (ECS, Network, UI, Persistence -- zero cross-deps)
 ->
PRIMITIVES (collections, math, serialization, logging)
```

### 4.1 Boundary Rules

| Layer | May Reference | Must Not Reference |
|-------|---------------|-------------------|
| Module.ECS | Core.* only | Engine types, Systems, Scripts, Module.Network |
| Module.Network | Core.* only | Engine types, Systems, Scripts, Module.ECS |
| Engine.* | Modules, Core | Systems, Scripts |
| System.* | Engines, Core | Scripts, other Systems (siblings) |
| Scripts.* | Systems, Engines | -- |

Integration rule:

- Code that uses both ECS and Network belongs in `Engine.*` (bridge/orchestration layer), not inside `Module.ECS` or `Module.Network`.

### 4.2 Verification

- Module.ECS compiles with zero engine references.
- Module.Network compiles with zero engine references.
- Module.ECS and Module.Network have no direct project/package references to each other.
- No system reaches "up" into engine types.
- Structural changes occur only through queues.
- No cache is treated as authoritative over ECS.

### 4.3 Component ID Stability Policy

Component type IDs must be stable in certain contexts:

| Context | Requirement | Approach |
|---------|-------------|----------|
| Fresh world (new game) | Runtime IDs OK | Register types at startup, IDs assigned sequentially |
| Persistence (save/load) | Stable identity required | Explicit `PersistenceId` + versioned mapping on load |
| Networking (wire protocol) | Stable IDs required | Shared registry, IDs fixed at compile time or handshake |

**Fresh worlds**: IDs can be recomputed each startup. Order-of-registration determines ID.

**Persistence**: Save files store explicit stable persistence ids, not runtime component ids. Loader maps stored persistence ids to current runtime ids through the persistence type registry. Supports schema evolution.

**Networking**: Client and server must agree on IDs. Either:

- Fixed at compile time via attributes: `[ComponentId(42)]`
- Negotiated at connection handshake
- Shared registry assembly referenced by both

**Implementation Notes (current code)**:
- Dependency boundaries are documented but not enforced in project structure yet.

---

## 5. Tick-Based Timing

### 5.1 Base Tick Rate

Fixed-step simulation with accumulator pattern.

```csharp
// Design knob: choose based on gameplay feel
const float TickDuration = 0.01f; // 10ms = 100 Hz (smooth combat)
// const float TickDuration = 0.02f; // 20ms = 50 Hz (sufficient for UO-style)

private float _accumulator;
private uint _worldTick;

public void Update(float deltaSeconds)
{
 _accumulator += deltaSeconds;

 int ticksThisFrame = 0;
 while (_accumulator >= TickDuration && ticksThisFrame < MaxTicksPerFrame)
 {
 SimulateTick(_worldTick++);
 _accumulator -= TickDuration;
 ticksThisFrame++;
 }
}
```

**Tick determinism rule**: Simulation time is `worldTick * TickDuration`. Wall clock only affects how many ticks are processed per host update. All gameplay logic uses `worldTick`, never wall-clock time.

- **100 Hz**: Smooth client interpolation, tight combat windows.
- **50 Hz**: Lower overhead, sufficient for classic MMO pacing.
- Multi-rate cadence handles expensive work regardless of base rate.

### 5.2 Spiral-of-Death Guard

```csharp
const int MaxTicksPerFrame = 5;

// If still behind after max ticks, options:
// A) Carry remainder (accept temporary lag)
// B) Time dilation (slow simulation)
// C) Drop ticks (non-deterministic, avoid)
```

### 5.3 System Cadence (Multi-Rate Scheduling)

Systems declare period in ticks. Run when `(worldTick - StartOffset) % Period == 0`.

| System | Period | Real Time (@10ms) |
|--------|--------|-------------------|
| Movement | 1 | 10ms |
| Combat | 1-2 | 10-20ms |
| Perception | 5 | 50ms |
| AI Light | 10 | 100ms |
| AI Decision | 25 | 250ms |
| Territory | 100 | 1s |
| Crops/Decay | 500 | 5s |

### 5.4 Phase Staggering

Spread systems with same period across ticks to avoid spikes.

```csharp
public sealed class SystemScheduler
{
 public void AssignOffsets(IReadOnlyList<BaseSystem> systems)
 {
 // Group by (Period, Stage)
 var groups = systems.GroupBy(s => (s.Period, s.Stage));

 foreach (var group in groups)
 {
 int maxStagger = group.Key.Stage.IsCritical() ? 0 : 10;
 int phaseLimit = (maxStagger <= 0) ? 1 : Math.Min(group.Key.Period, maxStagger);

 int i = 0;
 foreach (var system in group)
 system.StartOffset = i++ % phaseLimit;
 }
 }
}
```

- **Critical stages** (Movement, Combat): `MaxStagger = 0` (no staggering).
- **Non-critical stages** (AI, WorldSim): `MaxStagger = 5-10`.

**Implementation Notes (current code)**:
- Phase staggering is optional and configured through `EcsOptions`:
 - `EnablePhaseStaggering`
 - `MaxNonCriticalStagger`
- Staggering is assigned during `SystemManager.RebuildSchedules()`.
- Systems with explicit non-zero offsets are preserved.

### 5.5 Stage Ordering and Parallelism

Scheduler executes stages in strict order; systems within a stage may run in parallel if read/write sets allow.

```
Stage Order (example):
1. Input -- process client inputs
2. Movement -- apply velocity, resolve collisions
3. SpatialIndex -- update spatial structures
4. Perception -- update entity visibility
5. Combat -- process attacks, apply damage
6. AI -- decision making
7. WorldSim -- crops, decay, territory
8. Networking -- build and send deltas
9. Cleanup -- apply structural changes (barrier, not a normal system stage)
```

Within each stage, non-conflicting systems execute concurrently.

Clarification:

- vNext does **not** run all systems simultaneously.
- Parallel execution is stage-local and batch-based.
- Only non-conflicting systems in the same stage batch run concurrently.
- Conflicting systems are serialized into separate batches for deterministic safety.

**Implementation Notes (current code)**:
- Tick loop and scheduler are implemented.
- `SystemManager` precomputes stages/dependencies/conflict batches.
- Execution backend is configurable:
 - `SystemExecutionMode.Serial` (default)
 - `SystemExecutionMode.ParallelBatches` (persistent `ManualWorkerPool`)
- Stage barriers and read/write conflict batching are preserved across both modes.
- A jobified per-system chunk runner is still a future extension.

### 5.6 Structural Barriers (pre + post)

To minimize one-tick latency while keeping structural changes deterministic, the world tick uses two barriers:

1) **Pre-barrier**: flush external enqueues (network/input/IO) before systems run.
2) **Post-barrier**: flush system enqueues before the tick ends (for snapshot/exports).

**Implementation Notes (current code)**:
- `World.Tick` calls `ProcessStructuralQueues()` both before and after `SystemManager.Tick`.
- Each barrier emits `IWorldEventSink.OnStructuralBarrierApplied(...)`.
- Optional created-entity batches are emitted via `OnEntitiesCreated(...)` when enabled by sink.

### 5.7 World Bootstrap Lifecycle (explicit states)

World startup and runtime mutation are gated by explicit lifecycle states:

```csharp
public enum WorldLifecycleState : byte
{
 Created,
 Sealed,
 Initialized,
 Running,
 Shutdown,
}
```

State semantics:

- `Created`: registration/configuration is open.
- `Sealed`: component/prefab/system registries are sealed for lock-free reads.
- `Initialized`: `SystemManager.Initialize(world)` has run.
- `Running`: `Tick` is active.
- `Shutdown`: world is closed; enqueue/tick operations are rejected.

Bootstrap flow:

1) Register components, prefabs, and systems.
2) `World.Seal()` -> seals `ComponentManager`, `PrefabRegistry`, and `SystemManager`.
3) `World.Initialize()` -> calls system `OnInitialize`.
4) `World.Tick()` -> transitions to `Running` on first tick.
5) `World.Shutdown()` -> calls system `OnShutdown` once.

**Implementation Notes (current code)**:
- `ECS/World.cs` exposes `State`, `Seal()`, `Initialize()`, and `Shutdown()`.
- `World.Tick()` auto-initializes if needed, then transitions to `Running`.
- `SystemManager` now has explicit `Seal()` and blocks late registration.

### 5.8 Threading and Error Handling Patterns

Threading model (current code):

- `StructuralQueue` enqueue is lock-protected; apply uses swap-buffer snapshot + outside-lock processing.
- `StructuralQueue` supports worker-local capture/merge while systems run in parallel.
- `DirtyTracker` supports serial/parallel capture windows to reduce per-mark lock contention in system hot paths.
- Component/prefab registries are startup-sealed and use lock-free read snapshots after seal.
- `SystemExecutionMode.ParallelBatches` uses a persistent `ManualWorkerPool` (no per-tick task allocation).
- Optional stats counters use `Interlocked` where increments can occur from concurrent paths.
- DEBUG builds run post-barrier structural invariants (queue sanity + entity location + archetype column checks).

Error handling model (current code):

- Hot path argument checks are minimal; invalid state throws fail-fast exceptions.
- Worker pool exceptions are propagated back to caller after parallel batch completion.
- Throw-heavy paths are kept out of hot loops using helper methods with `NoInlining` where appropriate.

---

## 6. Scalability Patterns

### 6.1 Striping / Amortization

Spread large workloads across ticks.

```csharp
public void Update(uint worldTick)
{
 int stripe = (int)(worldTick % StripeCount);

 world.Query<PlantState, GrowTimer>((entity, ref plant, ref timer) =>
 {
 // Only process entities in this stripe
 if (GetStripe(entity) != stripe) return;

 if (worldTick >= timer.NextGrowTick)
 ProcessGrowth(ref plant, ref timer, worldTick);
 });
}
```

| Stripe Count | Full Sweep (@10ms tick) |
|--------------|------------------------|
| 10 | 100ms |
| 20 | 200ms |
| 100 | 1s |

### 6.2 Stripe Hash Stability

Use stable hash that survives entity pooling.

```csharp
// Option A: Include version to distinguish reused IDs
private int GetStripe(Entity e) =>
 HashCode.Combine(e.Index, e.Version) % StripeCount;

// Option B: Use spawn tick (stored in component)
private int GetStripe(SpawnInfo info) =>
 info.SpawnTick % StripeCount;
```

Avoid raw `entity.Index % S`--recycled entities inherit previous stripe.

**Striping tradeoff**:

- **Range striping** (slice contiguous index ranges per archetype) is most cache-friendly.
- **Hash striping** is more stable under heavy entity churn.
- Prefer range striping for hot loops; use hash striping when churn would cause hotspots.

### 6.3 Per-Entity Timers with Jitter

Prevent synchronized cascades with deterministic jitter.

```csharp
public void ScheduleGrowth(Entity entity, uint worldTick, int periodTicks, int jitterTicks)
{
 int jitter = (int)(HashCode.Combine(entity.Index, entity.Version) % jitterTicks);
 ref var timer = ref world.GetComponent<GrowTimer>(entity);
 timer.NextGrowTick = worldTick + periodTicks + jitter;
}
```

### 6.4 Timing Wheel (Optional, Large Scale)

For millions of timer-driven entities, avoid full scans.

```csharp
// Conceptual -- production version should be allocation-free
public sealed class TimingWheel
{
 private readonly List<Entity>[] _buckets = new List<Entity>[1024];

 public void Schedule(Entity entity, int tick)
 {
 _buckets[tick & 1023].Add(entity);
 }

 public void ProcessTick(int worldTick, Action<Entity> callback)
 {
 var bucket = _buckets[worldTick & 1023];
 foreach (var entity in bucket)
 callback(entity);
 bucket.Clear();
 }
}
```

- **O(due)** per tick instead of **O(all)**.
- Adds bookkeeping on reschedule/destroy.
- Consider when timer population exceeds ~100k entities.

**Implementation note**: Production timing wheel buckets should be allocation-free in hot paths. Options include pooled lists, arrays with head indices, or intrusive linked lists via "NextInBucket" component.

**Implementation Notes (current code)**:
- Implemented:
 - conflict batching (`Write/Write`, `Write/Read`, `Read/Write` rules),
 - serial execution mode,
 - parallel batch execution mode via persistent worker pool,
 - optional phase staggering at schedule rebuild (`EnablePhaseStaggering`, `MaxNonCriticalStagger`).
- Pending:
 - jobified per-system execution mode,
 - priority ordering polish.

---

## 7. Parallel Execution

### 7.1 Read/Write Sets

Systems declare component access for scheduler dependency analysis.

```csharp
public abstract class SystemBase
{
 public abstract SystemDescriptor Descriptor { get; }
 public abstract void Update(in SystemContext ctx);
}

public sealed class MovementSystem : SystemBase
{
 private static readonly ComponentId[] ReadIds = { ComponentManager.GetId<Velocity>() };
 private static readonly ComponentId[] WriteIds = { ComponentManager.GetId<Position>() };

 public override SystemDescriptor Descriptor { get; } =
 new SystemDescriptor(
 "Movement",
 SystemStage.Movement,
 defaultPeriodTicks: 1,
 defaultStartOffset: 0,
 ComponentMask.FromIds(ReadIds),
 ComponentMask.FromIds(WriteIds));

 public override void Update(in SystemContext ctx)
 {
 // movement update work
 }
}
```

### 7.2 Scheduler Rules

- Systems with overlapping write sets cannot run in parallel.
- Systems reading components another writes cannot run in parallel.
- Non-overlapping systems run concurrently.

**Implementation Notes (current code)**:
- Implemented in `ECS/System/SystemManager.cs`:
 - stage ordering + dependency topology,
 - read/write conflict batch construction,
 - serial and parallel-batch dispatch backends.
- `SystemContext.Commands` is an `IStructuralCommands` facade over `World` wrappers, so structural enqueues remain centralized and stats-aware.

### 7.3 Legacy Scheduler Reference (_OLD) -> vNext

The _OLD ECS implementation used a practical pattern that is still useful:

- Systems grouped into conflict-free batches.
- Batches executed by a persistent manual worker pool (`ManualWorkerPool`) instead of per-frame task allocation.
- Tick-rate buckets for cadence control.
- Manual systems isolated from scheduled systems.

vNext should preserve strict stage ordering and adopt the worker-pool model as an execution backend:

- Precompute batches at rebuild time (already in place).
- Add a pluggable stage runner:
 - `Serial` (implemented)
 - `ParallelBatches` (implemented; run each conflict-free batch on persistent worker pool)
 - `JobifiedSystem` (single system parallelizes its own archetype/chunk iteration)
- Keep queue/barrier semantics unchanged regardless of execution backend.

This keeps determinism while enabling "hose the CPU with system tasks" when enabled.

---

## 8. Archetype Stability Patterns

### 8.1 Problem: Archetype Churn

Frequent add/remove causes entity migration between archetypes, which is expensive.

### 8.2 Solution: Data-State Flags

For transient states, use flags instead of structural changes.

```csharp
[Flags]
public enum StatusEffect : ushort
{
 None = 0,
 Poisoned = 1 << 0,
 Burning = 1 << 1,
 Slowed = 1 << 2,
 Stunned = 1 << 3,
 Frozen = 1 << 4,
}

public struct StatusState : IComponent<StatusState>
{
 public StatusEffect Active;
 public int PoisonEndTick;
 public int BurnEndTick;
 public int SlowEndTick;
}

// Apply poison (no archetype change)
ref var status = ref world.GetComponent<StatusState>(entity);
status.Active |= StatusEffect.Poisoned;
status.PoisonEndTick = worldTick + 100;

// Check poison (flag check, not HasComponent)
if ((status.Active & StatusEffect.Poisoned) != 0) { }
```

**Trade-offs:**

- + Avoids migration cost for transient states.
- + Single archetype for all combat entities.
- - Queries for "all poisoned" require flag check, not archetype match.

**Guidance:**

- Use flags for high-frequency toggles (buffs, debuffs, status effects).
- Use structural components for fundamental entity differences (Player vs NPC vs Item).

### 8.3 Archetype Fragmentation

Too many tiny archetypes hurt cache and query performance.

- Group similar entities; prefer flags over structural splits.
- Profile archetype count and average population.

**Implementation Notes (current code)**:
- Not implemented yet.

---

## 9. Dirty Tracking

### 9.1 Purpose

Track changed entities for efficient network sync and derived cache updates.

### 9.2 Implementation (current code)

`DirtyTracker` uses dense arrays + per-index epoch/payload stamps (no per-mark allocation):

```csharp
public sealed class DirtyTracker
{
 public void Mark(Entity entity);
 public void Mark(ComponentId componentId, Entity entity);
 public void Mark(ComponentId componentId, ReadOnlySpan<Entity> entities);
 public void MarkUnique(ComponentId componentId, ReadOnlySpan<Entity> entities);

 public int CopyAllTo(Span<Entity> destination);
 public int CopyAndClearAllTo(Span<Entity> destination);

 public int CopyComponentTo(ComponentId componentId, Span<Entity> destination);
 public int CopyAndClearComponentTo(ComponentId componentId, Span<Entity> destination);
}
```

Current behavior:

- Supports both global dirty set and component-scoped dirty sets.
- De-duplicates by `(Index, Version)` within an epoch.
- Uses capture-aware write paths during tick execution:
 - serial capture uses a no-lock fast path on the tick thread,
 - parallel capture uses worker-local dirty buffers merged at barrier end.
- Uses lock-protected mutation/copy for external or non-captured calls.
- Supports batch marking (`ReadOnlySpan<Entity>`) and trusted-unique batch marking for dense one-touch system paths.
- `World.MarkDirty(...)` and structural queue apply paths invoke tracker only when `EcsOptions.EnableDirtyTracking` is true.

### 9.3 Event Pattern

Events carry entity IDs only; consumers pull current data from ECS.

```csharp
// Good: ID only
public readonly struct EntityDamagedEvent
{
 public readonly Entity Target;
 public readonly Entity Source;
 public readonly int Amount;
}

// Consumers query current state
void HandleDamage(EntityDamagedEvent e)
{
 ref var health = ref world.GetComponent<Health>(e.Target);
 // Use current health, not stale event data
}
```

**Implementation Notes (current code)**:
- Implemented in `ECS/DirtyTracker.cs`.
- Exposed via `World.Dirty` with helper methods:
 - `World.MarkDirty(Entity)`
 - `World.MarkDirty(ComponentId, Entity)`
 - `World.MarkDirty(ComponentId, ReadOnlySpan<Entity>)`
 - `World.MarkDirtyUnique(ComponentId, ReadOnlySpan<Entity>)`
 - `World.MarkDirty<T>(Entity)`
 - `World.MarkDirtyUnique<T>(ReadOnlySpan<Entity>)`
 - `World.CopyDirtyTo(...)` / `World.DrainDirtyTo(...)`
 - component-scoped overloads via `ComponentId` or generic `T`.
- Stats counters expose dirty call shape when statistics are enabled:
 - `DirtyMarkCalls`
 - `DirtyMarkBatchCalls`
 - `DirtyMarkTrustedBatchCalls`
 - `DirtyMarkedEntities`
 - `DirtyTrustedMarkedEntities`

---

## 10. Spatial Integration

Entities carry spatial components; spatial indices are derived.

### 10.1 Spatial Components

```csharp
public struct Position : ISerializableComponent<Position>
{
 public float X, Y, Z;
 // Serialize/Deserialize implementation...
}

public struct ChunkCoord : IComponent<ChunkCoord>
{
 public short ChunkX, ChunkZ;
}

public struct RegionId : IComponent<RegionId>
{
 public ushort Region;
}
```

### 10.2 Spatial Index Updates

- Movement system updates Position component.
- Spatial indexing system derives grid/region membership at defined cadence (e.g., 30ms).
- Spatial structures are caches, rebuildable from ECS state.

**Implementation Notes (current code)**:
- Not implemented yet.

---

## 11. Invariants Checklist

| Invariant | Enforcement |
|-----------|-------------|
| Lookup matches archetype + slot for alive entities | Version check on access |
| Archetype component arrays aligned in length | Add/remove maintain invariant |
| Each signature maps to one archetype | `ArchetypeManager.GetOrCreate(...)` via world-managed structural apply |
| Structural changes only at barriers | Queue-based API |
| Engine integration outside Module.ECS | Compile-time project references |
| ECS is source of truth | Caches explicitly derived |

---

## 12. Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Archetype churn from transient states | Use data-state flags |
| Too many tiny archetypes | Group similar entities |
| Parallel write contention | Narrow write sets, split systems |
| Timer cascades | Per-entity jitter |
| Full-scan timer overhead | Striping or timing wheel |
| Entity ID reuse affects striping | Hash with version or spawn tick |

---

## 13. Current Status and Next Phase

Current status snapshot:

- Core ECS runtime, deferred structural model, and world lifecycle gating are implemented.
- System scheduler supports deterministic stage ordering with `Serial` and `ParallelBatches` execution modes.
- World-mutation gateway is hardened (manager internals are not public mutation surfaces).
- Concurrency hardening tests are in place for:
 - parallel dirty capture merge correctness,
 - concurrent mixed enqueue + barrier apply determinism,
 - serial vs parallel final-state parity.
- Debug-only invariant checks are integrated after structural apply.
- Godot benchmark harness remains isolated under `_tmp`; production scene is benchmark-free by default.

Recommended next phase:

- Build networking vertical slice:
 - replication delta contract (dirty + interest + packet budget),
 - transport abstraction + loopback implementation,
 - networking stage systems for build/send,
 - deterministic encode/decode tests and throughput profiling.

---

## 14. Summary

| Aspect | Approach |
|--------|----------|
| Entity | `readonly struct` handle (index + version) |
| Component | `IComponent<TSelf>` with `unmanaged` constraint; `ISerializableComponent<TSelf>` opt-in |
| ComponentId | `readonly struct` wrapper for type-safe component type identification |
| ArchetypeSignature | `sealed class` with `ulong[]` bitset, interned for reference equality |
| Storage | Archetype SoA with dense arrays, accessed by signature; O(1) column lookup |
| Structural changes | Queued, applied at barriers (Destroy -> Spawn -> CreateDefault -> Remove -> Add) |
| Registries | Startup registration + sealed snapshot for lock-free reads |
| Host integration | `IHost` + `IWorldEventSink` injection with no-op defaults |
| Timing | Fixed-step tick (10-20ms), accumulator pattern, `worldTick`-based |
| Multi-rate | System cadence via period + offset |
| Stage ordering | Strict stage order; conflict batches precomputed; execution mode = `Serial` or `ParallelBatches` |
| Load spreading | Phase staggering + striping |
| Timer efficiency | Per-entity jitter, optional timing wheel |
| Parallelism | Declared read/write sets, scheduler enforced |
| World API | Deferred structural mutation facade + component/query helpers |
| Template prefabs | Implemented template-copy spawn path + optional per-instance mutation hook |
| Dirty deltas | World dirty mark/copy/drain contract (global + per-component) |
| Data prefabs | JSON-driven runtime loader into `PrefabRegistry.Reload*` |
| Transient states | Data-state flags over structural changes |
| ID stability | Fresh = runtime; Persistence = type hash; Network = fixed |
