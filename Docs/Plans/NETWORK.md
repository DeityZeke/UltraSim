# Network V3: N0 remote-authority implementation plan

Status: active forward plan. N0.0 contract/evidence, N0.1 canonical game
projection/OfflineDirect, N0.2 bounded Network kernel foundations, and N0.3
secure reliable protocol over deterministic datagrams are accepted. N0.4 remote
Admission/UI over ProtocolLoopback is next.

This plan now runs on accepted ECS 3.4.2, Server Runtime, game composition,
transactional persistence, cadence independence, and the neutral UI protocol.
It is an implementation contract for the machine that exists now—not a plan to
invent another server core around Network. `_V2` remains executable historical
specification and comparison material; it is not a production dependency.

The first complete milestone is **N0 — Remote Authority / Client Vertical**:

> Start the existing authoritative `ServerRuntime` and concrete game
> composition, establish one authenticated remote client, resolve it to a game
> participant, present the existing server-defined UI semantics remotely, enter
> the persistent world, submit authoritative input, replicate bounded
> owner/observer state and lifecycle, survive loss/reordering/reconnect and
> whole-generation replacement, and prove semantic parity across
> OfflineDirect, ProtocolLoopback, and localhost UDP.

Network moves and schedules information. It does not become authority,
persistence, game policy, or presentation.

## Frozen principles

```text
AUTHORITY DOES NOT MOVE INTO NETWORK.
CONNECTION IDENTITY IS NOT GAME IDENTITY.
RUNTIME ENTITY IDENTITY IS NOT WIRE IDENTITY.
NETWORK IDENTITY IS NOT PERSISTENT IDENTITY.

IDENTITY IS NUMERIC.
MESSAGES ARE DATA.
AUTHORITY IS SEPARATE FROM DELIVERY.
CAPTURE HAPPENS AFTER AUTHORITY COMMITS.
CAPTURE ONCE; RETAIN OUTSIDE ECS.
INTEREST DECIDES WHO. DELIVERY DECIDES WHEN.
CADENCE CONTROLS ELIGIBILITY, NOT EXISTENCE.
SENT IS NOT KNOWN. ACKNOWLEDGED/APPLIED IS KNOWN.
LATEST STATE COALESCES. EVENTS RETAIN FACTS.
BASELINES PREPARE, VALIDATE, THEN COMMIT.
AUTHORITY COMMIT PRECEDES PRESENTATION DELIVERY.

LANES DEFINE DELIVERY.
PACKETIZATION IS LAST.
TRANSPORT MOVES BYTES.
APPLICATION CROSSES A BARRIER.
MEMORY IS BOUNDED.
LOSS NEVER SILENTLY CHANGES SEMANTICS.

TICK IDENTIFIES STATE.
TIME DEFINES DURATION.
NETWORK SCHEDULING IS ITS OWN POLICY.
```

These rules survive changes to transport, codec layout, sharding, compression,
prediction, and content scale.

## What remains valid from earlier design work

- secure UDP remains the primary production candidate, subject to implementation-time
  platform, cryptographic, and CPU measurement;
- OfflineDirect, ProtocolLoopback, and real UDP are deliberately different
  topologies sharing semantic contracts;
- latest state and ordered events are distinct delivery kinds;
- recipient knowledge derives from acknowledgement/application, not send intent;
- initial and recovery baselines stage and publish atomically;
- recipient-relative pending and known state is fixed-capacity;
- AOI identity is canonical while recipient priority/cadence is relative;
- captured state can be fanned out without re-reading authority per recipient;
- Bulk cannot starve Control, Input, state recovery, or ordered gameplay events;
- packetization follows semantic capture, interest, and lane selection;
- parsers are strict, canonical, and bounded before proportional work;
- deterministic impairment and semantic-oracle testing precede socket tuning;
- sharding, native socket specialization, SIMD, and syscall batching follow the
  accepted single-owner baseline.

## Ownership and dependency shape

The authoritative center already exists:

```text
ServerRuntime
    -> ServerGeneration
        -> EcsWorld
        -> authoritative WorldClock
        -> complete GameGenerationState
```

Network participates in that architecture:

```text
AUTHORITATIVE GAME

Game/Composition
Game/Admission
Game/UI
Game/Inventory
Game/Replication              new: game-specific capture, relevance, projection
        |
        | semantic state and accepted intent
        v

RUNTIME / BRIDGES

ServerRuntime
Server/Hosting
Server/Network                new: generic Runtime/Network attachment only
        |
        v

KERNEL

Kernels/Network               identities, buffers, protocol, lanes, security,
                              reliability, pacing, transport, metrics

REMOTE CLIENT

Client/Network
        |
        v
Client/Core
        |
        v
Client/_Godot
```

Local world/UI projection crosses the same semantic boundaries directly:

```text
Game/Replication or Game/UI
        |
        v
Client/Core
```

The initial project candidates are:

- `Kernels/Network` — game/ECS/UI-independent bounded network mechanism;
- `Server/Network` — lifecycle attachment and translation around the existing
  Runtime; no game rules;
