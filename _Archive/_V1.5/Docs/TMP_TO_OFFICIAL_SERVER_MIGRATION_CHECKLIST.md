# `_tmp` to Official Server Migration Checklist

Purpose: track what should move out of `_tmp`, what already has a tracked home, what should move next, and what should intentionally remain as harness/experimental code.

This is the companion migration checklist for:
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE.md`
- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`
- `Docs/AUTHORITATIVE_PHYSICS_ARCHITECTURE.md`

---

## 1) Core Rule

`_tmp` is still valid for:
- benchmarks
- harnesses
- smoke tests
- experiments
- profiling/debug-only scenes

`_tmp` should not remain the permanent home for:
- official server bootstrap
- production runtime ownership
- production session/chat/menu services
- long-lived gameplay/network orchestration

---

## 2) What Is Already Tracked

These pieces now have tracked homes outside `_tmp`:

- server runtime shell
  - `UltraSim/Server/ServerRuntime.cs`
  - `UltraSim/Server/ServerRuntimeState.cs`
  - `UltraSim/Server/ServerRuntimeOptions.cs`
  - `UltraSim/Server/ServerEventHub.cs`
- server-driven UI service/runtime
  - `UltraSim/Server/UI/ServerUiPanelService.cs`
  - `UltraSim/Server/UI/ServerUiInboundDatagramHandler.cs`
  - `UltraSim/Client/Runtime/UI/ClientUiPanelRuntime.cs`
  - `UltraSim/Client/Runtime/UI/ClientUiInboundDatagramHandler.cs`
- shared UI/discovery contracts
  - `UltraSim/UI/Contracts/...`
  - `UltraSim/Discovery/...`
- client discovery/browser runtime
  - `UltraSim/Client/Runtime/Discovery/...`
- tracked server host seam
  - `Server.Godot/...`
- account/login system
  - `UltraSim/Server/Engines/Accounting/` — models, registry, storage, `ServerAccountService`
  - `UltraSim/UI/Contracts/LoginPacketCodec.cs` — challenge-response wire codec
  - `Server.Godot/UI/LoginPanel.cs` — schema-rendered login panel
  - `Server.Godot/ServerRuntimeHostNode.cs` — login challenge generation, `OnLoginRequest` handler
  - `Client.Godot/UI/MenuCanvasManager.cs` — packet-bound login action, SHA-256 chain
  - `Client.Godot/GodotClientNetworkRuntime.cs` — `TrySendLoginRequest`, login response dispatch

Implication:
- `_tmp/ECSNetworkIslandSliceNode.cs` is no longer the only place server ownership exists
- the remaining `_tmp` work is now mainly about runtime-service extraction and orchestration migration

---

## 3) Move Next

These items should move next because they unblock the break-away from `_tmp` most directly.

Note:
- this checklist is about removing `_tmp` as the owner of real runtime behavior
- once that break-away is substantially complete, the broader pre-terrain platform work is tracked in `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`

### 3.1 Official Server Host / Bootstrap

Status:
- in progress
- tracked server host now uses the tracked `TcpFramedNetworkTransport`
- tracked host now polls connection events and inbound control datagrams

Target home:
- `Server.Godot/` for the Godot host/bootstrap shell
- `UltraSim/Server/` for host-agnostic lifecycle/runtime ownership

Includes:
- tracked server scene/bootstrap
- tracked runtime creation/start/stop/tick ownership
- eventual headless/bootstrap parity

Why first:
- every other subsystem still depends on how the server runtime is created and driven

### 3.2 Session Connect / Disconnect Flow

Target home:
- `UltraSim/Server/`
- `UltraSim/Server/Engines/Networking/`

Status:
- in progress
- runtime-driven session lifecycle publication now exists
- tracked host now creates/removes sessions from real transport connection events
- player session/entity binding now has a tracked home via `UltraSim/Server/Gameplay/ServerPlayerSessionWorld.cs`

