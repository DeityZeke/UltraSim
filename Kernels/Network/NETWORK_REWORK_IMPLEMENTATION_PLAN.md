# Network Kernel Rework Plan

## Purpose

This document defines the rebuild plan for the new network kernel in `Kernels/Network`.

The goal is not to port `_OLD/UltraSim/Network` file-for-file.

The goal is to:

- preserve the old runtime's proven authority/session/client concepts,
- remove the structural debt that made replication/build/fanout expensive and hard to change,
- keep the new network kernel engine-agnostic and reusable,
- give the server, client, and future content/runtime systems a cleaner foundation.

## Executive Decision

Build the new network stack as a fresh kernel.

Use the old network as:

- behavioral reference,
- performance baseline context,
- source of test and telemetry ideas,
- source of individual utility/data-structure patterns where they are still good.

Do not use it as a direct architecture template.

## What The Audit Says

The old stack had useful pieces, but the main architectural problem was shape:

- dirty publication was relatively cheap
- dense local fanout was expensive
- replay/build/fanout cost landed too late
- transport, protocol, session, and replication concerns were too entangled in a few large types

The biggest old files that should not define the new shape are:

- `NetworkReplicationPipeline`
- `ReplicationReplayWindow`
- `TcpFramedNetworkTransport`

The old pieces still worth learning from conceptually are:

- client reconciliation/prediction/interpolation queues and buffers
- inbound command queue/pipeline flow
- packet/channel and handshake concepts
- loopback/fake transport testing strategy
- observability and benchmark discipline

## Hard Requirements

These requirements are design inputs, not optional polish:

1. Immutable outbound buffers
2. Explicit packet classes:
   - `Control`
   - `Realtime`
   - `ReliableEvent`
   - `Bulk`
3. Explicit transform stage:
   - compression
   - segmentation
   - encryption hook later
4. Shared payload fanout
5. Bulk/realtime isolation
6. Explicit backpressure behavior
7. Parallel-safe handoff boundaries from the start
8. Player-centered AOI policy in world-space meters, with chunk/cell overlap used only as indexing/maintenance support

## Kernel Boundary

`Kernels/Network` remains standalone:

- it must not reference `Kernels/ECS`
- it must not reference Godot or host wrappers
- it must not own gameplay/session/account systems

Bridge code that uses both ECS and Network belongs later in:

- `Server/...`
- `Client/...`
- shared bridge/helper folders when needed

## Planned Layout

