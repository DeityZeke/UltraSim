# Network V3 implementation plan

Status: active forward plan. Network V3 is not implemented.

This plan starts from accepted ECS 3.4.1 and S0 authority, persistence, UI, and
cadence contracts. `_V2` remains executable historical specification and
comparison material; it is not a production dependency.

## Non-negotiable rules

```text
IDENTITY NUMERIC.
MESSAGES DATA.
AUTHORITY SEPARATE DELIVERY.
SIM STATE CAPTURED ONCE.
INTEREST ROUTES STATE.
LANES DEFINE DELIVERY.
PACKETIZATION LAST.
TRANSPORT MOVES BYTES.
APPLICATION CROSSES BARRIER.
MEMORY BOUNDED.
LOSS NEVER SILENTLY CHANGES SEMANTICS.
```

The network kernel must not own gameplay authority, ECS storage, Godot objects,
world persistence, UI policy, or content authoring. Bridges translate, route,
and synchronize between those domains.

## Required execution modes

All modes share schemas, semantic messages, admission/session rules, replication
state, and client apply behavior.

### OfflineDirect

Typed semantic operations cross the authority and client-apply barriers without
encoding fake packets. It is the deterministic correctness oracle and normal
offline topology. It must not grant the client direct ECS access or bypass
server validation.

### ProtocolLoopback

The complete protocol encodes/decodes through a deterministic in-memory link
supporting latency, jitter, loss, duplication, reordering, corruption, MTU, and
bandwidth controls. This is the primary protocol/recovery test bed.

### Production transport

The same protocol runs through real localhost/LAN/Internet transport, initially
secure UDP where measurement supports it. Transport owns datagrams and byte
delivery, not application meaning.

OfflineDirect and ProtocolLoopback must remain semantic oracles for the
production path rather than evolving separate feature sets.

## Cadence model

Authoritative performance tuning remains fixed 60 Hz (about 16.667 ms), 1x
playback, with Runtime cadence changes and adaptation disabled. That is a
performance baseline, not a wire assumption.

```text
simulation cadence    Server Runtime policy
replication cadence   independent Network policy by lane/recipient
render cadence        independent Client policy
```

Network must remain correct if an explicitly variable Runtime changes among
supported exact frequencies. Packets are not inherently emitted once per
simulation tick. Network time, simulation tick, stream sequence, packet number,
and presentation time are distinct domains with explicit wrap/epoch rules.

## Architecture

```text
authoritative ECS/game state
        |
capture once at a stable post-simulation boundary
        v
immutable replication view / lifecycle batch / application events
        |
interest + lane scheduling + recipient-known state
        v
semantic messages
        |
generated canonical codecs
        v
packet assembly, reliability, pacing, congestion, encryption
        |
transport
        v
receive validation + session state
        |
client apply barrier
        v
client projected state, UI, presentation
```

Simulation capture occurs once per source epoch. Per-recipient fanout references
immutable captured data; it does not re-read live ECS columns independently for
every connection.

The initial implementation should introduce the fewest projects that preserve
these boundaries. Do not build a generic message bus, actor framework, or service
container.

## Identity domains

Numeric bounded value types distinguish at least:

- connection/session identity;
- packet number and acknowledgement sequence;
- logical stream/lane identity;
- stream sequence and baseline epoch;
- persistent world/entity identity;
- recipient-relative short entity identity when admitted by policy;
- prefab/content/schema identity;
- interest region/layer/anchor identity.

Runtime `Entity`, chunk row, archetype ID, pointer, and Godot instance ID never
cross the wire. Persistent identity is globally interpreted only with its world
identity. Recipient-relative IDs require explicit bind/unbind epochs so reuse
cannot create ABA.

## Messages, codecs, and parsing

Messages are bounded data, not object graphs or delegates. A source generator
should produce schema identity, maximum encoded size where knowable, encode,
strict decode, version compatibility, and inspection metadata.

Parsing is fail-closed and canonical:

- lengths/counts are checked against configured limits before allocation/copy;
- integer, boolean, string, enum, and padding encodings have one accepted form;
- duplicate/out-of-order fields reject where order is canonical;
- trailing bytes reject unless the version contract explicitly owns them;
- invalid UTF-8/UTF-16 does not silently normalize durable/protocol identity;
- fragmented data cannot request unbounded buffers;
- authenticated data is not applied before integrity/replay validation.

Compact encoding is earned by measured traffic: deltas, quantized transforms,
short recipient IDs, field masks, and cohort fanout. Never make correctness
depend on lossy compression.

## Lanes and delivery semantics

Delivery behavior is defined before packetization. Initial lanes should cover:

- latest-state snapshots (replaceable, baseline-aware);
- ordered reliable application events;
- lifecycle/bind/unbind state;
- input/control with expiry and acknowledgement;
- UI/admission semantic operations;
- low-priority bulk/content/terrain data;
- operator/diagnostic traffic isolated from gameplay fairness.

Each lane specifies ordering, reliability, supersession, expiry, priority,
recovery, and backpressure. Loss may delay, supersede, or trigger recovery only
according to that lane contract. An event stream cannot silently behave like
latest state; latest state cannot grow an unbounded reliable backlog.

## Transport and connection protocol

The first production candidate is a conservative-datagram secure UDP protocol,
subject to measurement. Required mechanisms include:

- version/capability handshake and stateless anti-amplification validation;
- authenticated encryption, key/epoch rotation, replay protection;
- packet number and acknowledgement ranges;
- RTT/RTO estimation;
- congestion control, pacing, and per-session fairness;
- bounded segmentation/reassembly below a conservative path MTU;
- persistent socket I/O ownership and bounded buffer pools;
- keepalive, idle timeout, orderly close, reconnect/resume policy;
- explicit protocol/codec/session metrics.

Do not begin with platform-specific zero-copy or native socket specialization.
Establish the portable semantic and ownership baseline first, then measure.

## Sessions and admission

The connection/session state machine must separate transport reachability,
cryptographic establishment, protocol compatibility, authentication, Admission,
active play, reconnect, and close.

Admission reuses current durable identity/account/character policy. Credentials
are validated by the authoritative server; the network kernel transports bounded
claims and results. A reconnect never revives stale generation-local `Entity`
values. Existing UI/action sessions rebind by durable identity or receive an
explicit resnapshot/close.

Session tables are capacity-planned dense storage with stable handles and
generation/epoch protection. Expected exhaustion rejects new work without
unbounded allocation.

## Replication

### Capture

At a defined post-simulation boundary, an ECS bridge captures immutable network
views from generated chunk jobs, change cursors, and lifecycle publication. It
must preserve success-only cursor acknowledgement and resync when retained
history is exceeded.

### Latest state

Latest-state tables keep the newest authoritative value by stable identity and
field/schema. Recipient-known state tracks the baseline/sequence actually safe
to delta against, not merely what was sent. Superseded unsent state is replaced.

### Lifecycle and ordered events

Create/destroy/add/remove/bind/unbind use explicit ordered semantics. Independent
game events use a bounded ordered stream with acknowledgement and resnapshot
policy. Lifecycle and value change are not conflated.

### Atomic baselines

Initial join, AOI expansion, reconnect, or history loss produces a baseline with
an epoch and completeness contract. A client does not expose a half-applied
world. Baseline completion atomically advances recipient-known state; deltas and
events outside the admitted epoch are rejected or buffered within fixed limits.

## Interest management

Interest decides which captured state is routed to each recipient. It does not
define entity identity or mutate the simulation.

The initial policy may use stable world regions/rings and multiple interest
layers with one or more anchors. Index updates are incremental and bounded.
Dynamic entities crossing regions, large cross-region entities, dormancy,
terrain/statics manifests, and recipient subscriptions have explicit semantics.

World coordinates remain authoritative and stable. Client floating origin and a
fixed render window are presentation policies. Region identity must not depend
on the client's current local origin.

## Scheduling, fanout, and backpressure

The connection scheduler owns per-lane due work, byte budgets, pacing, fairness,
and bounded queues. Replication cadence can differ by lane, entity fidelity,
interest, and recipient without changing authoritative simulation cadence.

Shared cohort fanout may reuse canonical encoded fragments only when every
recipient-relative field and baseline assumption is identical. Otherwise share
captured semantic state, not invalid wire bytes.

Overload policy is explicit: coalesce latest state, reduce optional replication
frequency/fidelity, defer low-priority bulk work, or disconnect persistently
nonviable sessions. Never silently drop reliable events or alter authority.

## Client runtime

The client owns a projected state, not a second authority. Receive processing
validates and stages complete semantic operations. One client apply barrier
publishes coherent updates to gameplay presentation/UI.

Input has sequence, expiry, and acknowledgement. Prediction/reconciliation is
introduced only for selected movement contracts with a deterministic oracle.
Observer interpolation uses presentation time and does not redefine server tick.

Disconnect/dispose closes renderer handles, clears retained definitions/views,
and invalidates session-relative identity. Reconnect establishes a new admitted
epoch before state becomes visible.

## Application bridges

Network V3 must prove these current game semantics through both local and remote
paths:

- trusted identity, account status, character selection, and entry;
- movement/input authority;
- server-driven UI definitions, models, deltas, actions, and resnapshot;
- compiled prefab/content identity needed by projection;
- chat/commands and ordered application events;
- lifecycle/value replication;
- persistence restart and durable identity rebinding;
- operator traffic separated from normal recipient budgets.

