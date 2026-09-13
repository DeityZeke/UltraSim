# UltraSim Network kernel

Status: **accepted / consumption-ready mechanism layer**.

Network N0 established the dependency-clean kernel and application transport
foundation. Hosted H0.0-H0.5 extend it through externally sourced admission,
authenticated promotion, explicit IPv4 path ownership/rebinding, exclusive
connection-owner packet handoff, and the accepted dedicated-server/independent-
client process topology.

The canonical library product is one full `UltraSim.Network.dll`. An ordinary
`dotnet build` or `dotnet pack` builds **Full**: shared protocol machinery plus
both Hosted client and Hosted server roles. Optional `ClientOnly` and
`ServerOnly` builds remove an unused Hosted role without changing assembly
identity, shared API, wire bytes, protocol version, or manifest fingerprint:

```text
same source tree

Core/             always
Hosted/Client/    Full + ClientOnly
Hosted/Server/    Full + ServerOnly

default package:
    UltraSim.Network -> Full UltraSim.Network.dll
```

```bash
dotnet build Kernels/Network
dotnet build Kernels/Network -p:UltraSimNetworkFlavor=ClientOnly
dotnet build Kernels/Network -p:UltraSimNetworkFlavor=ServerOnly
```

`dotnet pack Kernels/Network` likewise produces the canonical Full package.
Packing ClientOnly or ServerOnly under `PackageId=UltraSim.Network` fails closed;
specialist stripped files are deployment artifacts, not competing canonical
packages.

These are physical capability sets, not behavior tiers or separate architectures.
`Full` is the canonical development/release API and exact binary superset.
Capability stripping is deployment minimization and is **not a security
assumption**. A future product may replace either stripped assembly with the
compatible Full assembly without changing consumer source or assembly references.
Flavor-specific output and intermediate roots prevent one same-named assembly from
contaminating another build.

Assembly identity answers **which library this is**, not which physical capability
set a particular file contains. Every output is named `UltraSim.Network.dll`; build
and product tooling reads the `UltraSim.Network.Flavor` assembly metadata and
inspects required/forbidden role types. Product code should depend on the role API
it was compiled to use, not branch on assembly identity or assume that a same-named
file is physically stripped. Replacing ClientOnly or ServerOnly with Full is valid;
combining or substituting the two mutually incomplete stripped files is not.

The accepted API, protocol, product-inspection, and Release-size evidence is
recorded in
[`network-capability-packaging-2026-09-08.md`](../../_Benchmarks/Baselines/network-capability-packaging-2026-09-08.md).

The current UltraSim Game selects `ChunkPublication` for dynamic-public
replication, but that is a **Game/Replication decision**, not a Network-kernel
concept. The kernel is equally unaware of the retained entity replication
profiles, owner-private Inventory/UI, application Events, or future Game protocols.

The current architecture contract is [Network V3](../../Docs/Plans/NETWORK.md).
The external listener/deployment lifecycle is
[Hosted Network H0](../../Docs/Plans/HOSTED_NETWORK.md).

## First-class traffic accounting

Every established secure peer owns bounded cumulative traffic counters. Packet processing updates
only fixed owner-local primitive storage: there is no global packet lock, per-packet allocation,
reflection, string formatting, or process-wide atomic counter. Cold diagnostic snapshots copy the
finite lane and manifest-message catalogs and may be aggregated outside the hot path.

PF2.5 adds explicit `Off`, `Basic`, and `Detailed` timing levels without changing traffic semantics.
The peer and Hosted pumps record into fixed owner-local logarithmic histograms; queue/debt current
and high-water values are primitive connection state. `Off` performs no timestamp capture,
`Basic` observes service interval/duration, and `Detailed` also observes named protocol stages.
The observation count/total/max cover the complete configured measurement window; percentiles are
bounded rolling estimates where the owning subsystem uses a fixed recent-sample window. Diagnostic
snapshots are read-only and do not reset connection counters or create a protocol timestamp.

