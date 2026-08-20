# Server Runtime Architecture and Roadmap

Purpose: define the tracked server-side runtime shape that all server hosts should share, document what is already implemented, and make the remaining work explicit.

This is the server-side companion to:
- `Docs/NETWORK_PRD.md`
- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE.md`
- `Docs/TMP_TO_OFFICIAL_SERVER_MIGRATION_CHECKLIST.md`
- `Docs/AUTHORITATIVE_PHYSICS_ARCHITECTURE.md`
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`

The goal is to keep the real server implementation in `UltraSim/Server/` and keep host shells thin.

## 1. Host Model

There are three practical host shapes:

1. Embedded single-player server
   - lives inside the client process
   - uses the same core runtime as the dedicated server
   - does not require an external operator UI
   - may expose admin/debug controls through server-driven menus later

2. Headless dedicated server
   - same core runtime as the embedded server
   - launched via script, service, CLI, or batch file
   - no operator UI required

3. Operator-hosted server shell (`Godot.Server` / WinApp)
   - same core runtime as headless/embedded
   - thin UI shell for logs, start/stop/save/restart/quit, and status pages
   - should not own gameplay or network/runtime logic

Rule: `UltraSim/Server/` owns server behavior. Host apps own presentation, process/bootstrap, and operator controls.

## 2. Package Shape

Target package shape:

- `UltraSim/Server/`
  - `ServerRuntime`
  - `ServerRuntimeState`
  - `ServerRuntimeOptions`
  - `ServerEventHub`
  - `ServerRuntimeFault`
  - lifecycle/persistence/config abstractions
- `UltraSim/Server/Engines/Networking/`
  - ECS/network bridge code
  - session bindings
  - replication bridge
- `UltraSim/Server/UI/`
  - server-driven panel orchestration
  - panel response dispatch
  - server-side UI datagram handling
- `Godot.Server/`
  - operator shell only
- headless bootstrap
  - process/app entrypoint only
- embedded single-player bootstrap
  - process/app entrypoint only

## 3. Desired Runtime Responsibilities

`ServerRuntime` should eventually own:

- runtime lifecycle state
- world/ECS ownership
- network transport ownership
- session manager ownership
- replication bridge ownership
- authoritative physics service ownership
- server UI panel service ownership
- persistence/load/save orchestration
- host-facing events/observability
- operator-safe start/stop/save/restart entrypoints

It should not own:
- Godot operator UI
- dedicated-process shell concerns
- embedded-client-specific UI behavior
- game-specific menu content definitions directly

## 4. State Machine

Target runtime state machine:

- `Created`
- `Initializing`
- `Initialized`
- `Starting`
- `Loading`
- `Running`
- `Saving`
- `Stopping`
- `Stopped`
- `Restarting`
- `Faulted`

Why this matters:
- avoids boolean drift (`IsRunning`, `IsStopping`, `IsSaving`, etc.)
- makes lifecycle preconditions explicit
- keeps host behavior deterministic
- gives tests a clear contract

Expected method/state preconditions:

- `Initialize()`
  - valid from `Created`
- `Start()`
  - valid from `Created`, `Initialized`, or `Stopped`
  - `Created` may implicitly initialize first
- `Tick()`
  - valid only from `Running`
- `Save()`
  - valid from `Running` or `Stopping`
  - if already `Saving`, reject or no-op
- `Stop()`
  - valid from `Initialized`, `Running`, `Faulted`, or `Stopped`
- `Restart()`
  - valid from `Initialized`, `Running`, `Stopped`, or `Faulted`

## 5. Startup / Shutdown Ordering

### 5.1 Desired Startup Order

1. process/host starts
2. host loads config/options
3. host constructs `ServerRuntime`
4. runtime `Initialize()`
   - create services
   - create session manager
   - create UI service
   - create network/runtime support objects
5. runtime `Start()`
   - create/prepare world
   - start non-network server services
   - load persistent state
   - perform post-load initialization
6. start listener / allow connections
7. state = `Running`

Rule: do not accept client traffic until world/load/startup is complete.

### 5.2 Desired Tick Ordering

The server runtime tick should eventually make the ordering explicit:

1. poll/decode inbound network
2. apply inbound pre-barrier commands
3. tick the ECS world
4. build/send replication and other outbound server traffic
5. flush outbound transport
6. record metrics / host-visible snapshots

This mirrors the network PRD assumptions and should be visible in `ServerRuntime.Tick()` even before every subsystem is fully wired.

### 5.3 Desired Shutdown Order

1. transition to `Stopping`
2. stop accepting new connections
3. disconnect active sessions cleanly
4. save world state if configured/requested
5. stop internal services/background work
6. stop transport
7. state = `Stopped`

### 5.4 Restart / Fault Handling

Restart:
- stop current runtime state cleanly
- reinitialize or reuse services as appropriate
- start again

Fault:
- transition to `Faulted`
- emit a runtime fault event
- host decides whether to:
  - restart the runtime
  - shut down the host
  - show an error UI and return to menu (embedded single-player)

## 6. Event Model

Recommendation: runtime-owned event hub, not a global static event sink.

Why not a global static `EventSink`:
- bad fit for embedded single-player + dedicated + tests
- hard to isolate in tests
- encourages hidden global coupling
- makes multiple runtime instances unsafe

### 6.1 Event Categories We Want

Lifecycle:
- `StateChanged`
- `Faulted`
- eventually `SavingStarted`, `SavingCompleted`, etc. if needed

Session/network:
- `ClientConnected`
- `ClientDisconnected`
- `SessionReady`
- `SessionAuthenticated`

UI:
- `PanelAckReceived`
- `PanelResponseReceived`
- eventually `PanelOpened`, `PanelClosed` if needed for diagnostics

Messaging/ops:
- `ChatReceived`
- `ChatSent`
- `CommandReceived`
- `CommandHandled`

Persistence/world:
- `WorldLoading`
- `WorldLoaded`
- `WorldSaving`
- `WorldSaved`

Rule: events are for observation, extension hooks, and host integration. Primary control flow should stay explicit in runtime/services.

## 7. Configuration / Host Options

Split config into two levels.

### 7.1 Host-level settings

Examples:
- `AutoStart`
- whether an operator window should exist
- process/service shell behavior
- CLI/launcher choices

These belong to the host, not core runtime logic.

### 7.2 Runtime/server settings

Examples:
- tick rate / tick milliseconds
- bind host/port
- save path / world profile name
- autosave policy
- whether the listener should start on `Start()`

These belong to `ServerRuntimeOptions`.

## 8. Save Policy

Current policy is explicit:

- `RequestSave()` schedules capture at the end-of-tick barrier
- the barrier captures account + world snapshots while ECS state is quiescent
- account/world writes then continue on the background save path
- one active background write is allowed at a time; additional requests coalesce to the next barrier
- direct/manual `Save()` still exists and still completes synchronously when called explicitly

This keeps autosave/manual-save collisions deterministic without letting autosave block on full file IO.

Current implementation note:

- autosave now pays the snapshot/barrier cost on the runtime thread, not the full write/flush cost
- account persistence and world persistence are both written from snapshots on the background path
- the hot-path number that now matters most is barrier time, not full completion time
- future persistence work should focus on snapshot cost or incremental capture only if the current barrier becomes too large

## 9. Current Status

### 9.1 Implemented Today

Tracked in repo:

- `UltraSim/Server/`
  - `ServerRuntime`
  - `ServerRuntimeState`
  - `ServerRuntimeOptions`
  - `ServerRuntimeFault`
  - `ServerEventHub`
  - `ServerRuntimeHooks`
  - `ServerRuntimeStatusSnapshot`
  - `WorldPersistenceCoordinator`
  - `WorldPersistenceOptions`
  - `WorldPersistenceRecoveryMode`
  - `WorldPersistenceLoadSummary`
  - `IWorldPersistenceRecoveryInteraction`
  - `ConsoleWorldPersistenceRecoveryInteraction`
  - `ServerAccountPersistenceCoordinator`
  - `ServerPersistenceCoordinator`
  - account/world persistence timing snapshots
  - `ServerPersistenceRegistration`
  - runtime-driven session lifecycle event publication from `NetworkSessionManager`
  - runtime-owned control-datagram routing for ping/UI/chat control traffic
  - runtime-owned save/load hooks
  - save-on-stop and interval autosave policy
  - end-of-tick snapshot barrier + async background save path
  - staged world-save write + promote/replace flow

