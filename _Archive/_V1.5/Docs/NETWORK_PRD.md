# Network vNext - Product Requirements Document (PRD)

This document defines the network architecture for vNext and aligns with:

- `ECS_PRD.md`
- `ECS_Systems_Outline.md`
- `ECS_Archetypes_Prefabs_Scripts.md`

Core principle: **server-authoritative ECS with deterministic tick/barrier integration**, using shared ECS types across server and client worlds.

---

## 1. Goals

- Deterministic network processing aligned to world tick/barriers.
- Single gameplay path for dedicated, multiplayer, and local loopback.
- Zero/low-allocation hot paths in packet parse/build/dispatch.
- Strong separation of concerns: transport, protocol, sessions, replication.
- Predictable replication budget (interest + packet caps + cadence).
- Explicit compatibility/version checks at connection time.
- Broad, low-overhead statistics coverage across all network layers.

---

## 2. Non-Goals (v1)

- Full anti-cheat stack (kernel/usermode integrity, etc.).
- Cryptographic transport beyond standard TLS/DTLS choices.
- Dynamic runtime packet schema generation.
- Cross-version live protocol compatibility beyond declared supported window.

---

## 3. Runtime Topology

### 3.1 Dedicated Server

- Headless `World` with full gameplay systems.
- Network systems enabled.
- Rendering disabled.

### 3.2 Thin Client

- Godot client `World` with projected entity slice only.
- Input, prediction, interpolation, render bridge systems.
- No gameplay authority.

### 3.3 Combined (Local/Singleplayer)

- Server world + client world in one process.
- Loopback transport path by default (same contracts as remote).
- No special gameplay code path.

---

## 4. Layered Architecture

```
Game/App
 -> Runtime Bridge Layer
    -> Shared/Engines/Networking (shared bridge helpers; can be named Common/)
    -> Server/Engines/Networking (server host adapters + ECS/Network bridge code)
    -> Client/Engines/Networking (client host adapters + ECS/Network bridge code)
 -> Network Runtime
    -> Transport
    -> Protocol
    -> Session/Connection
    -> Replication
 -> ECS World (authoritative or projected)
```

### Boundary Rules

- `Network/` is an engine-agnostic module (same policy as `ECS/`).
- `Network/` and `ECS/` must not directly depend on each other.
- Integration code that uses both belongs in `Client/Engines/Networking`, `Server/Engines/Networking`, or shared bridge helpers under `Shared/Engines/Networking` (or `Common/Engines/Networking`), not in `ECS/` or `Network/`.
- Network runtime must not directly mutate ECS internals.
- All world mutations go through `World` queue-backed APIs.
- Inbound packet handling produces commands/intents first; application happens at structural barriers.
- Client networking/runtime core should emit semantic presentation/UI commands, not direct engine API calls.
- Protocol/gameplay/client-core paths must use logical IDs (entity/action/UI/asset IDs), not engine resource paths.

### 4.1 Module Dependency Contract

| Module | May Reference | Must Not Reference |
|---|---|---|
| `ECS/` | Core primitives only | `Network/`, engine code |
| `Network/` | Core primitives only | `ECS/`, engine code |
| `Shared/Engines/Networking/` | `ECS/`, `Network/`, core/runtime helpers | direct engine APIs, gameplay modules |
| `Server/Engines/Networking/` | `ECS/`, `Network/`, server host APIs, `Shared/Engines/Networking/` | `Client/` host code |
| `Client/Engines/Networking/` | `ECS/`, `Network/`, client host APIs, `Shared/Engines/Networking/` | `Server/` host code |

This keeps both modules reusable and testable in isolation.

Client-side note:
- Engine host/presentation code (rendering, animation, audio, VFX, UI scene management) belongs in client host/bridge layers.
- Prediction/reconciliation/interpolation/replication-apply logic should remain engine-agnostic and avoid direct `Godot.*` type coupling.
- Server-driven UI/gump flows should use an engine-agnostic prefab/schema protocol (fast prefab path + dynamic schema fallback), not engine control serialization.
- Client should maintain a server-scoped cache namespace (stable server identity, not only endpoint) for cached UI schemas/prefabs and future server-provided presentation content.

---

## 5. Tick and Barrier Contract

Server-side order per host tick:

1. Poll inbound transport.
2. Decode packets into network command queues.
3. `World.Tick` pre-barrier applies queued commands.
4. Gameplay/simulation systems run.
5. Networking stage builds outgoing deltas/intents.
6. `World.Tick` post-barrier applies system-enqueued structural changes.
7. Transport flushes outbound packets.

This preserves ECS determinism and avoids out-of-band mutation.

---

## 6. Transport Contract

Define a transport abstraction with two implementations:

- Socket transport (TCP + UDP/hybrid).
- Loopback transport (in-memory queues, same packet contracts).

Suggested interface:

```csharp
public interface INetworkTransport : IDisposable
{
    void Start();
    void Stop();

    int PollInbound(Span<InboundDatagram> destination);
    void EnqueueOutbound(in OutboundDatagram datagram);
    void FlushOutbound();
}
```

Hot-path rules:

- No per-packet heap allocation in steady state.
- Use pooled buffers and reusable queues.
- Avoid `Span -> byte[]` copies in send paths unless unavoidable by API surface.
- Avoid wall-clock calls in hot loops (`DateTime.UtcNow` per packet); use monotonic tick/stopwatch timing for runtime decisions.

### 6.1 v1 Transport Profile

v1 defaults to a TCP-first operational profile while keeping logical channel abstractions in protocol contracts.

- Start with reliable ordered delivery for all gameplay-critical traffic.
- Keep channel metadata in packet definitions so UDP/hybrid can be enabled later without changing gameplay/system APIs.
- Add UDP/hybrid selectively for latency-sensitive streams (for example, high-rate movement replication) after baseline validation.

### 6.2 Buffer Ownership and Lifetime

Inbound datagram/buffer ownership must be explicit:

- Data returned by `PollInbound(...)` is valid only for the current consume window.
- Consumers must copy only when data must outlive that window.
- Transport implementations may reuse backing buffers on next poll/flush cycle.

Debug recommendation:

- Add optional debug poison/clear of recycled inbound buffers after consume window closes to catch accidental stale references.

### 6.3 Framing and Queueing Contract

For TCP stream parsing and dispatch throughput:

- Use length-prefixed framing in v1 (length-first) for predictable stream reassembly.
- Use ring-buffer or equivalent segmented buffering for partial frame reassembly.
- Inbound processing is split:
  - transport thread parses frames into inbound queue entries,
  - game/network tick drains queues and performs decode/command dispatch.
- Outbound processing is batched:
  - producers enqueue payload descriptors,
  - flush stage writes contiguous batches to transport.
- Unknown/invalid frame handling must be explicit and counted (drop/strike/disconnect by policy).

---

## 7. Channels and Delivery Semantics

Retain old channel intent:

- `ReliableOrdered`
- `ReliableUnordered`
- `UnreliableSequenced`
- `Unreliable`

Channel defines delivery behavior; transport implementation decides mechanism.

### 7.1 Reliable UDP Policy (When Enabled)

If/when `ReliableUnordered` is enabled over UDP:

- ACKs should be piggybacked (`ack` + `ackBits` style) to avoid ACK-only packet overhead.
- Retransmit scheduling should run in the existing network tick loop, not per-packet worker threads.
- Retransmit timeout is per connection (smoothed RTT/RTO), not a global fixed timeout.
- Sequence handling must explicitly support wrap-around and duplicate/out-of-order suppression.

---

## 8. Protocol and Packet Model

Use strongly typed packet structs with compile-time metadata.

```csharp
public interface IPacket
{
    static abstract ushort PacketId { get; }
    static abstract ChannelType Channel { get; }
    void Serialize(ref BitWriter writer);
}

public interface IPacket<TSelf> : IPacket where TSelf : struct, IPacket<TSelf>
{
    static abstract TSelf Deserialize(ref BitReader reader);
}
```

Requirements:

- Packet IDs are centralized and non-overlapping.
- Packet registry supports O(1) dispatch.
- Unknown packet policy is explicit (drop/log/strike/disconnect based on context).

### 8.1 Packet Registry and Dispatch Rules (Performance-Critical)

- Build packet registry at startup, then freeze immutable dispatch tables.
- No reflection in hot dispatch path (reflection/codegen allowed at startup only).
- Provide O(1) lookup for:
  - packet ID -> decode/handler entry,
  - packet type -> packet ID/channel metadata.