PF3 does not add a Network transport cadence or change the kernel's wire behavior. The Hosted
client/server pumps remain event-driven owner-local mechanisms with a measured bounded service
interval. The authenticated Game application advertises that maximum interval through its finite
`ServerPerformanceProfile/v1`; it does not relabel the pump as a semantic Hz clock. Authority,
input submission, rendering, and owner/public delivery therefore remain independently observable.
The profile announcement/proof are ordinary classified Control messages, and the hard input-rate
limiter remains Game/application authority state rather than Network policy.

PF4D selects bounded unreliable frame aggregation. `NetworkProtocolLimits.Default` sends at most
two already-ready unreliable frames per secure Data datagram with `CrossLane` scope; scalar cap one,
`SameLane`, and cross-lane caps four/eight remain explicit controls. The manifest independently
states the maximum receive capability, so a sender always uses the smaller of its local send cap
and the negotiated manifest bound.

Aggregation never waits for future work. It starts with the ordinary next scheduler selection,
appends only work already ready in that `Emit`, stops if a reliable frame becomes next or the exact
secure plaintext/MTU bound would be exceeded, and consumes no selected message until the endpoint
accepts the complete datagram. Reliable Control/Event/Bulk frames remain singleton. Receive first
validates every frame's structure and negotiated count before publishing any frame, then applies
each unreliable lane's normal sequencing/capacity semantics independently. Correlated loss is a
deliberate physical consequence of sharing one datagram; it creates no retransmission for
unreliable state and changes no application-proof meaning.

Fixed aggregation histograms, lane masks, MTU-fill buckets, and maxima are exposed in the cold
protocol/traffic snapshot. The more expensive immediate-ready cap/scope opportunity census runs
only with `Detailed` telemetry. Both paths use owner-local fixed primitive storage and add no
per-packet managed allocation.

The physical metric is deliberately named **UDP payload bytes**. It is the exact datagram payload
length successfully handed to, or received from, the UDP socket boundary. It excludes IP, UDP, and
link-layer headers, and it never includes HTTP content acquisition or CAS/disk traffic. An optional
IPv4+UDP estimate is reported separately as `28 * datagram count`; it is an estimate, not an
observed NIC-wire value.

Secure physical traffic is split into Data, acknowledgement, and keepalive datagrams. Reliable Data
re-emission increases the physical retransmission counters without counting the logical message a
second time. Hosted endpoints additionally retain an exact physical bucket for handshake, path,
and other traffic that cannot honestly be attributed to authenticated secure framing. Pre-session
listener traffic is not assigned a fabricated connection identity.

Logical traffic is a separate coordinate system:

```text
logical application payload bytes
logical encoded data-frame bytes
logical messages
        !=
physical UDP payload bytes
```

Logical counters are available per finite Network lane and per manifest message. Several
unreliable messages may share one physical datagram, and encryption/framing overhead therefore
remains at the datagram/connection level rather than being apportioned speculatively. Game owns an
exhaustive allocation-free mapping from every current production message to a stable semantic
traffic family; a completeness gate fails when a new message lacks an intentional classification.

Snapshots are cumulative and generation-fenced. Measurement tooling takes start/end snapshots and
derives an explicit-window delta; production counters are not reset for benchmarks. Hosted server
retirement freezes a bounded final snapshot before slot reuse, and a cleanly closed client retains
its final snapshot so a reconnect becomes a distinct lifetime instead of moving old counters.

## Ownership boundary

```text
Game / application owners
    authority
    authorization / disclosure
    AOI / interest
    semantic coalescing and recovery
    application-proof meaning
    canonical Game message descriptor + bounded bytes
            |
            v
Kernels/Network
    ConnectionId / security identity
    lane contracts
    bounded message admission
    ready selection / retention
    reliability / unreliable delivery
    packet ownership
    security / replay
    pacing / fragmentation
    datagram endpoint contract / UDP transport
            |
            v
application/client adapter
```

Permanent rules:

