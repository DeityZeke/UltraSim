# Network Kernel

## Purpose

`Kernels/Network` is the clean-slate engine-agnostic networking kernel for UltraSim.

It replaces the old `_OLD/UltraSim/Network` stack by preserving the useful semantics while dropping the old replay-heavy replication shape and transport/runtime coupling.

## Current Status

This folder is now in active early implementation, not just scaffold.

What exists today:

- network-specific rules in [AGENTS.md](./AGENTS.md)
- a clean-slate, evidence-driven replacement specification in [NETWORK_NEXT_DESIGN.md](./NETWORK_NEXT_DESIGN.md)
- a concrete rebuild plan in [NETWORK_REWORK_IMPLEMENTATION_PLAN.md](./NETWORK_REWORK_IMPLEMENTATION_PLAN.md)
- an old-vs-new comparison guardrail in [NETWORK_PARITY_CHECKLIST.md](./NETWORK_PARITY_CHECKLIST.md)
- first foundation contracts for:
  - planes
  - packet classes
  - service classes
  - channel types
  - immutable outbound payload buffers
  - transform-stage hooks
  - loopback transport
  - UDP socket transport
  - packet-id metadata classification for real transports
- first control-plane/runtime contracts for:
  - bit serialization
  - typed packet contracts
  - small lane-neutral packet envelope headers
  - packet registry and typed dispatch
  - handshake packets and validation policy
  - minimal connection/session identity primitives
  - local session registry
  - session lifecycle snapshots/events
  - session metrics snapshots
  - minimal client/server control-plane host over loopback
  - duplicate-session and admission policy
  - timeout sweep support
  - control-plane metrics and typed control-plane events
  - a first post-handshake control message family (`PingProbe` / `PongProbe`)
  - explicit server/client disconnect notices for graceful shutdown, kick-style closes, and local client exits
- first stream-model contracts for:
  - `LatestStateStream`
  - `OrderedEventStream`
  - explicit cursor types
  - bounded event recovery signaling
  - simple stream snapshots/drain results
  - scope-aware stream publication for latest-state and ordered-event updates
  - packet-class-aware stream publication for:
    - latest-state `Realtime` / `Bulk`
    - ordered-event `ReliableEvent` / `Bulk`
- first scheduler contracts for:
  - per-class connection budgets
  - connection schedule state
  - typed scheduled work items
  - stream scheduling with recovery signaling
  - explicit `Bulk` budgeting alongside `Realtime` and `ReliableEvent`
- first assembly/finalization contracts for:
  - typed state/event frame packets
  - transform-aware outbound datagram assembly
  - header-flag mutation through the transform seam
- first publish/subscription contracts for:
  - subscription keys
  - connection subscription registry
  - published shared-frame descriptors
  - build-once fanout assembly over subscriber sets
  - scheduled-work projection into scoped published frames
  - scoped fanout dispatch over subscriber registries
  - one-step `ScheduledWork + Scope -> Fanout` bridge for future server adapters
  - direct `ConnectionStreamScheduler -> Fanout` bridge for scoped stream work
- first AOI-backed subscription maintenance contracts for:
  - world-space 2D positions
  - chunk coordinates and chunk-backed scope keys
  - player-centered AOI ring policy defined in meters
  - chunk-overlap subscription scope collection
  - per-connection AOI subscription reconciliation
  - registry-owned bulk reconcile path for AOI updates

What does not exist yet:

- broader protocol families beyond the first control-plane slice
- richer session policy:
  - richer disconnect/admission policy beyond the current minimal rules
- ECS/client integration layers

What now exists above the kernel:

- the first server-side bridge slice in [Server/Network](/d:/MEGA/Godot%20Projects/UltraSim/Server/Network)
- runtime snapshot/event publication from `Server/Core` into scoped network streams
- ECS world/runtime snapshot publication from `Kernels/ECS` into scoped network streams via the same server bridge layer
- dirty ECS component publication into scoped latest-state streams via the same server bridge layer
- typed ECS publish-rule descriptors for classifying component families into `Realtime`, `ReliableEvent`, or `Bulk`
- first AOI-backed ECS publication rules for:
  - latest-state over player-centered ring scopes
  - ordered events over player-centered ring scopes