- `Game/Replication` — what this game captures, who should observe it, and
  owner/observer semantic state;
- `Game/Network` — only when game-specific remote Admission/command binding
  becomes concrete;
- `Client/Network` — protocol receive/send and client semantic bridge;
- `Kernels/Network.Generators` — only if a generator split earns itself.

Do not create `Server/Core`, another authoritative runtime, a generic
`Server/Projection` framework, a message bus, actor framework, or service
container. If changing to a different game changes a rule, that rule normally
belongs in `Game/Replication` or `Game/Network`, not `Server/Network`.

Bridges remain responsible for exactly:

> Translate. Route. Synchronize.

## Required execution modes

All modes share semantic state/messages, Admission rules, projection behavior,
client apply rules, and canonical outcome traces.

### OfflineDirect

Typed semantic operations cross authority/capture and client-apply barriers
without fake packets. It is the deterministic semantic oracle and normal
offline/listen-host local path. It cannot expose ECS to the client or bypass
authoritative validation.

### ProtocolLoopback

The complete production protocol encodes and decodes across a deterministic
in-memory datagram link with latency, jitter, loss, duplication, reordering,
corruption, MTU, burst, and bandwidth controls. It is the protocol, security,
recovery, and chaos oracle.

### Production transport

The same protocol runs through real localhost/LAN/Internet transport. The first
candidate is secure UDP. Localhost is not a privileged plaintext protocol mode.
Transport owns datagrams and byte movement, never message meaning.

OfflineDirect and ProtocolLoopback remain oracles; they must not evolve as
separate reduced products.

## Authoritative capture boundary

ECS now defines the exact capture primitives:

```text
component topology selects work       generated chunk job
changed component values              ChangeCursor / changed-row job
create/destroy/add/remove history      LifecycleCursor
few arbitrary referenced entities     prepared lookup
external/preselected command array     RangeJob
shape mutation                         structural writer
```

Replication capture is not merely another in-graph `Networking` stage. The ECS
scheduler runs its graph, merges structural segments, commits the post-graph
structural barrier, publishes lifecycle facts, and only then completes the
tick. Therefore the canonical flow is:

```text
ECS graph
    Input -> ... -> Cleanup
post-graph structural barrier
    create/destroy/add/remove commit
    lifecycle publication
tick completes
        |
        v
Game/Replication capture
    changed values
    lifecycle facts
    explicit game events
        |
        v
game-owned canonical projection state
        |
        v
interest and recipient pending state
        |
        v
Network delivery
```

The replication capture consumer drains ECS promptly. Slow clients never own
ECS cursors or force ECS history retention. Lag is retained and recovered in
projection/network-owned state.

Cursor acknowledgement occurs only after corresponding canonical projection
state has been published successfully. History overflow becomes explicit
resynchronization, never partial truth.

### Lifecycle capacity

Lifecycle publication is opt-in and the current S0 game profile leaves its
capacity at zero. N0 introduces a deliberate **network-enabled world profile**.
Its lifecycle capacity is based on:

```text
maximum structural facts per interval
    x maximum admitted gap between canonical capture passes
    + measured safety margin
```

It is not multiplied by connection count because one ECS consumer captures once
and fans outward. The existing non-network profile should not silently acquire
network memory cost.

## Identity and lifetime domains

Keep these domains explicit and non-interchangeable:

```text
ConnectionId             transport/session slot and generation
SecurityEpoch            directional key/authenticated-replay lifetime
ParticipantId            stable game-side participant identity
ServerGenerationId       active authority root identity
StateEpoch               one published world projection lifetime
ReplicationEntityId      canonical dense generational identity within StateEpoch
WireEntityAlias          optional connection-relative compact encoding
ClientEntitySlot         client-local projection storage identity
PersistentEntityKey      durable world + entity identity
UiViewInstanceId         retained UI interaction lifetime
PacketNumber             authenticated datagram identity; admitted data may be transport-ACKed
LaneSequence             per-delivery-contract order/version domain
```

Runtime `Entity`, chunk row, archetype ID, pointer, and Godot instance ID never
cross the wire. Persistent identity is interpreted with its world identity.
`ReplicationEntityId` is connection-independent: every recipient normally
refers to the same canonical replicated entity within one `StateEpoch`. It is
rebuilt when that epoch changes. `WireEntityAlias` is optional and may be added
only when bandwidth evidence earns per-connection aliasing; it cannot redefine
canonical identity. `ClientEntitySlot` is private client storage. Every binding
includes epoch/generation protection so reuse cannot create ABA.

Do not add a public `RecipientId` for N0. Remote binding is:

```text
ConnectionId -> ParticipantId
```

Offline projection uses `ParticipantId` directly. Projection may use an
internal dense generational `ProjectionRecipientSlot`; it does not become a
public identity until a second semantic use case—such as multiple projections
per participant—actually exists.

### Generation replacement

Connections and world projections have different lifetimes. Publication of a
new `ServerGeneration` invalidates old runtime entities, network-entity maps,
baselines, pending world state, and `StateEpoch`; it need not disconnect a
healthy connection.

