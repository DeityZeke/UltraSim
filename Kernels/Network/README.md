# UltraSim Network kernel

Status: N0.0 through the hardened N0.9 integrated topology are accepted. N0.10
scale work now includes the first provisional V2/V3 bot matrix and a bounded
shared-listener specialization plus measured reliable-only ACK elicitation.

This project is the game-, ECS-, UI-, client-, and server-independent mechanism
layer for Network V3. It owns fixed capacities, generational connection/session
state, packet ownership, producer-owned rings, canonical framing, generated
codecs/manifests, deterministic datagrams, and owner-thread connected UDP. It does
not authenticate accounts, capture game state, or invent an alternate authority.

The complete forward plan is [Network V3](../../Docs/Plans/NETWORK.md).

## Ownership boundary

```text
Game/Replication       chooses canonical game projection and interest
Server/Network         attaches protocol lifecycle to ServerRuntime
Client/Network         applies remote protocol semantics to Client/Core
        |
        v
Kernels/Network        bounded identities, lanes, protocol, delivery, transport
```

`Kernels/Network` has no runtime assembly dependency; its only project reference
is the compile-time `Network.Generators` analyzer. The repository verifier freezes
that boundary. Network moves and schedules information; it does not own game policy,
accounting, Admission, persistence, UI meaning, or simulation authority.

## Identity domains

Identity is explicit and non-interchangeable:

| Identity | Owner | Lifetime / meaning |
| --- | --- | --- |
| `ConnectionId` | Network | Server-created transport slot plus reuse generation; neither durable nor client-chosen. |
| `SecurityEpoch` | Network | Key/replay lifetime within one connection. |
| `PacketNumber` | Network | Authenticated datagram sequence; admitted reliable-data packet numbers may be referenced separately by transport-delivery ACKs. |
| `LaneSequence` | Network | Reliable ordering is lane-scoped; sequenced unreliable coalescing is isolated per Input/OwnerState message family. WorldState containers are unordered and use application relation sequences instead. N0.6 uses one complete logical stream per Input/OwnerState family; a generic sub-stream key remains deferred until a second simultaneous stream earns it. |
| `StateEpoch` | Projection/protocol seam | One coherent canonical projection generation. |
| `ReplicationEntityId` | Canonical projection | Dense generational identity shared by all recipients within one `StateEpoch`. |
| `ParticipantId` | Game contracts | Server-created game/session participant; not defined by this kernel. |
| `PersistentEntityKey` | ECS persistence | Durable world plus entity identity; never replaced by a network ID. |
| client entity slot | Client | Private projection storage identity; not a wire contract. |

N0.0 deliberately does not implement a connection-relative `WireEntityAlias`.
If bandwidth measurement later earns one, it is only an encoding of
`ReplicationEntityId`; it cannot redefine canonical replicated identity.

## N0.2 bounded kernel

`NetworkKernelLimits` owns one peer-kernel reservation profile. One isolated
`NetworkKernel` binds a collision-checked protocol manifest to:

- a fixed generational `NetworkConnectionTable` with explicit state transitions,
  security epochs, and per-lane sequences;
- a fixed packet/slab pool whose `PacketBufferToken` proves slot generation and
  current stage ownership;
- bounded producer-owned ingress/egress rings;
- strict canonical big-endian readers, writers, and 32-byte packet framing.

The deterministic two-peer topology owns separate `DeterministicDatagramLimits`;
its slots are not charged to either peer kernel.

Capacity exhaustion returns before mutation. Stale, foreign, double-returned,
wrong-owner, or post-reuse packet tokens cannot access memory. Shutdown requires
all connections released, rings drained, and packet tokens returned.

`NetworkCodecGenerator` emits exact fixed-width measure/write/read codecs and an
immutable manifest from `[NetworkProtocol]` and `[NetworkMessage]`. Generated
measure/write operations reject invalid enum values locally, and read/write
operations stage their cursor and publish it only after the complete message
succeeds. Protocol and message declarations must be public, top-level,
non-generic, and partial; generated codec references and hint identities are fully
namespace-safe. Primitive integer representation is canonical and checked-build
signed values are bit-preserving; booleans accept only `0`/`1`; non-flags enums
accept declared values while flags enums accept only combinations within their
declared bit mask; exact readers reject trailing bytes. The lower-level reader additionally rejects non-minimal varints,
malformed UTF-8, unpaired UTF-16 during encoding, and caller limits before exposing
text bytes. Dynamic text/object allocation is not part of the admitted N0.2 hot
codec profile.