- the first client-side bridge slice in `Client/Network`
  - movement input send path
  - authoritative movement-state receive path
  - prediction acknowledgement hook
  - explicit ordered-event acknowledgement hook
  - interpolation-ready state buffering through `Client/Core`
- the first paired server/client movement round-trip slice in `Server/Network`
  - transport input polling on the server side
  - authoritative ECS movement application
  - owner-state packet projection back to the client bridge
- shared live movement/session hosts above the kernel:
  - `ClientMovementSessionHost`
  - `ServerMovementSessionHost`
  - one transport poll path shared by control-plane handshake and movement traffic
- separate benchmark coverage for that higher-layer publish/fanout path
- the first explicit ordered-event reliability seam above the kernel:
  - contiguous ordered-event apply on the client
  - out-of-order ordered-event buffering on the client
  - highest-contiguous `ReliableEventAckPacket` emission back to the server
  - per-connection ordered-event acknowledgement tracking in the live server session host
- the first explicit baseline snapshot seam above the kernel:
  - `StateBaselineBeginPacket`
  - `StateBaselineFramePacket`
  - `StateBaselineCompletePacket`
  - `StateBaselineAckPacket`
  - `LatestStateStream.CopySnapshot(...)`
  - `StateBaselineSnapshotBuilder`
  - client observer-state baseline apply/reset
  - immediate ordered-event sequence-floor reset when baseline completion lands in the live client host
  - baseline completion acknowledgement back to the server
- the first bounded recovery/replay seam above the kernel:
  - `ReliableEventReplayBuffer`
  - `StateBaselineAckTracker`
  - `ReliableEventRetransmitPolicy`
  - `ReliableEventRetransmitPlanner`
  - `ReliableEventRetransmitBuilder`
  - `ConnectionRecoveryPlanner`
  - `ConnectionRecoveryExecutionBridge`
  - `ConnectionLatestStateSentTracker`
  - `ConnectionLatestStateDeltaPlanner`
  - `StateDeltaFramePacket`
  - `StateDeltaSnapshotBuilder`
  - `ConnectionLatestStateDeltaExecutionBridge`
  - first higher-layer generic runtime composition host in `Server/Network`:
    - `ServerNetworkSessionRuntime`
  - bounded per-connection ordered-event replay retention
  - threshold-based, per-connection capped retransmit planning
  - retransmit execution now reuses normal ordered-event state-plane frames
  - explicit choice between bounded retransmit and full-baseline recovery
  - the first send-based per-connection latest-state coherence seam for future incremental baseline/delta advancement
  - explicit incremental latest-state delta frames now exist and the client can apply them after a completed baseline
  - an opt-in batched realtime latest-state envelope now exists for scheduled-work fanout:
    - `RealtimeStateBatchPacket`
    - `PacketCodec.EncodeRealtimeStateBatchEnvelope(...)`
    - batching is currently used only on the no-transform scheduled-work fanout path where higher layers opt into it
  - the generic runtime composition layer is now also wired into `ServerMovementSessionHost`, not just tested in isolation

## Design Position

The new network kernel is built around these requirements:

- immutable outbound buffers
- explicit packet/service-class separation
- explicit transform stage for compression/segmentation/encryption hooks
- shared payload fanout
- bulk/realtime isolation
- backpressure as a first-class rule
- future parallel-safe handoff points
- player-centered AOI policy backed by chunk/cell indexing

## Layering

Planned module layout:

```text
Kernels/Network/
  Core/
  Serialization/
  Protocol/
  Transport/
  Session/
  Subscriptions/
  Streams/
  Publish/
  Scheduler/
  Assembly/
  Metrics/
```

Bridge layers that consume this kernel belong outside this folder:

- server ECS/network integration
- client ECS/network integration
- host/wrapper glue

The first concrete example of that rule now exists:

