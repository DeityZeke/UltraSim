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

Use the old client network code as reference for:

- `ClientInputSendBridge`
- `ClientPredictionBridge`
- `ClientInterpolationBridge`
- `ClientNetworkEntityMap`
- `ClientNetworkBridge`

Preserve the useful behavior. Do not preserve the old project structure by default.

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
- what is still missing before the harness becomes worth wiring is the next broader gameplay/server movement/runtime integration layer on top of this slice, not another transport rewrite