```text
new ServerGeneration publishes
    -> Admission reconciles/rebinds durable game sessions
    -> projection discards old generation-local state
    -> projection increments StateEpoch and rebuilds canonical truth
    -> still-admitted participants receive a fresh baseline
    -> invalidated participants lose world binding explicitly
```

Network consumes Admission's reconciliation rather than duplicating
account/character rebinding.

## Host and Network pump order

Freeze the owner orchestration loop before protocol work:

```text
sample monotonic time
    -> receive completions / inbound datagrams
    -> authenticate and decode into bounded connection-owned ingress
    -> owner drain and game validation
    -> publish accepted work to the authoritative input arena
    -> execute 0..N complete ServerRuntime simulation steps
    -> capture canonical projection (never mid-step)
    -> update interest and recipient pending state
    -> schedule lanes and assemble packets
    -> bounded transport handoff
    -> socket submission
```

`ServerRuntime.Tick(TimeSpan)` may execute zero, one, or several complete steps
during host catch-up. Projection capture need not run after every step: changed
values may coalesce after several completed steps, while lifecycle history and
explicit game-event retention preserve facts that cannot coalesce. Lifecycle
capacity therefore includes the maximum admitted steps and structural facts
between captures, including catch-up and host stalls.

Ingress, catch-up, capture, assembly, and transport handoff all have explicit
budgets. Network never observes or captures a partially executed step.

## Connection and application state machines

Do not encode game workflow into the network kernel.

Kernel/connection state is approximately:

```text
Free
CookieValidated
KeyExchange
ProtocolValidated
Established
Draining
Closed
```

Game/application binding is separate:

```text
Connected
AuthenticatingPrincipal
WaitingForCharacter
Entered
Resynchronizing
Detached / Rejected
```

A connection may remain cryptographically healthy while the participant is
waiting for a character, temporarily resynchronizing, or detached from world
authority.

Transport security and game authentication are separate:

```text
Network handshake/security
    -> authenticated and integrity-protected channel/session

game authentication over reliable Control
    -> verifies account/platform credential or token
    -> TrustedIdentity { Provider, Subject }

Accounting
    -> TrustedIdentity -> Account

Admission
    -> Account -> durable Character -> current Entity
```

A cryptographically established channel does not imply account trust. Network
is the second real trusted-identity source, so the remote Admission phase
replaces the local-fixture-shaped `TrustedLocalIdentity` with a bounded
provider-scoped identity. Network never owns password or account policy. This
change includes persistence/version compatibility rather than an unreviewed
rename.

`ParticipantId` is server-created game/session identity: never client-chosen,
never an account ID, and never durable entity identity. It may survive a
generation replacement with a healthy connection. Reconnect policy may create
a new participant or explicitly reattach an existing application session from
trusted account/durable facts; possession of an old numeric value proves
nothing.

## UI and authoritative application semantics

Do not create a Network-specific UI model. `Kernels/UI` already owns:

```text
UiOpenView / UiHotOpenView
UiUpdateView / UiCloseView
UiActionRequest
IUiSemanticPort
```

The remote path is another adapter:

```text
Game/UI or Game/Admission
        |
 IUiSemanticPort
   +----+-------------------+
   |                        |
LocalUiSemanticPort   RemoteUiSemanticPort
   |                        |
LocalSemanticLink       Network protocol
   |                        |
   +------------> UiClientRuntime
```

The authoritative workflow remains unchanged. An accepted game mutation is not
rolled back because later UI delivery fails. Reliable UI failure detaches or
resnapshots presentation; it is not reported as a rejected gameplay action.

Application requests cross a bounded authoritative ingress barrier. A bridge
cannot invoke arbitrary game objects as a substitute for accepted ordering.

## Cadence and clocks

Fixed 60 Hz (about 16.667 ms), 1x playback, with Runtime cadence changes and
adaptation disabled remains the authoritative performance baseline. It is not a
wire assumption.

```text
authoritative simulation cadence   Runtime policy
network scheduling cadence         Network policy
input send cadence                  client/input policy
replication-family cadence          lane/game projection policy
render cadence                      client presentation policy
transport pacing/retransmission     monotonic-time transport policy
```

Packets are not emitted once per simulation tick by default. RTT, RTO, pacing,
timeout, and staleness use monotonic time. State identity uses `StateEpoch`,
`SimulationTick`, and state/lane versions.

On world entry and whenever an opt-in live simulation frequency changes, the
client receives a reliable authoritative clock anchor containing at least:

```text
StateEpoch
SimulationClockRevision
EffectiveFromSimulationTick
exact step duration / simulation frequency
authoritative world-time state required by gameplay
```

Owner/world samples carry the applicable `SimulationClockRevision` or remain
staged until that revision is known. For an idle-boundary frequency change after
completed tick `N`, the new cadence is effective from step `N+1`. This makes
Control-versus-State reordering safe under 60→30→60 changes. The replication
bridge may detect cadence change at capture; a new Runtime event is not presumed
necessary. Clients never derive time as `tick * 1/60`. Time-defined input
expiry and gameplay deadlines remain time-defined rather than being silently
reinterpreted as a count of 60 Hz steps.

