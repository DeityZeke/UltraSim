# Network Kernel

## Purpose

`Kernels/Network` is the clean-slate engine-agnostic networking kernel for UltraSim.

It replaces the old `_OLD/UltraSim/Network` stack by preserving the useful semantics while dropping the old replay-heavy replication shape and transport/runtime coupling.

## Current Status

This folder is now in the first scaffold stage.

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

What does not exist yet:

- broader protocol families beyond the first control-plane slice
- richer session policy:
  - richer disconnect/admission policy beyond the current minimal rules
- scheduler/assembly pipeline
- ECS/server/client integration layers

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
