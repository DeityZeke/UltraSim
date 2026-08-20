# Client Rebuild Plan

## Purpose

Rebuild the client runtime around the same boundary discipline as ECS, server, and network:

- engine-agnostic core first
- network bridge second
- Godot wrapper last

## Initial Layout

```text
Client/
  AGENTS.md
  CLIENT_IMPLEMENTATION_PLAN.md
  Core/
  Network/
  _Godot/
```

## Old-Code Reference

Use the old v2 client under `_OLD` as the primary reference for feature behavior and scene composition. The new project exists to keep the rebuilt network/runtime shape, then reintroduce the old v2 feature set with cleaner boundaries and less coupling.

Use the old client network code as reference for:

- `ClientInputSendBridge`
- `ClientPredictionBridge`
- `ClientInterpolationBridge`
- `ClientNetworkEntityMap`
- `ClientNetworkBridge`

Preserve the useful behavior. Do not preserve the old project structure by default. The older v1 tree under `_OLD/_OLD` is only a secondary concept reference; likely candidates are the old hybrid render/culling idea and the old `Client/ECS/ControlPanel` admin/debug panel concepts.

The rebuilt server-driven UI and admin/control-panel plan lives in `UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md`. Use it before starting schema UI work; it incorporates the old v2 UI/schema path, old v1 ECS settings/control panel, ACC-style pluggable RunUO admin pages, and the current ECS `SystemSettings` / `ControlDescriptors` hooks.

## Phase C1: Core Contracts

Deliver:

- input sample/sequencer
- prediction store
- interpolation buffer
- small movement input/state contracts

Done when:

- client-side sequencing/prediction/interpolation are test-covered without any transport dependency

Current status:

- done for the first narrow movement slice
- current delivered core pieces:
  - `ClientInputSequencer<TInput>`
  - `ClientPredictionStore<TState>`
  - `ClientInterpolationBuffer<TState>`
  - `ClientMovementInput`
  - `ClientMovementState`

## Phase C2: Client Network Bridge

Deliver:

- movement input packet send path
- authoritative movement-state receive path
- prediction acknowledgement hook
- loopback integration tests

Done when:

- one real movement vertical slice works over `Kernels/Network`

Current status:

- started and already useful
- current delivered bridge pieces:
  - `MovementInputPacket`
  - `MovementStatePacket`
  - `ClientMovementNetworkBridge`
  - first owner-correction tracking against predicted state
  - loopback integration tests
  - first loopback benchmark
  - first paired server/client authoritative movement round-trip over loopback via `ServerEcsMovementNetworkBridge`
  - widened live harness reporting for:
    - owner correction sample counts
    - average/max position error
    - average/max velocity error
  - first live owner-correction policy controls on the server path:
    - position deadband
    - max silent ticks
  - first harness-only owner divergence seam:
    - configurable prediction jitter to make correction pressure measurable without contaminating runtime code
  - widened live observer-consume path now includes:
    - movement latest-state
    - a second observer-visible latest-state family
    - a first bulk/latest-state observer family
    - ordered observer events

## Phase C3: Godot Wrapper

Deliver:

- bind local input to client runtime
- bind received authoritative state to presentation/runtime hooks
- keep `_Godot` thin

Current status:

- started and useful
- current delivered Godot pieces:
  - `Client/_Godot/UltraSim.Client.Godot.csproj`
  - `Scenes/ClientRoot.tscn`
  - `GodotClientRuntimeRoot`
  - persistent world and UI layer containers
  - preview actor/controller/camera flow
  - local app shell with direct connect, server browser placeholder, settings placeholder, session options, and chat panel
  - `GodotClientNetworkSessionDriver` over `ClientMovementSessionHost`
  - owner movement, observer latest-state, chat, ping/status, and disconnect presentation hooks
- next validation gate:
  - Godot build and root-scene smoke
  - `UltraSim.Client.Tests`
  - direct-connect smoke against the current headless/server runtime

## Bot-Harness Readiness

The first bot-harness checkpoint should happen after `C2`, not after a full client feature set.

At that point the shared shape should be real enough to test:

- connect/handshake
- input send cadence
- authoritative movement receive cadence
- disconnect/load behavior

Current note:

- the repo has now reached the first meaningful pre-bot-harness checkpoint on the client side
- the paired server-side movement ingestion/authoritative movement path now exists for one narrow loopback slice
- a real UDP socket transport path now also exists for that slice
- the sharded bot harness now lives under `_TMP/run_network_bot_harness_sharded.py`; `_tmp` is no longer the active harness folder
- the network rewrite is validated enough that client work should focus on rebuilding the old v2 feature set on the current runtime shape, not another transport rewrite