## Persistence boundary

Persistence restores authority. Network reconstructs projection.

Network is not a save-set participant and connection/projection state is not
persisted. Detached save capture releases authority before encoding, hashing,
flushing, and publication, so durable I/O does not require Network to pause.
Network reacts only when Runtime publishes a replacement generation.

## Messages, codecs, and hostile input

Messages are bounded data, not object graphs or delegates. Generated codecs
should provide stable schema/message identity, maximum encoded size where
knowable, exact measure/write/read, strict version compatibility, and inspection
metadata without reflection or runtime protocol registration.

Parsing is canonical and fail-closed:

- lengths and counts are checked against configured limits before allocation,
  copy, decompression, or proportional loops;
- integer, boolean, string, enum, flag, and padding forms have one accepted
  encoding;
- duplicate/out-of-order fields reject when ordering is canonical;
- trailing bytes reject unless explicitly owned by the version contract;
- malformed text never silently normalizes identity;
- fragmented data cannot request unbounded buffers;
- data is not applied before authentication, integrity, replay, epoch, and
  semantic validation;
- decoder failure cannot publish partial client or authority state.

Authentication, transport delivery, and application knowledge are separate:

```text
AuthenticatedReplayWindow
    successful AEAD packet-number fact
    commits even when bounded reliable admission defers delivery

TransportDeliveryAckWindow
    data admitted into bounded receive/reassembly state
    drives packet ACK ranges and sender fragment release

application/baseline acknowledgement
    higher semantic proof defined by the owning message contract
```

A delivery-blocked reliable packet is not transport-ACKed. Its exact ciphertext is
still replay-rejected, while the retained semantic fragment may be retransmitted under
a fresh packet number. A transport-delivery ACK never by itself means that gameplay
executed, UI rendered, a baseline committed, or recipient-known state advanced.

Compact encoding is earned through measured traffic: field masks, deltas,
quantized transforms, recipient-relative short identity, and shared cohort
fanout. Lossy representation never becomes a correctness dependency.

## Lanes and delivery contracts

Freeze semantic delivery before packet layout:

| Lane | Initial semantics |
| --- | --- |
| Control | reliable ordered; highest priority; connection/protocol/world control |
| Input | unreliable sequenced with bounded redundancy and time-defined expiry |
| OwnerState | sequenced latest/keyframe; high priority; correction/ack aware |
| WorldState | sequenced latest, coalesced, baseline aware |
| Event | reliable ordered application facts whose intermediate occurrence matters |
| Bulk | reliable segmented and isolated from realtime/control |
| Disposable | optional unreliable cosmetic state with explicit expiry |

UI/Admission operations use the appropriate reliable semantic lane; they do not
invent a separate transport. Each message family declares ordering,
reliability, supersession, expiry, priority, recovery, fragmentation admission,
and backpressure.

Projection lifecycle and application events are not the same log:

```text
ECS lifecycle evidence
    -> update canonical Game/Replication truth
    -> derive Spawn / Enter / Leave / Despawn / projection-shape operations
    -> ordered/versioned within bounded projection history
    -> baseline/resnapshot recovery is allowed

application event
    -> intermediate fact matters (purchase, trade, quest, chat, etc.)
    -> reliable ordered/idempotent as declared
    -> overflow cannot silently become a baseline unless that application
       contract explicitly defines equivalence
```

An ECS `ComponentAdded` need not become a wire message if it only changes the
canonical replicated shape/state. Loss may delay, supersede, or trigger recovery
only according to the declared projection/application contract. An application
event cannot silently become latest state. Latest state cannot grow an
unbounded reliable backlog.

## Transport, security, and connection protocol

The first production candidate is conservative-datagram secure UDP, subject to
measurement. Required mechanisms include:

- protocol/capability handshake and stateless anti-amplification validation;
- authenticated key exchange, encryption, key/epoch rotation, replay defense;
- packet numbers and bounded acknowledgement ranges;
- RTT/RTO estimation, congestion control, pacing, and session fairness;
- bounded segmentation/reassembly below a conservative path MTU;
- persistent socket ownership and fixed packet/slab pools;
- keepalive, idle timeout, orderly drain, reconnect/resume policy;
- explicit protocol, codec, lane, pool, and session metrics.

Cryptographic primitives/libraries are selected during N0.3 from current
official platform documentation, test vectors, deployment support, security
review, and measurements—not frozen from memory in this plan.

Do not begin with platform-specific zero-copy, native socket wrappers,
`sendmmsg`/`recvmmsg`, QUIC, or a TCP fallback. Establish portable semantics and
ownership first.

## Ownership, queues, and barriers

Prefer producer-owned bounded append/ring storage and barrier merge over shared
general-purpose concurrent queues:

```text
transport-owner receive rings
        -> bounded owner ingress barrier
        -> decode/application staging

projection/assembly owner work
        -> bounded outbound handoff
        -> transport owner
```

