# ECS vNext -- Archetypes, Prefabs, and Scripts

A design specification for **stable archetypes**, **zero-alloc prefabs**, and **RunUO-style scripted definitions** integrated with the current ECS core.

This document complements:

- **ECS vNext -- Product Requirements Document (PRD)**
- Tick-based timing guide
- Layered architecture / host injection guide
- Systems outline

It focuses on **how data is shaped (archetypes)** and **how instances are created (prefabs/scripts)** with the APIs that exist today.

---

## 1. Core Ideas (At a Glance)

| Concept | Meaning | Role in the system |
|--------------|----------------------------------------------|----------------------------------------|
| **Archetype**| Structural schema (set of component *types*) | Stable memory layout |
| **Prefab** | Value template for an archetype | Defines *initial data*, not structure |
| **Entity** | Runtime instance | A row inside an archetype |
| **Script** | C# definition of a prefab | RunUO-style authoring |

**Fundamental equation:**

> **Archetype = schema, Prefab = row template, Entity = row instance**

Design goals:

- Archetypes should be **stable and few**.
- Prefabs should be **fast, zero-allocation, and mostly value writes**.
- Bulk loading should be near-memcpy speed.
- Runtime should almost never create new archetypes.

---

## 2. Archetypes -- Structural Shape of the World

### 2.1 What an Archetype Is

An archetype is:

- A **unique set of component types** (a signature)
- Backed by **Structure-of-Arrays (SoA)** storage
- Dense: no holes, swap-and-pop on removal
- Column-aligned: every component array has the same length as the entity array

Conceptually:

```
Archetype (Signature: ItemBase, WeaponStats, Durability, Material)
---------------------------------------------------------------
Entities: [E0] [E1] [E2] [E3] ...
ItemBase: [I0] [I1] [I2] [I3] ...
WeaponStats: [W0] [W1] [W2] [W3] ...
Durability: [D0] [D1] [D2] [D3] ...
Material: [M0] [M1] [M2] [M3] ...
```

### 2.2 Archetype Stability Principle

**Do not create archetypes at runtime for transient state.**

Use:

- **Data-state flags** (inside components) for buffs, debuffs, statuses, toggles
- **Structural components** only for fundamental identity differences (Player vs NPC vs Item)

Why:

- Archetype migration is expensive
- Many tiny archetypes hurt cache and query performance
- Wide, stable archetypes are easier to optimize

---

## 3. Pre-Registered "Known Archetypes" (Startup Phase)

### 3.1 Why Pre-Register?

At startup:

- Register all **known shapes** once
- Give capacity hints for memory pre-allocation
- Eliminate runtime churn
- Make persistence and bulk loading predictable and fast

Examples of known shapes:

- Weapon
- Armor
- Container
- Mobile (NPC/Player)
- Plant/Crop
- MapChunk
- House
- StaticItem

### 3.2 Archetype Handles (Future)

A lightweight handle is the preferred long-term reference type:

```
public readonly struct ArchetypeHandle
{
 public readonly int Index;
}
```

**Current API note**: vNext currently uses `ArchetypeSignature` and `ArchetypeManager.GetOrCreate(...)`.
A handle layer can be added later without changing prefab semantics.

---

## 4. Prefabs -- Value Templates (Zero Allocation)

A prefab defines **initial values**, not structure.

### 4.1 Compile-Time Prefabs (RunUO-style C#)

```
public interface IPrefab<TSelf>
 where TSelf : unmanaged, IPrefab<TSelf>
{
 ArchetypeSignature Signature { get; }
 void ApplyTo(Archetype archetype, int slot);
}
```

Optional template-copy contract for burst spawn paths:

```
public interface ITemplatePrefab<TSelf> : IPrefab<TSelf>
 where TSelf : unmanaged, ITemplatePrefab<TSelf>
{
 bool HasPerInstanceData { get; }
 void ApplyTemplateTo(Archetype archetype, int slot);
 void ApplyInstanceTo(Archetype archetype, int slot);
}
```

Example prefab:

```
public readonly struct IronDagger : IPrefab<IronDagger>
{
 public ArchetypeSignature Signature => KnownSignatures.Weapon;

 public void ApplyTo(Archetype arch, int slot)
 {
 arch.GetComponentSpan<ItemBase>(WeaponCols.ItemBase)[slot] =
 new ItemBase { ItemId = 0x1001, Weight = 10 };

 arch.GetComponentSpan<WeaponStats>(WeaponCols.Stats)[slot] =
 new WeaponStats { MinDamage = 2, MaxDamage = 8, Speed = 35 };

 arch.GetComponentSpan<Durability>(WeaponCols.Durability)[slot] =
 new Durability { Current = 100, Max = 100 };

 arch.GetComponentSpan<Material>(WeaponCols.Material)[slot] =
 new Material { Type = MaterialType.Iron };
 }
}
```