`DeterministicDatagramLink` is an in-memory datagram endpoint, not a protocol. It
applies stateless loss/duplication/corruption/delay decisions to fixed slots and
makes capacity failure transactional. N0.3 runs secure delivery semantics across
this endpoint.

## N0.3 secure reliable ProtocolLoopback

`ProtocolLoopback` runs the complete admitted protocol between two bounded peers.
Cold connection setup uses a stateless HMAC-SHA256 cookie before allocation or
ECDH work, ephemeral P-256 ECDH, a pinned P-256 server signature over the complete
transcript, HKDF-SHA256, and client/server secret-possession proofs. It authenticates
the server endpoint, not an account or game identity; that remains N0.4 application
work above the secure Control lane.

Established datagrams use AES-256-GCM with a 128-bit tag. The canonical outer
header is associated data. Directional keys are distinct and the nonce domain is
`SecurityEpoch + PacketNumber`. Exact outer framing and AEAD authentication precede
all inner protocol processing. A successful authentication commits the packet number
to a security replay window even if bounded semantic admission must defer the data.
Malformed outer framing or unauthenticated bytes publish neither replay, epoch, lane,
reassembly, acknowledgement, nor delivery state.

Authentication replay and transport delivery are deliberately separate facts. A
second window contains data packets admitted into bounded delivery/reassembly
machinery. Only admitted **reliable** data is ACK-eliciting because only reliable
fragments own retained sender flight state for an ACK to release. Admitted
unreliable data remains authenticated, replay-protected, sequenced, and recorded in
the delivery window, but emits no semantically useless transport ACK. If reliable
admission is blocked, the packet is replay-protected but not ACKed; the sender
retains the semantic fragment and retransmits it under a fresh `PacketNumber`.

The bounded delivery machine provides:

- independent 64-packet authenticated-replay and transport-delivery windows;
- reliable ordered Control, Event, and Bulk retention with ACK-driven release;
- unreliable sequenced Input and OwnerState coalescing isolated per message
  family;
- bounded independent unordered WorldState containers whose application owner
  retains and retries newest relation debt;
- disposable unordered delivery;
- MTU-bounded fragmentation/reassembly;
- deterministic step-clock RTT/RTO, congestion-window/backoff, and independent
  inbound/outbound pump budgets;
- authenticated fail-closed security-epoch advancement;
- zero warmed managed allocation for normal secure delivery.

The loopback cookie uses a fixed path token and protocol time uses deterministic
steps. N0.5 binds cookies to real endpoint/path identity and expiry and maps timing
to a real monotonic clock. Monotonic UDP peers pace when RTT/CWND produces a
nonzero millisecond interval. N0.6 supplies game-specific input redundancy/expiry;
N0.7 supplies full recipient fairness and scheduling.

N0.3 remains single-owner. The N0.2 SPSC ring proves token transport and the packet
pool proves single-owner packet memory independently; cross-thread packet-memory
handoff is not claimed. N0.5 keeps socket I/O and protocol buffers on the same
owner thread, so no handoff exists. A future socket worker must admit per-owner
pools or one transactional ownership-transfer-plus-publication primitive first.

Reliable-head, ACK-record, and deterministic-datagram selection still use bounded
linear scans. They are acceptable at admitted N0.3 capacities; N0.7/N0.10 scale
measurements decide whether another index is earned.

An N0.3 ACK is specifically a **transport-delivery ACK**: the remote protocol peer
authenticated the packet and admitted its reliable payload into bounded receive or
reassembly state. It does not prove that game logic executed, UI state rendered, a
baseline was applied, or recipient-known projection state advanced. Those stronger
proofs remain distinct application/baseline contracts in later slices.

All-unreliable traffic intentionally produces no transport RTT sample. Unreliable
packets never owned matching sender flight records, so their former ACKs could not
update RTT or congestion state. If realtime traffic later needs independent path
measurement, it must use an explicit bounded probe/sampling contract rather than
ACKing every latest-state packet.

## Lane contracts

The executable `NetworkLaneContracts` table freezes message meaning before
packetization exists:

