# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GodotECS is a high-performance archetype-based Entity-Component-System framework for Godot 4.6 with C#. Engine-agnostic core (`ECS/`) with Godot integration via host injection. Server-authoritative networking layer (`Network/`) with client-side prediction. Targets .NET 8.0 (net9.0 for Android).

## Build & Test Commands

```bash
# Build the solution (main project + tests)
dotnet build GodotECS.sln

# Run all tests
dotnet test tests/ECS.Tests/ECS.Tests.csproj

# Run a single test by name
dotnet test tests/ECS.Tests/ECS.Tests.csproj --filter "FullyQualifiedName~TestMethodName"

# Run tests in a specific class
dotnet test tests/ECS.Tests/ECS.Tests.csproj --filter "FullyQualifiedName~ClassName"

# Open in Godot editor
godot --editor
```

Tests use xUnit with parallelization disabled. Test infrastructure in `tests/ECS.Tests/TestSetup.cs` provides `TestRuntime.ResetStatics()` to clear static registries between tests.

## Project Structure

| Directory | Role |
|-----------|------|
| `ECS/` | Engine-agnostic ECS core (entities, components, archetypes, systems, prefabs, dirty tracking) |
| `Network/` | Engine-agnostic networking runtime (transport, protocol, sessions, replication, client prediction) |
| `Server/` | Server-side Godot integration: networking bridge (`Engines/Networking/`) and spatial chunking (`Engines/Chunking/`) |
| `Client/` | Client-side Godot integration: networking bridge (`Engines/Networking/`) and presentation runtime (`Runtime/`) |
| `Shared/` | Shared bridge utilities (codecs, helpers) used by both Server and Client |
| `_Scenes/` | Godot scene files |
| `_tmp/` | Temporary test harnesses and scenes |
| `tests/ECS.Tests/` | xUnit test project |

Design docs: `Docs/ECS_PRD.md`, `Docs/NETWORK_PRD.md`, `Docs/NETWORK_IMPLEMENTATION_PLAN.md`, `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`, `Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`, `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`

## Architecture

### Core ECS Design (all in `ECS/` namespace)

**Entity** — Lightweight `readonly struct` handle with `Index + Version` fields. `Version != 0` means valid; version-bumped on recycle. `Entity.Null` is the sentinel.

**Component** — Must satisfy `where TSelf : unmanaged, IComponent<TSelf>`. This constraint is enforced at compile time via generics, never stored as interface values. Optional `ISerializableComponent<TSelf>` adds `static abstract` serialize/deserialize methods.

**Archetype** — Dense Structure-of-Arrays storage. Each unique component signature gets one archetype. Entity removal uses swap-remove. Column access via `GetComponentSpan<T>()` for zero-copy iteration.

**ArchetypeSignature** — Interned `ulong[]` bitset identifying a component set. Reference-equality fast path due to interning.

**Strongly-typed IDs** — `ComponentId`, `SystemId`, `PrefabId` are all readonly structs wrapping `int`. Never use raw ints.

### Registration & Sealing

All registries (`ComponentManager`, `PrefabRegistry`, `SystemManager`) follow a pattern: register during setup, then `Seal()` to freeze for lock-free reads at runtime. Post-seal registration throws.

### Structural Changes — Deferred Queue Model

**All entity creation/destruction/component add/remove is deferred.** Systems enqueue changes via `IStructuralCommands`; changes are applied at barriers.

`StructuralQueue` uses a swap-buffer pattern (write + read buffers). Apply order is deterministic: Destroy → Spawn → CreateDefault → Remove → Add.

Two barriers per tick:
- **Pre-barrier**: flush external enqueues before systems run
- **Post-barrier**: flush system enqueues after systems complete

### System Scheduling

Systems extend `SystemBase` with `Update(SystemContext)`. Each system has a `SystemDescriptor` declaring: stage, period (tick cadence), read/write component masks.

**9-stage pipeline**: Input → Movement → SpatialIndex → Perception → Combat → AI → WorldSim → Networking → Cleanup

**Conflict batching**: `SystemManager` analyzes read/write masks. Non-conflicting systems run in parallel batches; conflicting systems are serialized. Write∩Write and Write∩Read = conflict; Read∩Read = OK.

**Multi-rate cadence**: Systems declare `PeriodTicks`. Due-ness formula: `(worldTick - startOffset) % periodTicks == 0`.

### Prefabs & Spawning

Three registration modes:
1. **Compile-time**: `IPrefab<TSelf>` / `ITemplatePrefab<TSelf>` with `ApplyTo` methods
2. **Resolved columns**: Pre-computed column indices for hot-path spawning
3. **Data-driven**: Runtime JSON-loaded via `DataPrefabLoader`, supports hot reload

Bulk loading uses `Archetype.Reserve(count)` + `AddEntityUninitialized` + direct span writes.

### Dirty Tracking

Zero-allocation per-mark change tracking (`DirtyTracker`). Supports global and per-component dirty sets. Gated by `EcsOptions.EnableDirtyTracking`. Serial capture uses lock-free fast path; parallel capture uses worker-local buffers merged at barrier.

### Host Injection

`IHost` interface abstracts engine integration (logging, environment type). `IWorldEventSink` receives barrier-level structural events. Default no-op implementations for standalone/testing use.

