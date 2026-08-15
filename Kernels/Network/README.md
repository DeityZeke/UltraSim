# UltraSim Network kernel

Status: N0.0 contracts, N0.1 game projection/OfflineDirect, N0.2 bounded kernel
foundations, and N0.3 secure reliable ProtocolLoopback plus adversarial acceptance
hardening are accepted. Remote
Admission/UI begins at N0.4; real transport remains N0.5.

This project is the game-, ECS-, UI-, client-, and server-independent mechanism
layer for Network V3. It owns fixed capacities, generational connection/session
state, packet ownership, producer-owned rings, canonical framing, generated
codecs/manifests, and deterministic datagrams. It does not open sockets,
authenticate accounts, capture game state, or invent an alternate authority.

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
| `PacketNumber` | Network | Authenticated datagram sequence; admitted data packet numbers are referenced separately by transport-delivery ACKs. |
| `LaneSequence` | Network | Reliable ordering is lane-scoped; N0.3 unreliable sequencing/coalescing is isolated per message family. Deeper game-defined sub-stream keys remain N0.6 work. |
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
second window contains only data packets admitted into bounded delivery/reassembly
machinery and alone drives ACK ranges. If reliable admission is blocked, the packet
is replay-protected but not ACKed; the sender retains the semantic fragment and
retransmits it under a fresh `PacketNumber`.

The bounded delivery machine provides:

- independent 64-packet authenticated-replay and transport-delivery windows;
- reliable ordered Control, Event, and Bulk retention with ACK-driven release;
- unreliable sequenced Input, OwnerState, and WorldState coalescing isolated per
  message family;
- disposable unordered delivery;
- MTU-bounded fragmentation/reassembly;
- deterministic step-clock RTT/RTO, congestion-window/backoff, and independent
  inbound/outbound pump budgets;
- authenticated fail-closed security-epoch advancement;
- zero warmed managed allocation for normal secure delivery.

The loopback cookie uses a fixed path token and protocol time uses deterministic
steps. N0.5 binds cookies to real endpoint/path identity and expiry and maps timing
to a real monotonic clock. N0.3 does not implement time-based packet pacing; real
pacing is N0.5 transport policy. N0.6 supplies game-specific input redundancy/expiry;
N0.7 supplies full recipient fairness and scheduling.

N0.3 remains single-owner. The N0.2 SPSC ring proves token transport and the packet
pool proves single-owner packet memory independently; cross-thread packet-memory
handoff is not claimed. N0.5 must admit per-owner pools or one transactional
ownership-transfer-plus-publication primitive before a socket worker touches buffers.
The N0.2 and N0.3 ownership implementations remain deliberately independent proofs
until N0.5 either integrates them or removes the redundant path.

Reliable-head, ACK-record, and deterministic-datagram selection still use bounded
linear scans. They are acceptable at admitted N0.3 capacities; N0.7/N0.10 scale
measurements decide whether another index is earned.

An N0.3 ACK is specifically a **transport-delivery ACK**: the remote protocol peer
authenticated the packet and admitted its reliable payload into bounded receive or
reassembly state. It does not prove that game logic executed, UI state rendered, a
baseline was applied, or recipient-known projection state advanced. Those stronger
proofs remain distinct application/baseline contracts in later slices.

## Lane contracts

The executable `NetworkLaneContracts` table freezes message meaning before
packetization exists:

| Lane | Delivery / order | Retention meaning |
| --- | --- | --- |
| Control | reliable ordered | protocol, session, baseline, and clock control state |
| Input | unreliable sequenced | expiring authoritative input |
| OwnerState | unreliable sequenced | latest owner state |
| WorldState | unreliable sequenced | latest observer state |
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
hardening; N0.4 must measure the complete application path before any further
protocol micro-optimization is considered.

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

There is still no accepted end-to-end V2/V3 Network ratio. N0.3 has a real secure
loopback protocol, but no reviewed V2 profile performs the same authenticated,
bounded lane semantics. The N0.3 standalone baseline is not mislabeled as a ratio.

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
N0.4 now binds remote game authentication, Admission, CharacterSelect,
Inventory, and the existing UI semantics through ProtocolLoopback above this
dependency-clean kernel. N0.5 replaces only the deterministic datagram topology
with real UDP/session ownership while retaining the same application vertical.
