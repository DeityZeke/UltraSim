# Network V3: N0 remote-authority implementation plan

Status: active forward plan. N0.0 contract/evidence, N0.1 canonical game
projection/OfflineDirect, N0.2 bounded Network kernel foundations, N0.3 secure
reliable protocol over deterministic datagrams, N0.4 remote Admission/UI over
ProtocolLoopback, N0.5 real connected-UDP transport/session ownership, N0.6
bounded authoritative input/owner-state replication, and N0.7 maintained AOI,
pending state, application knowledge, and shared semantic fanout are accepted.
N0.8 atomic baseline/recovery, reconnect, and generation replacement and the
correctness-hardened N0.9 integrated topology acceptance are accepted. N0.10 scale and measured
specialization is next.

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
REGIONS INDEX SPACE; THEY DO NOT DEFINE RELEVANCE.
INTEREST IS MAINTAINED; IT IS NOT REBUILT FOR PACKET PRODUCTION.
DISTANCE AND GAMEPLAY DEFINE BASE DYNAMIC RELEVANCE.
CLIENT KNOWLEDGE IS NOT PRESENTATION.
CAMERA SELECTS PRESENTATION; IT DOES NOT DEFINE SERVER DYNAMIC TRUTH.
SHARE SEMANTIC WORK; PACKETIZE PER CONNECTION.
FRESH BASELINES ESTABLISH KNOWLEDGE; LATEST-STATE PREHISTORY IS NOT REQUIRED.
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

A cryptographically established channel does not imply account trust. Remote
game authentication over that channel is the second real trusted-identity source,
so the remote Admission phase
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
    retains delivery ordering evidence

reliable ACK elicitation
    only admitted reliable data requests a transport ACK
    drives sender fragment release

application/baseline acknowledgement
    higher semantic proof defined by the owning message contract
```

A delivery-blocked reliable packet is not transport-ACKed. Its exact ciphertext is
still replay-rejected, while the retained semantic fragment may be retransmitted under
a fresh packet number. A transport-delivery ACK never by itself means that gameplay
executed, UI rendered, a baseline committed, or recipient-known state advanced.
Admitted unreliable data remains authenticated and replay-protected but does not
elicit an ACK: it owns no sender flight record for that ACK to retire. Any future
unreliable path/RTT sampling must be explicit rather than an accidental ACK-everything
side effect.

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
| WorldState | unreliable unordered independent bundles; relation-versioned and baseline aware |
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
nor mutates simulation. Stable spatial regions answer where and accelerate
candidate discovery/reverse routing; maintained recipient/entity relations say
who currently cares and with what urgency. Regions never own canonical state,
dirty state, packet state, or visibility. Canonical region identity does not
contain a recipient-relative tier:

```text
same region is Immediate for A, Near for B, Far for C, invisible to D
```

The initial game-owned observation anchor is the controlled character and base
dynamic relevance is 360 degrees. `R0 Immediate`, `R1 Near`, and `R2 Far` are
recipient-relative meanings, not component schemas or protocol identities. R0
uses exact squared distance plus hysteresis; outer tiers may use coarse region
envelopes. Region edge length, tier radii, and target delivery intervals are
configurable evidence profiles rather than protocol constants. Interest cadence
controls when pending state becomes eligible; unchanged state produces no encode
or network work.

World coordinates remain authoritative. Camera direction does not demote base
server interest. Floating origin, render windows, positive presentation
activation, and terrain/static streaming are client/content concerns with their
own potentially much larger horizons.

### Fanout and overload

Canonical entity/family/version work is shared where profitable, while final
recipient bundles remain connection-relative. Explicit cohort managers, region
slabs, homogeneous packet batches, and shared scatter/gather submission are
measured N0.10 optimizations rather than N0.7 correctness machinery. Share
meaning, not invalid recipient-relative wire bytes.

Overload policy is explicit: coalesce latest state in the game-owned relation
graph, lower optional replication frequency/fidelity, defer Bulk, or disconnect
a persistently nonviable session.
It never silently drops ordered facts or changes authority.

## Client projection and baselines

The client owns a projection, never a second authority. Receive processing
validates and stages complete semantic operations. One client apply barrier
publishes coherent state to presentation/UI.

Initial join, generation replacement, reconnect, lifecycle/history loss, and
explicit family/scope repair use an atomic baseline over a declared target:

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

one state-family repair
    -> atomic candidate for that declared family/scope

unrelated live scopes
    -> continue normally
```

Ordinary AOI entry uses a complete self-contained current-state record and does
not replay history or replace an unrelated client scope. A scoped atomic baseline
is required only when a state family declares real atomic/reference dependencies
or projection repair demands it.

A fresh recipient has no prior latest-state knowledge. Its baseline establishes
current truth at a declared cutoff; post-cutoff latest state coalesces and only
non-coalescible application facts after the declared event floor retain history.
Pre-cut positions or projection-lifecycle events are not replayed merely to invent
interpolation history.

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
N0.6   authoritative input + owner-state vertical
N0.7   maintained AOI, pending state, and shared semantic fanout
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

### N0.3 — Secure protocol over ProtocolLoopback — accepted after adversarial hardening

Implemented as a single-owner bounded topology. A stateless HMAC cookie precedes
connection allocation and ECDH work. Ephemeral P-256 ECDH is authenticated by a
pinned P-256 server signature; HKDF-SHA256 derives directional AES-256-GCM epoch
keys. Canonical authenticated outer framing, independent 64-packet authenticated-
replay and transport-delivery ACK windows,
reliable ordered retention, sequenced latest delivery, fragmentation/reassembly,
RTT/RTO, congestion backoff, pump budgets, and authenticated epoch advancement
run across deterministic datagrams.

Acceptance hardening makes timeout discovery independent of congestion-window
admission, separates inbound and outbound budgets, advances unreliable stale floors
even when latest-state delivery is capacity-dropped, and isolates unreliable
pending/sequence state per message family. N0.6 proved one complete logical
Input stream and one complete OwnerState stream without needing a generic
sub-stream identity; that abstraction remains deferred until a second simultaneous
logical stream earns it.

The loopback cookie binds a fixed path token and its protocol clock uses
deterministic steps. N0.5 binds the cookie to the connected client IP/port with
current/previous expiry buckets and supplies monotonic milliseconds.
N0.3 authenticates the server endpoint, not the game principal. Input redundancy
is N0.6 and full recipient fairness is N0.7.

