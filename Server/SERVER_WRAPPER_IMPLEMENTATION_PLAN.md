# Server Wrapper Implementation Plan

## Purpose

This document defines the rebuild plan for the server runtime shells.

The goal is not to recreate the old `ServerRuntimeHostNode` shape.

The goal is to:

- rebuild the server runtime around a small explicit engine-agnostic core,
- keep `headless` as the canonical host topology,
- make `console` and `Godot` wrappers over the same control surface,
- validate the runtime with tests and benchmarks early,
- preserve the old use-cases without carrying forward the old host-node coupling.

## Executive Decision

Build the server in layers.

Use the current `Server` folder as the home for the subsystem, but tighten the layout:

```text
Server/
  AGENTS.md
  SERVER.md
  SERVER_WRAPPER_IMPLEMENTATION_PLAN.md
  Core/
  Hosting/
  Discovery/
  _Headless/
  _Console/
  _Godot/
```

Why this layout:

- `Core` is the reusable application runtime
- `Hosting` is the generic loop/control glue used by every shell
- `_Headless`, `_Console`, and `_Godot` are host fronts, consistent with the existing underscore wrapper naming
- `Discovery` remains a separate adapter area, not something baked into the core

## Design Position

The real reusable unit is not the UI wrapper.

The real reusable unit is the engine-agnostic server application core.

Topologies:

- `headless`: canonical dedicated server process
- `console`: same core with text output and optional command input
- `Godot`: same core with operator UI
- future single-player: embedded headless-style server plus local client connection

This means:

- no gameplay/runtime fork for console
- no Godot-owned server lifecycle logic
- no separate single-player-only server architecture

## Old-Code Lessons To Preserve

Keep from the old server runtime:

- explicit lifecycle states
- explicit `Start / Stop / Restart / Save / Tick` semantics
- explicit event hub / observability surface
- tests around save and lifecycle edge cases

Do not carry forward from the old Godot host node:

- mixed ownership of bootstrap, runtime composition, transport creation, save wiring, discovery wiring, login flow, and UI flow in one class
- wrappers reaching directly into runtime internals for normal operations
- Godot-specific orchestration becoming the effective runtime API

## Proposed Runtime Contract

The shared runtime/application contract should stay intentionally small:

```csharp
public interface IServerApplication
{
    ServerApplicationState State { get; }
    bool IsInitialized { get; }
    bool IsRunning { get; }

    void Initialize();
    void Start();
    void Tick(float deltaSeconds);
    void RequestSave();
    bool Save();
    void Stop();
    void Restart();

    ServerStatusSnapshot GetStatusSnapshot();
    ServerMetricsSnapshot GetMetricsSnapshot();
    bool TryExecuteCommand(ServerControlCommand command, out ServerCommandResult result);

    event Action<ServerApplicationEvent> EventRaised;
}
```

Notes:

- exact type names can change
- the shape should not grow casually
- wrappers should be able to operate the runtime using only this surface plus configuration/bootstrap inputs

## Responsibility Split

### `Server/Core`

Owns:

- runtime lifecycle state machine
- world and kernel ownership
- transport/session integration
- save/autosave/request-save behavior
- runtime status snapshots
- metrics snapshots
- control command surface
- event publishing
- composition of gameplay/runtime services

Does not own:

- CLI parsing
- stdin/stdout handling
- Godot nodes or controls
- HTTP status servers
- tracker publishing loops
- wrapper-specific timers beyond the explicit tick API

### `Server/Hosting`

Owns:

- host loop driver
- wall-clock tick cadence and catch-up policy
- cancellation/shutdown coordination
- log fanout
- command queue plumbing
- basic config-to-core bootstrap helpers

Does not own:

- gameplay logic
- world logic
- wrapper-specific rendering or UI

### `Server/_Headless`

Owns:

- process entry point
- config loading and command-line parsing
- signal/shutdown handling
- starting the host loop

Does not own:

- runtime business logic

### `Server/_Console`

Owns:

- console output formatting
- optional stdin command input
- same lifecycle controls as headless

Does not own:

- extra server behavior

### `Server/_Godot`

Owns:

- Godot-side operator controls
- status/log display
- calling the shared control surface

Does not own:

- server gameplay/session/account flow
- alternate lifecycle semantics

### `Server/Discovery`

Owns:

- discovery publishers
- status endpoint adapters
- tracker-specific contracts

Does not own:

- runtime state machine

## Naming Recommendation

Use these names unless the first implementation exposes a better reason to change them:

- `UltraSim.Server.Core`
- `UltraSim.Server.Hosting`
- `UltraSim.Server.Headless`
- `UltraSim.Server.Console`
- `UltraSim.Server.Tests`

