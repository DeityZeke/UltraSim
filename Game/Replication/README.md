# Game replication

Status: Network N0.1 canonical projection/capture and OfflineDirect oracle accepted.
N0.3 secure reliable ProtocolLoopback is accepted below this layer; N0.4 remote
application binding is accepted beside it. N0.5 carries the same binding over
real connected localhost UDP. N0.6 adds bounded authoritative input, revisioned
clock anchors, and one complete owner-state stream. N0.7 maintained recipient
interest/application knowledge and N0.8 atomic baseline/recovery are accepted.

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

## N0.6 controlled simulation vertical

`RemoteControlledSimulationServer` translates one authenticated participant's
bounded input bundles into the existing game-owned authoritative ingress. It
validates application epoch, projection epoch, strictly increasing input sequence,
target-tick bounds, and authoritative-world-time expiry before publication. A
processed-input sequence advances for every semantically handled sample, including
intentional rejection, so a client can retire expired or capacity-rejected intent
without confusing processing with authoritative correction.

The client repeats up to three newest unprocessed samples. This is bounded
redundancy, not reliable event delivery: intermediate facts that must occur use a
reliable application Event contract. Every remote sample is complete
`InputVelocity` state. Correctness must hold if every older unprocessed sample is
skipped and only the newest sample survives; additive commands are therefore not
part of the remote Input protocol. The internal authoritative ingress may still
use non-coalescible commands from an appropriately reliable/local producer.

After each complete Runtime step, the server captures canonical projection and
queues one complete superseding owner-state sample. The sample uses the
connection-independent `ReplicationEntityId`; the client maps that to its private
dense generational `ClientEntitySlot`. N0.6 owns exactly one local slot at index
zero rather than indexing client arrays by canonical replication index. A first-
seen or repeated tombstone is complete latest state: it records the canonical
generation floor, advances frame/input acknowledgement, and prevents a delayed
same-generation present sample from resurrecting the lifetime. A newer canonical
lifetime advances the independent client-local generation.

Cadence/time interpretation is separate reliable Control state. A clock anchor
declares `ClockRevision`, effective simulation tick, exact frequency, and fixed-
point authoritative time. Owner state names its revision and is staged if that
anchor has not arrived yet. The permanent cadence test proves 60→30→60 changes at
the declared tick without changing the meaning of scheduler `PeriodTicks`.

The focused gates cover loss/duplication/reordering, lost-stop recovery, stale,
duplicate, expired and future input, independent processing acknowledgement and
correction, safe omission of older coalesced control samples, prior-epoch state,
first-seen/repeated tombstones, entity retirement/reuse, Control/OwnerState
reordering, exact ProtocolLoopback/UDP authority and client parity, and warmed
zero caller allocation. N0.6 deliberately has one participant, one controlled
entity, and one complete owner-state family. Per-recipient AOI, observer/world
state, pending newest versions, and fanout belong to N0.7.

`InputVelocity` in this diagnostic vertical is persisted authoritative physical
state. Input expiry is an arrival/admission deadline, not a continuing control
lease, and application disconnect does not implicitly zero the value. Before this
field becomes real player control desire—or before N0.8 saves live controlled
characters—the game must explicitly choose and test disconnect/lease
neutralization. Network lifetime must not invent that gameplay policy.

Adding `InputVelocity` advanced persistent `GameSimulationState` to schema version
2. The game persistence catalog includes a direct v1 tick-marker reader that
defaults velocity to zero, and a permanent restore test proves accepted pre-N0.6
saves remain compatible rather than silently reinterpreting the wider payload.

Accepted paired evidence for one complete input→authority tick→projection→client
reconciliation cycle is preserved in
[`network-n0.6-controlled-input-owner-state-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.6-controlled-input-owner-state-2026-08-14.json).
The semantic-hardening rerun is preserved separately rather than rewriting that
historical artifact:
[`network-n0.6-controlled-input-owner-state-semantic-hardened-2026-08-14.json`](../../_Benchmarks/Baselines/network-n0.6-controlled-input-owner-state-semantic-hardened-2026-08-14.json).

## N0.7 maintained interest and N0.8 baseline recovery

`GameInterestService` owns bounded regions, recipient/entity relations, newest
unsatisfied relation state, fairness, and application knowledge. WorldState uses
independent unreliable bundles: Game/Replication coalesces per relation, while
Network merely carries arbitrary bounded relation subsets. Multiple bundles may
be in flight; no application-RTT stop-and-wait exists.

`RemoteWorldStateServer` and `RemoteWorldStateClient` add one explicitly cold
baseline transaction. The server copies a sorted complete present scope at a
cutoff. The client admits its descriptor, stages reliable Bulk segments into a
separate fixed candidate, validates exact count/schema/hash/epoch, and swaps the
whole scope once. Ordinary bundles are rejected while staging and remain server
debt; changes after cutoff cannot be erased by baseline acknowledgement.
The descriptor's relation-sequence floor makes omission authoritative during
same-epoch repair: delayed pre-cutoff present rows cannot resurrect an entity
absent from the committed complete scope.

Generation publication replaces the adapter's game-owned interest graph and
`StateEpoch`, not the connection or persistent identity. Delayed prior-epoch
state is fenced. The accepted persistence test restores ECS, clock, Accounting,
Admission and Inventory authority first, then reconstructs Network projection
from that published authority. Network state itself is never persisted.

Run the focused application/recovery gate and observation with:

```bash
dotnet run --project _Tests/UltraSim.Network.Application.Tests -c Release
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
  dotnet run --project _Tests/UltraSim.Network.Application.Tests -c Release -- \
    --benchmark-n08 --output _Benchmarks/results/network-n0.8-local-run.json
```
