# ECS Layered Architecture and Host Injection Guide

This doc captures the current ECS integration points, the dependency rule, and a vNext target
for a cleaner, engine-agnostic core with explicit host injection.

## Goals

- Keep Module.ECS engine-agnostic and reusable.
- Make integration explicit via a small host interface.
- Enforce one-way dependencies (no upward or sideways coupling).
- Minimize boxing and GC in hot paths.

## Have / Want / Need

### Have (current)
- World requires an `IHost` for runtime integration.
- Logging is routed through `Logging.DrainToHost()` into `IHost.Log`.
- Save/load uses `IHost.GetIOProfile()` in `World.Persistence`.
- Runtime context and environment capture exist (`RuntimeContext`, `HostEnvironment`).
- Deferred structural queues with predictable timing.

### Want (vNext)
- Smaller, stricter host surface; no engine types inside ECS.
- Zero/low boxing, unmanaged components, Span-first iteration.
- Explicit and enforceable dependency boundaries.
- Clear source-of-truth rule; derived caches are explicit.

### Need (non-negotiables)
- Deterministic structural changes at explicit barriers.
- Host injection only (no static engine singletons inside ECS).
- Stable component IDs (for save/network) or a clear mapping policy.
- Consistent ownership rules and minimal public API.

## Dependency Rule (Layered)

Dependencies flow downward only. No upward or sideways dependencies.

```
SCRIPTS (content)
  -> SYSTEMS (game logic)
     -> ENGINES (glue/integration)
        -> MODULES (ECS, Network, UI, Persistence)
           -> PRIMITIVES (collections, math, serialization, logging)
```

### Enforcement
- Project references must only point downward.
- Only `App.*` (composition root) wires concrete implementations.
- Module.ECS must not reference engine types (Godot, Unity, etc).

## ECS Host Interface (current usage)

Based on `UltraSim.IHost` and current ECS usage:

- `RuntimeContext Runtime { get; }`
- `EnvironmentType Environment { get; }`
- `object? GetRootHandle()`
- `void Log(LogEntry entry)`
- `IIOProfile? GetIOProfile()`

This is the minimum integration surface that ECS currently expects.

## Proposed Host Surface (vNext)

Keep a single host interface or split into small sub-interfaces. Either works
as long as the ECS module only depends on these abstractions.

### Option A: Single host interface (simple)
```
public interface IEcsHost
{
    RuntimeContext Runtime { get; }
    EnvironmentType Environment { get; }
    void Log(LogEntry entry);
    IIOProfile? GetIOProfile();
    object? GetRootHandle();
}
```

### Option B: Small interfaces (more explicit)
```
public interface IEcsHost
{
    IRuntimeContext Runtime { get; }
    ILogSink Logger { get; }
    IIOProfile? IO { get; }
    object? RootHandle { get; }
    EnvironmentType Environment { get; }
}
```

## Host Injection Pattern (engine-agnostic)

ECS never references engine types. The engine implements the host.

```
// Module.ECS
public sealed class World
{
    public IEcsHost Host { get; }
    public World(IEcsHost host) => Host = host;
}

// Engine.Godot
public sealed class GodotEcsHost : IEcsHost
{
    public RuntimeContext Runtime { get; }
    public EnvironmentType Environment => EnvironmentType.Client;
    public void Log(LogEntry entry) => GD.Print(entry.ToString());
    public IIOProfile? GetIOProfile() => new GodotIOProfile();
    public object? GetRootHandle() => GetTree().Root;
}
```

## Boundary Rules (explicit)

- Module.ECS must not reference:
  - Godot types
  - Client/Server namespaces
  - System.* subsystems or Scripts content
- Engines may reference Modules.
- Systems may reference Engines, not Modules directly.
- Scripts may reference Systems only.
- App layer is the only place that constructs concrete implementations.

## Data and Mutation Rules (core)

- Components are data-only (prefer unmanaged where possible).
- Systems mutate component data directly.
- Structural changes (create/destroy/add/remove) are queued and applied at barriers.
- Events carry identity/dirty sets, not full component payloads.
- Any cache is derived and rebuildable; ECS is the source of truth.

## Practical Checklist

- Can Module.ECS compile with no engine references? If not, fix the boundary.
- Does any system reach "up" into engine types? Move that to Engine layer.
- Are structural changes happening outside queues? Move them behind barriers.
- Is a cache treated as authoritative? Fix to make ECS the source of truth.