```text
Kernels/Network/
  AGENTS.md
  README.md
  NETWORK_REWORK_IMPLEMENTATION_PLAN.md
  UltraSim.Network.csproj
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

## Core Concepts

### Planes

- `Control`
- `Input`
- `State`

Planes describe semantic purpose.

### Packet Classes

- `Control`
- `Realtime`
- `ReliableEvent`
- `Bulk`

Packet class describes workload isolation and backpressure treatment.

### Service Classes

Keep the scheduling/degradation model explicit:

- `A` owner-critical
- `B` near observers
- `C` far observers
- `D` bulk/background

### Transform Stage

The outbound pipeline shape should be:

1. `Publish`
2. `Schedule`
3. `Assemble`
4. `Transform`
5. `Send`

`Transform` is where:

- compression
- segmentation
- framing/finalization
- future encryption

belong.

### Shared Payload Fanout

The network kernel must allow:

- payload built once
- reused across many recipients

This is especially important for:

- terrain/chunk updates
- housing updates
- shared baselines/snapshots
- large bulk world updates

### AOI

AOI policy should be defined in world-space rings centered on the player, not by chunk-size semantics.

Recommended direction:

- define rings/radii in meters
- derive chunk/cell overlap from those rings
- maintain reverse subscriber indexes by overlapping scope

That gives:

- clean gameplay visibility semantics
- efficient subscription/index maintenance

## What To Build First

Do not begin with full replication.

Build the smallest vertical slice that proves the architecture.

### Phase N0: Rules And Scaffold

Deliver:

- `AGENTS.md`
- this implementation plan
- `README.md`
- base project scaffold
- first test harness scaffold

Done when:

- the architecture is explicit on paper and in code
- the initial contracts exist in a real project

### Phase N1: Foundation Contracts

Deliver:

- core ids/enums
- planes
- packet classes
- service classes
- channels
- immutable payload buffer contract
- transform-stage contract
- transport contract
- loopback transport

Done when:

- the first kernel project builds
- loopback transport and immutable payload behavior are test-covered

### Phase N2: Control Plane Slice

Deliver:

- bit serialization
- packet/codec registry
- handshake contracts
- session/connection registry primitives
- control-plane loopback integration tests

Done when:

- control-plane traffic can round-trip through loopback
- handshake/session lifecycle basics are test-covered

Current status:

- in progress
- the first control-plane/session foundation is now in place:
  - `BitWriter` / `BitReader`
  - typed `IPacket<T>` contracts
  - `PacketEnvelopeHeader`
  - `PacketRegistry<TContext>`
  - `PacketCodec`
  - handshake packets and validation policy
  - `NetworkSessionManager`
  - session snapshots and lifecycle events
  - session metrics snapshots
  - `NetworkControlPlaneHost` for minimal loopback client/server handshake flow
  - duplicate-session and admission policy
  - timeout sweep support
  - control-plane metrics snapshots and typed control-plane events
  - `PingProbePacket` / `PongProbePacket`
- still deferred from the broader session/runtime slice:
  - broader control-plane message families
  - more complete disconnect semantics
  - higher-level bridge-facing host/session integration

### Phase N3: Stream Model Slice

Deliver:

- latest-state stream
- ordered-event stream
- cursor sets
- snapshot descriptors
- baseline/recovery semantics

Done when:

- latest-state and event semantics are test-covered
- snapshot recovery rules are explicit

Current status:

- started
- the first stream primitives are now in place:
  - `LatestStateStream`
  - `OrderedEventStream`
  - explicit latest-state and ordered-event cursors
  - bounded ordered-event recovery signaling when a reader falls behind retention
  - simple stream snapshots and drain-result records
- still deferred inside this slice:
  - richer snapshot descriptors/baseline metadata
  - shared payload references instead of direct immutable payload ownership
  - stream benchmarks
  - scheduler-facing work-item projection

### Phase N4: Scheduler And Assembly

Deliver:

- per-connection budget state
- work-item model
- bounded drain rules
- assembly ownership rules
- backlog/degradation policy

Done when:

- overload behavior is explicit and measurable
- budget/degradation tests exist

Current status:

- started
- the first scheduler primitives are now in place:
  - `PacketClassBudget`
  - `ConnectionSendBudget`
  - `ConnectionScheduleState`
  - `ScheduledWorkItem`
  - `ConnectionStreamScheduler`
- current behavior:
  - latest-state (`Realtime`) work is scheduled before ordered-event (`ReliableEvent`) work
  - per-class item and payload-byte budgets are enforced
  - ordered-event recovery requirements propagate through schedule results
- still deferred inside this slice:
  - shared payload references/fanout-aware scheduling
  - benchmark coverage for scheduling/drain behavior
  - broader packet-class handling beyond the current stream pair
  - higher-level publish/subscription integration

- the first assembly/finalization primitives are now in place:
  - `RealtimeStateFramePacket`
  - `ReliableEventFramePacket`
  - `OutboundDatagramAssembler`
  - transform-stage header flag mutation via `OutboundTransformResult`
- current behavior:
  - scheduled work items assemble into concrete outbound datagrams
  - transform hooks run before final envelope construction
  - compression/encryption header flags can now be set by transforms

- the first publish/subscription bridge primitives are now in place:
  - `SubscriptionKey`
  - `ConnectionSubscriptionRegistry`
  - `PublishedFrame`
  - `SharedFramePublisher`
  - `ScopedPublishedFrame`
  - `ScheduledWorkPublishedFrameProjector`
  - `SubscriptionFanoutDispatcher`
  - fanout assembly from a shared published frame to many recipient connections
- current behavior:
  - typed state/event frames can be built once as shared published bodies
  - subscriber sets can be copied out per scope key
  - scheduled work can be projected into scoped published frames
  - the assembler can fan out a shared published frame across many recipients
- still deferred from the broader bridge slice:
  - player-centered AOI policy and chunk/cell-backed subscription maintenance
  - integration between stream scheduling and shared published-frame fanout
  - bridge-facing publish classification from ECS/server state changes

Benchmark note:

- the first dedicated network fanout benchmark runner now exists in `_Benchmarks/UltraSim.Network.Benchmarks`
- current early data shows:
  - clear allocation improvement for shared-frame fanout
  - mixed wall-clock results depending on scale
- implication:
  - the seam is worth keeping
  - projection/dispatch and final envelope assembly still need tuning before this path can be treated as a definitive throughput win

### Phase N5: Server Bridge Slice

Deliver:

- publish-stage bridge from ECS dirty/state changes into streams
- subscription maintenance
- connection-owned cursors
- no live ECS reads off background workers

Done when:

- server bridge can publish and schedule outbound work without violating ECS boundaries

### Phase N6: Client Bridge Slice

Deliver:

- input plane
- owner correction
- observer state consumption
- prediction/reconciliation/interpolation bridge

Done when:

- client-side correction/interpolation structures are integrated against the new kernel shape

## Testing Strategy

Tests should land with each slice.

First required coverage:

- immutable payload ownership
- transport loopback behavior
- transport lifecycle semantics
- control-plane codec and registry tests
- handshake/session lifecycle tests
- latest-state stream semantics
- ordered-event retention and recovery tests
- cursor and baseline rules
- backpressure/degradation tests
- client correction/reconciliation tests

## Benchmark Strategy

Benchmarks should begin before full server/client integration.

Initial benchmark targets:

- loopback transport throughput/allocations
- latest-state publish cost
- latest-state drain cost per connection
- ordered-event drain cost
- snapshot assembly cost
- client reconciliation/interpolation structure cost

## Current Recommended Immediate Order

1. finish rules/docs
2. scaffold the kernel/test projects
3. build the foundation contract slice
4. lock it with tests
5. only then move into control-plane runtime work

## Current Status

Current status in this repo:

- `Kernels/Network` previously had no meaningful implementation
- the repo now has:
  - foundation contracts
  - control-plane/session loopback
  - typed control/session observability seams
  - the first stream-model slice
  - scheduler/assembly primitives
  - the first publish/subscription fanout bridge

The next implementation target after this bridge slice should be:

- AOI-backed subscription maintenance policy
- stream-integrated publish projection into fanout scheduling
- then broader server bridge work

not full replication yet.