- `UltraSim/Server/Engines/Networking/`
  - `ServerNetworkBridge<TCommand>`
  - `ServerNetworkReplicationBridge`
  - `ServerSessionEntityBindings`
  - `ServerNetworkControlService`
- `UltraSim/Server/Gameplay/`
  - `ServerPlayerSessionWorld`
  - restart-safe character-backed player entity persistence
  - disconnect-safe in-memory parking and rebind for character-backed entities
  - session rebind to loaded player entities by `CharacterId`
  - tracked player ECS world/bootstrap outside `_tmp`
  - tracked player input queue/apply/movement tick outside `_tmp`
  - `ServerPlayerReplicationService`
  - tracked baseline correction/despawn replication outside `_tmp`
  - tracked baseline chunk-AOI visibility filtering outside `_tmp`
  - tracked owner-ack patching and observer-payload compaction outside `_tmp`
  - tracked correction batching and deadband policy outside `_tmp`
  - tracked chunk-based recipient-selector fast path outside `_tmp`
  - tracked dual correction streams outside `_tmp`
  - tracked minimal priority-LOD and dense-observer shaping outside `_tmp`
  - tracked AOI despawn grace / pending-despawn handling outside `_tmp`
- `UltraSim/Network/Transport/`
  - `TcpFramedNetworkTransport` is now tracked, not `_tmp`-local
- `UltraSim/Server/UI/`
  - `ServerUiPanelService`
  - `ServerUiInboundDatagramHandler`
- `UltraSim/Server/Chat/`
  - `ServerChatService`
- `UltraSim/UI/Contracts/`
  - panel contract types
  - panel transport codec
- `UltraSim/Discovery/`
  - discovery publisher/query interfaces
  - canonical advertised/status/row models
  - in-memory query implementation
- `UltraSim/Network/Protocol/`
  - shared network control/chat protocol codecs and message types
  - shared despawn codec
- `UltraSim/Client/Runtime/UI/`
  - `ClientUiPanelRuntime`
  - `ClientUiInboundDatagramHandler`
- `UltraSim/Client/Runtime/Discovery/`
  - browser service
  - provider snapshots/definitions
  - multiplayer browser controller
- `Client.Godot/`
  - client transport/runtime can now receive/send UI panel traffic
  - bundled `MessageBox` exists
  - multiplayer browser/menu now consumes normalized discovery rows
- `Server.Godot/`
  - tracked server host bootstrap exists
  - current host is minimal and runtime-owned, not the final operator shell
  - periodic host status logging now includes tracked ping-probe receive counts
  - tracked host now polls real transport connection events and inbound control traffic
  - tracked host now drives player session/entity spawning through `ServerRuntime`
  - tracked host now drives player input into the runtime-owned world tick path
  - tracked host now exercises runtime-owned baseline replication/correction flow
  - tracked host now exposes baseline chunk-AOI settings through runtime options
  - tracked host now wires account persistence coordinator creation and runtime load/save hooks
  - tracked host now wires world persistence coordinator creation and save path overrides
  - tracked host now injects a recovery interaction that logs partial-load state and falls back to configured policy
  - runtime save now snapshots offline player fallback state into account persistence
  - runtime save now compacts offline parked persistent player entities after snapshot capture
- `UltraSim/ECS/Persistence/`
  - explicit `PersistenceId` registry and type database support
  - save-set path/header helpers
  - system persistence serializer
  - entity persistence signature model and save writer
  - entity load create/remap, hydration, and reference fixups

Local-only proof wiring still exists in `_tmp/ECSNetworkIslandSliceNode.cs`, but it is no longer the only tracked home for server runtime ownership.

### 9.2 Not Yet Implemented

Tracked server runtime pieces still missing:

- tracked operator shell UI in `Server.Godot`
- tracked headless bootstrap
- tracked embedded single-player host/bootstrap
- broader world/ECS ownership under `ServerRuntime`
- authoritative physics service/backend integration
- adaptive / higher-order pressure-cap and priority-LOD policy under `ServerRuntime`
- deeper session/network event publication from the real transport accept path
- full gameplay-aware chat identity/permission flow migrated out of `_tmp`
- other runtime services migrated out of `_tmp`
- real production server-side gameplay/menu flows outside `_tmp`
- interactive/operator-facing partial-load recovery UX
- broader offline-character policy beyond the current parked-entity approach
- more advanced snapshot/incremental save policy if the current async snapshot path becomes insufficient

### 9.3 What This Means For UI Work

Menu/panel work is far enough along to continue, and there is now a tracked runtime owner plus a tracked server-host seam.

What is still true:
- the richer gameplay/server flows still depend on `_tmp` for world/bridge/gameplay orchestration
- the tracked server host is currently a bootstrap shell, not the final operator UI
- finishing the break away from `_tmp` now depends more on subsystem migration than on panel-contract work

Most of the remaining `_tmp` ownership is now concentrated in:
- gameplay-aware chat identity / permission / command expansion
- adaptive / higher-order replication tuning knobs
- harness/stress/debug scene behavior that should likely stay in `_tmp`

The next platform-defining runtime seam after `_tmp` break-away is authoritative physics:

- server-authoritative gravity/collision/query integration
- backend-agnostic contracts in core code
- first backend in `Server.Godot` using Godot/Jolt

### 9.4 Validation and persistence perf status

Tracked persistence validation now includes:

- full-stack runtime integration coverage
  - account store + world save-set written through the real runtime hook composition
  - restart/load verification for account data, entity files, and `CharacterId` rebind
- dedicated persistence perf coverage under `tests/Server.PersistencePerf/`
  - players-only, items-only, and mixed scenarios
  - per-phase save/load timing
  - allocation, GC, and file-size reporting
- dedicated live-load persistence coverage under `tests/Server.NetworkPersistencePerf/`
  - real transport connection pumping
  - active harness bots that send movement/input instead of idle sockets
  - before/during/after tick windows plus barrier/completion timing

Representative current perf baseline (`Release`, `--iterations 3`):

- `1000` players
  - save avg `5.491 ms`
  - load avg `5.966 ms`
- `1000` players + `100000` items
  - save avg `42.941 ms`
  - load avg `16.062 ms`
  - save p95 `54.627 ms`
  - load p95 `19.191 ms`

Representative live-load runs (`Release`, serial save mode):

- `100 players + 1000 items + 50 active bots`
  - before tick avg/p95 `0.469 / 0.729 ms`
  - save barrier `10.594 ms`
  - save completion `66.024 ms`
  - after tick avg/p95 `0.432 / 0.604 ms`
- `1000 players + 100000 items + 100 active bots`
  - before tick avg/p95 `1.540 / 1.946 ms`
  - save barrier `12.496 ms`
  - save completion `89.610 ms`
  - background write `32.017 ms`
  - after tick avg/p95 `1.447 / 1.836 ms`

Current measured scaling shape:

- save completion cost is dominated by world entity write time
- save barrier cost is materially lower than full completion cost because writes happen from snapshots on the background path
- load cost is dominated by hydrate time, then entity create time
- fixups are negligible in the current player/item scenarios

### 9.5 Practical priority read

For day-to-day planning, the runtime/platform work now falls into these buckets.

Done enough:
- runtime lifecycle shell and tracked host bootstrap
- core session/player world/runtime ownership baseline
- snapshot-based persistence with background write path
- live-load persistence validation with active bot traffic

Near-term remaining work:
- finish chat migration/polish
  - permissions/access levels
  - broader command surface
  - fold save/autosave player-facing notifications into that pass
- continue `_tmp` migration where it still touches official runtime ownership
- operator shell / recovery UX polish
- remaining client/menu usability polish that depends on the runtime being stable

Later:
- authoritative physics integration
- broader world/ECS ownership cleanup beyond current minimum platform needs
- more advanced snapshot/incremental save only if the current barrier/write behavior stops being sufficient

### 9.6 Planned review pass

The next deliberate step should be a short review/verify pass before another major subsystem lands.

