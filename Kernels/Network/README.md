# UltraSim Network kernel

Status: N0.0 contracts, N0.1 game projection/OfflineDirect, and N0.2 bounded
kernel foundations are accepted. Secure delivery/reliability begins at N0.3;
real transport remains later.

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
| `PacketNumber` | Network | Datagram acknowledgement/replay sequence domain. |
| `LaneSequence` | Network | Per-connection delivery-lane sequence domain. |
| `StateEpoch` | Projection/protocol seam | One coherent canonical projection generation. |
| `ReplicationEntityId` | Canonical projection | Dense generational identity shared by all recipients within one `StateEpoch`. |
| `ParticipantId` | Game contracts | Server-created game/session participant; not defined by this kernel. |
| `PersistentEntityKey` | ECS persistence | Durable world plus entity identity; never replaced by a network ID. |
| client entity slot | Client | Private projection storage identity; not a wire contract. |

N0.0 deliberately does not implement a connection-relative `WireEntityAlias`.
If bandwidth measurement later earns one, it is only an encoding of
`ReplicationEntityId`; it cannot redefine canonical replicated identity.

## N0.2 bounded kernel

`NetworkKernelLimits` owns the complete initial reservation profile. One isolated
`NetworkKernel` binds a collision-checked protocol manifest to:

- a fixed generational `NetworkConnectionTable` with explicit state transitions,
  security epochs, and per-lane sequences;
- a fixed packet/slab pool whose `PacketBufferToken` proves slot generation and
  current stage ownership;
- bounded producer-owned ingress/egress rings;
- strict canonical big-endian readers, writers, and 32-byte packet framing;
- a deterministic fixed-capacity datagram pair for protocol work before sockets.

Capacity exhaustion returns before mutation. Stale, foreign, double-returned,
wrong-owner, or post-reuse packet tokens cannot access memory. Shutdown requires
all connections released, rings drained, and packet tokens returned.

`NetworkCodecGenerator` emits exact fixed-width measure/write/read codecs and an
immutable manifest from `[NetworkProtocol]` and `[NetworkMessage]`. Generated
read/write operations stage their cursor and publish it only after the complete
message succeeds. Primitive integer representation is canonical; booleans accept
only `0`/`1`; generated enums accept only declared values; exact readers reject
trailing bytes. The lower-level reader additionally rejects non-minimal varints,
malformed UTF-8, unpaired UTF-16 during encoding, and caller limits before exposing
text bytes. Dynamic text/object allocation is not part of the admitted N0.2 hot
codec profile.

`DeterministicDatagramLink` is an in-memory datagram endpoint, not a protocol. It
applies stateless loss/duplication/corruption/delay decisions to fixed slots and
makes capacity failure transactional. N0.3 runs production delivery/security
semantics across this endpoint.

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

N0.2 admits warmed pool rent/transfer/return, ring enqueue/dequeue, fixed generated
codec/framing, connection reuse, and deterministic datagram send/receive as zero
managed-allocation profiles. Security, reliability, fragmentation, scheduling,
and socket submission must earn their own promises later.

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

There is no honest end-to-end V2/V3 Network ratio at N0.0 because V3 does not yet
have a protocol. Primitive codec, loopback protocol, secure delivery, and real
application comparisons become eligible only when the corresponding N0 checkpoint
exists under an equivalent evidence profile.

## N0.0–N0.2 gates

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

Run the focused gate:

```bash
dotnet run --project _Tests/UltraSim.Network.Tests -c Release
dotnet run --project _Tests/UltraSim.Network.Generator.Tests -c Release
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Benchmarks/UltraSim.Network.KernelBenchmarks -c Release
```

N0.1 is implemented by [Game replication](../../Game/Replication/README.md).
N0.3 is next: authenticated secure protocol, replay/ACK/reliability, lane delivery,
fragmentation, congestion/pacing foundations, and impairment validation over the
deterministic datagram endpoint.