- Avoid interface boxing in packet send/dispatch paths; keep generic struct-based fast paths.
- Separate decode and handler dispatch stages so decode work can be profiled and budgeted independently.

---

## 9. Compatibility and Handshake

Connection handshake must validate:

- Protocol version.
- Content/schema hash (components, packet registry, optional prefab/schema hash).
- Feature flags (compression, encryption, transport capabilities).

Failure behavior:

- Reject with reason code.
- Graceful disconnect.

---

## 10. Session Lifecycle

States:

- `Connecting`
- `Handshaking`
- `Authenticated`
- `Ready`
- `Disconnecting`
- `Disconnected`

Required transitions:

- Auth timeout.
- Keepalive timeout.
- Duplicate login/session handling policy.
- Graceful shutdown broadcast and disconnect.

### 10.1 Entity Identity Across the Wire

Network identity must be defined separately from runtime storage identity:

- Server runtime `Entity` handle (`Index + Version`) is authoritative for server internals.
- Wire format uses `NetworkEntityId` (stable for the connection/session scope at minimum).
- Client projected world maps `NetworkEntityId -> local Entity`.

Rules:

- Do not expose server runtime entity indices directly as a long-term protocol guarantee.
- Reconnect behavior (same/different `NetworkEntityId` for same world object) must be explicit per feature.
- Persistence identity, runtime entity identity, and wire identity may be separate namespaces and must not be conflated.

### 10.2 Session State Boundaries

To preserve module isolation and reduce coupling:

- Session objects in `Network/` must not hold direct gameplay object references.
- Session state stores network-native identity and opaque game identity keys only.
- Any mapping from session/account/character to ECS entities happens in bridge code under `Server/Engines/Networking` (with optional shared helpers in `Shared/Engines/Networking`).
- Session IDs are required and stable for connection lifetime for logging, metrics, and diagnostics.

---

## 11. Replication Model

Server replication pipeline:

1. Drain dirty entities (`World.DrainDirtyTo*`).
2. Apply interest filter.
3. Apply per-connection packet budget.
4. Encode deltas/snapshots.
5. Queue for outbound send.

Design constraints:

- Dirty is an input signal, not final payload.
- Interest and budget are first-class controls.
- Packet generation is deterministic per tick.

### 11.1 Dirty Contract and Delta Cursors

Dirty tracking semantics for replication:

- Track global dirty by component type and entity identity.
- Use per-connection replication cursors/watermarks (for example `LastAckedTick` + per-stream cursor) to avoid full rescans.
- Support resend/backfill behavior when a connection misses updates.
- Drain/build logic must remain deterministic for a given tick and connection state.

Storage-model requirement (must be decided before implementation):

- Choose one explicit model for per-connection delta tracking:
  - per-connection bitsets,
  - per-connection tick cursor + replay window,
  - or per-connection staleness/priority accumulators.

v1 default recommendation:

- Tick-cursor + bounded replay window, plus coalesced latest-state fallback for overflow.

### 11.2 Interest Contract (Scalability Requirement)

Interest filtering must combine:

- Spatial partition baseline (cell/chunk/region membership), and
- Explicit subscriptions (party/guild/ownership/forced relevance).

Operational rules:

- AOI enter/leave events are explicit and ordered.
- Replication cost should scale with local interest, not total world population.
- Avoid O(N^2) per-connection world scans in steady state.

### 11.2.1 Chunked Interest Partition Layers (Implementation Guidance)

Recommended chunking split for the MMO/world runtime:

- **Spatial chunks (runtime/AOI):** dynamic entity membership for interest filtering and replication fanout grouping.
- **Terrain chunks (authoritative content):** terrain tiles + frozen static/deco/building records + dirty/versioning + collision/nav rebuild triggers.
- **Render chunks (client sliding window):** pooled client-side render containers mapped to absolute server chunk coordinates.

Frozen map statics (baked '/freeze'-style objects) should be loaded as chunk content records, not default ECS entities. They must exist on the server for collision/LOS/nav queries and on the client for rendering. Promote to ECS entities only when runtime state/logic is required (doors, containers, destructibles, harvestables, etc.).

### 11.3 Budget Policy (Per Connection)

Replication budgets are per connection, not global.

Minimum controls:

- Max bytes per tick per connection.
- Max packets per tick per connection.
- Priority lanes (critical -> state -> cosmetic).
- Explicit overflow policy (defer/drop/coalesce) with metrics.