N0.2's SPSC token ring and single-owner packet pool remain independent proofs.
Cross-thread packet-memory handoff is not claimed or needed by N0.5: nonblocking
socket I/O and both secure peers share one explicit owner thread. A future socket
worker must still earn a transactional handoff before touching protocol buffers.

N0.3 has congestion-window/backoff behavior but no real time-based pacing. N0.5
adds monotonic-clock pacing when RTT/CWND produces a nonzero millisecond interval.
Bounded linear record/datagram scans
remain admitted proof-stage machinery until N0.7/N0.10 scale evidence earns a
different representation.

N0.3 is frozen after corrective hardening. Its final machine-readable observation
is additional immutable evidence rather than a replacement for the earlier
replay-hardened performance reference. Application-level N0.4 evidence decides
whether protocol CPU is material before any further N0.3 micro-optimization.

Deliver cookie/anti-amplification, authenticated key exchange/encryption,
packet/replay windows, ACK ranges, RTT/RTO, reliable Control/Event/Bulk,
sequenced Input/State, admitted fragmentation, congestion/backoff foundation,
and deterministic impairment.

Tests cover every lane under loss, duplication, reorder, delay, burst loss,
wrap, MTU edges, associated-header/ciphertext/tag tamper, authenticated invalid
frames, replay during delivery backpressure, key change, and shutdown.

Gate: ProtocolLoopback exhibits production delivery/security semantics without
OS sockets.

### N0.4 — Remote Admission and UI over ProtocolLoopback — accepted

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

Structurally valid wire input is not automatically valid application intent.
The owning retained UI model validates action identity, source provenance, row
existence, input contract, and current enabled state before any authoritative
callback begins. Expected validation failure is contained as rejection; an
exception after that boundary remains a trusted implementation/authority fault.

Gate: an existing V3 game workflow runs remotely through the complete protocol
without moving its authority into Network.

Accepted implementation keeps the dependency split explicit:

```text
Kernels/UI          bounded transport-independent semantic UI codec
Game/Protocol       game message manifest + server RemoteUiSemanticPort
Game/Replication    secure-peer authentication/application session binding
Client/Network      remote Client/Core bridge + deterministic detach
```

Provider credentials are authenticated above the secure transport into a
provider-scoped `TrustedIdentity`; `ParticipantId` and application-session epoch
are server-created. The accepted oracle compares OfflineDirect and
ProtocolLoopback from the same fixture/actions and proves equal authority,
normalized UI semantics, and eventual client state through CharacterSelect,
Play, Inventory Split/Equip, close, and exact hot reopen. Impairment,
unauthenticated/stale/duplicate actions, post-commit presentation failure,
disconnect, and reconnect are executable gates.

The parity oracle captures the complete retained client-semantic state at each
workflow milestone: view/cache/definition identity, model sequence, every
scalar/list value, row identity, and action enabled/reason state. Transport
packet identities and timing remain intentionally outside that oracle.

The focused benchmark is intentionally application-shaped. Cold complete
workflow setup and established Inventory selection round trips are reported
separately; allocation includes UI application work and is not mislabeled as a
Network hot-loop zero-allocation claim. The immutable N0.3 protocol microbaseline
remains the protocol-mechanism reference. N0.4 does not justify reopening that
machine before N0.5/N0.6 provide real transport and high-rate state workloads.

### N0.5 — Real UDP transport/session runtime — accepted

Deliver persistent UDP receive/send ownership, bounded endpoint/pre-auth state,
path validation, timeout/drain, cleanup, and localhost production transport.
Run the exact N0.4 Admission/UI scenario through UDP; synthetic transport traffic
is not the acceptance workload.

Tests cover abrupt peer/server disappearance, spoofing, migration if admitted,
reconnect with delayed packets, pre-auth saturation, outstanding-send shutdown,
client cleanup, and exact session/pool balance after churn.

Gate: ProtocolLoopback and localhost UDP produce the same normalized
protocol/application trace for the real application vertical.

The accepted implementation uses connected nonblocking IPv4 UDP sockets on the
same owner thread as `NetworkProtocolPeer`. This deliberately removes any hidden
cross-thread packet-buffer handoff. Socket buffers, datagram size, handshake
pumps, cookie bucket duration, idle timeout, and drain timeout are explicit limits.
Empty receive polling avoids normal-path `WouldBlock` exceptions and preserves
the warmed 0-B secure-delivery profile.

The secure handshake crosses the real sockets. Its stateless cookie binds the
client path and a current/previous monotonic expiry bucket before connection
allocation and ECDH. Because both buckets are accepted, maximum cookie age can
approach twice `CookieBucketMilliseconds`. Connected socket filtering rejects unrelated paths. Fresh
reconnect owns fresh socket endpoints and independently derived keys. Prior UI
and application traffic is additionally fenced by the application-session epoch;
before authentication, detach messages are ignored rather than selecting an
arbitrary future epoch.

Graceful shutdown first closes new peer/application-message admission, then drains
existing reliable retention or reaches its bounded deadline before socket close;
closed sessions remain unable to enqueue unsendable work. Authenticated keepalives
preserve idle sessions without creating application messages; lack of authenticated
receive activity reaches the idle/drain policy. An injected monotonic clock gives
deterministic permanent coverage of keepalive, idle expiry, nonzero pacing, and
cookie-bucket expiry without wall-clock sleeps. Permanent gates cover real bidirectional delivery, pinned
identity failure, spoofed paths, graceful/abrupt cleanup, reconnect isolation,
pre-auth failure churn, outstanding sends, the exact N0.4 workflow, delayed
detach fencing, and warmed 0-B secure UDP delivery.

Accepted paired application evidence:

| Scenario | ProtocolLoopback | localhost UDP | UDP / loopback |
| --- | ---: | ---: | ---: |
| Complete cold workflow mean | 2.178 ms | 2.368 ms | 1.087x |
| Established Select mean | 240.457 us | 241.120 us | 1.003x |