Characteristics:

- No boxing
- No heap allocations
- Pure writes into column memory
- C# acts as your "item scripting language" (RunUO style)

---

## 5. Column Index Caching (Critical for Performance)

Do **not** call `GetId<T>()` or do dictionary lookups per spawn.

Cache column indices once per archetype:

```
public readonly struct WeaponColumns
{
 public readonly ComponentId ItemBase;
 public readonly ComponentId Stats;
 public readonly ComponentId Durability;
 public readonly ComponentId Material;
}
```

**Current API note**: `Archetype.GetComponentSpan<T>(ComponentId id)` exists.
If you want `int` indices instead, use `GetColumnIndex` + `GetComponentSpanAt<T>(index)`.

---

## 6. Fast Spawning API (World Layer)

The recommended API surface is:

```
public void Instantiate<TPrefab>() where TPrefab : unmanaged, IPrefab<TPrefab>
```

**Current API note**: public spawn/create APIs are deferred and enqueue structural work.
At barrier apply, the pipeline is:

1. Resolve archetypes from queued signatures/prefabs.
2. Stable-group requests by archetype and `Reserve(count)` once per group.
3. Allocate entity handles and append rows.
4. Apply prefab data:
 - standard path: write values per entity,
 - template path: write template once, `CopySlot` for the run, optional per-instance mutation after copy.
**Implementation status (current code)**:
- `World.Instantiate<TPrefab>()`
- `World.InstantiateTemplate<TPrefab>()`
- `World.Instantiate(PrefabId)`
- `World.Instantiate(string prefabKey)`
- `World.Instantiate(ArchetypeSignature, PrefabApply)`
- `World.CreateDefault(ArchetypeSignature)`

**Current API note (structural queue)**:
- `World.Instantiate*` and `World.CreateDefault(...)` enqueue into structural queues.
- Manager internals (`EntityManager`, `ArchetypeManager`, `StructuralQueue`) are not public mutation entrypoints; callers go through `World`.
- `World.Entities` exposes read-only entity state (`IEntityReadOnly`) for inspection only; all structural writes are queue-backed world APIs.
- Low-level queue wrappers are also exposed:
 - `World.EnqueueSpawn(ArchetypeSignature, PrefabApply)`
 - `World.EnqueueSpawn(PrefabId)` / `World.EnqueueSpawn(string prefabKey)`
 - `World.EnqueueCreateDefault(signature)`
- No public immediate spawn/create paths are exposed; structural mutation APIs are queue-backed.

**Current API note (prefab registry)**:
- `PrefabRegistry.Register<TPrefab>()` registers standard prefab apply delegates.
- `PrefabRegistry.RegisterTemplate<TPrefab>()` and `RegisterTemplate(...)` register template-copy prefabs.
- `PrefabRegistry.RegisterResolved<TPrefab>()` (via `IResolvedPrefab<TSelf>`) enables resolved-column caching.
- Data/runtime prefabs can use keyed registration (`Register(key, ...)` / `RegisterResolved(key, ...)`) and be spawned by key via `World.EnqueueSpawn(string prefabKey)`.
- Minimal runtime refresh policies exist:
 - `Reload(key, ...)` / `ReloadResolved(key, ...)` / `ReloadTemplate(key, ...)` updates existing keyed prefabs in-place (or adds if missing).
 - `UnregisterKey(key)` removes key lookup while preserving numeric prefab IDs already in use.
- `World.Instantiate<TPrefab>()` auto-detects `ITemplatePrefab<TPrefab>` and routes to the template queue path.
- **Design note**: `IResolvedPrefab<TSelf>` currently inherits `IPrefab<TSelf>` for compatibility. A future tightening option is to split them so resolved prefabs can only be registered through `RegisterResolved(...)`.
- `World.EnqueueSpawn(PrefabId)` enqueues by prefab id and applies via the registry at structural barrier time.

Example resolved prefab:

```csharp
public readonly struct IronDaggerResolved : IResolvedPrefab<IronDaggerResolved>
{
 public static ComponentId[] ResolvedComponentIds => new[]
 {
 ComponentManager.GetId<ItemBase>(),
 ComponentManager.GetId<WeaponStats>(),
 };

 public ArchetypeSignature Signature => KnownSignatures.Weapon;

 public void ApplyTo(Archetype archetype, int slot)
 {
 // Compatibility path; resolved path is preferred.
 var itemId = ComponentManager.GetId<ItemBase>();
 var statsId = ComponentManager.GetId<WeaponStats>();
 archetype.GetComponentSpan<ItemBase>(itemId)[slot] = new ItemBase { ItemId = 0x1001 };
 archetype.GetComponentSpan<WeaponStats>(statsId)[slot] = new WeaponStats { MinDamage = 2, MaxDamage = 8 };
 }

 public void ApplyResolvedTo(Archetype archetype, int slot, ReadOnlySpan<int> columns)
 {
 // No ComponentId->column lookups in the hot path.
 archetype.GetComponentSpanAt<ItemBase>(columns[0])[slot] = new ItemBase { ItemId = 0x1001 };
 archetype.GetComponentSpanAt<WeaponStats>(columns[1])[slot] = new WeaponStats { MinDamage = 2, MaxDamage = 8 };
 }
}
```

**Current API note (timing + batching)**: Spawns and default creates are applied at structural barriers.
`StructuralQueue` uses stable archetype batching and calls `Reserve(count)` before allocating rows, so burst spawns
from prefabs or data loads avoid per-entity reallocation.

**Current API note (event emission)**:
- World emits one structural summary event per barrier (`created/spawned/destroyed/removed/added` counts).
- Created-entity notifications are batch-based (`ReadOnlySpan<Entity>`) and emitted only if the event sink opts in.

---

## 7. Bulk Loading (Persistence Fast Path)

### 7.1 Key Techniques

For loading 100k+ entities:

1. **Reserve once**
2. **Add entities uninitialized**
3. **Write components directly via spans**

```
archetype.Reserve(count);
var itemBase = archetype.GetComponentSpan<ItemBase>(WeaponCols.ItemBase);
var stats = archetype.GetComponentSpan<WeaponStats>(WeaponCols.Stats);

for (int i = 0; i < count; i++)
{
 int slot = archetype.AddEntityUninitialized(entity);
 itemBase[slot] = ReadItemBase(reader);
 stats[slot] = ReadStats(reader);
}
```

This approaches memcpy speed.

---

## 8. AddEntityUninitialized (Unsafe Fast Path)

Used only in controlled contexts (bulk load).

Semantics:

- Increments entity count
- Increments all column counts
- Does **not** write defaults
- Caller must write every component before systems run

---

## 9. Template Entity Path (Implemented)

For repeated prefab entries in a barrier batch, the runtime can use a template row copy path:

```
public void CopySlot(int src, int dst)
{
 foreach column:
 memcpy(column[src] -> column[dst]);
}
```

Used for:

- Mass spawning identical items.
- Data prefabs with mostly static values.

When values must vary per instance (for example `Damage = RandomRange(5, 10)`), keep
template copy enabled and implement the randomization in `ApplyInstanceTo(...)` (or
`applyPerInstance` delegate) after the copy.

---

## 10. Hybrid Prefabs (C# + Data Tables)

Recommended for MMO-scale content:

- **C# prefab = structure + logic**
- **Data table = tuning values**

Example:

```
public readonly struct WeaponPrefab : IPrefab<WeaponPrefab>
{
 private readonly WeaponDefId _id;

 public ArchetypeSignature Signature => KnownSignatures.Weapon;

 public void ApplyTo(Archetype arch, int slot)
 {
 ref readonly var def = ref WeaponDefTable.Get(_id);

 arch.GetComponentSpan<ItemBase>(WeaponCols.ItemBase)[slot] =
 new ItemBase { ItemId = def.ItemId, Weight = def.Weight };

 arch.GetComponentSpan<WeaponStats>(WeaponCols.Stats)[slot] =
 new WeaponStats
 {
 MinDamage = def.MinDamage,
 MaxDamage = def.MaxDamage,
 Speed = def.Speed
 };
 }
}
```

Benefits:

- Designers tweak JSON/CSV without recompiling
- Engineers control layout in C#
- Runtime remains allocation-free

---

## 11. Runtime Prefabs and Modding Policy

### 11.1 Runtime Prefabs (Data)

