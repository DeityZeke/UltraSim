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