Use MPSC only where ownership actually demands it. Every buffer and token has
one owner at a time, an explicit return path, generation protection, and
shutdown accounting.

## Replication, interest, and recipient knowledge

### Canonical projection

Game/Replication owns canonical semantic state, explicit game events, and
game-specific relevance. It is rebuilt from authority and remains independent
of packet, socket, and connection layout.

Projection capture is derived observation, not authority. Expected capacity,
history, and resync outcomes remain contained inside the Runtime game callback
as explicit results:

```text
expected capacity/history failure
    -> no cursor acknowledgement
    -> mark projection rebuild/resync required
    -> keep authority healthy

unexpected derived-projection failure
    -> fault/detach the projection subsystem as policy requires
    -> do not escape merely because derived state failed
```

An exception crosses `ServerGameFacet` only if authority itself may have been
mutated or compromised. N0.1 first proves contained result semantics; it does
not pre-emptively add a `ServerObservationFacet`.

### Latest state

Pending tables retain the newest authoritative version until eligible and
admitted. Recipient-known state advances only from the protocol/application
proof that a baseline/version is safe to reference. Superseded unsent state is
replaced rather than queued forever.

```text
change exists              -> pending
cadence                     -> eligible
byte/fairness budget        -> admitted
successful bounded handoff  -> queued/sent bookkeeping
acknowledged/applied proof  -> recipient-known state
```

### Projection lifecycle and application events

Raw ECS create/destroy/add/remove facts update canonical projection truth.
Derived spawn/enter/leave/despawn/bind/unbind operations have bounded ordered
retention and baseline recovery. Independent application events retain their
own declared facts and overflow policy. Value change, projection lifecycle, and
application events are not conflated. Tombstones protect against delayed
prior-epoch state.

### Interest

Interest decides who may receive captured state. It neither defines identity
nor mutates simulation. Canonical region/chunk identity does not contain a
recipient-relative ring:

```text
same region is Immediate for A, Near for B, Far for C, invisible to D
```

Initial policy may use stable regions/layers/anchors with incremental bounded
indexes. World coordinates remain authoritative. Floating origin and render
window are later client/world-streaming presentation policy.

### Fanout and overload

Cohorts may share captured semantic state and may share encoded fragments only
when every recipient-relative field and baseline assumption is identical.
Otherwise share meaning, not invalid wire bytes.

Overload policy is explicit: coalesce latest state, lower optional replication
frequency/fidelity, defer Bulk, or disconnect a persistently nonviable session.
It never silently drops ordered facts or changes authority.

## Client projection and baselines

The client owns a projection, never a second authority. Receive processing
validates and stages complete semantic operations. One client apply barrier
publishes coherent state to presentation/UI.

Initial join, AOI expansion, reconnect, lifecycle/history loss, and generation
replacement use an atomic baseline over an explicitly declared target:

```text
PREPARE private candidate
    -> RECEIVE / RESOLVE
    -> VALIDATE count, size, schema, hash, epoch, event floor
    -> COMMIT one complete projection
```

The live client projection does not mutate while fragments arrive. Deltas and
events outside the admitted epoch are rejected or buffered within fixed limits.
Baseline acknowledgement advances recipient-known state only after complete
client admission.

Target scope is intentional:

```text
initial world entry / StateEpoch replacement
    -> complete relevant projection candidate

AOI expansion / one state-family repair
    -> atomic candidate for that declared region/family/scope

unrelated live scopes
    -> continue normally
```

Walking into one region does not require replacing an enormous unrelated client
projection merely because baseline commit is atomic.

Prediction/reconciliation is introduced only for selected movement semantics
with a deterministic oracle. Observer interpolation uses presentation time and
does not redefine authoritative tick.

## Allocation and performance scope

The protected warmed zero-allocation path is:

```text
replication capture
interest routing and state scheduling
packet pool/rings, codec, framing, assembly, encryption
socket receive/send/completion
authenticated parse and protocol dispatch
world-state decode and normal client projection apply
input ingress
```

Cold or application paths remain bounded and are measured separately:

```text
connect, authentication, key exchange
join/recovery baseline construction
UI cold definition open and large model reconciliation
content/bulk transfer
persistence and operator diagnostics
```

Do not reopen the accepted retained UI implementation merely to claim that every
inventory-window operation allocates zero bytes.

## N0 checkpoint plan

```text
N0.0   contract, dependency shape, and evidence freeze
N0.1   canonical game projection/capture + OfflineDirect oracle
N0.2   Network kernel capacity, identity, pools, and generated codecs
N0.3   secure reliable protocol over ProtocolLoopback
N0.4   remote Admission + server-defined UI through ProtocolLoopback
N0.5   real UDP transport/session runtime using that application vertical
N0.6   authoritative input + owner/world replication vertical
N0.7   AOI, pending-state scheduler, and cohort fanout
N0.8   atomic baseline, recovery, reconnect, and generation replacement
N0.9   integrated offline/loopback/UDP/listen-host acceptance
N0.10  scale, sharding, measured specialization, and acceptance
```