- `Server/Network` joins server runtime state to `Kernels/Network`
- `Kernels/Network` itself still has no ECS or wrapper references

## Old-Code Lessons

The old stack is still useful as reference material, especially for:

- client prediction/reconciliation/interpolation structures
- inbound command pipeline shape
- packet/channel concepts
- transport/test harness expectations
- telemetry discipline

The parts we explicitly do not want to reproduce structurally are:

- replay-window-driven late fanout as the normal hot path
- giant monolithic transport/runtime classes
- transport owning protocol/runtime policy
- cross-cutting build/fanout work landing too late on the tick thread

## Early Validation Strategy

Start small and measurable:

1. contracts and loopback transport
2. control-plane handshake/session slice
3. stream model slice
4. scheduler/assembly slice
5. server/client adapters

Tests and benchmarks should land with each slice rather than after the full stack exists.

## Early Benchmark Note

The first dedicated network fanout benchmark now exists at:

- `_Benchmarks/UltraSim.Network.Benchmarks`
- output: `_Benchmarks/results/network-benchmarks.json`

Current early result:

- shared-frame fanout clearly reduces allocations versus naive per-recipient rebuild
- after removing the intermediate projection step from the hot dispatch path, shared-frame fanout is now also ahead in wall-clock on the current synthetic benchmark
- AOI-backed subscription maintenance is now implemented and test-covered
- the AOI update path was materially improved by moving reconcile ownership into the subscription registry and collapsing per-connection duplicate state
- the current AOI benchmark is now in a much healthier range
- the stream-integrated `schedule -> fanout` benchmark path is now in place and is also behaving well
- the benchmark runner now also includes the first higher-layer server-runtime publish and publish-plus-fanout scenarios
- the benchmark runner now also includes the first ECS-originated dirty publish and dirty publish-plus-fanout scenarios
- the benchmark runner now also includes the first mixed ECS-classification scenario with realtime latest-state plus bulk ordered events
- the benchmark runner now also includes the first AOI-backed ECS publish-plus-fanout scenario and a mixed AOI-classified scenario
- the benchmark runner now also includes the first client loopback movement scenario
- the benchmark runner now also includes the first paired server/client movement loopback scenario
- the benchmark runner now also includes the first UDP socket-backed movement round-trip scenario
- the benchmark runner now also includes the first small live multi-client UDP movement ramp scenario over the shared session-host pump shape
- the seam is worth keeping, and the current shared-frame path is now a credible throughput win rather than just an allocation win

Current movement/ramp checkpoint:

- `ClientMovementLoopback_1024Inputs`: `0.913 ms`, `1.22 MB`
- `ServerMovementLoopback_1024Inputs`: `2.176 ms`, `3.43 MB`
- `UdpMovementRoundTrip_1024Inputs`: `15.431 ms`, `4.32 MB`
- `UdpMovementMiniRamp_32Clients_240Ticks`: `108.954 ms`, `14.46 MB`

That last scenario is the first honest “many live clients moving through one shared pump loop” signal. It is the intended pre-bot-harness checkpoint, not the final ramp harness.

That checkpoint has now been followed by the first standalone ramp tool:

- `_Benchmarks/UltraSim.Network.BotHarness`
- current first harness snapshot at `32` clients / `5s` / `62 Hz`:
  - `tick_avg = 0.312 ms`
  - `tick_p95 = 0.336 ms`
  - `tick_p99 = 0.393 ms`
  - `tick_max = 0.869 ms`
  - `pending_max = 0`

Current first scaling read:

- `256` clients / `30s`: `tick_p95 = 1.959 ms`
- `496` clients / `30s`: `tick_p95 = 3.954 ms`
- `512` clients / `30s`: borderline, observed around the `4 ms` ceiling across runs
- `640` clients / `30s`: `tick_p95 = 5.194 ms`

That makes the current practical ceiling for the narrow movement-only UDP/session-host slice roughly `~500` active clients at `62 Hz` before a `4 ms` p95 network budget is exceeded.

The standalone harness now also tracks control-plane ping RTT, so the same slice can be evaluated two ways:

- real-use ceiling:
  - based on tick/network budget (`tick_p95`)
- latency ceiling:
  - based on player-facing responsiveness (`ping_p95`)

Current read:

- tick-limited ceiling: roughly `~480-500`
- ping-limited ceiling: roughly `~2048`

Current first widened standalone-harness read:

- the first AOI observer-replication pass is now wired into the standalone UDP harness
- transport-level same-connection envelope bundling is now in place for the UDP transport
- client-side observer-frame consumption is now wired into the standalone UDP harness through a narrow `ClientObserverStateBridge`
- the widened harness now reports both:
  - logical replicated datagrams
  - actual wire datagrams after transport bundling
- dense observer case (`spawn-spacing = 4m`) with actual client consume now has a practical `tick_p95 < 4 ms` ceiling of about `64` clients:
  - `64` clients / `10s`: `tick_p95 = 2.429 ms`
  - `68` clients / `10s`: `tick_p95 = 6.039 ms`
  - `64` clients / `5s`: `replication_datagrams = 535,228`, `replication_wire_datagrams = 34,198`
- less-dense observer case (`spawn-spacing = 12m`) with actual client consume now has a practical `tick_p95 < 4 ms` ceiling of about `128` clients:
  - `128` clients / `10s`: `tick_p95 = 2.643 ms`
  - `132` clients / `10s`: `tick_p95 = 4.821 ms`
  - `128` clients / `5s`: `replication_datagrams = 208,252`, `replication_wire_datagrams = 39,680`

That means the transport bundling fix is real, but once the client actually decodes and applies observer-visible state the widened ceiling drops well below the movement-only ceiling. The next question is now concrete: how much additional headroom is left once physics, AI, and broader replication families are added on top of this first honest observer path.

Current higher-layer policy direction:

- `Realtime` latest-state is the preferred lane for small continuously changing observer-visible entity state
- `ReliableEvent` is the preferred lane for discrete gameplay events that must preserve order/reliability
- `Bulk` is the preferred lane for large or compression-friendly world payloads that must stay isolated from combat/movement
- player-centered AOI rings are now the preferred gameplay scope model for that higher layer; chunk keys remain the maintenance/indexing primitive underneath

Current latest widened live-path result:

- after preserving stable latest-state item identity across AOI scopes and switching the widened harness observer latest-state store to slot-indexed arrays, the current less-dense broadened `0.40` profile now has a passing `64`-client run:
  - report: `_Benchmarks/results/network_bot_harness_20260423_034433.json`
  - `32`: `tick_p95 = 1.504 ms`
  - `48`: `tick_p95 = 3.589 ms`
  - `64`: `tick_p95 = 3.865 ms`
- a follow-up harness-side ordered-event slot/store pass also now exists and is being evaluated against longer runs:
  - report: `_Benchmarks/results/network_bot_harness_20260423_124205.json`
  - current `10s` widened profile read:
    - `32`: `tick_p95 = 1.856 ms`
    - `48`: `tick_p95 = 2.434 ms`
    - `64`: `tick_p95 = 1.733 ms`

Current interpretation:

- the new architecture is now broad enough to produce honest multi-lane, multi-ring widened results under a real UDP/session-host path
- the next important question is no longer “does the widened path fundamentally work?”
- it is “why does the `32–64` band still show non-monotonic cost behavior across multiple runs and profiles?”
- that investigation should happen before treating the newest widened ceilings as fully settled
- on correctness/runtime semantics, the next meaningful gap after this is now wiring the new generic runtime composition host into the real movement/control hosts and then expanding that host under harness load

## Parity Position

The new stack is broad enough for honest movement-only and first observer-replication ramp results.

It is not yet broad enough to claim direct parity with the old network runtime.

The parity question is now tracked explicitly in:

- [NETWORK_PARITY_CHECKLIST.md](./NETWORK_PARITY_CHECKLIST.md)

That checklist is not a separate implementation branch. It exists to make sure later old-vs-new comparisons are fair once the new feature surface naturally catches up.