```text
AUTHORITY DOES NOT MOVE INTO NETWORK.
GAME SEMANTICS DO NOT MOVE INTO NETWORK.
AOI DOES NOT MOVE INTO NETWORK.
CONNECTION IDENTITY != GAME IDENTITY.
IP/PORT != CONNECTION IDENTITY.
TRANSPORT ACK != APPLICATION PROOF.
LANES DEFINE DELIVERY; PACKETIZATION IS LAST.
LOSS / CAPACITY MAY NOT SILENTLY CHANGE SEMANTICS.
HOT / REMOTELY INFLUENCEABLE MEMORY IS BOUNDED.
```

`Kernels/Network` has no runtime project dependency. Its compile-time analyzer
reference generates protocol codecs/manifests. The repository architecture gate
freezes this dependency direction. Deterministic impairment, two-ended loopback,
and self-trusted transport fixtures live in the separate
`Kernels/Network.Testing` assembly; no production Network capability set ships
that assembly.

## Identity domains

Network identities are fixed/value identities with explicit generations/lifetimes:

| Identity | Meaning |
| --- | --- |
| `HostedCandidateId` | generation-fenced cookie-validated pre-session row |
| `ConnectionId` | server-created generational transport connection; not client chosen |
| `SecurityEpoch` | key/replay lifetime within one connection |
| `PacketNumber` | authenticated datagram sequence within one security epoch |
| lane/message sequence state | delivery ordering/supersession state owned by the selected lane contract |
| `HostedConnectionOwnerId` | generation-fenced protocol/connection worker ownership |
| `HostedPacketTransferId` | generation-fenced exclusive packet-slab ownership transfer |
| `HostedPathGeneration` | authenticated current-path lineage for one established connection |
| `PacketBufferToken` | pool-instance-bound, generation-fenced ownership of one packet slot |

Game/application identities such as `ParticipantId`, `StateEpoch`,
`ReplicationEntityId`, `WorldPublicEntityId`, `PersistentEntityKey`, ECS handles,
and client semantic/render slots are outside this kernel and must not be aliased to
Network identities.

An endpoint is only an observed path. Hosted path rebinding may change IPv4/port
while preserving the same `ConnectionId`, secure peer, replay state, lanes, and
application session.

## Bounded kernel and packet ownership

`NetworkKernelLimits` and the protocol manifest determine bounded peer reservations.
The kernel uses:

- a fixed generational connection table;
- fixed packet/slab storage with generation/stage ownership tokens;
- bounded producer-owned rings/arenas;
- bounded reliable-flight and reassembly state;
- bounded independent-unreliable receive/state containers;
- manifest/lane-aware retained payload arenas;
- transactional capacity admission.

Stale, foreign-pool, double-returned, wrong-owner, or post-reuse tokens cannot access
packet storage. A token includes the issuing pool identity as well as slot/generation;
matching slot layouts in unrelated pools do not confer ownership. Capacity failure must occur before partial publication. Shutdown
requires connections, queues, and packet ownership to reconcile according to the
owning lifecycle.

H0.4 adds the explicit cross-thread handoff: listener-owned packet bytes cross to
one connection owner under an exclusive transfer token. No mutable packet object is
shared between owners. H0.5 composes that handoff with the real external listener
and fixed owner threads.

## Canonical generated protocol

`NetworkCodecGenerator` emits canonical fixed/bounded codecs and immutable manifest
metadata from `[NetworkProtocol]` / `[NetworkMessage]` declarations.

The codec boundary is hostile-input transactional:

- canonical integer endian/layout;
- checked enum/flags domains;
- canonical booleans;
- bounded/non-minimal varint rejection;
- malformed UTF rejection;
- length/count preflight;
- staged read/write cursors published only after complete success;
- exact readers reject trailing bytes;
- schema/manifest identity changes when receive capability changes.

Live CLR/Game structs are never implicitly memcpy'd onto the wire. Game protocol
source is the authority for current message/manifest versions; old benchmark
fingerprints are historical checkpoint data, not kernel constants.

## Security and handshake

The accepted secure session machinery uses:

- cookie-before-allocation admission;
- pinned deployment/server identity for production-shaped Hosted clients;
- ephemeral P-256 ECDH;
- P-256 server signature over the canonical manifest-bound transcript;
- HKDF-SHA256 key derivation;
- client/server secret-possession proofs;
- AES-256-GCM established datagrams with a 128-bit tag;
- directional keys and `SecurityEpoch + PacketNumber` nonce domains;
- authenticated replay protection distinct from reliable-delivery acknowledgement.