Each checkpoint ends with executable semantics, bounds, failure behavior,
allocation evidence, and a decision before the next layer is admitted.

### N0.0 — Contract and evidence freeze — accepted

Implemented in `Kernels/Network`, its canonical README, the focused Network test
project, and the comparison evidence core. This checkpoint intentionally contains
no packet, security, reliability, transport, or game-replication implementation.

Deliver:

- freeze project/dependency boundaries and extend repository verification;
- inspect current `_V2` behavior through comparison adapters rather than prose;
- define deterministic normalized semantic trace and canonical hashes;
- define deterministic impairment/oracle behavior;
- freeze identity and lane tables;
- freeze clock/cadence and hot/cold allocation profiles;
- classify direct, provisional, directional, and unsupported V2 comparisons.

Tests/gate:

- Network kernel has no ECS/UI/Game dependency;
- semantic trace is deterministic;
- mismatched profiles refuse headline ratios;
- no production protocol work begins until ownership is explicit.

### N0.1 — Canonical game projection and capture — accepted

Implemented in `Game/Replication` and the network-enabled concrete game profile.
The accepted shape is one bounded, owner-thread canonical projection using exact
changed values plus ECS lifecycle evidence; independent recoverable lifecycle and
non-coalescible application-event retention; current Admission participant
bindings; bounded preselected authoritative ingress; and atomic OfflineDirect
baseline/frame application. The local game profile retains zero lifecycle and
replication reservation.

The focused gate proves cursor transactionality, lifecycle overflow/resync,
same-tick post-graph structural visibility, rapid topology folding, derived-fault
containment, generation replacement for durable and accountless sessions,
deterministic hashes/traces, and zero warmed managed allocation for the admitted
tick/capture/direct-apply loop. It also added an ECS regression for typed
structural payload merge across successive scheduler epochs.

Deliver:

- `Game/Replication` bounded projection service keyed semantically by
  `ParticipantId` and bound to `ServerGenerationId`/`StateEpoch`;
- changed-value cursors, lifecycle cursor, explicit application-event ingress,
  canonical state slots, lifecycle/event retention, and projection snapshots;
- network-enabled ECS lifecycle capacity profile;
- post-tick capture and OfflineDirect client semantic apply;
- bounded authoritative ingress for preselected external commands.

Use `RangeJob` for already-selected command arrays, generated chunk jobs for
composition-selected simulation work, and structural writers for topology.

Tests:

- independent change consumers do not interfere;
- every committed create/destroy/add/remove is captured;
- same-tick post-graph structural changes are not missed;
- cursor failure does not acknowledge;
- expected projection capacity/history outcomes remain contained results and do
  not terminally fault Runtime;
- unexpected derived-state failure faults/detaches projection without escaping
  the authority callback unless authority was compromised;
- lifecycle overflow requests resync;
- create/destroy and rapid add/remove between captures have defined canonical
  folding semantics;
- generation replacement rejects old runtime entities, rebuilds projection,
  retains valid durable Admission bindings, and removes invalid accountless
  bindings;
- OfflineDirect canonical hash is deterministic.

Gate: authority becomes complete client-semantic truth without Network existing.

### N0.2 — Network kernel foundations — accepted

Implemented as a dependency-clean runtime kernel plus compile-time codec generator:
fixed `NetworkKernelLimits`; isolated connection tables and kernel roots;
generation-protected packet ownership; producer-owned rings; canonical big-endian
reader/writer/framing; a collision-checked generated protocol manifest and exact
primitive codec; and a fixed deterministic datagram link consuming the N0.0
impairment oracle. N0.2 deliberately contains no security, ACK/retransmission,
game messages, UI, or sockets.

N0.1 hardening completed before this seam was frozen: same-generation repair uses
a baseline revision without changing `StateEpoch`, retains application events,
preserves or advances canonical entity generations correctly, and does not poison
the projection when one excess participant is excluded.

Deliver:

- fixed capacities, generational connection/security/sequence identities;
- packet/slab pools, ownership tokens, producer-owned rings;
- strict packet reader/writer and protocol manifest;
- generated exact measure/write/read codecs;
- deterministic in-memory datagram endpoint.

No ECS, UI, Admission, or Game dependency.

Tests:

- every capacity boundary and transactional exhaustion;
- pool/token state model, stale/double return, shutdown balance;
- sequence and epoch wrap/reuse;
- every codec truncation boundary, trailing bytes, malformed canonical forms,
  maximum counts, and generator ID collisions;
- multiple isolated Network runtimes;
- zero warmed allocation for admitted pool/ring/codec/framing profiles.

### N0.3 — Secure protocol over ProtocolLoopback — accepted

Implemented as a single-owner bounded topology. A stateless HMAC cookie precedes
connection allocation and ECDH work. Ephemeral P-256 ECDH is authenticated by a
pinned P-256 server signature; HKDF-SHA256 derives directional AES-256-GCM epoch
keys. Canonical authenticated outer framing, independent 64-packet authenticated-
replay and transport-delivery ACK windows,
reliable ordered retention, sequenced latest delivery, fragmentation/reassembly,
RTT/RTO, congestion backoff, pump budgets, and authenticated epoch advancement
run across deterministic datagrams.