These same-process localhost values are not Internet-latency claims. With tiered
compilation and ReadyToRun disabled, the established application path is at parity;
the cold path includes real socket creation/handshake and was 8.7% slower in the
accepted observation. The important result is semantic parity and bounded ownership. Application allocation
includes UI/game work, while the kernel gate separately proves warmed UDP delivery
at 0 B. Immutable evidence:
[`network-n0.5-real-udp-application-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.5-real-udp-application-2026-08-14.json).

### N0.6 — Authoritative input and replicated owner state — accepted

Deliver bounded decoded input ingress, sequence/redundancy/expiry, participant
to controlled-entity binding, one complete owner-state stream, client-dense
generational slots, owner lifecycle visibility, reliable cadence/clock anchor,
and minimal measured prediction/reconciliation. Observer/world state remains
part of N0.7's earned multi-entity recipient scheduler.

Tests include stale/duplicate/future input, lost stop, prior-epoch state, entity
reuse, clock Control/State reordering, 60→30→60 cadence change effective from
the declared tick/revision, exact final authority parity, and input ACK remaining
independent from owner correction.

Gate: one controlled entity remains semantically correct under loss.

The accepted implementation intentionally proves the narrow first vertical:

```text
bounded three-sample client input redundancy
        ↓
application/session + StateEpoch + sequence + tick/time validation
        ↓
existing game-owned authoritative ingress
        ↓
one complete ServerRuntime scheduler step
        ↓
canonical projection capture
        ↓
reliable revisioned clock anchor + latest complete owner state
        ↓
private dense generational ClientEntitySlot + minimal reconciliation
```

Input processing acknowledgement is not correction: `LastProcessedInputSequence`
advances for every semantically handled sample, including expiry/capacity rejection,
while owner state independently carries authoritative values. The accepted
latest-control input is complete `SetInputVelocity` state. The permanent contract
is that Input remains semantically correct when all older unprocessed samples are
skipped and only the newest survives. Non-coalescible application facts must use
reliable Event semantics rather than assuming unreliable redundancy guarantees
every occurrence.

Owner state is a complete superseding sample for one participant and one controlled
entity. State referencing a future clock revision is staged until its reliable
anchor arrives. The anchor declares its effective simulation tick and exact
fixed-point authoritative time; the permanent test proves 60→30→60 without
changing step-relative scheduler cadence. A first-seen or repeated despawn
tombstone is complete state: it advances frame/input acknowledgement, establishes
the canonical generation floor, and prevents same-generation resurrection.
N0.6 uses one true client-local slot at index zero; canonical replication-slot
reuse advances its independent private generation.

Permanent tests cover stale/duplicate/expired/future input, safe omission of older
coalesced input, loss/duplication/reorder, lost-stop recovery, prior-epoch state,
Control/OwnerState reordering, first-seen/repeated tombstones, entity reuse,
despawn, exact ProtocolLoopback/UDP parity, independent processing acknowledgement,
and warmed 0-B caller allocation. N0.7, not transport coalescing, owns multiple
entities, observer/world state, AOI, per-recipient pending versions, and fanout.

The persisted N0.6 `InputVelocity` is explicitly diagnostic physical state.
Expiry bounds late admission; it is not a control lease, and disconnect does not
neutralize the value. Real player-control desire must declare and test its own
disconnect/lease policy before N0.8 rather than inheriting transport lifetime by
accident.

Accepted paired evidence (21 alternating samples, 128 complete cycles/sample,
tiered compilation and ReadyToRun disabled) records approximately 139.13 us per
ProtocolLoopback cycle and 139.12 us per localhost-UDP cycle, ratio 1.0000x, with
0 B caller allocation. This same-process ratio means UDP added no material cost
to this admitted workload; it is not a WAN or universal syscall-cost claim.
Immutable artifact:
[`network-n0.6-controlled-input-owner-state-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.6-controlled-input-owner-state-2026-08-14.json).
The post-audit semantic-hardening observation is additional immutable evidence,
not a replacement for the original accepted run:
[`network-n0.6-controlled-input-owner-state-semantic-hardened-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.6-controlled-input-owner-state-semantic-hardened-2026-08-14.json).

### N0.7 — Maintained AOI, pending state, and shared fanout

Deliver a game-owned bounded spatial broadphase and maintained recipient/entity
relevance graph over canonical projection state. Stable regions accelerate
candidate discovery and reverse routing but do not define identity, presentation,
relevance semantics, or replicated-state ownership.

Each admitted recipient has one game-owned observation anchor, initially the
controlled character. Base dynamic relevance is 360 degrees and classified into
recipient-relative `R0 Immediate`, `R1 Near`, and `R2 Far`. R0 uses exact close-
range squared distance and enter/leave hysteresis; outer tiers may use coarse
region envelopes. Exact meter thresholds, region edge length, and target delivery
intervals are configurable evidence profiles rather than protocol constants.

Interest tier controls priority and newest-state eligibility only. It does not
create separate identity, require periodic transmission of unchanged state, or
implicitly choose a payload schema. Game-owned replication-family policy decides
which dynamic entities are meaningful at each horizon. Camera direction does not
demote base server interest.

Region/entity and region/recipient membership changes incrementally establish,
remove, or retier compact `RecipientEntityRelation` records. Each
recipient/canonical-slot relation retains the effective lifetime and tier,
recipient-known version, newest pending canonical version, application-knowledge
sequence, and bounded scheduling state. Canonical changes fan directly through
maintained reverse relations; packet production never rescans ECS or rebuilds a
recipient world.

The first WorldState representation uses complete self-contained entity/family
samples. Shared semantic construction is cached/reused per demanded canonical
version; interest cadence remains independent from representation. Each final
WorldState bundle is an independent bounded unreliable container, not one
message-family latest stream: multiple disjoint relation subsets may be in flight
and acknowledged out of order. Per-relation sequence/version state supplies
staleness and recovery, while Network neither reliably retains nor coalesces these
bundles. Final bundle assembly, packet numbering, encryption, and socket submission
remain connection-relative and happen last. Explicit cohort managers, region slabs,
aliases, delta compression, and syscall batching require N0.10 measurements.

World-state application acknowledgement remains separate from transport ACK.
Every newest version stays pending until application knowledge proves it safe or
a baseline replaces the scope. Newer changes and newer canonical lifetimes
replace older unsatisfied state;
application ACK of an older bundle cannot clear a newer pending version. Interest
entry sends current complete state. Interest exit and canonical destruction
publish recoverable absence/tombstone state whose sequence defeats delayed old
present records. Normal AOI expansion does not replay history.

Client/Core retains dynamic state independently from presentation. A local camera
positively activates or warms presentation from retained state with an appropriate
overscan margin. Terrain/static visual streaming is outside Network dynamic AOI;
a future distant-observation mechanic must earn an explicit game-owned interest
source rather than coupling camera frusta into the base server graph.

The accepted baseline is single-owner, flat, bounded, and allocation-free. Its
cost should track moved spatial membership, changed real relations, actual pending
state, unique demanded semantic versions, and delivered bytes—not clients times
world entities.

Permanent tests include a brute-force relation/tier oracle; same-region and cross-
region R0; hysteresis; local-neighborhood recipient movement; zero work for
unchanged/no-subscriber state; newest-version collapse; R0 promotion; phased
outer-tier eligibility; family policy; shared semantic construction with
independent acknowledgements; pipelined unordered WorldState bundles; bounded
lower-tier progress under continuous R0 load; slow/full recipient retention; older
ACK versus newer pending state; canonical-slot lifetime collapse; tombstone versus
delayed present; client knowledge independent from camera presentation; and
explicit capacity degradation/nonviability.

The accepted artifact matrix sweeps region edge, density, dirty percentage, and
recipient fanout. The hardened reference additionally measures every recipient
moving within its current cell. Radius and state-family parameters remain
configurable for future targeted experiments but are not claimed as preserved
sensitivity evidence yet. Record time alongside
edges touched, live relations, pending writes, unique semantic constructions,
semantic copies/bytes, bundle bytes, and reserved bytes per bounded domain.

Gate: exact maintained recipient set, bounded pending state, shared semantic work,
and no lost latest version.

Accepted implementation notes:

- `GameInterestService` owns fixed-capacity entity/region/recipient indexes,
  reverse region-interest edges, compact recipient/entity relations, exact R0
  hysteresis, coarse R1/R2 envelopes, per-tier pending queues, bounded weighted
  tier progress, fair recipient selection, byte budgets, tombstones, and
  application-ACK knowledge;
- canonical updates are pushed into the maintained graph after capture. Packet
  production reads cached canonical semantics and pending relations; it neither
  scans ECS nor reconstructs recipient worlds;
- one semantic construction is cached per demanded entity/version and copied
  into independent recipient records. Explicit cohort objects and shared encoded
  packet bytes remain unearned N0.10 optimizations;
- WorldState uses the kernel's bounded independent-unreliable retention class.
  Multiple disjoint application bundles may be queued/in flight without waiting
  an application RTT. Lost bundles are not transport-retained; their relations
  remain newest-version pending and become eligible for a later fresh bundle;
- one recipient/canonical slot owns at most one unsatisfied lifetime. A newer
  replication generation supersedes an older present/tombstone debt, preventing
  duplicate-index wire bundles and slot-reuse liveness loops;
- same-cell recipient movement scans the small exact-R0 candidate neighborhood
  plus the maintained current-R0 list. It does not rescan unrelated R1/R2
  relations; region crossings retain differential edge-strip maintenance;
- per-recipient outstanding quotas keep a slow recipient from consuming the
  complete application-ACK ledger. Capacity exhaustion is explicit nonviability,
  never silent state loss or an endless impossible-scope retry loop;
- `RemoteWorldStateClient` maps canonical IDs to private dense generational
  client slots, retains tombstone knowledge independently from active storage,
  preflights complete bundles, frees before allocating during atomic commit, and
  treats camera selection as a local positive presentation query only;
- the current dynamic family is a complete position plus `GameSimulationState`
  sample. Broader game families and spatial-source integration extend this game
  layer; they do not alter Network identity or delivery contracts;
- managed dictionary indexes are pre-sized and count-bounded. Reported reserved
  bytes include owned arrays plus deterministic dictionary-capacity estimates,
  not CLR object headers or process working set.

Permanent evidence now includes 29 game-replication tests and 32 remote
application tests. The N0.7 additions prove the brute-force tier oracle,
cross-region exact R0, hysteresis, differential region-edge maintenance,
newest-version collapse and promotion, horizon policy, shared semantic reuse,
old-ACK/new-pending safety, canonical-slot generation collapse, R1/R2 progress
under R0 saturation, local-only same-cell movement, slow-recipient isolation,
explicit capacity failure, zero warmed allocation, strict transactional wire
decoding, independent-unreliable kernel queueing, pipelined secure-loopback
application ACK, first-seen/repeated tombstones, camera-independent knowledge,
and record-order-independent atomic client capacity replacement.

The original accepted 5,000-entity/16-recipient/10%-dirty reference observation records
56,592 live relations and 10,000 region-interest edges. Mean canonical update is
approximately 881.42 us, pending delivery plus immediate application ACK is
355.16 us, one recipient cross-and-return operation is 366.25 us, and idle
scheduling is 0.208 us. All timed phases allocate 0 B; known reserved payload is
15,677,408 B. These are single-owner semantic scheduler measurements, not socket
or WAN claims.

The post-hardening v2 observation preserves the same 56,592 relations and 10,000
region-interest edges while adding all-recipient same-cell movement. Mean canonical
update is approximately 734.33 us, pending delivery plus immediate application ACK
is 330.08 us, all 16 recipients moving out-and-back within their current cells is
194.60 us total, one recipient region cross-and-return is 85.04 us, and idle
scheduling is 0.444 us. All phases remain 0 B allocated. The additional immediate
list and independent-lifetime bookkeeping raise known reserved payload to
15,955,444 B.

Sensitivity evidence deliberately varies dirty state (1%, 10%, 100%), region
edge (8 m, 16 m, 32 m), density (1 m, 4 m, 16 m entity spacing), and fanout
(4, 16, 64 recipients). For example, 1% versus 100% dirty produces about
97.79 us versus 6,270.03 us of canonical update work; 4 versus 64 recipients
produces 12,480 versus 273,440 maintained relations and about 203.85 us versus
3,792.71 us of 10%-dirty update work. The artifacts preserve the full timing,
semantic-copy, bundle-byte, relation/edge, allocation, and reservation facts:

- [`network-n0.7-maintained-interest-reference-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-reference-2026-08-14.json)
- [`network-n0.7-maintained-interest-dirty1-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-dirty1-2026-08-14.json)
- [`network-n0.7-maintained-interest-dirty100-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-dirty100-2026-08-14.json)
- [`network-n0.7-maintained-interest-edge8-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-edge8-2026-08-14.json)
- [`network-n0.7-maintained-interest-edge32-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-edge32-2026-08-14.json)
- [`network-n0.7-maintained-interest-dense-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-dense-2026-08-14.json)
- [`network-n0.7-maintained-interest-spread-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-spread-2026-08-14.json)
- [`network-n0.7-maintained-interest-fanout4-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-fanout4-2026-08-14.json)
- [`network-n0.7-maintained-interest-fanout64-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.7-maintained-interest-fanout64-2026-08-14.json)
- [`network-n0.7-hardening-reference-2026-08-15.json`](../../_Benchmarks/Baselines/network-n0.7-hardening-reference-2026-08-15.json)