### 11.4 Replication Execution Strategy (Scalability)

To avoid per-client O(world) behavior under high CCU:

- Build shared deltas per interest group/partition when possible, then fan out to members.
- Avoid re-filtering and re-serializing identical payloads for each client in the same visibility group.
- Keep per-connection customization minimal and explicit (for example, identity remaps or lane trimming only).
- Encode path should prefer deterministic ordering and cache-friendly iteration (stable entity order within a tick).

### 11.5 Encoding Priority Order (Bandwidth ROI)

When reducing bandwidth/cost, prioritize in this order:

0. Spatial chunking/AOI interest partition correctness (membership tracking + enter/leave semantics).
1. Spatial/interest culling.
2. Change-only/delta replication.
3. Per-connection budget lanes and coalescing.
4. Bit/field packing and compact IDs.
5. Batching.
6. Compression for large/batched payloads only.

---

## 12. Client Prediction and Reconciliation

Client flow:

- Send input with sequence numbers.
- Predict locally in client world.
- Receive authoritative ack/state.
- Reconcile if divergence exceeds threshold.
- Interpolate remote entities (non-self).

Rules:

- Reconciliation mutates client projected world only.
- Server remains sole authority.
- Client reconciliation follows world mutation gateway rules (queue-backed world APIs + barrier application), not direct archetype/storage mutation.
- Barrier discipline implies reconciliation is not an out-of-band write; rollback/correction design must account for barrier-apply timing.

---

## 13. Security and Hardening

Minimum v1 requirements:

- Per-connection rate limits (packets/bytes per tick/window).
- Packet size and field bounds validation.
- Invalid/abusive packet strike policy.
- Handshake/auth timeout enforcement.
- Connection caps and back-pressure behavior.

---

## 14. Performance Requirements

Targets (initial):

- No avoidable allocations in steady-state parse/build/dispatch loops.
- Network stage time budget is measurable and bounded.
- Replication drain/build should degrade gracefully under overload via budget caps.

Mandatory instrumentation:

- Inbound/outbound bytes per tick.
- Packets by channel/type.
- Dirty drained, interested selected, dropped by budget.
- Decode/encode timing.
- Queue depths and flush latency.
- Session lifecycle counters (auth success/fail, timeout/disconnect reasons).
- Budget pressure counters (deferred, coalesced, dropped by lane/reason).
- Buffer/queue efficiency counters (pool rent/return/miss, queue depth/high-water marks).
- Serialization/dispatch efficiency counters (decode failures, unknown IDs, per-stage allocation counters where available).

### 14.1 Statistics Contract (Required)

Network runtime must expose statistics at multiple levels:

- Transport stats:
  - inbound/outbound bytes, packets, send/receive failures, reconnect attempts.
- Session stats:
  - active sessions, handshake/auth success/fail counts, timeout/kick/disconnect reasons.
- Protocol stats:
  - packet counts by packet ID and channel, unknown packet counts, validation failures.
- Replication stats:
  - dirty drained, interested selected, entities encoded, bytes produced, bytes dropped/deferred by budget.
- Tick-phase stats:
  - poll/decode/apply/build/flush timing per tick, plus p50/p95 windows.

Operational requirements:

- Stats collection is optional/toggleable and designed for minimal hot-path overhead.
- Dev/staging can run full stats; production can run sampled/reduced stats.
- Counters must be safe under concurrent access.
- No silent dropping: overflow/defer/drop paths increment explicit counters.

### 14.2 Statistics Tiers and Cardinality Rules

To keep observability broad without turning stats into a bottleneck:

- Always-on core counters:
  - world-level network totals (bytes, packets, errors, timings).
  - per-tick stage timings and moving-window summaries.
- Optional detailed counters:
  - per-packet-type counts and per-channel timings.
  - per-connection detailed stats.
- Cardinality constraints:
  - detailed per-connection stats must be sampled, capped, or top-N bounded in production.
  - avoid unbounded label dimensions (for example, raw user identifiers as metric labels).
- Export requirements:
  - maintain both cumulative totals and windowed rates (for example 1s/10s/60s windows).
  - structured benchmark/status line output remains available for soak parsing.

---

### 14.2 Island Slice Empirical Baseline (Pre-AOI, Feb 2026)

Measured with a single-process multi-socket synthetic client harness on the fixed island slice (pre-chunking / pre-AOI):