The loopback cookie binds a fixed path token; endpoint binding and expiry are N0.5.
The deterministic protocol clock uses steps; N0.5 supplies real monotonic time.
N0.3 authenticates the server endpoint, not the game principal. Input redundancy
is N0.6 and full recipient fairness is N0.7.

N0.2's SPSC token ring and single-owner packet pool remain independent proofs.
Cross-thread packet-memory handoff is not claimed. N0.5 must admit a transactional
ownership/handoff model before transport workers share packet buffers.

Deliver cookie/anti-amplification, authenticated key exchange/encryption,
packet/replay windows, ACK ranges, RTT/RTO, reliable Control/Event/Bulk,
sequenced Input/State, admitted fragmentation, congestion/pacing foundation,
and deterministic impairment.

Tests cover every lane under loss, duplication, reorder, delay, burst loss,
wrap, MTU edges, associated-header/ciphertext/tag tamper, authenticated invalid
frames, replay during delivery backpressure, key change, and shutdown.

Gate: ProtocolLoopback exhibits production delivery/security semantics without
OS sockets.

### N0.4 — Remote Admission and UI over ProtocolLoopback

Deliver:

- `RemoteUiSemanticPort` and matching client bridge using existing UI semantics;
- provider-scoped trusted identity and Accounting compatibility;
- game authentication/application binding above the secure connection;
- remote CharacterSelect, Play, Inventory, action, delta, cache, detach, and
  resnapshot flows through the complete ProtocolLoopback stack;
- deterministic client shutdown: detach the remote semantic source, close or
  detach remote views, release renderer handles best-effort under a defined
  fault policy, and apply an explicit definition-cache retention policy;
- reconnect from one coherent client session state.

Required scenario:

1. authenticate a trusted principal;
2. receive CharacterSelect and submit Play;
3. enter through existing Admission;
4. open Inventory;
5. split Bread and receive exactly `Update + Add`;
6. equip the sword and receive exactly `Remove`;
7. close/reopen using exact definition-cache identity;
8. disconnect/dispose and prove deterministic view/renderer cleanup.

Tests include stale/duplicate actions, list/row provenance, lost UI messages,
reconnect, bounds, unauthenticated action, client disposal, and Bulk starvation
resistance.

Gate: an existing V3 game workflow runs remotely through the complete protocol
without moving its authority into Network.

### N0.5 — Real UDP transport/session runtime

Deliver persistent UDP receive/send ownership, bounded endpoint/pre-auth state,
path validation, timeout/drain, cleanup, and localhost production transport.
Run the exact N0.4 Admission/UI scenario through UDP; synthetic transport traffic
is not the acceptance workload.

Tests cover abrupt peer/server disappearance, spoofing, migration if admitted,
reconnect with delayed packets, pre-auth saturation, outstanding-send shutdown,
client cleanup, and exact session/pool balance after churn.

Gate: ProtocolLoopback and localhost UDP produce the same normalized
protocol/application trace for the real application vertical.

### N0.6 — Authoritative input and replicated world state

Deliver bounded decoded input ingress, sequence/redundancy/expiry, participant
to controlled-entity binding, network entity maps, owner/observer state, client
dense generational slots, lifecycle visibility, reliable cadence/clock anchor,
and minimal measured prediction/reconciliation where real movement requires it.

Tests include stale/duplicate/future input, lost stop, prior-epoch state, entity
reuse, clock Control/State reordering, 60→30→60 cadence change effective from
the declared tick/revision, exact final authority parity, and input ACK remaining
independent from owner correction.

Gate: one controlled entity remains semantically correct under loss.

### N0.7 — AOI, pending state, scheduler, and fanout

Deliver canonical region identity, incremental participant interest, reverse
scope edges, recipient-relative priority/cadence, pending newest versions,
tombstones, age/debt, byte budgets, fairness, cohorts, and explicit degradation.

Tests include brute-force interest oracle, cross-boundary movement, different
rings per participant, off-phase one-shot update, full output ring, oversized
Bulk, slow client, no subscribers, partial cohort failure, and tombstone versus
delayed old state.

Benchmarks include spread, dense, multi-hotspot, traversal, immediate-radius,
1/2/3 cadence, and cohort-size fanout controls.

Gate: exact recipient set, bounded pending state, and no lost latest version.

### N0.8 — Atomic baseline and recovery

Deliver baseline descriptors/staging, counts/limits/schema/hash, one active
candidate, post-cutoff state, event floor, cancellation/restart, resnapshot,
reconnect, and fresh projection epoch after generation publication.

Mandatory scenario:

```text
connect and enter Alice
replicate state
save authority
mutate authority
load saved generation
Admission rebinds Alice
connection remains established
old world traffic rejects
new baseline commits atomically
Alice continues
```

Gate: whole-authority replacement never requires Network identity to become
persistent identity.

### N0.9 — Integrated topology acceptance

