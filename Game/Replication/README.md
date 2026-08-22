# Game replication

Status: Network N0.1 canonical projection/capture and OfflineDirect oracle accepted.
N0.3 secure reliable ProtocolLoopback is accepted below this layer; N0.4 remote
application binding is accepted beside it. N0.5 carries the same binding over
real connected localhost UDP. N0.6 adds bounded authoritative input, revisioned
clock anchors, and one complete owner-state stream. N0.7 maintained recipient
interest/application knowledge and N0.8 atomic baseline/recovery are accepted.
N0.9 composes controlled state, maintained WorldState, atomic recovery, and
multi-consumer retained application Events across OfflineDirect, secure
ProtocolLoopback, and connected localhost UDP. Corrective hardening adds exact
WorldState parity, reliable baseline cancellation, and client-slot ABA fencing.

Terrain T0 is also accepted/frozen. N1.0 adds a separate packet-free
`OfflineDirectChunkRecipient` over T0's `ChunkInterestQuery` and
`ChunkPublicationStore`. N1.1 adds canonical snapshot/delta/absence encoding and
application-proof fencing around that same oracle. N1.2 carries MTU-admissible
ordinary deltas/absences through real independent-unreliable transport with a
bounded delivery ledger and production client-cache application. N1.3 adds the
exceptional atomic Bulk snapshot transaction. N1.4's shared dirty-work scheduler,
exact stable-interest fast path and bounded mixed-cadence traversal are accepted
mechanisms. Compact recipient/reverse-index storage is stopped for checkpoint
review; current-game profile adoption remains open. None replaces N0 yet.

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

## N1.0 OfflineDirect chunk recipient

`OfflineDirectChunkRecipient` is the game-owned oracle for recipient-to-chunk
subscription/cursor state. It maintains one bounded canonical subscription set,
tier/phase/cadence policy, and one chunk-bound applied cursor per key. Interest
reconciliation is complete-or-fail. Publication reads must be committed before
the cursor advances, so cancellation and client-apply failure cannot falsely
publish application knowledge.

The recipient borrows authoritative semantic data from `ChunkPublicationStore`
and the tests apply it to the production `ClientWorldSemanticCache`. It owns no
Network connection, packet, crypto, watcher, renderer, or duplicate dirty history.
Permanent coverage includes stable/crossing topology, history fallback, epoch
replacement, capacity transactionality, migration reorder, canonical client
equivalence, and warmed zero allocation.

The first packet-free evidence observes 503 subscriptions for the accepted
5/96/192 m profile and 72 changes for one chunk crossing. At 256 recipients,
stable/crossing reconciliation measured 42.74/43.50 us p95 per recipient. A broad
1,024-subscription capacity currently reserves 187,904 B per recipient; this is
explicit N1 layout/capacity evidence, not a production CCU or N0 replacement
claim. N1.1 now wraps this oracle with canonical messages and hostile wire codecs.

## N1.1 canonical chunk wire preparation

`OfflineDirectChunkWireEncoder` is an owner-bound packet-free adapter from one
borrowed publication read to the canonical Game/Protocol chunk schema. It owns no
socket, peer, crypto, watcher, or authority. Encoding returns a trusted delivery
ticket and releases publication scratch without changing the recipient's applied
cursor. A matching authenticated `ChunkApplied` proof is the only remote path that
can commit that ticket; transport send/ACK is insufficient.

Snapshot/delta codecs are bounded at 64 rows, fixed-endian, hash-checked,
canonically ordered, overlap-safe, and transactional on hostile input. The first
applied `ChunkAbsent` establishes empty knowledge. Later cadence probes that are
still absent are committed locally as no-change observations and produce no
repeated empty wire payload.

## N1.2 ordinary independent-unreliable delivery

`RemoteChunkWorldStateServer` is the connection-owned transport adapter around
one `OfflineDirectChunkRecipient`. It retains a fixed ledger of trusted delivery
tickets; queueing bytes does not advance a cursor. Exact applied proof commits
knowledge, while chunk leave and publication-source replacement invalidate old
delivery lineage. `RemoteChunkWorldStateClient` decodes into bounded scratch,
atomically applies to `ClientWorldSemanticCache`, and only then emits the proof.
Loss of either data or proof therefore causes redundant current-state work, never
false application knowledge.

The nonfragmenting WorldState lane admits 1,405 message bytes under the default
1,472-byte UDP profile. The fixed N1.1 delta layout therefore carries at most 30
rows in one ordinary datagram. Larger valid semantic deltas return a named
`PayloadTooLarge` status and retain the old applied cursor; they are never split
into independently applicable subsets.

## N1.3 atomic snapshot recovery