- Dense/everyone-active profile (20 Hz movement + chat): ping p95 SLO passed through ~56 CCU and failed at 64 CCU.
- More realistic profiles (10 Hz movement-only or ~40% active movers/chatters): ping p95 SLO passed through ~80 CCU and failed at 96 CCU.
- Fresh vs warm server runs showed the same knee (no meaningful uptime drift in tested windows).

Interpretation: the next major scaling win should come from spatial chunking + AOI interest filtering before micro-optimizing fanout/serialization paths.

## 15. Logging and Diagnostics

- Route network logs through host/log abstraction (same pattern as ECS log sink).
- One-line structured events for benchmark parsing.
- Runtime toggles for log verbosity.

---

## 16. Testing Strategy

### 16.1 Unit

- Packet serialize/deserialize round-trip.
- Sequence/ack wraparound correctness.
- Channel routing and fallback behavior.
- Registry collisions/unknown packet handling.

### 16.2 Integration

- Client/server handshake success/failure matrices.
- Auth flow and disconnect scenarios.
- Loopback deterministic simulation tests.
- World mutation gateway enforcement under network input.

### 16.3 Load/Soak

- High connection count with synthetic traffic.
- Dirty replication with budget pressure.
- Long-duration soak with leak/allocation monitoring.

### 16.4 Determinism

- Same input stream -> same server world state hash (within deterministic config constraints).
- Determinism target is scoped to same build/config/runtime profile (not guaranteed cross-architecture floating-point equivalence in v1).

---

## 17. Phased Implementation Plan

### Phase 1 - Foundation

- `INetworkTransport` abstraction.
- Loopback transport.
- Packet registry + typed packet contracts.
- Handshake/version gate.
- Baseline transport/protocol counters + decode/poll/flush timings.
- Length-prefixed TCP framing + stream reassembly buffer contract.
- Startup-built, frozen dispatch table with O(1) ID/type metadata lookups.

### Phase 2 - Server Integration

- Connection/session manager.
- Inbound command queue -> pre-barrier apply.
- Outbound queue flush hook.
- Session lifecycle stats (connect/handshake/auth/ready/disconnect) with reason codes.
- Explicit session ID model and no direct gameplay object references inside `Network/Session`.

### Phase 3 - Replication

- Dirty drain + interest filter + packet budget pipeline.
- Per-connection replication snapshots/deltas.
- Metrics and structured logs.
- Budget pressure stats per priority lane (defer/coalesce/drop) and per-connection caps.
- Interest-group/shared-delta fanout path to reduce per-client duplicate filter/encode work.

### Phase 4 - Client Runtime

- Input send + sequence tracking.
- Prediction/reconciliation.
- Interpolation systems.
- Client prediction/reconciliation stats (rollback counts, correction magnitudes, queue apply latency).

### Phase 5 - Hardening

- Rate limiting and abuse handling.
- Soak/load profiles.
- Failure recovery scenarios.
- Production stats profile (sampling/caps/top-N rules) validated under soak.
- Hot-path audit for avoidable allocations, wall-clock calls, and unintended boxing regressions.

---

## 18. Current Status vs vNext Network

Current repo status:

- ECS world/barrier model, dirty tracking, and networking stage slot are in place.
- Macro benchmark harness for server-profile simulation exists under `_tmp`.
- Full socket networking runtime is **not implemented in vNext ECS module yet**.

Legacy `_OLD/Network` provides useful reference implementations for:

- channel model,
- packet contracts,
- bit-level serialization,
- session/connection flow,
- Godot wrappers.

vNext should reuse the valid concepts, but be rebuilt to enforce ECS barrier/mutation rules and stricter hot-path allocation control.

---

## 19. v1 Decision Defaults

These defaults are selected for first implementation and can be revised later.

1. Transport scope:
   - TCP-first baseline; keep channel abstraction; defer hybrid UDP enablement to a later phase.
2. Compatibility policy:
   - strict protocol/schema/content-hash match for connection acceptance.
3. Compression:
   - disabled in v1; revisit as optional per-channel/per-payload policy in v2.
4. Encryption/auth transport:
   - handled by infrastructure boundary (for example TLS terminator/proxy) in v1.
5. Reconciliation thresholds:
   - component/system-specific thresholds (movement/combat/inventory can differ).
