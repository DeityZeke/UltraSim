# UltraSim Network kernel

Status: N0.0 contract and evidence foundation accepted. Packet framing, pools,
codecs, security, reliability, transport, and game replication remain subsequent
N0 checkpoints.

This project is the game-, ECS-, UI-, client-, and server-independent mechanism
layer for Network V3. It currently contains only the executable contracts that
later protocol work must share. It does not open sockets, authenticate accounts,
capture game state, or invent an alternate authoritative runtime.

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

`Kernels/Network` has no project references. The repository verifier freezes that
boundary. Network moves and schedules information; it does not own game policy,
accounting, Admission, persistence, UI meaning, or simulation authority.

## Identity domains

Identity is explicit and non-interchangeable:

| Identity | Owner | Lifetime / meaning |
| --- | --- | --- |
| `ConnectionId` | Network | Server-created transport slot plus reuse generation; neither durable nor client-chosen. |
| `StateEpoch` | Projection/protocol seam | One coherent canonical projection generation. |
| `ReplicationEntityId` | Canonical projection | Dense generational identity shared by all recipients within one `StateEpoch`. |
| `ParticipantId` | Game contracts | Server-created game/session participant; not defined by this kernel. |
| `PersistentEntityKey` | ECS persistence | Durable world plus entity identity; never replaced by a network ID. |
| client entity slot | Client | Private projection storage identity; not a wire contract. |

N0.0 deliberately does not implement a connection-relative `WireEntityAlias`.
If bandwidth measurement later earns one, it is only an encoding of
`ReplicationEntityId`; it cannot redefine canonical replicated identity.

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
topologies. It is not yet a datagram endpoint.

The N0.0 allocation profile is explicit:

| Profile | Operations | Contract |
| --- | --- | --- |
| Hot/warmed | identity value operations, lane lookup, trace append/digest, impairment decision | zero managed allocation |
| Cold/setup | trace capacity construction, evidence-profile strings/results, test/report setup | managed allocation allowed and reported separately |

Packet pools, codec framing, protocol scheduling, and transport submission do not
inherit a hot-path promise merely because N0.0 primitives have one; each later
checkpoint must admit and measure its own protected region.

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

## N0.0 gates

- production kernel has zero project dependencies;
- generational connection and canonical replication identities are fixed width;
- lane table is exhaustive and rejects unknown values;
- trace capacity failure is transactional and deterministic;
- warmed trace append/digest allocates zero managed bytes;
- impairment decisions are deterministic, bounded, and replayable;
- mismatched evidence profiles cannot report a headline ratio;
- repository architecture and documentation maps include the new kernel and tests.

Run the focused gate:

```bash
dotnet run --project _Tests/UltraSim.Network.Tests -c Release
```

N0.1 is next: a game-owned canonical projection/capture service and OfflineDirect
semantic oracle over the already accepted `ServerRuntime` and ECS capture boundary.