Malformed outer framing or unauthenticated data publishes no replay, epoch, lane,
reassembly, acknowledgement, application, or route state.

The handshake authenticates the pinned Network endpoint/transport transcript. It
does **not** authenticate an account or grant a `ParticipantId`; that happens in the
Game application protocol above the secure connection.

## Transport ACK versus application proof

Network reliable ACK means the remote protocol peer authenticated the datagram and
admitted its reliable payload into bounded receive/reassembly state. It does not
prove that Game logic, UI, a world snapshot, or any other application semantic
committed.

An ACK authenticates the exact opposite-direction sending `SecurityEpoch` it
acknowledges in addition to packet number/mask. A delayed genuine ACK from an older
epoch is valid traffic but cannot retire reused packet numbers, update RTT, or alter
congestion state in the current epoch. Transport wire revision 2 records this framing
change in every protocol-manifest fingerprint independently of application messages.

Unreliable state does not emit semantically useless transport ACKs merely to pretend
knowledge. Game/application owners retain the semantic debt they require and advance
knowledge only after their own authenticated application proof.

This distinction is the lower-layer half of the permanent `SENT != KNOWN` rule.

## Lane contracts

`NetworkLaneContracts` freezes delivery semantics before packetization:

| Lane | Delivery / order | Retention meaning |
| --- | --- | --- |
| Control | reliable ordered | protocol/session/control and ordered application control |
| Input | unreliable sequenced | expiring complete input/latest command state |
| OwnerState | unreliable sequenced | latest owner-private state |
| WorldState | unreliable unordered / independent | recoverable independent latest-state containers; semantic coalescing/debt remains above Network |
| Event | reliable ordered | application facts whose intermediate occurrence matters |
| Bulk | reliable ordered, fragmentable | bounded segmented definitions/baselines/snapshots/large semantic operations |
| Disposable | unreliable unordered | explicitly disposable presentation data |

Latest state and retained facts are different contracts. Network does not turn ECS
lifecycle notifications into Events or collapse application facts into latest state.

## Reliable / unreliable delivery

The bounded delivery machine includes:

- independent authenticated-replay and reliable-delivery windows;
- reliable ordered Control/Event/Bulk retention with ACK-driven release;
- sequenced unreliable Input/OwnerState supersession according to their declared
  message-family contracts;
- independent unordered WorldState containers without Game semantic interpretation;
- bounded fragmentation/reassembly for lanes/descriptors that permit it;
- deterministic or monotonic RTT/RTO, congestion/backoff, and pump budgets;
- authenticated security-epoch advancement;
- explicit fail-closed capacity/nonviable outcomes.

P0.3H later replaced repeated ready-table scans with bounded ready-state structures:
per-lane reliable FIFO heads, IndependentState ready bitsets/counts, and a priority-
ready mask. Delivery/loss semantics did not change.

P0.3I's chunk application-proof envelope is a **Game protocol** optimization using
this same Network mechanism. Network does not interpret its per-entry proof meaning.

## Connected UDP and shared listener

`HostedUdpProtocolClient` composes one client-owned secure peer over a connected,
nonblocking `UdpDatagramEndpoint`. Socket/protocol state share one owner in that
production client topology. The server-side `SharedUdpServerListener` instead owns
one shared socket, deployment/path routing, admission, and bounded per-session
delivery to connection owners.

`UdpProtocolSession` is the accepted two-ended connected-UDP evidence fixture. It
now lives in `UltraSim.Network.Testing` and is not part of Full, ClientOnly,
ServerOnly, or any production product. Its semantic evidence remains valid; its
two peers and self-contained server identity are test topology rather than a
production connection composition.

Linux deployments may use accepted `sendmmsg` / `recvmmsg` batch paths beneath the
same endpoint contract; unsupported platforms retain scalar fallbacks. Batching
never waits to fill a batch and never changes per-datagram security/admission
semantics.