Application requests cross an explicit authoritative ingress barrier. No bridge
may call game objects as a substitute for accepted input ordering.

## Capacity and observability

Cold options and planners cover sessions, streams, packet buffers, reassembly,
message sizes, captured rows, lifecycle/event retention, recipient-known state,
interest subscriptions, baseline caches, UI payloads, and diagnostics.

Always-on metrics include bytes/packets by lane, loss/retransmit, RTT/RTO,
congestion window/pacing, queue occupancy, supersession, baseline/resync,
session state, capture/encode/assembly/send/receive/apply time, and allocation.
Detailed tracing is sampled and bounded. A deterministic trace recorder/replayer
must avoid retaining live authority.

## Implementation phases

Each phase ends in executable semantic parity, bounded-memory evidence, and a
decision before the next layer is added.

1. **Semantic oracle and OfflineDirect vertical.** Freeze message identity,
   capture/apply barriers, one admission/UI/state flow, and canonical hashes.
2. **Capacity and generated codec foundation.** Strict parsers, fixed pools,
   ownership tests, schema compatibility, and protocol inspection.
3. **ProtocolLoopback.** Deterministic impairment, session handshake, packet
   envelope, lanes, ACK/replay/reassembly, and recovery oracles.
4. **Production localhost UDP.** Secure handshake, socket engine, pacing,
   congestion/fairness, shutdown, and loopback parity.
5. **Latest state and lifecycle.** Capture once, recipient-known state,
   baseline/delta/lifecycle correctness, independent replication cadence.
6. **Interest and enormous-world routing.** Regions/layers/anchors, incremental
   subscription indexes, dormancy, coordinates, render window.
7. **Client movement and projection.** Input expiry, selected prediction,
   reconciliation, interpolation, atomic apply, disconnect/reconnect.
8. **Application parity.** Admission, UI, chat/commands, content, persistence
   restart, and operator paths.
9. **Measured specialization.** Cohort fanout, compression, native I/O, sharding,
   or platform paths only where accepted profiles justify complexity.

## Mandatory test matrix

- codec round trips, canonical rejection, version/schema evolution, fuzzing;
- handshake/auth/replay/anti-amplification and hostile byte streams;
- loss, duplication, reordering, jitter, corruption, MTU, bandwidth, reconnect;
- packet/stream wrap and epoch reuse;
- latest-state supersession, ordered-event retention, baseline atomicity;
- lifecycle lag/resync and recipient-known-state correctness;
- interest enter/leave/crossing, multiple anchors/layers, floating origin;
- deterministic OfflineDirect/ProtocolLoopback/UDP canonical parity;
- authority ingress ordering and client apply atomicity;
- session/generation replacement and persistence restart;
- capacity exhaustion, ownership, use-after-return, shutdown, worker faults;
- zero warmed allocation for admitted hot profiles;
- long soak/chaos with fixed memory and no silent semantic loss.

## Benchmark policy

Measure semantic work separately from packetization and transport. Protected
profiles include codec throughput/size, capture, interest routing, fanout,
assembly, encryption, loopback/UDP latency, connection scaling, baseline storms,
impairment recovery, and complete tick/network/client-apply latency.

Every performance result carries canonical-state validation, allocation scope,
capacity/configuration, cadence disclosure, process ordering where relevant, and
mean plus tail latency. Fixed 60 Hz/adaptation-off remains the authoritative
performance control. Replication-rate experiments are labeled independently.

## Definition of done

Network V3 is complete only when:

- all three execution modes share one semantic/protocol contract;
- untrusted bytes cannot escape bounds or publish partial state;
- authority and client projection remain separate in every topology;
- simulation capture is shared and recipient fanout is bounded;
- lanes preserve their declared loss/order/recovery semantics;
- baseline, lifecycle, event, reconnect, and epoch behavior are deterministic;
- admission/UI/current game flows work through the real protocol;
- memory is capacity-planned and warmed allocations meet accepted budgets;
- production UDP and impairment tests pass Debug and Release;
- fixed-60 simulation performance cannot be hidden by replication or cadence
  degradation;
- documentation and XML contracts describe the final public API rather than this
  implementation plan's intermediate phases.

## Deliberate open decisions

These require measurement during the relevant phase rather than premature
commitment:

- exact secure-UDP primitives/library versus narrowly owned implementation;
- acknowledgement detail retained for latest-state baselines;
- cohort wire-fragment reuse granularity;
- initial coordinate quantization and transform encoding;
- compression thresholds and content classes;
- native/platform socket specialization;
- client prediction scope;
- sharding boundaries.

They may change mechanisms, but not the non-negotiable semantic rules at the top
of this plan.