| Lane | Delivery / order | Retention meaning |
| --- | --- | --- |
| Control | reliable ordered | protocol, session, baseline, and clock control state |
| Input | unreliable sequenced | expiring authoritative input |
| OwnerState | unreliable sequenced | latest owner state |
| WorldState | unreliable unordered | independent recoverable state containers; no Network coalescing |
| Event | reliable ordered | application facts whose intermediate occurrence matters |
| Bulk | reliable ordered, fragmentable | bounded segmented definitions/baselines/content |
| Disposable | unreliable unordered | explicitly disposable presentation data |

ECS lifecycle facts are not automatically Event-lane messages. They are capture
evidence used by `Game/Replication` to update canonical truth and derive spawn,
enter, leave, despawn, or projection-shape operations. Projection lifecycle is
recoverable through a declared baseline/resnapshot scope. Application events such
as purchase results, chat, trade completion, or quest facts retain their own
reliable/idempotent semantics and cannot silently collapse into latest state.

## Semantic evidence

`NetworkSemanticTrace` is a fixed-capacity canonical fact trace. It records
observable meaning rather than packet retries, fragments, aliases, encryption
records, or client storage positions. Append and digest are allocation-free after
construction. Exhaustion returns false without mutation. Correctness compares the
exact fact sequence; the stable 128-bit digest is compact evidence, not a substitute
for exact comparison.

`DeterministicNetworkImpairment` is a stateless oracle keyed by a profile seed and
submission ordinal. Loss, duplication, corruption, and bounded delay decisions are
therefore independent of call order and exactly reproducible across protocol
topologies. `DeterministicDatagramLink` consumes that oracle without adding message
or protocol meaning.

The N0.0 allocation profile is explicit:

| Profile | Operations | Contract |
| --- | --- | --- |
| Hot/warmed | identity value operations, lane lookup, trace append/digest, impairment decision | zero managed allocation |
| Cold/setup | trace capacity construction, evidence-profile strings/results, test/report setup | managed allocation allowed and reported separately |

N0.2 admits warmed pool/ring/codec/framing/datagram operations. N0.3 additionally
admits normal secure encode/encrypt/send/decrypt/replay/lane delivery as zero managed
allocation. Cold handshake and hostile authentication failures are bounded but are
not claimed as zero-allocation profiles. Socket submission remains later.

`NetworkProtocolSnapshot.ReservedBytes` reports peer-owned managed-array payload
and both directional master-secret copies. It excludes CLR object/array headers
and opaque cryptography-provider state. The preconstruction limits API is therefore
explicitly an estimate, not total process memory.

The N0.3 acceptance-hardening run (25 isolated iterations, 10K default operations)
reported 28.162 µs mean / 28.484 µs p95 for unreliable state, 28.923 / 29.137
µs for reliable Control, and 34.901 / 35.196 µs for 4 KiB reliable Bulk,
with 0 B timed allocation. Against the immutable replay-hardened N0.3 baseline,
means are about 9.3%, 10.5%, and 10.1% slower respectively. This is retained as
an explicit correctness cost rather than silently replacing the earlier performance
reference: the hardened path adds whole-window liveness bookkeeping and independent
message-family state. Impairment convergence remains identical (2,162 steps, 131
retransmissions, 15 authentication failures), and N0.5 must remeasure when the
real ownership/transport path replaces proof-stage loopback machinery.