`RemoteChunkWorldStateServer` wraps the frozen canonical snapshot body in one
bounded reliable-Bulk Begin/segment/Commit transaction. Publication history is
released immediately after capture and is never pinned for a slow connection.
Timeout, leave, source replacement, and same-key retry retain reliable Cancel and
invalidate the old proof ticket without changing recipient knowledge.

`RemoteChunkWorldStateClient` stages all bytes privately, validates the complete
canonical body, and publishes once through `ClientWorldSemanticCache`. Delivery-ID
lineage spans snapshot/delta/absence application for each resident key, fencing a
delayed absence after a newer snapshot and a delayed snapshot after newer absence.
The maximum 64-row body is 2,634 bytes and uses two 1,375-byte Bulk payload
segments under the default transport profile. Ordinary WorldState remains on its
independent-unreliable lane and is not ordered behind Bulk recovery.

## N1.4 shared dirty-work discovery

`ChunkPublicationWorkScheduler` is the concrete current-game candidate for
`Incoming / Current+dirty / Leaving` discovery. It consumes accepted recipient
topology changes and the canonical affected-chunk span calculated once by the
publication commit. Its bounded reverse index fans one dirty chunk to current
subscribers; clean current memberships do not query publication. A missed commit
observation or publication-source replacement fails closed to current-truth
refresh. The scheduler owns no transport, proof, encoding, or dirty history.

The recipient's exact interest query now returns a conservative movement budget.
Cumulative anchor movement inside it cannot cross any current enter/leave
threshold, so repeated ordinary movement avoids rebuilding the complete chunk
set. Exceeding the budget executes the unchanged exact query.

Remote delivery accepts scheduler work by `SpatialChunkKey`; sorted recipient
array positions remain private ephemeral storage. The work then uses the accepted
N1.1-N1.3 codec, lane, delivery-ticket, snapshot, and proof machinery unchanged.

This is an implementation checkpoint, not N0 replacement. The first 256-recipient
semantic census makes shared clean/dirty scheduling cheap, but the broad recipient
plus reverse-index capacities reserve about 95.8 MB after cadence-pass accounting,
and real chunk crossings still have a visible tail. A follow-up dirty-density
diagnostic found and removed repeated cadence-deferred prefix scans using one
bounded pass marker over the existing array. At 1,024 recipients, broad declared
capacities initially reserved about 387.6 MB. Compact 32-byte retained subscription
rows, 40-byte scheduler memberships, 24-byte chunk rows, and two fixed 4-byte-slot
indexes reduce the repeated crossover to about 221.8 MB. A 640-slot sensitivity
case reports about 147.9 MB without freezing 640 as a production maximum. The
roughly 100-us synchronized crossing tail and remaining Dense-400/end-to-end matrix
must still decide current-game profile adoption before N1.5.

The accepted compact layout is now used by the first remaining-adoption matrix.
`OfflineDirectChunkSubscriptionView` is a non-escaping `ref struct` live view, and
recipient crossing attribution remains dormant unless explicitly enabled. The
fixed-runtime observations report 1.272 ms p95 for Dense-400-shaped server
semantic work, 2.386 ms p95 for a separate 64-recipient secure deterministic
protocol replay, and 29.082 ms to recover 30 populated 64-row chunks through the
single private snapshot candidate. The latter does not earn concurrent candidates.

Exact candidate geometry now uses bounded anchor-relative 64-bit distance math,
and exact no-change reconciliations preserve the current subscription bank. These
are implementation optimizations only; the accepted geometry, topology,
cursor/history, and leaves-before-enters contracts are unchanged. Sustained
distributed movement still measures 6.553 ms p95 and synchronized transitions
remain expensive, so current-game N0-to-N1 adoption is still open pending
mixed-private and sharded/socket evidence. N1.5 and 2K/4K remain unstarted.

The follow-up representative run disperses recipients across independent world
regions and includes 152 boundary plus 116 corner crossings. It reports 5.975 ms
p95 total topology work. A staggered 400-teleport run reports 7.496 ms p95 without
turning the synchronized-jump controls into a production claim.

Mixed deterministic replay combines public chunk deltas, recipient-specific owner
state, retained Events, Control action headers, and both application-proof streams.
All 3,840 values per stream converge with zero disclosure mismatches and 3.364 ms
p95 total work. A separate 64-recipient real shared-UDP-listener run reports
1.578 ms p95 and zero warmed allocation. True external sharded clients remain a
hosted-Network lifecycle gate because the accepted shared host deliberately owns
only controlled in-process clients; Replication does not weaken that boundary.

`ParticipantId` remains game/session identity. Account-backed Admission sessions
rebind through durable character identity after publication. Accountless sessions
are explicitly invalidated. Projection bindings are refreshed from that Admission
authority; they do not retain stale ECS handles as authorization.

## Capture contract

The network-enabled game profile reserves ECS lifecycle history, the canonical
main structural merge arena, exact changed-row observation, and a bounded input
RangeJob. The local profile reserves no replication observation/ingress machinery;
it now retains the lifecycle history required by the independent T0.1 Spatial
consumer.

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