### N0.8 — Atomic baseline and recovery — accepted and hardened

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

Implemented as a game-owned WorldState transaction rather than a generic
Network snapshot framework:

```text
server cutoff
    -> one bounded sorted recipient scope
    -> reliable Control Begin
    -> client capacity/schema preparation ACK
    -> independently reliable Bulk segments
    -> reliable Control Commit
    -> private client validation + one swap
    -> application baseline ACK
    -> recipient knowledge advances to cutoff
    -> post-cutoff relation debt resumes
```

Begin declares application/session epoch, `StateEpoch`, baseline identity and
revision, event floor, schema fingerprint, canonical content hash, record count,
relation-sequence floor, segment count, and segment size. The preparation round
trip is intentional: Bulk
cannot race ahead of the client's admitted descriptor across independent lanes.
Exactly one candidate exists. A newer epoch/revision supersedes an older
candidate. Explicit abandonment or timeout queues a reliable baseline-cancel
transition before ordinary state resumes; delayed old segments and commits
cannot strand or affect the client candidate.

The client preallocates separate candidate knowledge, dense entity slots, slot
generations, records, and receipt bits. Live state does not change during Begin,
segment arrival, duplicate delivery, or Commit arrival. Only a complete exact
count/schema/hash/epoch candidate swaps live. Capacity or semantic failure emits
an application rejection and preserves the prior projection exactly. Baseline
ACK is distinct from transport delivery ACK. Begin rejects a record count that
cannot fit either known-identity storage or active dense slots before any Bulk
segment is admitted. Slot generations survive baseline compaction: regrowing a
historically used local index always advances its private generation and a
retired generation is never made virgin again.