The final observation is preserved separately as
[`network-n0.3-secure-protocol-corrective-hardening-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.3-secure-protocol-corrective-hardening-2026-08-14.json).
It does not overwrite or normalize the
[`replay-hardened`](../../_Benchmarks/Baselines/network-n0.3-secure-protocol-replay-hardened-2026-08-14.json)
performance reference. N0.3 is accepted and frozen after this adversarial
hardening. N0.4 and N0.5 now measure the complete application path through
ProtocolLoopback and UDP; neither result justifies reopening N0.3 micro-optimization.

## N0.5 connected UDP transport

`UdpProtocolSession` runs the accepted secure peers across actual connected,
nonblocking localhost UDP sockets. `UdpProtocolLimits` bounds datagram bytes,
configured socket buffers, handshake work, cookie buckets, idle retirement, and
graceful drain. Socket and protocol state share one owner thread.

The handshake cookie covers the client address/port and a current or immediately
previous monotonic time bucket. Maximum accepted age can therefore approach twice
`CookieBucketMilliseconds`. Server pinning, ECDH, transcript signature, secret
proofs, AEAD, replay, delivery ACK, lanes, fragmentation, and application meaning
remain the N0.3/N0.4 contracts. Connected-path filtering rejects unrelated source
endpoints before protocol parsing.

Normal pumping uses monotonic milliseconds for RTT, retransmission, loss discovery,
and admitted pacing. Empty receives poll readiness rather than allocate a normal
`SocketException`; the warmed secure UDP delivery test remains 0 B. Beginning
drain closes new application-message admission while retained reliable data and
ACKs continue until empty or the deadline expires; a closed session cannot accept
new peer work. Authenticated keepalives preserve live idle paths without acquiring
application meaning. Deterministic clock gates directly prove keepalive retention,
idle expiry, nonzero pacing, and current/previous-cookie-bucket acceptance/expiry.
Fresh reconnects own fresh endpoints and independently derived secrets. The
accepted topology is a same-process localhost proof, not a WAN-latency or
socket-worker claim.

### N0.10.1 bounded shared listener

`SharedUdpProtocolHost` replaces one server socket per connection with one
owner-thread IPv4 listener, a fixed open-address path table, bounded per-session
datagram rings, and deterministic session-slot traversal. Every session keeps
its own `NetworkProtocolPeer`, cryptographic state, replay/ACK windows, lane
queues, congestion state, and application semantics. There are no workers,
shards, cross-connection packet batches, compression, or alternate protocol.

The shared socket requests the aggregate send/receive capacity formerly spread
across the server sockets it replaces, capped by the existing socket-buffer
contract. Client socket and protocol capacities are unchanged. This matters:
using one connection's buffer for the aggregate listener caused kernel-level
burst loss that was invisible to managed queue counters. The permanent
64-session initial-flight regression proves exact delivery without
retransmission when the admitted burst fits that aggregate contract.

Session capacity, demux queues, listener receive budget, path registration,
connection generation reuse, unknown paths, drain/retirement, socket bytes, and
known managed payload are explicit and observable. The current public
constructor remains controlled loopback evidence, not an Internet listener or
multi-owner socket host.

### N0.10.2 reliable-only ACK elicitation

The 256-peer causal suite found one transport ACK for almost every unreliable
Data packet even though unreliable sends own no retained flight record. The
accepted correction makes only admitted reliable Data ACK-eliciting while
preserving authentication, replay, delivery-window, sequence, capacity, and
reliable backpressure behavior.

Input+Owner p95 fell from 5.100 to 2.790 ms and Full from 4.975 to 2.868 ms;
their timed wire bytes fell about 39%. The post-change 32/64/128/256 V3 p95
range is 0.374-0.389 / 0.725-0.741 / 1.437-1.459 / 2.809-2.813 ms, with exact
canonical V2/V3 state in every run. Full traffic still averages roughly 6.2%
datagram fill and one send syscall per datagram; bounded multi-message assembly
was subsequently earned only by the WorldState-shaped sparse workload.
Reservation specialization is
still required independently for memory viability. See the
[bot comparison guide](../../_Benchmarks/NETWORK_V2_V3_BOT_COMPARISON.md) for
the complete before/after counters and immutable artifacts.

N0.10's batch-opportunity census and protocol-stage timing are deliberately
internal, opt-in benchmark instrumentation. The census capacity-scans pending
unreliable state and therefore never runs in normal protocol operation. Stage
timing adds timestamp probes only after explicit reset/enable and is cleared when
disabled. A later WorldState-shaped sparse census earned a bounded same-peer
unreliable multi-frame Data experiment. Manifest v11 fingerprints the maximum
unreliable frame count a peer can decode; the local
`MaximumUnreliableFramesPerDatagram` remains a send-policy cap, so a cap-one
sender still decodes aggregates. The default cap-one path retains scalar
selection/encoding/admission and does not update aggregate telemetry. The
experiment remains opt-in pending sustained tail, freshness, and fairness
acceptance.

The follow-up shared-listener experiment optionally uses Linux `sendmmsg` beneath
the endpoint contract. Peers still create and consume individual authenticated
datagrams. A fixed listener-owned ring provides per-datagram admission and retains
an unsent native suffix; unsupported platforms and handshake traffic use scalar
socket operations. The listener submits only work already present in the current
pump and never delays traffic to form a batch. This Linux fast path remains an
explicit deployment choice with the scalar path as portable fallback; its fixed
ring is included in reservation accounting.

Dense-400 later earned the matching receive experiment. `PlatformBatch` remains
the accepted send-only control; `PlatformSendReceiveBatch` additionally owns
fixed listener receive payload/address/length storage and uses Linux `recvmmsg`
when available. It performs one zero-time readability check and drains only the
datagrams already available at that pump—there is no wait to fill a batch.
Every returned datagram still routes through the existing path table and bounded
per-session ring independently. Truncation, unknown paths and per-session
pressure retain the scalar rejection/accounting semantics, while unsupported
platforms keep the portable scalar receive path. Permanent tests cover
independent routes, per-session pressure and warmed 0-B delivery.

Protocol payload reservation is manifest- and lane-aware. The global maximum
message size is an admission ceiling, not an allocation behind every slot.
Reliable/receive strides use the largest admitted descriptor; nonfragmenting
independent state is capped at one datagram; only retained unreliable descriptors
own stream regions; and only represented reliable lanes own reassembly regions.
The count-only estimate remains conservative, while the manifest overload
estimates a concrete protocol.

`NetworkProtocolSnapshot.RetainedPayloads` reports exact current/high-water/
capacity bytes by ownership category, plus the largest retained message, explicit
byte-capacity failures, and reliable/received compactions. Reliable and received
slots use separate fixed byte arenas rather than per-slot maximum-size slabs.
The default arenas are 256 KiB each per peer while slot count remains 128 and an
individual message may remain 16 KiB. Exhaustion rejects transactionally;
fragmented arenas compact using preallocated scratch and allocate nothing.

The earlier approximately 303 KiB estimate described the small benchmark
manifest, not the game protocol. A permanent cross-layer test now constructs the
actual `UltraSimGameProtocol.Manifest`: its default two-peer reservation is
1,705,248 bytes (~1.63 MiB), with a 1,706,054-byte preconstruction estimate.

## N0.6 application-owned input and owner state

The kernel did not acquire game state in N0.6. `Game/Protocol` adds bounded Input,
OwnerState, and Control message families; `Game/Replication` validates and
publishes intent through existing authority, while `Client/Network` owns its
private projection. Input repeats at most three newest unprocessed samples and
expires against authoritative world time. Every admitted Input sample is complete
velocity state and remains correct if older samples are skipped; non-coalescible
facts use reliable application semantics above the kernel. One complete owner-
state sample supersedes the prior sample for the single controlled entity, and a
first-seen/repeated tombstone is itself complete absence plus a canonical
generation floor. Reliable revisioned clock anchors define how state is
interpreted across live cadence changes.

This deliberately does not add a generic transport sub-stream identity. There is
one semantic latest-state stream in each admitted family, so message-family
coalescing is correct. N0.7 must keep per-entity/recipient pending truth above the
kernel and assemble bundles with explicit supersession semantics rather than let
transport coalescing invent AOI behavior.

## Evidence admission

`UltraSim.Network.ComparisonCore` freezes four evidence classes:

- **Direct** — equivalent semantic work and exact measurement profile; a relative
  result is admitted.
- **Provisional** — exact declared profile with a clearly stated contract gap; a
  labeled relative result may be shown.
- **Directional** — useful side-by-side evidence, but no headline ratio.
- **Unsupported** — no honest equivalent and no ratio.

Workload identity, semantic contract, capacity, clock, topology, and allocation
scope must all match before Direct or Provisional evidence can produce a relative
headline. A mismatch is automatically classified Unsupported. The general
methodology remains in the [benchmark guide](../../_Benchmarks/README.md).

## V2 archaeology

The frozen V2 network is executable historical specification, not a dependency.
N0.0 inspected its kernel, streams, scheduler, loopback transport, client/server
bridges, tests, and benchmarks.

Semantics worth preserving:

- connection identity is separate from account/game identity;
- latest state and ordered events are different retention contracts;
- retained event overflow has explicit recovery behavior;
- transport moves datagrams while higher layers own semantic meaning;
- recipient knowledge cannot be inferred solely from send intent;
- deterministic loopback/impairment is useful protocol evidence;
- lane and connection metrics belong in accepted evidence.

Representations and ownership not carried forward:

- `ConcurrentQueue`, dictionary/lock latest streams, and managed per-entry objects
  are not the bounded N0 hot-path design;
- V2 game policy and server-core ownership under Network do not fit the accepted
  `ServerRuntime`/game-composition architecture;
- old send-state machinery is not acknowledgement/application truth;
- historical benchmark numbers are not accepted V3 comparisons merely because
  both implementations use words such as loopback, latest, or reliable.

The N0.10 bot harness now admits a **provisional** end-to-end V2/V3 owner-movement
ratio through normalized identity/input/velocity semantics. It is not a direct
protocol-equivalence claim: V2 and V3 retain different security, reliability,
reservation, and allocation contracts. N0.3 standalone evidence remains
unrelated to that ratio.

Cold protocol snapshots also expose packet-kind counts, encrypted/decrypted
plaintext bytes, reliable-fragment sends, and exact reliable retransmitted wire
bytes. UDP endpoint/listener snapshots expose actual send, receive, and poll
calls, transient WouldBlock observations, and maximum observed datagram sizes.
These are cheap N0.10 counters; crypto/codec phase timing remains an opt-in
future diagnostic and is not charged to the normal hot path.

## N0.0–N0.3 gates

- production kernel has zero project dependencies;
- generational connection and canonical replication identities are fixed width;
- lane table is exhaustive and rejects unknown values;
- trace capacity failure is transactional and deterministic;
- warmed trace append/digest allocates zero managed bytes;
- impairment decisions are deterministic, bounded, and replayable;
- mismatched evidence profiles cannot report a headline ratio;
- repository architecture and documentation maps include the new kernel and tests.
- every fixed capacity and exhaustion boundary is explicit and transactional;
- connection, security, lane, packet-token, and slot reuse reject stale identity;
- packet ownership transfer and shutdown balance are verified;
- generated codec truncation boundaries, trailing bytes, boolean/enum canonicality,
  manifest ID/key collisions, varints, and UTF forms are adversarially tested;
- multiple kernel/connection instances remain isolated;
- admitted pool/ring/codec/framing/datagram profiles allocate zero after warmup.
- signed primitive and signed-enum codecs round-trip under checked overflow;
- protocol fingerprints include protocol version;
- cookies gate allocation and pinned-server key exchange;
- authentication rejects corruption before state publication;
- authenticated replay is independent from transport-delivery ACK state;
- header-associated-data, ciphertext, and tag tampering publish no protocol state;
- duplicated authentic ciphertext is replay-rejected even while reliable delivery is blocked;
- admitted unreliable Data does not elicit a transport ACK or create a false RTT
  sample, while mixed traffic and retransmitted reliable data still complete;
- replay, ACK, retransmission, RTT/RTO, congestion, sequencing, fragmentation,
  receive backpressure, and epoch rotation pass deterministic impairment tests;
- complete congestion-window and complete ACK-window loss make progress, including
  a configured one-packet congestion window;
- authenticated unreliable capacity drops advance their message-family stale floor,
  while unrelated unreliable families retain independent pending/sequence state;
- inbound flood is bounded independently and cannot prevent outbound Control/ACK work;
- normal warmed secure delivery allocates zero managed bytes.

The N0.3 recovery invariant is:

> No bounded resource exhaustion, complete packet-window loss, stale packet
> history, invalid generated value, sequence exhaustion, or malformed oracle input
> may leave protocol/projection state unable to make progress through its documented
> recovery action.

Run the focused gate:

```bash
dotnet run --project _Tests/UltraSim.Network.Tests -c Release
dotnet run --project _Tests/UltraSim.Network.Generator.Tests -c Release
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Network.KernelBenchmarks -c Release
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Network.ProtocolBenchmarks -c Release
```

N0.1 is implemented by [Game replication](../../Game/Replication/README.md).
N0.4 binds remote game authentication, Admission, CharacterSelect, Inventory,
and the existing UI semantics through ProtocolLoopback above this dependency-clean
kernel. Accepted N0.5 replaces only byte transport/session ownership with real
UDP while retaining the same application vertical and complete client-state oracle.
Accepted N0.6 then adds one bounded controlled simulation stream above the same
protocol for both deterministic loopback and UDP.