Socket submission, would-block observations, batching, listener routing, and packet
ownership are transport evidence. They do not define Game AOI, semantic freshness,
or application knowledge.

## Hosted H0 external lifecycle

H0.0-H0.5 are accepted. The canonical policy is maintained in
[`HOSTED_NETWORK.md`](../../Docs/Plans/HOSTED_NETWORK.md); the kernel owns the
mechanisms summarized here:

- stateless manifest/path/time-bound cookies before expensive stateful work;
- fixed rate/candidate capacity and <=1:1 unauthenticated response amplification;
- transactional authenticated candidate promotion into an existing protocol peer;
- deployment-key pinning rather than implicit self trust;
- authenticated IPv4 Request/Challenge/Proof/Commit path rebinding;
- exclusive listener<->connection-owner packet-slab handoff;
- fixed connection-owner runtime and real dedicated-server process composition.

Scalar and sharded hosts share one application-retirement/idle/drain/deadline policy.
Terminal sessions release their application, peer, route, connection-table row, and
admission slot; the sharded owner applies peer transitions on its owner thread and the
listener reaps only after quiescence. A bounded retirement wait that expires leaves the
session draining, its generation-fenced handoff binding intact, and its admission
capacity reserved; a later listener reap completes cleanup only after the owner reports
that application/peer/endpoint disposal has finished. Idle-age evaluation also treats a
newer listener-side activity publication as fresh when it races an earlier owner time
sample; it never subtracts those samples out of order. Owner ingress work per pass is explicitly bounded
so a continuously refilled fixed queue cannot postpone application progress or shutdown
indefinitely. Shutdown joins/reclaims every quiescent owner and closes the listener
before reporting collected callback/worker failures.

H0.5's accepted topology uses one dedicated server process, four owner threads,
eight independent client processes, and two fresh application phases per client.
It is a process/socket/lifecycle correctness gate, **not** a public-WAN or production
CCU claim.

Hosted currently does not claim IPv6, public-WAN hardening, production admission
rates, NAT/relay, load balancing, multi-host live connection migration, or DDoS edge
integration.

## Deterministic evidence

`UltraSim.Network.Testing` supplies `DeterministicDatagramLink` and
`DeterministicNetworkImpairment` for reproducible
loss, duplication, corruption, delay/reorder, and capacity evidence without adding
protocol semantics.

Correctness compares semantic outcomes owned above Network, not packet retries or
internal storage positions. Accepted impairment evidence proves progress or
fail-closed recovery across packet-window loss, retransmission, reassembly, stale
history, capacity pressure, and security/replay boundaries.

## Capacity and allocation policy

Every remotely influenceable hot structure has declared limits. Capacity exhaustion
is not permission to allocate a dictionary/list fallback in the hot path.

Where a warmed path claims zero managed allocation, setup/cryptography provider
allocation and hostile/cold work are reported separately. Reservation snapshots state
what they count and their CLR/native exclusions; a byte estimate is not total process
memory unless explicitly measured as such.

Protocol payload reservation is descriptor/lane aware: the global maximum message
size is an admission ceiling, not a per-slot slab behind every message.

## Evidence interpretation

Network evidence distinguishes:

- mechanism microbenchmarks;
- Game semantic/profile work;
- server transport/security replay;
- shared-listener/socket work;
- client decode/application;
- complete same-process integrated fixture;
- dedicated-process topology evidence.

A result from one level cannot silently become a claim about another. In particular:

```text
Game-core scaling != strict server-role rating
strict localhost rating != production CCU
same-process integrated tick != dedicated-server budget
localhost process/socket correctness != public-WAN readiness
```

See [Network V3](../../Docs/Plans/NETWORK.md) and the benchmark methodology for the
current rating/cadence distinctions.

## Current stop

The kernel is infrastructure to consume, not an open-ended optimization project.
Do not add generic AOI/replication semantics, profile switching, another packet
format, N1.5-style Game semantic caching, or multi-host migration to the kernel
without a concrete consumer proving the need.

New Network-kernel work should be driven by a demonstrated correctness, security,
capacity, freshness, deployment, or measured hot-path problem in a real Game/client
vertical.