Ordered tasks:
- review pass 1: trace the real login/character/session event flow and document what is still direct host orchestration
- review pass 2: run mixed live-load + ECS-heavy + autosave validation and recheck tick/p95/save behavior
- review pass 3: add direct bandwidth telemetry and measure bytes/packets per connection
- review pass 4: validate a minimal headless host and use that result to tighten host/runtime ownership boundaries

Review pass 1 status:
- partially complete
- confirmed: the runtime owns bind/unbind on session lifecycle transitions, but `Server.Godot/ServerRuntimeHostNode.cs` still directly orchestrates login success, character select/create, and the final authenticated -> ready transition before runtime bind occurs
- confirmed: quit-to-character-select and normal disconnect cleanup both resolve through runtime lifecycle events and host-side cache cleanup without an obvious state leak in the current hosted ordering
- confirmed: autosave should see post-disconnect state in the normal hosted ordering because transport disconnects are processed before `Tick()` and lifecycle events are drained before the end-of-tick save barrier
- remaining review-pass-1 work: add focused regression tests for those edge cases and verify whether archetype fallback is ever hit in normal character flows

## 10. Bare-Minimum Server Skeleton

The minimal tracked server skeleton now exists:

- `UltraSim/Server/ServerRuntimeState.cs`
- `UltraSim/Server/ServerRuntimeOptions.cs`
- `UltraSim/Server/ServerRuntimeFault.cs`
- `UltraSim/Server/ServerEventHub.cs`
- `UltraSim/Server/ServerRuntime.cs`
- `Server.Godot/ServerRuntimeHostNode.cs`
- `Server.Godot/_Scenes/server_runtime_host.tscn`

Minimum `ServerRuntime` ownership for now:
- `NetworkSessionManager`
- `ServerUiPanelService`
- optional transport reference
- runtime state
- event hub
- lifecycle entrypoints (`Initialize`, `Start`, `Tick`, `Save`, `Stop`, `Restart`)

It is still acceptable for the current skeleton to use placeholder hooks for:
- world creation
- network bridge wiring
- replication wiring

The skeleton exists so future work has the correct home and lifecycle shape while `_tmp` responsibilities are migrated deliberately.

## 11. Implementation Roadmap From Here

### Phase A - tracked skeleton
- completed
- runtime state/options/event hub/runtime shell exist
- `ServerUiPanelService` is owned by the runtime
- UI service events route into the runtime event hub

### Phase B - tracked runtime wiring
- partially complete
- simple panel flow exists through the tracked runtime
- a tracked server host/bootstrap now exists
- `_tmp` still owns most world/bridge/gameplay orchestration

### Phase C - host shells
- `Server.Godot` bootstrap shell exists
- still needed:
  - embedded single-player bootstrap
  - headless bootstrap
  - full operator shell UI

### Phase D - broader server lifecycle
- persistence/load/save implementation
- autosave option plumbing
- broader world creation and ECS tick ownership
- network listener/accept ownership folded fully into runtime rather than only the tracked host
- advanced AOI-aware outbound correction flow and broader bridge ownership
- operator actions / recovery UX hardening
- diagnostics/status pages

### Phase E - `_tmp` migration
- continue moving session/connect/disconnect flow into tracked server ownership
- continue moving ping/status service out of `_tmp`
- continue moving chat out of `_tmp`
- fold save/autosave player-facing notification polish into the later chat rework/polish pass
- continue moving world/replication/AOI orchestration out of `_tmp`
- move world tick/bridge ownership under `ServerRuntime`
- keep benchmarks/harnesses in `_tmp` when they are intentionally non-production

## 12. Design Rules To Preserve

- `UltraSim/Server` owns server behavior; hosts own presentation/bootstrap only.
- Runtime lifecycle should be explicit and stateful, not boolean soup.
- Events are scoped to the runtime instance, not global statics.
- Events are for observation and integration, not hidden gameplay control flow.
- Listener/accept comes after world/load readiness.
- `Tick()` ordering should remain explicit and visible.
- `Save()` reentrancy policy should stay deterministic.
- UI work should continue to build against tracked runtime/server ownership, not `_tmp` seams.
- `_tmp` remains valid for benchmarks and experiments, but not as the permanent home of official server bootstrap or production subsystems.
