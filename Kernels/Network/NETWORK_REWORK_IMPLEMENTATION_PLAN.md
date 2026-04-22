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

- started
- the first control-plane foundation is now in place:
  - `BitWriter` / `BitReader`
  - typed `IPacket<T>` contracts
  - `PacketEnvelopeHeader`
  - `PacketRegistry<TContext>`
  - `PacketCodec`
  - handshake packets and validation policy
- a full session-runtime state machine is still deferred to the next slice

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
- this first pass establishes the architecture docs and the initial foundation scaffold

The next implementation target after this scaffold should be:

- `Serialization + Protocol + Control-plane loopback`

not full replication yet.
