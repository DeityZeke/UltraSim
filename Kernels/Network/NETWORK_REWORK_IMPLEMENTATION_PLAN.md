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

Parity note:

- old-network parity is a comparison target, not the implementation order
- track that comparison in `NETWORK_PARITY_CHECKLIST.md`
- keep building the new architecture in the current direction and only judge parity once the runtime shapes are actually comparable

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
  NETWORK_PARITY_CHECKLIST.md
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
  - `PacketMetadataRegistry`
  - `DefaultPacketMetadata`
  - `UdpSocketNetworkTransport`
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
  - scope-aware latest-state and ordered-event publication
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
  - `Bulk` is now a first-class scheduled packet class with its own budget lane
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
  - `ScheduledWorkFanoutBridge`
  - `ConnectionStreamFanoutBridge`
  - fanout assembly from a shared published frame to many recipient connections
- current behavior:
  - typed state/event frames can be built once as shared published bodies
  - subscriber sets can be copied out per scope key
  - scheduled work can be projected into scoped published frames
  - scheduled work plus scope keys can now be bridged directly into fanout dispatch through a single kernel path
  - scope-aware stream publication can now flow through scheduling directly into fanout dispatch
  - the assembler can fan out a shared published frame across many recipients
  - published latest-state and ordered-event frames now preserve `Bulk` packet-class metadata through assembly as well
- still deferred from the broader bridge slice:
  - integration between stream scheduling and shared published-frame fanout
  - bridge-facing publish classification from ECS/server state changes

- the first AOI-backed subscription maintenance primitives are now in place:
  - `WorldPosition2`
  - `ChunkCoord`
  - `ChunkSubscriptionScope`
  - `ChunkSubscriptionKey`
  - `ChunkAoiPolicy`
  - `ChunkAoiSubscriptionMaintainer`
  - registry-owned `ReconcileConnection(...)` support in `ConnectionSubscriptionRegistry`
- current behavior:
  - AOI rings are defined in meters around the player, not by chunk-count semantics
  - chunk overlap is derived from those world-space radii
  - chunks are assigned to the nearest overlapping ring only
  - connection subscription sets can now be reconciled as a player moves
  - AOI maintenance no longer keeps a second authoritative per-connection scope map outside the registry
- still deferred from the broader subscription bridge slice:
  - direct integration between AOI maintenance and stream-work publication
  - denser reverse-index/backpressure-aware subscription maintenance
  - broader server-bridge-facing AOI classification

Benchmark note:

- the first dedicated network fanout benchmark runner now exists in `_Benchmarks/UltraSim.Network.Benchmarks`
- current early data shows:
  - clear allocation improvement for shared-frame fanout
  - after removing the intermediate projection step from the hot path, shared-frame fanout now also leads the naive rebuild path in the current benchmark
  - the first integrated `schedule -> fanout` benchmark path is now in place and is healthy
  - AOI subscription maintenance benchmarks now exist and improved substantially after the registry-owned reconcile pass
- implication:
  - the seam is worth keeping
  - the shared-frame path is now strong enough to treat as the preferred kernel direction
  - AOI maintenance is no longer the same level of concern it was in the first benchmark pass

### Phase N5: Server Bridge Slice

Deliver:

- publish-stage bridge from ECS dirty/state changes into streams
- subscription maintenance
- connection-owned cursors
- no live ECS reads off background workers

Done when:

- server bridge can publish and schedule outbound work without violating ECS boundaries

Current status:

- started
- the first server-side bridge slice now exists in `Server/Network`
- current behavior:
  - `ServerApplication` status, metrics, system runtime snapshots, player session snapshots, and account summaries can be published into scoped `LatestStateStream` items
  - `ServerApplication` events can be published into scoped `OrderedEventStream` items
  - those published items can already flow through the existing `schedule -> fanout` bridge
  - ECS world/runtime snapshots can now be published into scoped latest-state items through the same layer
  - dirty ECS component updates can now be classified and published into scoped latest-state items through the same layer
  - typed ECS publish rules now exist for:
    - `Realtime` latest-state
    - `Bulk` latest-state
    - `ReliableEvent` ordered events
    - `Bulk` ordered events
  - first AOI-backed ECS publish rules now exist for:
    - `Realtime` latest-state over player-centered ring scopes
    - `ReliableEvent` ordered events over the same ring scopes
    - `Bulk` ordered events over the same ring scopes