The server suppresses ordinary WorldState scheduling while a baseline is active.
Canonical changes after capture continue to replace relation debt. On successful
baseline ACK, only matching cutoff sequences advance recipient-known state; a
newer pending version cannot be cleared accidentally. Rejected capacity marks the
recipient explicitly nonviable instead of retrying an impossible scope forever.
For same-epoch repair, the committed relation floor also rejects delayed
pre-cutoff rows for identities omitted by the new complete scope; omission cannot
be followed by resurrection from an old unreliable bundle.

The transaction also has a bounded monotonic application lifetime. Progress
through Begin, Prepared, Bulk, and Commit refreshes its deadline; failure to
receive a terminal application result abandons the retained cutoff without
advancing recipient knowledge and reliably cancels the client's private
candidate. A newer attempt may start after that cancellation is admitted, while
repeated stalls reach an explicit recipient-nonviable policy instead of blocking
WorldState forever. Manual abandonment has the same no-knowledge-advance rule.
Successful complete-scope acknowledgement consumes both explicitly present rows
and omitted absence debt at or below the captured relation floor; post-cutoff
debt remains untouched.

`EventFloor` is carried and committed transactionally in N0.8, but N0.8 does not
by itself prove an application Event delivery stream. End-to-end post-floor
event retention, idempotence, and fresh-client omission are N0.9 acceptance
requirements. A newer same-epoch baseline may never lower either the relation or
event floor, and replacement projection epochs must increase strictly.

Fresh `StateEpoch` publication rebinds the connection-owned adapter without
replacing the healthy connection, application epoch, participant, or any durable
identity. Persistence continues to restore only authority. The permanent gate
saves Alice, diverges authority, publishes the restored Runtime generation,
proves Admission durable rebind, rebuilds projection, atomically commits the new
epoch over the existing secure session, rejects old-epoch traffic, and continues
simulation afterward. Reconnect creates a fresh application session and baseline
rather than reviving old view/projection instances.

Permanent N0.8 evidence covers private staging, post-cutoff updates, replacement
epochs, actual durable generation publication, loss/reorder/duplication,
capacity rejection without partial publication, candidate cancellation/restart,
delayed prior transactions, server application timeout/abandonment, monotonic
epoch/floor rejection, omission-debt consumption, and fresh reconnect. The focused 256-entity baseline
observation uses eight 32-record segments and 21 alternating paired samples:

- ProtocolLoopback mean: approximately 1.449 ms;
- connected localhost UDP mean: approximately 1.498 ms;
- UDP/loopback ratio: approximately 1.034x for this same-process recovery workload;
- warmed caller allocation: 0 B;
- known adapter reserved payload: 44,032 B server and 94,000 B client.

These are cold/recovery latency facts, not a hot per-tick replication target.
The immutable artifact is
[`network-n0.8-atomic-world-baseline-2026-08-15.json`](../../_Benchmarks/Baselines/network-n0.8-atomic-world-baseline-2026-08-15.json).

### N0.9 — Integrated topology acceptance — accepted

Run one normalized scenario through OfflineDirect, ProtocolLoopback, and
localhost UDP: authentication, CharacterSelect, entry, Inventory, simulation,
owner/world state, second observer where useful, save/mutate/restore, generation
resync, continued input, reconnect, and shutdown.

For overlapping semantics, authority hash, game outcomes, client projection,
and UI outcomes match. Network-specific traces may differ.

Also prove listen-host topology: local participant through OfflineDirect and
remote participant through secure UDP, sharing one Runtime/game without
simulation migration.

N0.9 also closes the first application Event continuity proof. A real retained
non-coalescible game event is delivered after the baseline-declared event floor,
is idempotent under duplicate/reordered delivery, survives resnapshot or epoch
replacement as policy requires, and is not replayed from pre-floor history to a
fresh client.

Gate: N0 is functionally complete.

The accepted implementation composes the previously independent controlled,
WorldState, and Event adapters through one fixed game-owned handler set. This is
not a general message bus: Network still identifies messages, the game orders a
bounded number of mutually exclusive owners, and the first owning adapter
consumes each message. No game policy moved into Network.

