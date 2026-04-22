# Network Kernel

## Purpose

`Kernels/Network` is the clean-slate engine-agnostic networking kernel for UltraSim.

It replaces the old `_OLD/UltraSim/Network` stack by preserving the useful semantics while dropping the old replay-heavy replication shape and transport/runtime coupling.

## Current Status

This folder is now in active early implementation, not just scaffold.

What exists today:

- network-specific rules in [AGENTS.md](./AGENTS.md)
- a concrete rebuild plan in [NETWORK_REWORK_IMPLEMENTATION_PLAN.md](./NETWORK_REWORK_IMPLEMENTATION_PLAN.md)
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
  - interpolation-ready state buffering through `Client/Core`
- the first paired server/client movement round-trip slice in `Server/Network`
  - transport input polling on the server side
  - authoritative ECS movement application
  - owner-state packet projection back to the client bridge
- separate benchmark coverage for that higher-layer publish/fanout path

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
- the seam is worth keeping, and the current shared-frame path is now a credible throughput win rather than just an allocation win

Current higher-layer policy direction:

- `Realtime` latest-state is the preferred lane for small continuously changing observer-visible entity state
- `ReliableEvent` is the preferred lane for discrete gameplay events that must preserve order/reliability
- `Bulk` is the preferred lane for large or compression-friendly world payloads that must stay isolated from combat/movement
- player-centered AOI rings are now the preferred gameplay scope model for that higher layer; chunk keys remain the maintenance/indexing primitive underneath