- still deferred inside this slice:
  - richer ECS-driven state classification beyond the first realtime/reliable/bulk rule families
  - connection-owned cursor persistence/policy
  - richer server-side scope classification beyond the current operator/runtime and first dirty-component plus first AOI ring-scoped slices
  - transport/session-owned publish loops at the server runtime layer
  - full reliable ordered replay/resend policy beyond the first explicit ack seam

### Phase N6: Client Bridge Slice

Deliver:

- input plane
- owner correction
- observer state consumption
- prediction/reconciliation/interpolation bridge

Done when:

- client-side correction/interpolation structures are integrated against the new kernel shape

Current status:

- started with the first narrow movement slice
- current delivered pieces:
  - `MovementInputPacket`
  - `MovementStatePacket`
  - `ReliableEventAckPacket`
  - `StateBaselineBeginPacket`
  - `StateBaselineFramePacket`
  - `StateBaselineCompletePacket`
  - `Client/Core` sequencing/prediction/interpolation primitives
  - `Client/Network` movement send/receive bridge
  - `ClientMovementSessionHost`
  - `ServerMovementSessionHost`
  - loopback integration tests
  - first loopback benchmark
  - first paired server/client authoritative movement round-trip over loopback through `Server/Network`
  - first paired server/client authoritative movement round-trip over real UDP socket transport after handshake
  - first small live multi-client UDP movement ramp benchmark over the shared session-host pump path
  - first widened observer-replication ramp path with actual client-side observer latest-state decode/apply
  - transport-level same-connection UDP envelope bundling in the widened live path
  - first explicit ordered-event reliability seam:
    - client applies reliable-event frames contiguously instead of just accepting any newer sequence
    - client buffers out-of-order reliable-event frames until the gap closes
    - client emits highest-contiguous `ReliableEventAckPacket` acknowledgements
    - server session host now tracks highest acknowledged reliable-event sequence per connection
  - first explicit baseline snapshot seam:
    - full-state baseline descriptor and begin/frame/complete packets
    - `LatestStateStream.CopySnapshot(...)`
    - `StateBaselineSnapshotBuilder`
    - client observer-state bridge can apply a full baseline and surface completion
    - client session host now resets ordered-event sequence floor immediately when a baseline completes
  - first bounded recovery/replay seam:
    - `StateBaselineAckPacket`
    - `StateBaselineAckTracker`
    - `ReliableEventReplayBuffer`
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
    - first higher-layer generic runtime composition host:
      - `ServerNetworkSessionRuntime`
    - baseline completion is now acknowledged back to the server
    - ordered-event replay is now explicitly bounded per connection
    - recovery can now choose between normal incremental scheduling and full-baseline resync
    - retransmit planning is now threshold-based and per-connection capped
    - retransmit execution now emits normal ordered-event state-plane datagrams
    - latest-state incremental advancement is now explicitly heading toward send-based per-connection tracking on top of `LatestStateStream`, not a new client-driven latest-state ack protocol
    - explicit incremental latest-state delta execution now exists with a dedicated delta packet instead of overloading baseline frames
    - the shared scheduled-work latest-state fanout path now has a first opt-in batching seam:
      - `RealtimeStateBatchPacket`
      - `PacketCodec.EncodeRealtimeStateBatchEnvelope(...)`
      - `SubscriptionFanoutDispatcher(enableLatestStateBatching: true)`
    - the first generic server-side runtime layer now composes:
      - filtered baseline bootstrap
      - baseline ack processing
      - retransmit execution
      - ordered-event delivery
      - latest-state delta delivery

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
  - real UDP socket transport
  - typed control/session observability seams
  - the first stream-model slice
  - scheduler/assembly primitives
  - the first publish/subscription fanout bridge
  - the first higher-layer `Server/Network` publish adapter over the kernel