Protocol version 8 retains the version-7 non-coalescible Event messages and adds
reliable baseline cancellation. Fresh baseline floors omit prior history;
post-floor facts remain in `GameProjectionService` until every registered
participant consumer has advanced beyond them, survive
same-epoch resnapshot, converge under deterministic loss/reorder, and apply
idempotently. Event audience is participant-scoped, client retention is bounded
working storage with explicit consumption, transient Control backpressure keeps
one retryable application ACK, and missing application ACKs reach a bounded
retry/nonviable policy. Generation replacement resets generation-local facts
and requires an explicit new floor. Transport acknowledgement remains weaker
than Event application acknowledgement.

Application Event sequences remain projection-global. A connection receives a
trusted monotonically increasing participant-filtered subsequence, so gaps mean
facts for other participants rather than packet loss. Capacity rejection names
the received global sequence. Duplicate delivery and ordinary owner-pump retry
cannot leave a stale retained ACK, and baseline cancellation/supersession retires
the matching pending baseline acknowledgement before a successor begins.

The normalized three-topology gate keeps Admission/CharacterSelect, Inventory
Split/Equip/hot reopen, canonical projection, initial and same-epoch baselines,
Event, complete authoritative input, owner state, maintained WorldState, durable
save/diverge/restore, fresh `StateEpoch` recovery, continued input, and shutdown
alive in one scenario. OfflineDirect, secure ProtocolLoopback, and connected
localhost UDP produce the same authority, complete UI/client state, simulation
state, normalized WorldState identities/versions/horizons/positions/payloads,
and participant/epoch-qualified Event. Dedicated permanent gates in
the same suite cover fresh application/WorldState reconnect and delayed prior
traffic rather than weakening the normalized oracle with transport-specific
trace identity.

The listen-host gate binds a local direct participant and a remote secure-UDP
participant into one `ServerRuntime`, proves both participant bindings own the
same `ServerGenerationId`, and projects the same shared authority without moving
simulation ownership.

The accepted protocol-v8 corrective-hardening observation uses seven
rotated-order cold samples:

- OfflineDirect mean: approximately 0.650 ms;
- secure ProtocolLoopback mean: approximately 5.209 ms;
- connected localhost UDP mean: approximately 6.314 ms;
- UDP/ProtocolLoopback ratio: approximately 1.212x;
- median cold caller allocation: approximately 1.89 MB direct, 18.39 MB
  ProtocolLoopback, and 16.15 MB UDP.

These figures include fixture/handshake, UI, persistence I/O, projection
recovery, and shutdown. They are cold integration evidence, not a hot tick or
socket-cost claim. The accepted hardened artifact is
[`network-n0.9-corrective-hardening-2026-08-15.json`](../../_Benchmarks/Baselines/network-n0.9-corrective-hardening-2026-08-15.json).
The original protocol-v7
[`network-n0.9-integrated-topology-acceptance-2026-08-15.json`](../../_Benchmarks/Baselines/network-n0.9-integrated-topology-acceptance-2026-08-15.json)
remains immutable historical evidence rather than being normalized or replaced.

### N0.10 — Scale and measured specialization

Only after correctness admit connection/assembly shards, worker counts, slab
backend decisions, syscall batching, homogeneous packet batches, vectorization,
compression, hot/cold splitting, cache alignment, and snapshot thresholds.

N0.10 begins with a cross-generation measurement instrument, not a production
specialization. `UltraSim.Network.Comparison` runs frozen V2 and current V3 in
isolated child processes in both orders over the same deterministic
`owner_movement` bot script and the 32/64/128/256 stage catalog. The first V3
topology still owns one connected client/server UDP pair per bot, while V2 owns
one shared server socket; results are therefore explicitly **provisional
topology evidence**. Exact normalized owner identity, acknowledged input
sequence, and velocity are required before any relative row is admitted.
Arrival-relative position remains a diagnostic rather than a false equality
claim.

The comparison-v2 instrument reports tick/server/client time, ping and V3
input-to-owner acknowledgement age in both milliseconds and simulation ticks,
per-client freshness/fairness, deadline misses/streak/headroom, allocation/GC,
process memory and combined-process CPU, configured socket buffers, protocol
reservation, per-surface queue/in-flight occupancy distributions and pressure,
exact retransmitted bytes, actual V3 UDP bytes/syscalls/MTU use, handshake cost,
secure packet/byte counters, marginal p95 cost per added client, and a normalized
logical-message-byte count. The latter measures semantic messages admitted by
the sender; applied counts remain separate so coalescing stays visible.
Unavailable metric families are explicitly flagged rather than inferred from a
zero. The V2 adapter also preserves every top-level numeric frozen-harness stage
diagnostic under `v2.*`. V3 reports the current default per-connection protocol
reservation estimate beside the deliberately small owner-workload benchmark
profile. That evidence—not the desire for a flattering scale graph—decides the
first production capacity specialization.

Instrumentation remains layered. Cheap counts, bytes, occupancy, freshness,
deadline, and amplification counters belong in normal evidence. Overall phase,
semantic latency, and handshake timing are normal benchmark measurements. Deep
crypto/codec/assembly, per-lane CPU, GC-pause, and future worker/shard timing are
opt-in investigations so measurement does not become the workload.

`spread_mix` and `dense_mix` are catalog names only until both adapters perform
equivalent observer WorldState/Event semantics and their canonical oracles are
implemented. The comparison executable rejects them today rather than silently
running owner-only work under a broader label. Event-ledger examined/skipped,
minimum-cursor scan entries, and retained high water are now always-available
N0.10 counters for deciding whether participant-local Event indexing is earned.
Those profiles must add WorldState age by Immediate/Near/Far tier, application
Event publish-to-apply latency, worst-recipient freshness, and canonical-change
to semantic-construction/recipient-copy/bundle/datagram amplification. Likewise,
`traversal` must expose edge/relation/retier work, `generation_resync` must expose
baseline and all-clients-current latency, `slow_client` must expose debt and
nonviability escalation, and `join_storm` must expose join throughput and
per-established-session cost. Owner-only evidence must not report synthetic
zeros for those semantics.

The first accepted provisional owner matrix matched canonical state in all eight
scale/order runs. V2 tick p95 ranged from approximately 0.26 ms at 32 bots to
1.86 ms at 256; V3 ranged from approximately 0.72–0.80 ms to 5.47–5.61 ms. V3 remained
inside the 16.667 ms tick budget and reached roughly 20.7 ms input-to-owner ACK
p95 at 256, but cost stayed about 2.77–3.12x V2 under this topology. That is not a
kernel or wire-format verdict: it is evidence that per-bot server sockets, peers,
and pump traversal are now the dominant structural difference worth testing.
This admitted a narrow bounded shared-server-listener experiment with the same
protocol/application semantics before sharding or micro-optimizing
crypto/codec instructions.