Includes:
- connection accepted -> session created
- disconnect cleanup
- session lifecycle publication into `ServerEventHub`

Why:
- this is foundational for chat, auth, menus, and spawning

### 3.3 Ping / Status Service

Target home:
- `UltraSim/Server/`
- or `UltraSim/Server/Networking/` if it remains transport-adjacent

Status:
- in progress
- tracked ping-probe handling now exists in `UltraSim/Server/Engines/Networking/ServerNetworkControlService.cs`
- final transport polling/accept integration still remains to move

Includes:
- server-owned ping/pong handling
- runtime status publication
- simple connect/readiness diagnostics

Why:
- simpler than chat/auth
- useful for both operator shell and client UX

### 3.4 Chat

Target home:
- `UltraSim/Server/Chat/`

Status:
- substantially complete

Done:
- tracked `ServerChatService` owns all production chat behavior
- shared client/server chat protocol codecs live under `UltraSim/Network/Protocol/`
- tracked runtime control-datagram routing covers UI/chat/ping control traffic
- tracked chat owns:
  - per-session chat cooldown
  - local-scope recipient selection
  - dense local-chat recipient caps
  - dense local-chat rate caps
  - server-side chat batching
  - default slash-command registry (`/help`, `/ping`)
  - gameplay-aware sender naming (archetype display name, e.g. "Default #4")
- `_tmp/chat/` items are classified as debug/harness tooling only:
  - `FloatingActorLabelStack*` — overhead label rendering (local preview visual)
  - `ChatPresentationProfile.cs` — prototype UI knobs (debug only)
  - `IslandChatPrototypeBootstrap.cs` / `IslandChatCommandRegistry.cs` — used only by island
    slice harness, not production server; superseded by `ServerChatCommandBootstrap`

Remaining (non-blocking):
- permission/access-level integration (access level currently hardcoded to 0 for all players)
- command expansion beyond `/help`, `/ping` (e.g. `/sayas` admin command)

Includes:
- inbound chat message handling
- validation/rate limiting
- outbound fan-out
- optional runtime event publication

Why:
- a good first “real feature” slice after bootstrap/session basics
- helps remove `_tmp` as the owner of a real gameplay-adjacent subsystem

---

## 4) Move After That

### 4.1 Spawn / Session-to-Entity Binding

Target home:
- `UltraSim/Server/Gameplay/`
- `UltraSim/Server/Engines/Networking/`

Status:
- in progress
- tracked `ServerPlayerSessionWorld` now owns:
  - ECS world creation
  - player component registration
  - session -> entity binding
  - spawn-slot allocation/reuse
  - disconnect-driven entity destroy
- basic player input queueing/application and movement tick now run in tracked code
- replication/AOI-aware spawn orchestration still remains in `_tmp`

Includes:
- session -> entity ownership
- spawn/despawn orchestration
- disconnect cleanup for player entities

### 4.2 World Tick / Bridge Ownership

Target home:
- `UltraSim/Server/ServerRuntime.cs`
- `UltraSim/Server/Engines/Networking/`

Includes:
- inbound apply ordering
- world tick ownership
- outbound replication build/send ordering

Status:
- in progress
- tracked runtime now ticks the official player ECS world
- tracked runtime now routes player input datagrams into the tracked world/input queue
- tracked player replication service now owns:
  - connection tracking
  - dirty publish
  - outbound correction packets
  - basic despawn broadcast on disconnect
  - owner ack patching
  - observer payload compaction
  - correction batching
  - correction deadband / max-silent-ticks
  - chunk-based recipient selector fast path
  - dual correction streams
  - minimal priority-LOD and dense-observer cadence/cap shaping
- chunk-based AOI visibility filtering and AOI-driven despawn now have a tracked baseline path
- adaptive / higher-order pressure-cap and priority-LOD policy still remains in `_tmp`

### 4.3 AOI / Chunk Subscription Policy