- the first ECS-facing dirty-state publication path over that adapter

The next implementation target after this bridge slice should be:

- broader server bridge work from ECS/runtime state into these scoped streams
- specifically: keep moving from generic dirty-component publication toward actual gameplay replication families on top of the new rule split:
  - observer-visible continuous state -> `Realtime`
  - discrete gameplay events -> `ReliableEvent`
  - terrain/housing/large world payloads -> `Bulk`
- and then use that richer slice plus the new paired movement round-trip as the first real candidate for socket-driven harness/ramp validation
- that checkpoint now exists in minimal form through the shared session-host UDP mini-ramp; the next harness step should be a broader standalone bot runner, not another throwaway transport-specific benchmark shape
- that broader standalone bot runner now exists in first form under `_Benchmarks/UltraSim.Network.BotHarness`; the next step is scaling/stressing it, not inventing a third temporary harness path
- first scaling/stress passes now exist too, and the current narrow movement-only slice is landing around a practical `~500` active-client ceiling at `62 Hz` before a `4 ms` p95 network-tail budget is crossed
- the harness now also tracks ping RTT, and the same narrow slice is currently landing around a separate latency-limited ceiling of roughly `~2048` active clients before `ping_p95` crosses `50 ms`
- the widened standalone harness now also supports the first honest observer-replication read with actual client consume:
  - dense `spawn-spacing = 4m`: practical current ceiling about `64`
  - less-dense `spawn-spacing = 12m`: practical current ceiling about `128`
  - the live harness AOI policy is now configurable, so the same runtime path can be exercised with either:
    - the current narrow single-ring slice
    - broader multi-ring policies such as `7.5,23.5,39.5`
  - the widened live path now also has its first observer-event family:
    - client-side ordered-event decode/apply
    - harness-side AOI-scoped ordered-event publication on top of the same movement entities
    - configurable event cadence for widening runs
  - the widened live path now also has a second observer-visible latest-state family on top of the base movement state
  - the widened live path now also has its first `Bulk` observer latest-state family with configurable cadence
  - ring-aware AOI latest-state cadence now exists at the server publish-policy layer rather than in the network kernel
  - the first comparable broader three-lane, three-ring matrix with ring-aware cadence now exists too:
    - less-dense `spawn-spacing = 12m`
    - `aoi-radii = 7.5,23.5,39.5`
    - ring cadence `1,2,3`
    - observer latest-state + visual latest-state + bulk latest-state + ordered events
    - owner deadband/silent-tick policy and harness-side owner jitter enabled
    - current results:
      - `0.35` active:
        - `32`: `tick_p95 = 1.738 ms`
        - `48`: `tick_p95 = 3.037 ms`
        - `64`: `tick_p95 = 4.152 ms`
      - `0.40` active:
        - `32`: `tick_p95 = 1.407 ms`
        - `48`: `tick_p95 = 3.387 ms`
        - `64`: `tick_p95 = 4.343 ms`
    - current read:
      - `48` passes under this broader shape
      - after preserving stable latest-state item identity across AOI scopes and switching the widened harness observer store to slot-indexed arrays, the current less-dense `0.40` / `64` profile now reaches `tick_p95 = 3.865 ms`
      - that puts the current broadened `0.40` / `64` profile inside the `4 ms` tick-p95 target
  - the live owner path now also has its first explicit correction-send policy seam:
    - server-side owner correction deadband
    - server-side max silent ticks
    - harness-level configuration for both
  - chunk-AOI latest-state now preserves stable item identity across scopes inside `LatestStateStream`
  - the widened harness now uses structured item keys for movement, visual, and bulk observer families
  - widened-harness observer latest-state storage is now slot-indexed by structured item key instead of dictionary-backed by scoped item key
  - the widened harness now also enables the first batched realtime latest-state envelope path for contiguous scheduled-work fanout items

Current recommendation:

- do not pivot into a parity-only branch
- keep implementing the broader real runtime slices
- keep widening the live harness as those slices land
- use `NETWORK_PARITY_CHECKLIST.md` only to judge when an old-vs-new comparison is finally fair

not full replication yet.