The occupancy evidence rules out simple capacity exhaustion: the admitted V3
profile never crossed 50% queue occupancy, reached only one in-flight packet and
two received messages, and recorded zero capacity drops. A different cliff does
appear at 256: localhost retransmissions rise from 2, 3–5, and 6–11 at
32/64/128 to 426–430
at 256 in the two orders. Together with the ACK-latency increase, this identified
serial per-session pump traversal and step-based timeout observation—not queue
size—as the first behavior the shared listener/pump experiment had to explain.

Both generations admitted essentially the same normalized logical message
bytes at each stage (about 1.026 MB at 256). V3 put about 7.51 wire bytes on the
localhost UDP path per normalized logical byte, including framing, AEAD,
transport/application acknowledgements, handshake traffic, and retransmission.
Frozen V2 does not expose physical wire bytes, so this is a V3 amplification
diagnostic rather than a cross-generation wire ratio.

At 256 bots the admitted owner-workload profile reserved about 5.04 MiB of V3
protocol payload and configured 16 MiB of socket buffers. Instantiating the
current general default protocol profile would instead estimate about 3.16 GiB
of protocol payload for the same connected-pair topology. This earns explicit
profile specialization, but the benchmark-only profile does not silently change
production defaults. Frozen V2 requested about 2.01 GiB of socket buffers at
that scale while observed process working set stayed near 130 MiB, demonstrating
why configured requests, owned payload, and physical memory remain separate
metrics.

N0.10.1 now implements that bounded shared listener without changing protocol,
cryptography, peer capacities, application adapters, cadence, or the canonical
oracle. One server socket owns a fixed path table and fixed per-session datagram
rings; the existing per-connection protocol peers remain independent. The
listener requests the same aggregate socket-buffer capacity formerly distributed
across the server sockets it replaces. A permanent 64-session burst gate was
added after an initial one-buffer implementation exposed unreported OS receive
drops and failed to converge.

The accepted both-order matrix is preserved as
[`network-v2-v3-bot-n0101-shared-listener-2026-08-15.json`](../../_Benchmarks/Baselines/network-v2-v3-bot-n0101-shared-listener-2026-08-15.json).
It again matches canonical state in all eight runs. Shared-listener V3 tick p95
ranges from 0.691–0.705 ms at 32 bots to 5.112–5.229 ms at 256. Against the
connected-pairs entry, the fitted V3 slope improves only from about 21.2 to 19.8
microseconds per bot; the approximate 4 ms marker moves from 184 to 194 bots.
At 256, retransmissions fall from 426–430 to 210–331 and wire/logical
amplification falls slightly from ~7.51x to 7.47–7.48x. The listener therefore
earns its simpler production ownership shape, but falsifies the stronger idea
that server socket count explains most of V3's roughly 3x CPU cost.

The fixed demux rings raise admitted managed payload at 256 from about 5.04 MiB
to 6.51 MiB while aggregate configured socket bytes remain 16 MiB. N0.10's next
representation work should specialize per-connection reservation without
reintroducing warmed allocation. Any further CPU specialization must first
profile the remaining per-peer protocol/application path. Sharding and workers
remain unearned: one bounded owner thread has not yet been shown to be the
limiting representation after those simpler steps.

N0.10.2 separates memory viability from CPU attribution. Per-connection
reservation specialization remains required because the general profile's
256-connection two-peer estimate is about 3.39 GB, regardless of whether smaller
arrays improve tick time. A V3-only causal suite now holds 256 secure connections
constant and runs established-idle, 10/35/100% active, Control-only, Input-only,
Input+Owner, and full cases in forward/reverse order with opt-in coarse phase
timing.

The first suite rejects idle housekeeping as the dominant CPU explanation:
idle mean is ~0.645 ms versus ~4.816 ms full. Forward and return protocol pumps
account for over 99% of the full timed work, while adding owner publication
accounts for roughly 75% of the active-work increment above idle. The maintained
linear active-population curve still shows no pairwise semantic pathology.

It also identifies the next isolated refinement. Input+Owner emitted 48,640
unreliable Data packets, zero reliable fragments, and 48,640 transport ACKs.
Security replay knowledge and reliable delivery knowledge remain distinct;
unreliable traffic must still advance authenticated replay state, but an ACK
which cannot retire reliable sender state is not automatically earned. Suppress
that ACK emission in one narrow experiment, preserve all replay/backpressure/
reliable gates, then rerun full/Input/Input+Owner before considering generic
packet batching, crypto micro-optimization, or workers.

That isolated N0.10.2b experiment is now accepted. Only admitted reliable Data
is ACK-eliciting; authenticated unreliable Data continues to advance replay,
sequencing, semantic admission, and delivery evidence without generating a
transport ACK which cannot retire sender flight state. Permanent tests prove
pure-unreliable zero-ACK/zero-RTT behavior, mixed reliable/unreliable completion,
and the existing reliable loss/backpressure/retransmission contracts.

At 256 connections, paired Input p95 falls from 1.867 to 1.247 ms,
Input+Owner from 5.100 to 2.790 ms, and Full from 4.975 to 2.868 ms. Input and
Input+Owner ACK counts fall from 10,240 and 48,640 to zero; Full falls from
about 48,806 ACKs to about 1,194.5, matching its reliable-fragment count. Wire
bytes fall about 39-44%, input-ack freshness slightly improves, and all
forward/reverse canonical hashes match.

The post-change V2/V3 matrix also matches canonical state in all eight runs.
V3 p95 is 0.374-0.389, 0.725-0.741, 1.437-1.459, and 2.809-2.813 ms at
32/64/128/256 bots, with a nearly linear 10.6-11.2 microsecond marginal cost per
client. V2 remains about 6.8-7.3 microseconds per client, so V3 is now roughly
1.4-1.6x rather than roughly 2.8-3.0x this control. The simple 4 ms marker moves
to approximately 368 bots. Immutable evidence:

- [`network-v3-peer-cost-unreliable-ack-suppression-2026-08-15.json`](../../_Benchmarks/Baselines/network-v3-peer-cost-unreliable-ack-suppression-2026-08-15.json)
- [`network-v2-v3-bot-n0102-unreliable-ack-suppression-2026-08-15.json`](../../_Benchmarks/Baselines/network-v2-v3-bot-n0102-unreliable-ack-suppression-2026-08-15.json)