Target home:
- `UltraSim/Server/Engines/Chunking/`
- server-owned visibility/AOI service(s)

Includes:
- chunk-based observer selection
- visibility subscription management
- tracked policy knobs

### 4.4 Replication / Correction Policy Ownership

Target home:
- `UltraSim/Server/Engines/Networking/`

Includes:
- replication prioritization
- observer pressure caps
- any remaining recipient-selection / dual-stream replication policy
- tracked runtime configuration instead of scene-monolith settings

---

## 5) Leave In `_tmp`

These should stay in `_tmp` unless they are deliberately promoted into tracked tooling.

- benchmark harness scenes
- soak/load/probe scripts
- report outputs
- ad hoc profiling scenes
- one-off experimental transports
- visual/debug-only playgrounds

Examples:
- `_tmp/ECSBenchmarkHarness.tscn`
- `_tmp/ECSNetworkLoopbackHarness.tscn`
- `_tmp/island_ramp_output.txt`
- profiling/report scripts under `_tmp`

---

## 6) Refined Remaining `_tmp` Ownership

After the tracked server runtime, transport, player world, baseline replication, AOI, recipient selection, dual streams, and basic replication shaping moved out, the remaining `_tmp` ownership is much narrower.

### 6.1 Must Move For Real Runtime Parity

These are still real runtime behaviors, not just harness code.

- permission/access-level integration for chat commands (access level hardcoded to 0; non-blocking)
- command expansion beyond `/help`, `/ping` (non-blocking)

Resolved:
- AOI despawn grace / pending-despawn policy — now tracked in server replication path
- `ServerDrivenUiShapeExamples.cs` — deleted; tracked `ServerUiPanelService` is the real impl
- gameplay-aware chat naming — archetype display name now resolved per sender in tracked chat
- account/login system — full challenge-response auth flow in tracked server host; tested end-to-end

### 6.2 Useful To Move, But No Longer Blocking “Official Server”

- adaptive recipient selector bands / hysteresis
- visible-pressure and adaptive outer-cadence LOD variants
- global observer cadence
- richer fanout / selector / AOI telemetry windows
- additional scene-monolith tuning knobs that do not materially change baseline correctness

### 6.3 Should Stay In `_tmp`

- bot-lite mode
- bot chat generation
- ramp / soak / stress orchestration
- harness scripts may still live in `_tmp`, but they should prefer launching the official server executable for validation
- scene-local keyboard shortcuts and debug toggles
- benchmark/report generation
- local preview visuals / floating labels / overhead debug displays
- mixed client/server playground behavior in one scene

---

## 7) Near-Term Migration Order

Recommended order:

1. richer local/system chat policy + command decision
2. adaptive selector / higher-order LOD only if needed
3. keep bots/ramp/benchmark tooling in `_tmp` until there is a clear need for tracked stress tools

Reason:
- the tracked server runtime is already the real baseline owner
- the highest-value remaining runtime behavior is visible AOI despawn grace and richer chat behavior
- adaptive tuning knobs are valuable, but they are no longer on the critical path
- bot/ramp harness work should follow the official runtime, not lead it

---

## 8) Exit Criteria

We can say the “official server” has materially broken away from `_tmp` when:

- ✅ `MainSceneRoot` server builds no longer point at `_tmp/island_slice.tscn`
  - fixed: `server_build` feature check now runs before legacy harness routing
- ✅ tracked server bootstrap owns runtime creation/start/stop/tick
- ✅ chat and session lifecycle no longer depend on `_tmp`
  - tracked chat owns all production behavior including gameplay-aware naming
  - remaining `_tmp/chat/` items are classified harness/debug tooling only
- ✅ tracked player spawn/session binding no longer depends on `_tmp`
- ✅ operator/headless/embedded hosts all target the same tracked `ServerRuntime`
- ✅ `_tmp` remains only for harnesses, experiments, and performance testing
  - remaining `_tmp` items are classified: Godot bot client scene, visual/debug tooling, and harnesses