Run one normalized scenario through OfflineDirect, ProtocolLoopback, and
localhost UDP: authentication, CharacterSelect, entry, Inventory, simulation,
owner/world state, second observer where useful, save/mutate/restore, generation
resync, continued input, reconnect, and shutdown.

For overlapping semantics, authority hash, game outcomes, client projection,
and UI outcomes match. Network-specific traces may differ.

Also prove listen-host topology: local participant through OfflineDirect and
remote participant through secure UDP, sharing one Runtime/game without
simulation migration.

Gate: N0 is functionally complete.

### N0.10 — Scale and measured specialization

Only after correctness admit connection/assembly shards, worker counts, slab
backend decisions, syscall batching, homogeneous packet batches, vectorization,
compression, hot/cold splitting, cache alignment, and snapshot thresholds.

Protected profiles:

```text
idle
owner_movement
spread_mix
dense_mix
multi_hotspot
traversal
join_storm
generation_resync
lossy_wan
slow_client
ui_burst
listen_host
```

Normal authoritative controls remain fixed 60 Hz, cadence changes/adaptation
off, playback 1x. Network degradation has separately labeled stress profiles.

## Work deliberately outside N0

N0 preserves seams but does not require:

- chat, commands, administration/inspection, server status, rich application
  event families, or external authentication providers;
- complete toroidal render window, floating origin, terrain/static streaming,
  asynchronous terrain cache, or large-world content transfer;
- RunUO-style console, Godot server terminal, or richer host capabilities;
- QUIC, TCP/TLS fallback, platform-specific datagram batching, or alternate
  transport research.

Those become N1 application networking, World Streaming, Hosting, or measured
transport work. Network V3 needs their bounded extension points, not their
implementations.

## Cross-cutting test matrix

- codec canonical round trips, version/schema evolution, fuzz and hostile bytes;
- handshake, authentication, replay, anti-amplification, and resource attacks;
- loss, duplication, reorder, jitter, corruption, bursts, MTU, bandwidth,
  reconnect, wrap, and delayed previous-epoch traffic;
- lane ordering/supersession/expiry/recovery and starvation boundaries;
- recipient-known versus merely sent state;
- lifecycle lag/resync, event floors, baseline atomicity, tombstones;
- interest enter/leave/crossing, layers, anchors, cohort fanout;
- authority ingress ordering and client apply atomicity;
- generation replacement, Admission rebind, persistence restore, reconnect;
- capacity exhaustion, ownership, use-after-return, shutdown, worker faults;
- warmed allocation gates for protected hot profiles;
- long deterministic chaos/soak with fixed memory and no silent semantic loss;
- Native/Pinned or backend parity only where the new kernel actually offers
  multiple storage backends.

## Benchmark and evidence policy

Measure semantic capture/projection independently from codec, packet assembly,
security, transport, and client apply. Every accepted result records canonical
state/trace, capacity, topology, cadence, allocation scope, mean and tail
latency, process order where relevant, and exact comparability classification.

Important profiles include codec bytes/throughput, capture, lifecycle, interest,
pending scheduling, cohorts, encryption, loopback/UDP latency, connection scale,
baseline storms, impairment recovery, and complete authoritative-to-client
latency.

Caller-thread allocation does not prove worker allocation. Cold UI/auth/join
allocations are reported separately from protected packet/state hot paths. Fixed
60 Hz with adaptation off remains the authoritative control; replication-rate
experiments are labeled independently.

## N0 definition of done

N0 is accepted only when:

- all three execution modes share one semantic/application contract;
- untrusted bytes cannot escape bounds or publish partial state;
- authority and client projection remain separate in every topology;
- capture occurs once after structural authority commits;
- host pump order admits 0..N complete steps and never captures mid-step;
- slow recipients retain no ECS cursor/history ownership;
- lifecycle capacity and every Network memory surface are planned explicitly;
- lanes preserve declared loss/order/supersession/recovery semantics;
- sent and known state remain distinct;
- canonical replication identity is connection-independent within one epoch;
- projection lifecycle, application events, and latest state remain distinct;
- baseline target scope, lifecycle, events, reconnect, and generation
  replacement are deterministic;
- Admission/UI and one real controlled simulation flow work remotely;
- persistence restores authority while Network reconstructs projection;
- localhost UDP and impairment tests pass Debug and Release;
- protected warmed paths meet accepted allocation/bounds profiles;
- fixed-60 performance cannot be hidden by cadence or replication degradation;
- documentation/XML describe accepted public APIs rather than intermediate
  checkpoint history.

## Deliberate implementation-time decisions

These require evidence rather than premature commitment:

- exact secure-UDP primitives/library;
- acknowledgement detail retained for baseline/delta safety;
- codec/generator project split;
- cohort encoded-fragment reuse granularity;
- coordinate quantization and transform encoding;
- compression thresholds and admitted content classes;
- socket backend and sharding boundaries;
- client prediction scope;
- whether a second real projection identity eventually justifies public
  `RecipientId`;
- when application, streaming, hosting, or alternate-transport work becomes a
  separate milestone.

They may change mechanisms. They cannot weaken the frozen principles at the top
of this plan.