For Godot:

- keep the existing `Server/_Godot` project folder
- treat it as the shell project, not the runtime home

## Initial Folder / Project Targets

First wave:

- `Server/Core/`
- `Server/Hosting/`
- `Server/_Headless/`
- `Server/_Console/`
- `_Tests/UltraSim.Server.Tests/`
- optionally `_Benchmarks/UltraSim.Server.Benchmarks/`

Second wave:

- wire `Server/_Godot/` to the same host/controller contract
- flesh out `Server/Discovery/` once the control and status surfaces are stable

## Rebuild Sequence

### Phase S1: Contracts And Guardrails

Deliver:

- `Server/AGENTS.md`
- this implementation plan
- minimal `Server/Core` contract types
- `ServerApplicationState`
- status/metrics snapshot types
- control command types
- test project scaffold

Done when:

- the runtime surface is explicit on paper and in code
- wrappers have a clearly bounded target API

### Phase S2: Core Lifecycle Skeleton

Deliver:

- `Server/Core` application state machine
- `Initialize`
- `Start`
- `Tick`
- `RequestSave`
- `Save`
- `Stop`
- `Restart`
- event hub / event stream

Done when:

- lifecycle legality is test-covered
- save semantics mirror the old intended behavior where still desired

### Phase S3: Hosting Loop

Deliver:

- generic host loop in `Server/Hosting`
- fixed-step tick driver
- catch-up policy
- cancellation model
- log routing abstraction
- command queue abstraction

Done when:

- the runtime can be driven without Godot
- loop behavior is test-covered

### Phase S4: Headless Shell

Deliver:

- `Server/_Headless`
- config/bootstrap path
- start/stop lifecycle wiring
- clean process shutdown

Done when:

- a dedicated process can run the runtime with no wrapper-specific leaks

### Phase S5: Console Shell

Deliver:

- `Server/_Console`
- stdout log display
- basic stdin control commands:
  - `save`
  - `shutdown`
  - `restart`
  - `status`

Done when:

- console is only a thin control/view shell over the same hosted runtime

### Phase S6: Godot Shell

Deliver:

- replace old Godot host-node ownership with thin operator bindings
- buttons and status panels call the shared host/controller APIs
- no gameplay/session orchestration lives in Godot shell code

Done when:

- Godot shell contains UI/operator code only

### Phase S7: Discovery / Operational Adapters

Deliver:

- tracker publishing adapters
- optional status endpoint adapter
- any process-level operational extras that depend only on snapshots/events

Done when:

- discovery/ops concerns remain adapters rather than core runtime ownership

## Test Plan

Start tests immediately in `_Tests/UltraSim.Server.Tests/`.

First required test groups:

- lifecycle transition legality
- created -> initialized -> running -> stopped
- restart semantics
- fault transition behavior
- request-save deferral semantics
- manual save semantics
- stop waits for in-flight save completion
- command dispatch behavior
- headless host-loop smoke test
- console command-input smoke test

Old tests worth using as shape references:

- `ServerRuntimeSessionLifecycleTests`
- `ServerRuntimeAutosaveTests`

Reuse the intent, not the old static-reset assumptions.

## Benchmark Plan

Add benchmarks early once `Server/Core` and `Server/Hosting` exist.

Initial benchmark targets:

- idle hosted tick cost
- fixed-step catch-up overhead
- command dispatch overhead
- log fanout overhead
- save barrier overhead
- no-op transport pump overhead

Only expand beyond that once the rebuilt runtime has real transport/gameplay slices again.

## Acceptance Criteria Before Phase 2

Before shifting focus back to broader gameplay/persistence/network work:

- headless shell runs cleanly
- console shell runs cleanly
- Godot shell is designed against the same control surface, even if implemented last
- lifecycle semantics are test-covered
- save semantics are test-covered
- basic host-loop benchmarks exist
- wrappers do not reach into internals for normal operations

## Risks To Watch

- letting wrapper needs bloat the core API
- rebuilding another giant Godot owner class
- mixing operator UX flow with runtime logic
- embedding discovery/status server concerns into the core
- skipping host-loop benchmarks until after the runtime grows complicated

## Recommendation

Implement this in the following immediate order:

1. create the server contract and tests
2. build `Server/Core`
3. build `Server/Hosting`
4. build `Server/_Headless`
5. build `Server/_Console`
6. review the shell API again
7. only then wire `Server/_Godot`

That preserves the clean kernel-style workflow:

- explicit boundaries first
- tests early
- benchmarks early
- wrappers last
