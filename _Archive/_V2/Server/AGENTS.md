# AGENTS.md

## Mission

Build the new server runtime as an engine-agnostic application core with thin wrapper shells.

The canonical topology is headless. Console and Godot are wrappers over the same core, not separate runtime implementations.

## Priority Order

Do not violate this order without explicit justification:

1. Correctness
2. Safety
3. Simplicity
4. Maintainability
5. Performance

## Core Architectural Rules

- Keep the real runtime engine-agnostic.
- Keep Godot, console IO, CLI parsing, and process supervision out of the runtime core.
- Keep startup/bootstrap thin.
- Prefer explicit lifecycle and ownership over ambient globals.
- Prefer composition over inheritance.
- Preserve one clear control surface for all wrappers.
- Treat host shells as adapters, not feature owners.

## Required Layering

Use this split unless there is a strong reason not to:

- `Server/Core`
  - engine-agnostic server application core
- `Server/Network`
  - server-side bridge between runtime state and `Kernels/Network`
- `Server/Hosting`
  - generic host-loop and command/log plumbing
- `Server/_Headless`
  - canonical self-hosted process entry point
- `Server/_Console`
  - thin wrapper with stdout/stderr and optional stdin command input
- `Server/_Godot`
  - thin operator shell with UI binding only
- `Server/Discovery`
  - discovery/status publishing adapters
- `Server/Scripting` or `Server/ScriptHost`
  - RunUO-style C# script compiler, cache manifests, diagnostics, assembly load, and script registration
- `Server/RuntimeTemplate`
  - source-controlled template for exported server `scripts`, `data`, `docs`, and `web` folders

## What Belongs In Core

- lifecycle state machine
- runtime composition
- world ownership
- transport integration
- tick entry points
- save/request-save/autosave semantics
- status/metrics snapshots
- command surface
- event stream for observability

## Script Compiler Boundary

Use [SCRIPT_COMPILER_AND_RUNTIME_LAYOUT_PLAN.md](SCRIPT_COMPILER_AND_RUNTIME_LAYOUT_PLAN.md) as the canonical plan.

Important rules:

- scripts are server-side only and trusted by the server operator
- client content patches must not deliver executable code
- editable script source belongs under exported `scripts/`
- generated compiler output belongs under `cache/scripts/` by default
- `scripts/Mobiles` is the preferred actor folder name, not `scripts/Mobs`
- human NPCs and player characters should share the same base human mobile shape; a player is a controlled human, not a separate runtime species
- entity-backed script content should register ECS prefab definitions on shared archetype shapes rather than creating a parallel script-only entity model
- archetype families such as `BaseItem > BaseWeapon > BaseMeleeWeapon > iron_dagger` are additive component-set composition and value overrides, not OOP entity inheritance
- wide components are fine for common shape data; add sub-archetype/effect components when systems need efficient query boundaries such as all timed, poisoned, burning, or aligned entities
- script-facing archetype/prefab helper classes are authoring facades over `ArchetypeSignature`, `PrefabDefinition`, and `PrefabCatalog`; do not move bespoke runtime entity inheritance into the simulation
- future live item/mobile/content builders should save versioned data-authored prefab descriptors in durable runtime data folders, not generated C# scripts and not disposable cache output
- data-authored prefabs must register into the same ECS prefab/runtime registries as script-authored prefabs; they may compose existing registered components/archetype descriptors, but new CLR component types or executable behavior require scripts/plugins/core code
- live-authored prefab saves must reject duplicate keys, use edit leases for concurrent staff edits, validate against registered assets/components/archetypes, and atomically swap registry snapshots
- dependent systems should collect rows from active registries and surface missing references through diagnostics; do not broadcast bespoke prefab-delete messages into every feature as the primary consistency mechanism
- future ModernUO-style serialization generation should target annotated ECS
  component fields and generate component serializers/migrations/admin metadata;
  do not reintroduce Item/Mobile entity-class serialization or property-setter
  dirty tracking outside ECS mutation paths
- most customization should compile as scripts; defer true custom-kernel/plugin loading until a real use case requires it
- headless, console, and Godot wrappers must use the same runtime-root resolver and script host

## What Belongs In Server/Network

- publish classification from server/runtime state into network scopes
- payload shaping for server-owned snapshots/events
- future cursor/subscription bridge logic that needs both server concepts and `Kernels/Network`

Keep this out of `Kernels/Network` itself. The network kernel stays engine-agnostic and ECS-agnostic.

## What Does Not Belong In Core

- Godot nodes, scenes, or controls
- console rendering or stdin loops
- CLI parsing
- process signal handling
- HTTP admin/status endpoints
- discovery tracker wiring specifics
- UI/login/character panel flow

If a wrapper needs to reach into internals to do something normal, that usually means the core contract is missing an API.

## Wrapper Rules

- wrappers may configure and drive the runtime
- wrappers may observe logs, status, and metrics
- wrappers may submit control commands
- wrappers may not own gameplay, session orchestration, or save logic
- wrappers may not duplicate lifecycle state
- wrappers should stay replaceable

## Runtime Contract Rules

The shared runtime surface should remain small and explicit:

- `Initialize(...)`
- `Start()`
- `Tick(...)`
- `RequestSave()`
- `Stop()`
- `Restart()`
- `GetStatusSnapshot()`
- `GetMetricsSnapshot()`
- `TryExecuteCommand(...)`

Expose observability through explicit events or snapshots, not wrapper-specific callbacks scattered across the runtime.

## Testing Rules

Write tests early and keep them close to the core contract.

Minimum required coverage as the runtime is rebuilt:

- lifecycle legality and state transitions
- start/stop/restart semantics
- request-save and autosave semantics
- fault transitions
- command dispatch behavior
- host-loop tick cadence behavior
- wrapper smoke coverage

## Benchmark Rules

Measure early for runtime-host concerns just like the ECS kernel:

- idle tick cost
- catch-up tick behavior
- command dispatch overhead
- log fanout overhead
- save barrier overhead
- transport pump overhead

Do not speculate on wrapper performance without a baseline.

## Single-Player Topology Rule

Future single-player should embed the same headless-style server core locally and connect through the same loopback/network path where practical.

Do not add a separate special-case "solo server" architecture if the standard embedded headless topology can serve the same role.