Runtime prefabs are **data-defined templates** (JSON/binary) that reference an existing archetype.
They can be hot-reloaded without recompiling.

Rules:

- Prefabs define shape by **component type names** (resolved to registered `ComponentId` values).
- Archetype shape should still map to known schemas; missing/unregistered component types fail load.
- Runtime loader uses keyed reload (`DataPrefabLoader` -> `PrefabRegistry.ReloadResolved`) for hot updates.
- Default data loader path is resolved-writer based; template-copy registration is available for static-heavy prefab families.

### 11.2 Compile-Time Prefabs (Code)

Keep C# prefab structs for performance-critical or logic-rich templates.
Both code and data prefabs use the same spawn pipeline.

### 11.3 Modding Boundaries

- **New archetypes** require code changes (new components + registration).
- **New prefabs** can be data-only and hot-reloaded.
- Schema changes require recompile/restart; content changes do not.

### 11.4 Suggested Load Order

1) Core assemblies (Modules -> Engines -> Systems) 
2) Mod assemblies (Mods/Modules -> Mods/Engines -> Mods/Systems) 
3) Script assemblies (Scripts/ or Mods/Scripts) 
4) Prefab data files (base + mod override folders) 

### 11.5 Data Prefab Registration (Current Loader)

Runtime data prefabs are loaded through `DataPrefabLoader`:

```
var def = new DataPrefabDefinition
{
 Key = "weapons.iron_dagger",
 Components = new[]
 {
 new DataPrefabComponentDefinition { Type = "Namespace.ItemBase, Game", Value = jsonValue1 },
 new DataPrefabComponentDefinition { Type = "Namespace.WeaponStats, Game", Value = jsonValue2 },
 }
};

DataPrefabLoader.LoadOrReload(def);
DataPrefabLoader.LoadOrReloadManyJson(jsonBlob);
```

Behavior:

- Deserializes component values once (cold path).
- Builds resolved writer delegates per component.
- Registers via `PrefabRegistry.ReloadResolved(...)` for key-based hot reload.

---

## 12. Scripts vs Prefabs (RunUO Mapping)

| RunUO Concept | ECS vNext Equivalent |
|--------------|----------------------------|
| BaseWeapon | Wide archetype (schema) |
| Dagger | Prefab (value template) |
| Item instance| Entity in Weapon archetype |

So:

- **BaseWeapon = Archetype**
- **IronDagger = Prefab**
- **Actual dagger in inventory = Entity**

---

## 13. Guardrails & Invariants

Must be true at all times:

- All columns in an archetype have the same count
- No new archetypes are created during gameplay
- Component registry is sealed before archetype creation
- Prefabs never change structure, only values

Debug checks (recommended):

- In DEBUG, assert column counts align occasionally
- Validate that all components are written after `AddEntityUninitialized`

---

## 14. What This Enables

With this design you can:

- Spawn millions of items per second
- Bulk load persistent worlds efficiently
- Keep simulation cache-friendly
- Write content in C# like RunUO scripts
- Still support data-driven tuning

### 14.1 Godot visual benchmark path (_tmp)

For end-to-end validation in engine runtime:

- Use `ECSVisualBenchmarkNode` to stage spawn ramps + benchmark windows.
- `ECSVisualBenchmarkNode` supports:
 - `StepSweep` (fixed entity-count ladder),
 - `FpsThresholdSweep` (target FPS search, e.g. 60/45/30/20).
- Use `ECSMultiMeshBridge` to sync ECS transform components into `MultiMeshInstance3D`.
- Emit benchmark lines (`BENCHMARK|...`) from Godot runs to compare against pure ECS test-suite results.
- Keep all Godot-only benchmark tooling inside `_tmp` to avoid contaminating core ECS code paths.
- Benchmark nodes default to `AutoRun = false`; profiling runs are opt-in per harness scene.

### 14.2 Runtime hygiene status

- Production scene (`_Scenes/main.tscn`) is kept free of `_tmp` benchmark scripts.
- Benchmark harness scene (`_tmp/ECSBenchmarkHarness.tscn`) is the dedicated profiling entrypoint.
- Structural mutation remains world-facade-only; archetype/entity manager internals are not public mutation APIs.

---

## 15. Summary

| Layer | Role |
|-----------|------------------------------------------|
| Archetype | Memory layout (schema) |
| Prefab | Initial values |
| Script | C# definition of a prefab |
| Entity | Runtime instance |
| Bulk load | Reserve + AddUninitialized + span writes |