Application Event ordering remains one projection-global sequence and one bounded
global ring. Delivery lookup is participant-local: each retained slot links only
to the next Event for the same participant, while a fixed participant→consumer
map resolves the connection-owned cursor in O(1) expected time. A consumer with
no relevant Event advances directly to the current global publication floor;
it does not reread or count unrelated payloads. Retention minimum tracking keeps
the current minimum and its tie count incrementally, rescanning all consumers
only when the last consumer at that minimum advances. This is an acceleration of
the existing global Event contract, not a per-participant ordering identity.

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

Global projection capture is performed once after each complete authoritative
step. `CaptureAndQueueOwnerStates` fans the immutable frame/clock result to every
owner adapter; invoking one global capture independently per connection is not an
admitted multi-owner composition. The one-owner convenience method remains for
focused fixtures.

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

Pending latest-state is one delivery obligation, not a new scheduling object for
every canonical write. A same-lifetime/presence/tier update coalesces an unsent
payload in place, preserving its relation sequence, pending age, phased
eligibility deadline, and intrusive-list position. Once a sequence has been put
in a bundle—or captured into a baseline cutoff—a superseding value receives a
new relation sequence so old proof cannot clear it, but retains the next cadence
opportunity earned by the prior send. Near/Far eligibility advances only when a
version is actually sent; same-tier coalescing must never move it later.
Presence, tier, and lifetime changes continue through their explicit
retier/tombstone/generation paths. Production snapshots separate new
obligations, unsent coalesces, exposed-version supersessions, sequence
allocations, relinks, preserved/advanced deadlines, and any illegal same-tier
deadline deferral. Permanent tests cover skipped phase boundaries, continuous
Near/Far dirtiness, old/lost/duplicate proof, baseline cutoffs, transitions,
sequence exhaustion, and warmed zero allocation.

`RemoteWorldStateServer` and `RemoteWorldStateClient` add one explicitly cold
baseline transaction. The server copies a sorted complete present scope at a
cutoff. The client admits its descriptor, stages reliable Bulk segments into a
separate fixed candidate, validates exact count/schema/hash/epoch, and swaps the
whole scope once. Ordinary bundles are rejected while staging and remain server
debt; changes after cutoff cannot be erased by baseline acknowledgement.
The descriptor's relation-sequence floor makes omission authoritative during
same-epoch repair: delayed pre-cutoff present rows cannot resurrect an entity
absent from the committed complete scope.

The server-side transaction has a bounded monotonic deadline. An application
peer that authenticates and receives reliable baseline data but never returns a
terminal apply result cannot suppress ordinary WorldState indefinitely. Timeout
or explicit abandonment queues a reliable cancel for the matching client
candidate, clears only retained transaction state, never advances recipient
knowledge, and repeated stalls make the recipient explicitly
nonviable. Applying a complete scope also consumes omitted tombstone debt through
the captured relation floor while preserving every newer write.

Begin preflights both known-identity and active dense-slot capacity before Bulk.
Baseline compaction preserves all historical local-slot generations and rebuilds
the free list; a later regrow cannot recreate an old `ClientEntitySlot` handle.

The descriptor's `EventFloor` is structural N0.8 state. N0.9 supplies the first
real reliable application Event path and its post-floor/fresh-client proof.
Each `RemoteApplicationEventServer` is bound to one participant and advances an
independent projection-owned consumer cursor. Only the minimum cursor across
registered consumers releases global history. `RemoteApplicationEventClient`
accepts a monotonically increasing participant-filtered subsequence into
reclaimable bounded working storage. Gaps are trusted proof that the server
filtered other participants' facts; no cross-participant payload crosses the
connection. Semantic duplicates are idempotently acknowledged. One pending ACK
survives temporary Control backpressure, and the normal client owner pump retries
it through `IRemoteGameProtocolProgressor`. Baseline cancellation/supersession
retires only the matching retained ACK so a prior transaction cannot poison its
successor. Missing application proof has a bounded retry and terminal policy.
Same-epoch relation and event floors are
monotonic, and generation replacement requires a strictly increasing
`StateEpoch`.

N0.10 first exposed the cost of the deliberately simple filtered global ledger,
then earned and installed the participant-local acceleration above. Diagnostics
retain examined/skipped records, O(1) consumer lookup attempts, participant-index
hits, no-relevant fast-forwards, minimum fast-path updates, full rescans, scan
entries, consumer count, and retained-event high water. Permanent gates cover
interleaving, a lagging consumer pinning retention, tied minima, no-relevant
fast-forward, unregister/reregister, physical ring-slot reuse, generation
replacement, capacity rejection, duplicate retry, and warmed zero allocation.