This closes unreliable ACK suppression as a demonstrated large recovery. The
remaining full workload still averages only about 6.2% datagram fill and one
send syscall per datagram, making bounded multi-message secure datagram assembly
the next candidate to test with an opportunity census. Per-connection reservation
specialization remains a separate memory-viability requirement. Batching must be measured before
workers, sharding, compression, or protocol-format micro-tuning are admitted.

A batch-opportunity census now resolves that question for `owner_movement`.
Across 230,400 peer-direction pump inspections per case, every nonempty
Input/Input+Owner/Full opportunity contained exactly one unreliable frame;
maximum ready frames was one and every 2+/3+/4+ count was zero. Immediate greedy
packing therefore equals one datagram per observed frame. The accepted artifact
is
[`network-v3-peer-cost-batch-opportunity-census-2026-08-15.json`](../../_Benchmarks/Baselines/network-v3-peer-cost-batch-opportunity-census-2026-08-15.json).

This evidence does **not** admit a new multi-frame wire format for the current
workload. Low fill currently means one small frame is ready for each connection
and direction, not that the peer sends before sibling unreliable frames can be
packed. A future observer/WorldState workload must run the same census and may
earn assembly independently. Current CPU attribution proceeds to bounded
selection/encode/security/endpoint and receive-stage timing; cross-connection
syscall batching remains a separate later candidate.

That stage attribution is now captured as
[`network-v3-peer-stage-attribution-2026-08-15.json`](../../_Benchmarks/Baselines/network-v3-peer-stage-attribution-2026-08-15.json).
After subtracting established-idle scan/poll cost, the 48,640-packet Input+Owner
case attributes approximately 69% of peer time to endpoint send/receive, 24% to
secure framing/AES-GCM, and only about 3.6% to selection plus frame
encode/admission. Approximate per-Data costs are 2.876 us endpoint send,
0.999 us endpoint receive, 0.673/0.672 us secure send/receive, and less than
0.2 us combined selection/codec/admission.

This rejects pending-head restructuring as the next major owner-workload target.
The next CPU experiment is bounded cross-connection socket submission/receive
batching at the shared listener while keeping independent secure datagrams and
application semantics. It requires an explicit portable fallback and ownership/
backpressure contract; a Linux-only fast path is evidence, not the universal
Network API. Same-connection multi-frame assembly remains deferred until a
WorldState workload demonstrates 2+ ready frames for one peer direction.

That experiment is now implemented as an opt-in, bounded Linux `sendmmsg` path
with the portable scalar path retained as fallback. It changes no peer packet,
crypto, reliability, replay, or application semantics. Server endpoints admit
complete datagrams into a fixed listener-owned ring; one host pump submits only
the already-ready prefix and never waits to fill a batch. Partial native success
removes exactly the submitted prefix, bounded pressure leaves later peer work
pending, and retired endpoints cannot leak queued traffic into a reused slot.

At 256 clients, Input+Owner changes 38,400 server send calls into 600 native calls
of 64 datagrams and lowers diagnostic tick p95 by 6.5%. Full changes about 38,067
calls into 846 calls averaging 45.5 datagrams and lowers p95 by 8.6%. Input-only,
which has no server outbound state, is unchanged. Server socket-submit time falls
about 23%, not 64x: `sendmmsg` removes repeated syscall crossings but does not
remove per-datagram kernel UDP work. No partial/WouldBlock native submissions
occurred. Evidence:

- [`network-v3-shared-listener-scalar-split-attribution-2026-08-15.json`](../../_Benchmarks/Baselines/network-v3-shared-listener-scalar-split-attribution-2026-08-15.json)
- [`network-v3-shared-listener-native-send-batch-2026-08-15.json`](../../_Benchmarks/Baselines/network-v3-shared-listener-native-send-batch-2026-08-15.json)

The native mode remains opt-in while connection reservation is specialized; its
fixed outbound ring raises the 256-client known reservation from approximately
6.51 MiB to 7.97 MiB in the current profile. This is a retained modest server
optimization and platform seam, not the previously hypothesized 30-50% recovery.
Native receive batching is deferred because measured listener receive syscall
time is small in `owner_movement`; observer-heavy profiles must remeasure it.

N0.10 also freezes cadence decoupling as a first-class acceptance dimension:

> Additional Network pumps without additional authoritative publication may
> advance transport state but may not create duplicate application-semantic debt.

Simulation cadence governs production of committed authoritative semantic state.
Network cadence governs receive, authentication, ACK/retransmission, retained
flush, and socket progress. A faster Network clock must not reconstruct unchanged
WorldState, advance semantic sequences, consume new queue slots, or regenerate
owner/Event work. A slower Network clock may coalesce intermediate latest state,
but must preserve bounded ordered Event/Control facts and newest complete state.

Every cadence result reports rates per wall-clock second, authoritative step, and
Network pump. Required ratios include Network pumps per authoritative step,
captures per step, and publications per capture. Freshness is always expressed in
both ticks and milliseconds. The focused correctness/liveness matrix is:

| ECS Hz | Network pump Hz | Purpose |
| ---: | ---: | --- |
| 60 | 60 | fixed control |
| 60 | 120 | Network outruns authority |
| 60 | 240 | aggressive outrun |
| 30 | 60 | common cadence reduction |
| 30 | 120 | strong mismatch |
| 20 | 120 | extreme mismatch |
| 120 | 60 | authority outruns Network |
| 60->30->60 | 120 | live transition |
| 60->20->60 | 120 | severe live transition |

Permanent stall cases pause authority for 250 ms while Network pumps and pause
Network for 250 ms while authority steps. They report peak queue occupancy,
oldest work/debt age, relation debt, retransmission/wire burst, recovery time, and
maximum client state age. A one-publication/10,000-pump invariant test must leave
owner frame, WorldState semantic sequence, Event cursor, relation debt, and
application queues unchanged absent legitimate timers. The inverse test advances
authority repeatedly without Network and proves latest-state coalescing plus
lossless bounded Event/Control retention. Fixed 60 Hz remains the normal
performance gate; adaptive cadence cannot conceal a regression.

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
- interest enter/leave/crossing, layers, anchors, and shared semantic fanout;
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
- whether measured traffic earns explicit cohorts/encoded-fragment reuse;
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
