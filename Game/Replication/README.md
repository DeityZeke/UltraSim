# Game replication

Status: Network N0.1 canonical projection/capture and OfflineDirect oracle accepted.
N0.3 secure reliable ProtocolLoopback is accepted below this layer; N0.4 remote
application binding is accepted beside it. Real transport remains N0.5 and
recipient scheduling remains N0.7 work.

Same-generation projection repair and authoritative generation replacement are
different boundaries. Replacement creates a new `StateEpoch`, resets canonical
replication identity, and invalidates old generation-local application facts.
Lifecycle-history repair retains the existing `StateEpoch`, increments an explicit
baseline revision, preserves surviving replication identities/generations, advances
identity generations for missed slot reuse, and retains independently acknowledged
application events. A state baseline can repair projection lifecycle; it cannot
silently discard or recreate a non-coalescible application event.

Participant capacity is non-poisoning. An excess Admission session is excluded
from the bounded projection and can be rejected by the remote admission
boundary; it does not freeze capture for already admitted participants.

`Game/Replication` is the game-owned seam between committed authoritative state
and Network delivery. It depends on the concrete game composition and Runtime;
`Kernels/Network` remains independent of ECS, Game, Server, UI, and Client.

## Ownership

```text
complete ServerRuntime step
        ↓
ECS changed-value + lifecycle cursors
        ↓
GameProjectionService
    canonical connection-independent entity slots
    participant bindings
    recoverable projection lifecycle
    non-coalescible application events
        ↓
OfflineDirectGameProjection (N0.1 oracle)
        ↓
future interest / protocol / client projection
```

The service never exposes an `Entity` in its output. Runtime entities are used
only while borrowing one callback-scoped generation. Published identity is a
`ReplicationEntityId`, stable for every recipient within one `StateEpoch`.
Generation replacement creates a new epoch and rebuilds canonical truth.

`ParticipantId` remains game/session identity. Account-backed Admission sessions
rebind through durable character identity after publication. Accountless sessions
are explicitly invalidated. Projection bindings are refreshed from that Admission
authority; they do not retain stale ECS handles as authorization.

## Capture contract

The network-enabled game profile reserves ECS lifecycle history, the canonical
main structural merge arena, exact changed-row observation, and a bounded input
RangeJob. The local profile reserves none of the replication/lifecycle machinery.

A capture reads complete committed ECS state only after a Runtime step. It stages
all lifecycle and changed-value work, then publishes canonical slots and advances
cursors together. Before commit:

- capture-interval scratch/delta overflow returns `CaptureResyncRequired`, leaves
  cursors unacknowledged, marks the projection rebuild-required, and can make
  progress through an explicit baseline rebuild;
- terminal configured/identity capacity exhaustion returns `CapacityExceeded`
  without partial publication;
- lost ECS lifecycle history returns `LifecycleResyncRequired` and requires a
  baseline;
- frame, delta, lifecycle, and overwrite sequence domains are preflighted before
  canonical slots or histories publish;
- an expected rebuild never escapes the trusted Runtime callback;
- an unexpected derived-state exception is caught inside the callback and faults
  the projection, not authoritative Runtime.

Every committed create, destroy, add, and remove remains ordered lifecycle
evidence. Values may coalesce. A rapid remove/add or create/destroy between
captures therefore retains both topology facts while canonical state folds to the
final committed state.

Projection lifecycle uses an overwriting fixed ring because a scoped baseline can
recover it. Explicit application events use a non-overwriting fixed ring because
their intermediate occurrence may matter. Full application-event retention
rejects without mutation until a consumer-safe prefix is discarded.

## OfflineDirect

`OfflineDirectGameProjection` is the packet-free semantic oracle. Baselines and
frames stage into fixed private arrays and publish only after the supplied frame
exactly equals the source's current frame and staged entity/participant state
exactly equals current canonical truth. It also validates epoch, sequence, and
capacity. It receives no ECS
world or runtime entity authority. Its canonical hash and `NetworkSemanticTrace`
are the reference meaning later ProtocolLoopback and UDP paths must reproduce.

## Authoritative ingress

Preselected external commands enter a fixed game-owned arena and execute through
one serial `RangeJob` in the Input stage. Admission resolves `ParticipantId` to
the current controlled entity before publication. Duplicate/stale client
sequences reject. Value work uses prepared lookups; add/remove/retire operations
use the scheduler structural writer and become visible at the post-graph barrier.

This is not a public untrusted wire decoder. Later Network layers authenticate,
bound, decode, and translate input before calling this game seam.

## N0.1 evidence

The focused executable proves:

- local/network capacity separation;
- canonical baseline and OfflineDirect atomic publication;
- independent exact changed consumers;
- bounded input and duplicate-sequence rejection;
- same-tick post-graph add/remove/destroy capture;
- rapid membership folding and ordered lifecycle history;
- recoverable interval overflow, cursor non-acknowledgement, and explicit baseline repair;
- sequence-domain exhaustion preflight before any projection publication;
- checked-build-safe scratch-generation rollover that clears stale working-set
  stamps before reusing generation 1;
- future history cursors return `FutureSequence` rather than throwing;
- lifecycle overflow/resync and explicit rebuild;
- same-generation repair preserves `StateEpoch`, retained application events,
  baseline sequencing, and canonical identity reuse generations;
- excess participant capacity rejects that participant without poisoning capture;
- derived projection fault containment;
- bounded independent application events;
- atomic malicious frame/state-payload rejection against exact canonical truth;
- accountless invalidation and durable account-backed rebind across generation
  replacement;
- deterministic canonical hash/semantic trace;
- zero warmed caller-thread allocation for tick + capture + OfflineDirect apply.

Projection `ReservedBytes` includes fixed-array payload plus the reserved participant
map bucket/entry payload. It excludes CLR object/array headers and is not a total
managed-process-memory claim.

It also exposed and fixed a scheduler regression in which a second-tick typed
structural `Add<T>` copied payload before opening the canonical command-buffer
epoch. The ECS scheduler suite now owns a direct cross-tick regression test.

The accepted 100K profile records 100K changed rows per step. On the acceptance
machine, canonical capture averaged 5.84 ms (58.4 ns/entity), OfflineDirect apply
averaged 2.12 ms (21.2 ns/entity), and timed caller allocation was zero. The
benchmark's 91.2 MB OfflineDirect reservation deliberately retains the semantic
trace for every warmup/sample frame; it is evidence capacity, not a recommended
per-client production profile. See the immutable
[N0.1 artifact](../../_Benchmarks/Baselines/network-n0.1-game-projection-100k-2026-08-14.json).

Run:

```bash
dotnet run --project _Tests/UltraSim.Game.Replication.Tests -c Release
dotnet run --project _Benchmarks/UltraSim.Game.ReplicationBenchmarks -c Release
```

N0.4 also proves the separate application surface: a secure peer authenticates
provider credentials into `TrustedIdentity`, then existing Accounting, Admission,
CharacterSelect, and Inventory workflows run through `RemoteUiSemanticPort` and
the remote client bridge. Network never resolves an ECS entity or owns game
policy. OfflineDirect and ProtocolLoopback produce the same authority, normalized
UI trace, and eventual client model for Play, Split (`Update + Add`), Equip
(`Remove`), cold close, and exact hot reopen. Loss, duplication, corruption,
reordering, unauthenticated traffic, duplicated actions, post-commit presentation
failure, disconnect, and reconnect are permanent gates.