The subsequent Dense-400 WorldState census keeps production instrumentation
dormant by default. When explicitly enabled, `GameInterestService` attributes
canonical relation/reverse-edge/pending visits, per-tier scheduling scans and
skip causes, bundle fullness/stop causes, and application-ACK work;
`RemoteWorldStateServer` attributes final record projection, encoding, queueing,
and ACK handling. Bundle selection now carries one local cursor per pending tier
for the lifetime of a build, so a 24-record bundle never restarts at the list
head and rediscovers its own selected rows. The weighted tier policy and every
semantic/application-knowledge contract remain unchanged. A permanent gate
proves one full 16-record Immediate bundle examines and selects each relation
exactly once.

WorldState application proof is now explicitly reconstructible rather than a
reliable Control fact. Message 110/version 2 travels as one bounded independent-
unreliable WorldState container. Loss cannot advance server knowledge; ordinary
relation retry causes idempotent duplicate client application and regenerates
the same per-bundle result. State/application epochs and the outstanding-bundle
ledger fence stale proof. Reliable baseline, Event, UI, and other Control facts
are unchanged. Permanent loss/duplication and warmed-allocation gates protect
this distinction. An opt-in client census records proof production at existing
flush boundaries; it demonstrated that natural reliable batching was too sparse
to earn a batch wire format.

Healthy same-lifetime, same-region `Far→Far` entity updates now use one maintained-
relation pass. Region-edge creation/retier/removal already guarantees the complete
Far relation set, so the pass can preserve exact Immediate hysteresis and publish
the newest canonical version without reverse discovery or a second pending walk.
All creation, lifetime, region, horizon-change, and nonviable shapes retain the
general path. Opt-in counters prove fast-path eligibility, necessary visits,
avoided work, tier transitions, and version publications; boundary and warmed-
allocation gates protect the invariant.

Outstanding-bundle expiration remains a global fixed-array sweep, but it now
runs at most once per exact semantic `nowMilliseconds`. After a complete sweep
at T, any bundle created later at T has `SentAtMilliseconds == T` and cannot
already be timed out; acknowledgements and abandon operations can only remove
bundles. Any different time—including a backward test-time jump—performs the
normal sweep. Opt-in attribution reports expiration requests, consecutive
distinct/repeated request times, actual sweeps, same-time cache hits, total and
active slots examined, expirations, `_bundleHigh` high-water, and sweep ticks.
Permanent gates cover multiple recipients, ACK/abandon between same-time calls,
the exact timeout boundary restoring retry eligibility, forward/backward time
jumps, and zero warmed allocation.

Pending-selection attribution is also opt-in and dormant on the production fast
path. It separates immediately selectable, cadence-deferred, retry-deferred and
already-selected classification time; total list traversal; tier arbitration;
record materialization/commit; all-deferred `NoData` calls; and logarithmic
remaining-delay histograms. Dense-400 found 3.92 million known-deferred checks
and 44,430 all-deferred `NoData` builds. A service-private 256-bucket
ready/deferred wheel removed those scans but regressed the normal integrated
workload, so it was rejected and no deferred-wheel state remains in relations.
Counts alone do not justify enlarging or churning the hot maintained graph.

The later adversarial Dense-400 audit earned a smaller sidecar: each recipient
stores only the earliest effective ready time for its three existing pending
tiers plus dirty bits. Enqueue, requeue, send, rejection, abandon, ACK cleanup,
and timeout invalidate the affected tier. A clean future deadline makes repeated
`NoData` builds O(1); a dirty tier recomputes from the authoritative intrusive
list, so the cache cannot suppress newly ready work. A permanent regression proves
100 repeated same-time no-data builds examine no additional relations. In one
sequential same-machine WorldState-only observation, p95 moved 13.669→12.650 ms
and adapter build time 459.8→315.2 ms across the run. Deadline recomputation
examined 4.70 million relations in 46.5 ms, while ordinary pending traversal fell
271.9→127.0 ms. Semantics, 53,200 bundles,
40.32 MB semantic bytes, 50.11 MB wire bytes, debt, and freshness were unchanged.
This is retained as a measured bounded specialization, not a dense acceptance
claim or a replacement for the rejected wheel. The compact control/candidate
evidence is preserved in
[`network-adversarial-hardening-2026-08-19.json`](../../_Benchmarks/Baselines/network-adversarial-hardening-2026-08-19.json).

Authoritative ingress similarly stages each participant's last accepted sequence
in a pre-sized scratch dictionary. Batch validation is now linear in admitted
commands instead of scanning every earlier accepted command; generation preflight
and transactional Runtime commit remain unchanged.

`RemoteGameProtocolComposition` is the small seam earned by N0.9's simultaneous
controlled, WorldState, and Event consumers. It owns no handlers and introduces
no routing policy; game/client composition supplies a fixed bounded ordered set.

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