### World Tick Loop

```
1. Auto-initialize if needed
2. Pre-barrier: ProcessStructuralQueues(PreSystems)
3. System tick (with capture windows for structural/dirty)
4. Post-barrier: ProcessStructuralQueues(PostSystems)
5. Increment worldTick
```

## Key Conventions

- **LF line endings enforced** via `.gitattributes` — all text files use LF
- **Nullable reference types enabled** project-wide
- **`[MethodImpl(AggressiveInlining)]`** on hot-path helpers
- **Span<T> access** for zero-copy array iteration in systems
- **No Godot types in `ECS/`** — the core module is engine-agnostic
- Components that need serialization implement `ISerializableComponent<TSelf>` with `BitReader`/`BitWriter`
- **No Godot types in `Network/`** — the network module is also engine-agnostic
- **`Network/` ↔ `ECS/` must not depend on each other** — integration happens only in bridge layers
- **Hot-path discipline** — no avoidable allocations, no interface boxing in packet fast paths; prefer `ArrayPool<byte>`, `Span<T>`, `ref struct`, and `delegate*` over managed abstractions
- **Stats are first-class** — all network subsystems expose stats types with snapshot capture; use `Interlocked` or `[ThreadStatic]` counters, never bare field access from multiple threads

### Network Layer (all in `Network/` — engine-agnostic)

Layered architecture for server-authoritative multiplayer with deterministic tick/barrier integration:

```
Game/App
 → Bridge Layers (Server/, Client/, Shared/Engines/Networking/)
    → Network Runtime (Network/)
    → ECS World (ECS/)
```

**10 sub-modules** in `Network/`:

| Module | Purpose |
|--------|---------|
| `Core/` | Transport abstraction (`INetworkTransport`), channel types, datagram structs, `PacketRegistry` dispatch |
| `Serialization/` | `BitReader`/`BitWriter` — `ref struct` zero-alloc binary serialization |
| `Framing/` | `LengthPrefixedFrameReader` — circular-buffer frame extraction with invalid-frame policies |
| `Protocol/` | Handshake flow (`HandshakeGate`, hello/result packets), feature flags, version negotiation |
| `Session/` | `NetworkSessionManager` — full session lifecycle (auth, active, timeout sweep, duplicate policies) |
| `Stats/` | `TransportStats`, `ProtocolStats`, `FrameReaderStats` — thread-safe counters with snapshot capture |
| `Runtime/` | `NetworkInboundCommandPipeline` — double-buffered command queue with rate limiting |
| `Replication/` | `NetworkReplicationPipeline` — per-connection budgeted delta replication with replay window, lane priorities, coalesce/overflow, group-based interest |
| `Client/` | Client-side prediction (`ClientPredictionStore`, `ClientInputSequencer`), reconciliation queue, interpolation buffer, correction stats |
| `Transport/` | `LoopbackNetworkTransport` — in-process loopback for testing/combined topology |

**Runtime topologies**: Dedicated server (headless), Thin client (projected slice), Combined/local (server + client in one process via loopback).

**Replication pipeline flow**: Dirty entities → `PublishCandidates` (encode via delegate) → `ReplicationReplayWindow` (tick-bucketed storage) → `BuildTick` (per-connection budget, interest filtering, lane priorities) → `ReplicationPacket` sink.

**Client prediction flow**: `ClientInputSequencer` (tagged input samples) → server processes → `ClientReconciliationQueue` (double-buffered corrections) → `ClientPredictionStore` (rollback + re-simulate) → `ClientInterpolationBuffer` (lerp for render).

### Bridge Layers

Bridge layers wire `ECS/` and `Network/` together without creating circular dependencies:

- **`Shared/Engines/Networking/`** — Reusable codec helpers (e.g., `EntityFloat3StateYawAckOwnerTick32Codec`, `InputFloat3Codec`). May reference both `ECS/` and `Network/`.
- **`Server/Engines/Networking/`** — Session-to-entity mapping, inbound command application, outbound replication sourcing.
- **`Client/Engines/Networking/`** — Input send wiring, reconciliation hooks, interpolation/render feed.

### Spatial Chunking (`Server/Engines/Chunking/`)

Grid-based spatial partitioning for interest management and replication fanout:
- `ChunkCoord` — 2D chunk coordinate with grid↔chunk conversion
- `SpatialChunkIndex` — tracks entity membership per chunk; `CollectEntitiesInSquare()` for area-of-interest queries
- Used by replication pipeline's group-based interest filtering to limit which connections receive which entity deltas

Design docs: `Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md` (three-layer chunking strategy)

### Client Runtime (`Client/Runtime/`)

Engine-agnostic client core separated from Godot-specific presentation:
- `ClientPresentationCommandBuffer` / `ClientPresentationCommandDispatcher` — decouple ECS state changes from UI/rendering via command queue
- Enables the core client (networking, prediction, reconciliation) to run without Godot dependencies

Design docs: `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`

## Test Infrastructure

Tests reference the main `GodotECS.csproj` and have access to:
- `TestRuntime.ResetStatics()` — clears all static registries (call in test setup)
- `TestHost` — captures log messages for assertions
- `RecordingEventSink` — captures structural barrier events and entity batches
- Test components: `TestPosition`, `TestVelocity`, `SerializableValue`
