# Network V3: N1 chunk-native world replication plan

Status: active forward plan. Network N0 remains accepted/frozen as the current
remote-authority implementation/reference. Terrain / World T0 is accepted/frozen
as of 2026-08-21. N1.0's packet-free `OfflineDirectChunkRecipient` is accepted.
N1.1 canonical chunk messages/proof semantics and N1.2 independent-unreliable
ordinary delivery are accepted. N1.3 atomic snapshot recovery is implemented and
accepted. N1.4 dirty-driven work discovery is accepted at `682fcdcd`; mixed-cadence,
dirty-density, roaming, and the first 512/768/1024 semantic crossover are now
captured. N1.4 recipient/scheduler storage compaction is implemented and stopped
for review. The current-game AOI adoption decision remains open on transition
tails and the remaining integrated matrix.

The completed N0 implementation and its checkpoint history remain in git and in
immutable `_Benchmarks/Baselines` evidence. This file records the active N1 plan,
the implemented N1.0-N1.4 stops, and only forward decisions with a concrete owner.

N0 proved the difficult semantic foundations:

- authority remains in `ServerRuntime` / game composition;
- secure ProtocolLoopback and real UDP carry the same application semantics;
- authenticated Admission/UI, bounded input, owner state, observer WorldState,
  retained Events, reconnect, baseline recovery, and generation replacement are
  accepted;
- latest state coalesces while Events retain facts;
- sent state is not application-known state;
- stale/delayed state is fenced by epoch/lifetime/relation/baseline floors;
- bounded baseline compaction reclaims long-lived client identity knowledge;
- shared-listener native batching and opt-in unreliable frame aggregation are
  retained measured mechanisms;
- Dense-400 reached the accepted <=6 ms server-role usability target in sustained
  evidence;
- sparse V3 remains materially behind frozen V2 scale;
- N0.11 proved that exact per-recipient/entity AOI admission is not the answer in
  the current relation graph: removing 31.55% of 4K relations and 40.36% of
  WorldState records doubled interest-update p95 and worsened total server-role
  p95.

The remaining sparse problem is therefore treated as a world-spatial architecture
problem, not an invitation to keep tuning packet code.

N0's shared UDP host is still controlled IPv4 loopback topology, not a public WAN
acceptor. A later hosted-Network checkpoint must separately earn pinned deployment
identity, listener-lifetime cookie keys, bounded/rate-limited unauthenticated
handshake admission, amplification limits, IPv6 and path-rebinding policy, and
connection sharding with explicit cross-owner packet-memory transfer. Do not fold
that lifecycle/security work into N1's chunk-replication experiment, and do not
quote same-process loopback population as public remote CCU.

N1 tests the original chunk-native AOI model for the current UltraSim game against
that frozen N0 baseline. It does not make chunks the permanent Network-level AOI
unit for every game built on the substrate.

The N1 milestone is:

> Reuse T0's packed, revisioned `WorldChunk` publications as the public spatial
> replication unit. A recipient maintains a small set of chunk subscriptions and
> revision cursors. First interest receives a complete chunk snapshot; continued
> interest queries bounded dirty history at tier cadence; history loss falls back
> to another snapshot. AOI tier changes query frequency rather than retiering
> every member. Fine public visibility is a client presentation concern. Network
> remains responsible for bounded delivery, security, application proof, recovery,
> and per-connection framing—not for rediscovering world membership.

N1 is accepted only if it preserves N0 semantics and materially improves the
measured sparse production path. If it does not, reject the experiment rather than
weakening correctness to force a win.

## Frozen principles inherited from N0

```text
AUTHORITY DOES NOT MOVE INTO NETWORK.
CONNECTION IDENTITY IS NOT GAME IDENTITY.
RUNTIME ENTITY IDENTITY IS NOT WIRE IDENTITY.
NETWORK IDENTITY IS NOT PERSISTENT IDENTITY.

CAPTURE/PUBLICATION HAPPENS AFTER AUTHORITY COMMITS.
NETWORK MOVES SEMANTIC DATA; IT DOES NOT OWN WORLD TRUTH.
SENT IS NOT KNOWN. APPLIED/ACKNOWLEDGED IS KNOWN.
LATEST STATE COALESCES. EVENTS RETAIN FACTS.
BASELINES/SNAPSHOTS PREPARE, VALIDATE, THEN COMMIT.
LOSS NEVER SILENTLY CHANGES SEMANTICS.
MEMORY IS BOUNDED.

LANES DEFINE DELIVERY.
PACKETIZATION IS LAST.
SECURITY/TRANSPORT DO NOT DEFINE AOI.
TICK IDENTIFIES AUTHORITY STATE.
TIME DEFINES DURATION.
CADENCE IS DELIVERY POLICY.
```

N1 adds:

```text
AOI IS GAME/REPLICATION POLICY, NOT NETWORK POLICY.
THE ACTIVE GAME PROFILE DEFINES ITS SPATIAL SUBSCRIPTION UNIT.
NETWORK CONSUMES BOUNDED DELIVERY RESULTS; IT DOES NOT OWN AOI REPRESENTATION.

A GAME PROFILE MAY USE:
    RECIPIENT x ENTITY RELATIONS
    RECIPIENT x CHUNK SUBSCRIPTIONS
    ANOTHER BOUNDED GAME-OWNED REPRESENTATION

THE CURRENT ULTRASIM N1 CANDIDATE USES WORLD CHUNKS.
CHUNK TIER CONTROLS QUERY FREQUENCY, NOT PUBLIC STATE TYPE.
ONE CHUNK PUBLICATION IS SHARED SEMANTIC WORK.
RECIPIENTS OWN CURSORS, NOT PRIVATE COPIES OF CHUNK HISTORY.
FIRST INTEREST GETS A SNAPSHOT.
HISTORY MISS GETS A SNAPSHOT.
CLIENTS MAY CULL PUBLIC STATE MORE PRECISELY.
PRIVATE/SECRET STATE NEVER ENTERS A PUBLIC CHUNK PUBLICATION.
ENTITY IDENTITY SURVIVES CHUNK MIGRATION.
```

## Accepted Terrain T0 foundations

N1 now consumes these accepted executable T0 contracts:

```text
SpatialChunkKey / normalized WorldPosition
ChunkInterestQuery(anchor, rings) -> bounded chunk keys + cadence tier
ChunkPublicationStore.TryCopySnapshot(key, destination)
    -> complete revisioned public scope
ChunkPublicationStore.TryCopyDelta(cursor, destination)
    -> NoChanges | Delta | SnapshotRequired
chunk-bound ChunkPublicationCursor
ChunkPublicationRevision / HistoryFloor
public identity/lifetime semantics across chunk migration
ClientWorldSemanticCache atomic local snapshot/delta apply
separate public vs owner/private/event state
bounded resident/history capacities
```

The local T0 consumer remains the semantic oracle. Remote replication must
reproduce its eventual client state without introducing another definition of
world relevance.

## Ownership and dependency shape

The target path is:

```text
AUTHORITATIVE GAME / WORLD

ECS committed dynamic state
        |
        v
Game/World
    WorldSpatialIndex
    ChunkPublicationStore
    packed current public pages
    bounded dirty histories
    chunk overlap query
        |
        v
Game/Replication / Game/Network
    authorization
    public vs private/event routing
    game-owned AOI / interest policy
    replication source/profile
    semantic coalescing + application-proof meaning
    Game codec + retained delivery ticket
        |
        v
    bounded semantic delivery offer
        |
        v
Kernels/Network
    connection + message descriptor/lane
    bounded payload admission
    reliability / independent-unreliable state
    security
    pacing
    transport
        |
        v
Client/Network
        |
        v
Client world projection
        |
        v
Godot presentation / precise visibility culling
```

`Kernels/Network` must not reference `Game/World` types. Game-owned adapters encode
chunk semantics into dependency-clean Network messages just as N0 game adapters do
for current WorldState.

N0 and N1 are not intended to remain separate Network architectures. Their
game-owned interest and replication models differ, but both flatten after Game
encoding into the same minimal Network delivery boundary:

```text
EntityRelationAoI                 ChunkPublicationAoI
        |                                  |
Entity latest-state source        Chunk revision/history source
        |                                  |
        +------------ Game codec ----------+
                         |
                         v
              recipient/connection
              message descriptor + lane
              bounded payload bytes
                         |
                         v
                      Network
```

The Game adapter retains the semantic ticket. An authenticated application proof
returns through the Game protocol adapter, which decides whether it advances an
entity relation, a chunk revision, or another profile-specific knowledge domain.
Network does not interpret that meaning.

This is an ownership and convergence seam, not a requirement for one generic AOI
interface. Interest membership and replication-source/cursor semantics may remain
concrete and different. They meet only at the bounded delivery boundary; do not
invent an `IAoiThing<TScope, TRevision, ...>` hierarchy to erase those differences.

Keep two Game-owned layers conceptually distinct:

```text
AOI / interest policy
    decides scope membership and entitlement

Replication source/profile
    decides how current state, change, cursors, and recovery are represented

EntityRelationAoI              ChunkAoI
    recipient -> entities          recipient -> chunks
EntityLatestStateReplication   ChunkPublicationReplication
    entity versions/state          chunk revisions/history
```

Those pairs need not expose identical internal APIs or cursor models. Only their
bounded encoded delivery offers converge on Network.

Network does not load terrain files, build meshes, inspect ECS chunks, resolve
Godot resources, or decide whether a tree is visible behind a wall.

## Remote control ingress handoff

T0's local controls strengthened the remote-input contract that existed when this
plan was first drafted. N1 must not route a remote player through the historical
`InputVelocity` path or create a second Network-specific movement simulation.

The target is:

```text
trusted connection/session authority
    -> establish exact GameControlSessionEpoch

client semantic control update
    -> epoch + monotonically increasing sequence
    -> locomotion mutation: Unchanged | Stop | Direct | Destination
    -> adjacent actions
    -> optional facing mutation when that wire consumer is implemented

one game-owned command-admission boundary
    -> the same GameWorldActions authority used by OfflineDirect
```

A remote client's process-local epoch counter is not trusted uniqueness. The
server/session authority must assign or otherwise prove the epoch within the live
authority lineage, so a restarted client cannot collide with an earlier control
session. Locomotion, actions, and a future optional facing update compose under
one epoch/sequence admission boundary; facing must not become an unsequenced side
RPC that can be replayed independently. This freezes no aim/combat framework and
does not add remote control messages in N1.0.

## Public spatial state versus other streams

Chunk-native replication is for **public spatial latest-state**.

Good candidates:

```text
public transforms
public appearance/prefab identity
public animation/stance state
public environmental state
public dropped/world objects
public NPC/creature state
public mutable door/world-object state where disclosure is allowed
```

Do not put these into a broadly shared chunk page merely for convenience:

```text
owner inventory
private stats/skills
hidden or GM-only entities
quest-private state
container contents without authorization
combat/chat/application facts that must occur in order
account/session data
```

Owner/private latest-state remains a separately authorized stream. Ordered facts
remain Event semantics. Terrain/static bulk streaming may use related chunk
identity but does not automatically share the dynamic latest-state cadence or
lane.

## Current chunk-profile recipient state target

The hot N1 public-AOI state should scale primarily with **recipient x subscribed
chunk**, not recipient x visible entity.

Conceptually:

```text
RecipientChunkCursor
    SpatialChunkKey
    LastAppliedRevision
    CadenceTier
    Phase / next eligibility
    bounded in-flight/application-proof metadata
```

The exact layout is measured. The design target is a compact fixed array/set for
roughly the Immediate/Near/Far chunk window, with no heap node per subscription
and no per-recipient copy of chunk dirty history.

A recipient moving inside the same chunk window does not create/remove entity
relations. Crossing a chunk boundary changes a small number of chunk subscriptions.
Near/Far movement normally changes cadence metadata on chunk subscriptions, not
member topology.

## Chunk interest policy

The current UltraSim N1 chunk profile consumes the T0 chunk-overlap query. It does
not reintroduce exact recipient/entity distance filtering inside Network. A
different game-owned AOI profile may legitimately use precise entity relations;
that choice still remains above the shared Network delivery boundary.

### Immediate

Immediate is the local chunk set selected by the T0 observer footprint. A
canonical home point belongs to one half-open chunk; a nonzero local footprint
may intersect two chunks near an edge or up to four near a corner. The complete
public page of each selected chunk is eligible at Immediate cadence.

### Near / Far

Outer radii select chunk AABBs. Each chunk receives its nearest applicable tier.
The data schema is unchanged:

```text
Immediate same chunk public page, highest query cadence
Near      same chunk public page, lower cadence
Far       same chunk public page, lower cadence
Outside   no subscription
```

This intentionally accepts public false positives inside an admitted chunk.
N0.11 is permanent evidence that spending server CPU to eliminate coherent
false-positive rows can lose overall.

The client is free to cull by exact camera distance, view frustum, occlusion, LOD,
interior visibility, or render budget. Camera state must not become authorization.
Client world/view distance controls structural presentation capacity only; it
does not enlarge the server/game-owned Immediate/Near/Far dynamic-public entitlement.
Structural freshness reconciliation may use a different, likely farther policy,
but N1 must earn and freeze that policy independently rather than reading a
graphics slider as replication authority.

## Chunk snapshot semantics

The first time a recipient subscribes to a chunk, or whenever its cursor falls
behind `HistoryFloor`, the server sends a complete chunk snapshot.

A snapshot descriptor names at minimum:

```text
ApplicationSessionEpoch / StateEpoch as appropriate
SpatialSpaceId / SpatialChunkKey
ChunkPublicationRevision
snapshot/schema fingerprint
record count / segment count
content hash
optional world/static layer revision references where needed
```

N1.1 froze canonical chunk snapshot/delta/absence/application-proof messages and
their bounded schemas. N1.3 retains the canonical snapshot bytes as the private
body of this reliable ordered Bulk transaction:

```text
ChunkSnapshotBegin
bounded ChunkSnapshotSegment messages
ChunkSnapshotCommit
terminal ChunkApplied result
ChunkSnapshotCancel / timeout
```

All transaction lifecycle messages currently share the Bulk lane, avoiding a
cross-lane descriptor/segment race. Ordinary dynamic deltas and absence remain on
independent-unreliable WorldState. A later encoding optimization may change byte
packing only through an explicit protocol/schema version; semantic atomicity may
not be weakened.

Applying a snapshot replaces the client's complete corresponding public chunk
scope at its revision. Delayed pre-snapshot deltas cannot resurrect older rows.

## Chunk delta semantics

When a subscription cadence fires:

```text
cursor == current revision
    -> no semantic payload

cursor covered by retained history
    -> ask chunk for coalesced dirty set
    -> encode latest current values + required removals

cursor older than history floor
    -> snapshot required
```

A chunk delta names source/floor/current revisions sufficiently to reject stale or
misapplied data. It carries public entity identity/lifetime; chunk-local runtime
row slots are never wire identity.

The chunk owns one bounded dirty history. Network never creates one history per
recipient.

### Application proof

N0 proved that transport acknowledgement cannot stand in for application
knowledge. N1 retains that rule.

The likely proof is per chunk revision/range rather than per entity relation:

```text
recipient applied chunk K through revision R
```

A proof may clear delivery obligations only for data covered by that chunk/revision
and epoch. Old proof cannot clear a newer revision. Duplicate proof is idempotent.

Application-proof encoding/delivery must remain reconstructible under loss without
turning every state update into reliable ordered traffic.

## Cross-chunk entity migration

Entity movement from chunk A to chunk B is the most important correctness case.

Server publication semantics conceptually produce:

```text
A @ revision X
    entity E lifetime G is no longer present in A

B @ revision Y
    entity E lifetime G is present with current public state
```

Those messages may be delayed/reordered independently.

The client therefore cannot treat `SpatialChunkKey + local row slot` as entity
identity. Global canonical replication identity/generation from the game
projection, or an accepted T0/N1 successor with equivalent lifetime fencing,
must determine whether an old removal/update is stale.

Permanent tests must cover:

- B-present arriving before A-remove;
- A-remove arriving before B-present;
- delayed old A-present after B-present;
- leave A -> B -> A before slow recipient catches up;
- entity destruction during migration;
- canonical slot/lifetime reuse;
- chunk snapshot on one side while deltas remain in flight on the other;
- generation/StateEpoch replacement mid-migration.

A chunk snapshot establishes complete membership **for that chunk**, not universal
absence from every other chunk unless global identity/version evidence proves it.

## Terrain/static streaming relationship

T0 may expose terrain/frozen-static chunk layers whose change frequency is far
lower than dynamic public state.

N1 should not resend immutable terrain because a dynamic Near cadence fires.
Treat layer revisions separately:

```text
terrain/base layer
    snapshot/cache by content/world-pack revision

frozen static / structural layer
    snapshot + rare mutable overlay revision

interior/decor layer
    independently admitted/retired by game/world policy

dynamic public layer
    regular snapshot/delta cadence
```

The same `SpatialChunkKey` can index all of them without forcing one message or lane.

Large immutable content transfer may use Bulk and client cache/fingerprint proof.
The server should prefer proving the client already owns a matching immutable
world-pack chunk rather than retransmitting bytes when a deployment/content model
makes that possible.

## N1.0 — freeze T0 handoff and build OfflineDirect chunk oracle

Before a new wire message exists:

- bind one authenticated/entered participant to T0 `ChunkInterestQuery`;
- maintain fixed recipient chunk subscriptions/cursors;
- consume `ChunkPublicationStore.TryCopySnapshot` / `TryCopyDelta` directly;
- apply to the same retained client-world model used by T0;
- normalize semantic traces so current local T0 oracle and N1 OfflineDirect path
  end in identical chunk/entity state.

Benchmark chunk subscription mutation separately from current N0
`GameInterestService` relation maintenance.

**Gate:** movement inside stable chunk window performs no entity-level AOI mutation;
chunk crossing is bounded; canonical client state matches T0 local oracle; warmed
hot path allocation is zero.

### N1.0 implementation stop — 2026-08-21

`OfflineDirectChunkRecipient` now implements the packet-free game-owned recipient
layer. It owns a fixed sorted subscription set, tier/phase/cadence state, and one
chunk-bound applied cursor per subscription. Reconciliation is complete-or-fail;
leaves publish before enters; source publication-epoch replacement clears cursor
knowledge; and reads remain pending until the consumer explicitly commits them.
Cancelling a pending read cannot advance recipient knowledge.

N1.0 takes an authority-resolved `WorldPosition` anchor; it is not a decoder or
client-position authorization boundary. N1.1 application integration must derive
that anchor from the authenticated entered participant's current controlled
entity rather than accepting a claimed remote coordinate.

The production recipient contains no connection ID, packet, socket, encryption,
watcher, or client-render state. Tests apply its snapshots/deltas to the existing
`ClientWorldSemanticCache` and compare that canonical state with the accepted T0
direct publication path. They cover history-wrap snapshot fallback, publication
epoch replacement, capacity transactionality, warmed allocation, and both
cross-chunk orders (`B Present` before `A Remove`, and the reverse) while retaining
exactly one public identity.

The default 5/96/192 m profile produces 503 subscriptions in the scale fixture;
one chunk crossing changes 72. At 256 packet-free recipients, stable reconciliation
measured 42.74 us p95 per recipient and crossing reconciliation 43.50 us p95 per
recipient, with zero warmed caller-thread allocation. The deliberately broad
1,024-subscription recipient capacity reserves 187,904 B per recipient
(48,103,424 B for 256), which is now an explicit pressure target for later N1
capacity/layout evidence rather than a hidden production claim. Due-cadence reads
also show that interested absent keys dominate direct snapshot probes; wire N1
must not translate that oracle behavior into repeated empty payload transmission.

Evidence:
[`network-n1.0-offline-direct-chunk-recipient-2026-08-21.json`](../../_Benchmarks/Baselines/network-n1.0-offline-direct-chunk-recipient-2026-08-21.json).

This accepts no N0 replacement or sparse-CCU improvement claim. N1.0 has no new
wire schema, application proof, loss/reorder transport, or public WAN work. N1.0
was accepted at `af493bc6`; N1.1 owns the wire boundary below.

## N1.1 — canonical chunk messages and protocol manifest

Add game-owned chunk snapshot/delta/proof messages. Keep numeric fixed-width or
bounded variable-width layouts. Chunk-local coordinates should exploit the T0
format rather than resend unnecessary global high bits for every row.

Candidate row encoding may use:

```text
entity identity/generation
local X/Z (often uint16 mm if T0 selected <=64 m chunks)
quantized/declared Y/orientation
prefab/public-state fields
field/change mask where it reduces bytes
```

Do not memcpy live C# structs onto the wire. Canonical codecs own endian/layout
and protocol versioning.

Manifest fingerprint changes whenever decode capability changes. Receive
capability remains distinct from local send-policy choices, following the N0 v11
aggregation lesson.

**Gate:** canonical round trips, hostile lengths/counts, duplicate IDs, malformed
revisions, capacity preflight, schema mismatch, and zero partial client mutation.

### N1.1 implementation stop — 2026-08-21

The game protocol now defines four canonical, fixed-endian message families:

```text
119  ChunkSnapshot   Bulk
120  ChunkDelta      WorldState
121  ChunkAbsence    WorldState
122  ChunkApplied    WorldState
```

The game manifest is version 10 with fingerprint
`16305363788555709530`; the chunk semantic schema fingerprint is
`6148331928277823537`. Snapshot and delta bodies use canonical local `uint16`
X/Z coordinates beneath one explicit `SpatialSpaceId + ChunkX + ChunkZ`, sorted
reusable public identity indices, generation/version lineage, and declared
revision/history floors. Live CLR structs are not copied to the wire.

The first bounded schema admits at most 64 public records in one message, matching
the accepted default client public-page capacity. Larger pages fail preflight;
segmented atomic snapshot recovery remains N1.3 work. Decoders reject unknown
operations/fields/state bits, invalid identities/revisions, duplicate or
noncanonical IDs, count/length/schema/hash mismatches, trailing bytes, and
undersized destinations. They validate before publishing decoded rows, so hostile
input cannot partially mutate the caller's destination. Source/destination
overlap is rejected transactionally by encoders.

Remote preparation preserves the N0 rule **SENT IS NOT KNOWN**. Encoding releases
borrowed publication scratch and returns a trusted server-side delivery ticket,
but does not advance the recipient cursor. Only an authenticated exact application
proof matching session, publication epoch, delivery ID, chunk key, kind, and
revision may commit that ticket. Old/duplicate proof is harmless and cannot
regress newer knowledge; publication-epoch replacement fences outstanding
tickets. The first absence requires proof and establishes authoritative empty
knowledge. Repeated due-cadence probes while that key remains absent emit no wire
payload.

Permanent N1.1 tests cover canonical round trips, hostile corruption/truncation/
trailing bytes, overlap, capacity transactionality, exact manifest admission,
send-without-proof, wrong/rejected/stale proof, two outstanding revisions proved
out of order, absence suppression, encoding retry, and epoch replacement. This is
still packet-free wire preparation: it adds no socket, encryption, peer, watcher,
or Network-owned game authority.

The tiering-disabled Release artifact records zero warmed caller-thread allocation.
For 64 records, snapshot encode/decode measured 12.54/12.83 us p95 at 2,634 B;
mixed delta encode/decode measured 15.90/15.81 us p95 at 2,834 B. Absence and
proof are 48 B and 50 B and decoded at 17.36 ns and 19.61 ns p95 respectively.
The retained N1.0 control still reports 503 subscriptions, 72 changes for a
one-chunk crossing, and 187,904 B reserved per broad-capacity recipient. These
are local codec/control observations, not encrypted datagram, WAN, N0-replacement,
or production-CCU evidence.

Evidence:
[`network-n1.1-canonical-chunk-codecs-2026-08-21.json`](../../_Benchmarks/Baselines/network-n1.1-canonical-chunk-codecs-2026-08-21.json).

N1.1 was accepted at `73ae0b14`. N1.2 owns the real delivery lifecycle below.

## N1.2 — independent-unreliable dynamic chunk deltas

Carry coalescible dynamic chunk deltas through the accepted independent-unreliable
state path where semantics permit.

Network packetization remains per connection, but semantic packing is shared:

```text
chunk publication/query once
    -> one canonical semantic delta
        -> many recipients needing compatible revision interval may reuse work
        -> per-peer framing/security/send
```

Do not implement cross-recipient encoded-byte caching until census proves recipients
actually share the same revision interval/field subset often enough.

Retain bounded latest-state retry/application proof. A lost delta does not require
intermediate reconstruction if a later query can coalesce to current state.

**Gate:** deterministic loss/dup/reorder/corruption produces the same eventual
chunk state as OfflineDirect; no reliable-state head-of-line behavior is
introduced.

### N1.2 implementation stop — 2026-08-21

`RemoteChunkWorldStateServer` now owns one connection/session's bounded ordinary
chunk-delivery ledger. `RemoteChunkWorldStateClient` decodes into private fixed
scratch, applies to the existing production `ClientWorldSemanticCache`
transactionally, and only then queues `ChunkApplied`. Transport submission and
transport acknowledgement never advance the recipient cursor.

The server owns monotonic delivery IDs and retains the exact trusted N1.1 ticket
until a matching authenticated proof arrives or its lineage becomes obsolete.
Leaves invalidate every outstanding ticket for that chunk before re-entry can
establish a new subscription lifetime. Publication-store replacement invalidates
the whole ledger and requires recipient reconciliation against the new epoch.
Rejected/capacity/contradictory application proofs retire only their exact
delivery; delayed/duplicate/stale applied proofs cannot regress newer knowledge.
A lost proof causes a redundant later delivery from the still-applied server
cursor, which the client recognizes as already covered and proves again.

`WorldState` is independent-unreliable and nonfragmenting. The accepted UDP
datagram profile provides 1,405 application-message bytes after secure/protocol
framing. The 82-byte delta header plus 43-byte rows therefore admits exactly 30
ordinary rows (1,372 bytes); 31 rows require 1,415 bytes and are rejected as
`PayloadTooLarge` before queueing. A semantic 64-row delta remains valid N1.1
data, but it is not silently split into partial messages carrying one final
revision. Complete oversize/history/first-interest recovery belongs to N1.3.

Permanent tests carry actual delta and absence messages through authenticated
`ProtocolLoopback`, apply them to the real client semantic cache, and return
application proofs. Deterministic loss, duplication, delay/reorder, corruption,
lost-proof redundancy, subscription leave/re-entry, publication-epoch replacement,
and MTU overflow all converge or fail closed without false recipient knowledge.
Known absence still produces no repeated empty payload.

The evidence artifact records the exact 1,472/1,405-byte datagram/message envelope,
30-row cap, first rejected size, and a 9,642-byte server-adapter reservation for a
128-ticket evidence ledger. Retained N1.0/N1.1 timing controls are repeated only
as same-run context; they are not a controlled performance regression claim.
This checkpoint does not implement snapshot transfer, UDP redesign, shared
encoded-byte caching, N0 deletion, public-WAN admission, or production scale.

Evidence:
[`network-n1.2-independent-chunk-delivery-2026-08-21.json`](../../_Benchmarks/Baselines/network-n1.2-independent-chunk-delivery-2026-08-21.json).

This checkpoint was accepted before N1.3 began.

N1.2 was accepted at `4e847c41`. N1.3 owns complete exceptional recovery below.

## N1.3 — atomic chunk snapshot recovery

Implement the selected snapshot transaction over ProtocolLoopback first, then UDP.

Recovery causes:

```text
first chunk subscription
history floor overrun
client cache mismatch
explicit repair/resync
StateEpoch/generation replacement as applicable
```

Snapshot transfer is bounded by T0 maximum public rows/chunk and selected Bulk
limits. A malicious/stalled recipient cannot pin server chunk history or suppress
ordinary service forever.

Timeout/cancel follows N0's fail-closed principles. History is owned by the chunk
and may advance regardless of one slow client; that client snapshots again.

**Gate:** loss/reorder around Begin/segments/Commit, cancellation, timeout,
history wrap during transfer, post-cutoff changes, delayed old deltas, and warmed
ordinary-delta path allocation.

### N1.3 implementation stop — 2026-08-22

N1.3 retains the accepted N1.1 canonical full-snapshot bytes as its private
transaction body. It does not define another public-row schema. Protocol manifest
version 11 adds four transaction messages:

```text
123  ChunkSnapshotBegin     Bulk
124  ChunkSnapshotSegment   Bulk
125  ChunkSnapshotCommit    Bulk
126  ChunkSnapshotCancel    Bulk
```

All four use reliable ordered Bulk. This avoids a cross-lane race where a valid
segment could arrive before its descriptor. Ordinary delta/absence messages remain
independent-unreliable WorldState, so a recovery transaction does not impose Bulk
head-of-line ordering on ordinary dynamic state. The current manifest fingerprint
is `11204815876809708509`; the accepted chunk semantic schema fingerprint remains
`6148331928277823537`.

The server captures one complete bounded canonical body and immediately releases
publication scratch/history. It retains at most one connection-local snapshot
transaction plus the accepted bounded proof ledger. Begin, MTU-sized byte segments,
Commit, and reliable Cancel progress under peer backpressure. The delivery ID is
consumed when the transaction starts, and only the exact later `ChunkApplied`
proof may advance the recipient cursor. Timeout, leave, source replacement, and
newer same-key recovery invalidate the older ticket/candidate; none pins shared
publication history.

The client stages bytes and segment receipt privately, validates the reconstructed
body through the frozen N1.1 hostile decoder, converts into private semantic
scratch, and invokes `ClientWorldSemanticCache.TryApplySnapshot` once. No partial
row becomes live. A per-resident-key delivery lineage fences same-session ordering
across snapshot, delta, and absence. Therefore a delayed old absence cannot erase
a newer snapshot, and a delayed old snapshot commit cannot resurrect state after
a newer absence.

The default 1,472-byte UDP profile admits 1,405 application bytes. A transaction
segment uses a 30-byte header and up to 1,375 payload bytes. The maximum 64-row
canonical snapshot is 2,634 bytes and therefore uses two Bulk segments. An
MTU-oversize ordinary delta, first populated interest, history-floor loss, or an
explicit repair request can all enter this same complete recovery route.

Permanent tests cover exact first-interest publication, oversize-delta repair,
private atomic commit, both absence/snapshot reorder directions, timeout/cancel,
history advancing beyond the captured revision, deterministic loss/duplication/
delay/corruption, lost-proof retry, and ordinary WorldState progress while Bulk
recovery is pending. Snapshot capture never extends chunk history lifetime; if
the committed cutoff has already fallen below the current history floor, another
snapshot is required.

Evidence:
[`network-n1.3-atomic-chunk-snapshot-recovery-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.3-atomic-chunk-snapshot-recovery-2026-08-22.json).

This checkpoint was accepted at `ac96ddfe` before N1.4 began. It does not delete
N0, share encoding across recipients, define public-WAN lifecycle, or make
production CCU claims.

## Public chunk scheduling target

N1.0 deliberately proves correctness by polling bounded recipient subscriptions.
That is the oracle, not the intended production discovery scheduler. Interest
topology and publication dirtiness are independent:

```text
Incoming
    first interest
    re-entry
    invalid knowledge
    history recovery
        -> establish complete current state

Current + clean
        -> zero replication work

Current + dirty + cadence eligible
        -> query bounded history from the recipient's applied cursor
        -> send entity/field-granular delta or recover by snapshot

Leaving
        -> retire scope membership
        -> invalidate outstanding delivery lineage
```

A dirty chunk is one shared publication/scheduling fact, not an instruction to
send the complete chunk. The retained history still identifies granular public
entity operations:

```text
same-chunk movement in K
    K dirty
    history: Present/Transform change for P

boundary migration A -> B
    A dirty: Remove P
    B dirty: Present P
```

N1.4 therefore measures replacing relation-driven maintenance with the
combination of bounded interest changes and shared dirty publication work. It may
change how due work is discovered without changing the N1.0-N1.3 subscription,
cursor, wire, proof, or recovery semantics.

## N1.4 — measured AOI-profile scheduling and adoption experiment

Run both paths behind explicit benchmark/config controls:

```text
N0 entity-relation AOI + entity latest state       frozen game profile/reference
N1 chunk AOI + chunk publication/history           current game candidate

both
    -> Game codec + retained semantic ticket
    -> the same minimal Network delivery mechanisms
```

Do not delete N0 before N1 earns production adoption. A measured N1 win selects
the current UltraSim game's replication profile; it does not prohibit another
game, spectator mode, or specialized public stream from using a different bounded
AOI policy.

Canonical profiles must include at least:

```text
512 / 768 / 1024 sparse crossover region
2048 stress
4096 attribution stress
Dense-400
chunk-boundary traversal
chunk-corner traversal
dense town / static-heavy world
mixed public/private state
```

Measure:

- recipient chunk subscriptions;
- Incoming/Leaving membership and tier changes;
- Current+clean scopes skipped without a publication query;
- shared dirty scopes and dirty-to-recipient fanout;
- cadence-eligible Current+dirty queries;
- dynamic rows/chunk;
- chunk revisions and dirty entries;
- snapshot count/records/bytes;
- delta queries returning no-change vs changed;
- delta rows/bytes;
- application proofs;
- server world publication time;
- N1 subscription scheduling time;
- codec/protocol/listener time;
- server-role p50/p95/p99;
- complete tick p50/p95/p99;
- client apply;
- memory/reserved bytes;
- freshness/input-ACK fairness;
- capacity/nonviable/recovery events.

Compare against frozen V2 only as historical scale context. The primary adoption
control is accepted N0 on the same current machine/build/workload.

### Adoption criterion

N1 must produce a **material** sparse server-role improvement, not merely fewer
relations or bytes. A change that improves counters while worsening p95 is
rejected exactly as N0.11 was.

Do not invent a single percentage before the controlled N1.4 A/B matrix is
captured, but an N1 production replacement should clearly move the practical
sparse crossover, not deliver a noisy single-digit gain at the cost of substantial
complexity.

Dense-400 may not regress outside normal run variance without a measured reason.

### N1.4 dirty-scheduling implementation stop — 2026-08-22

`ChunkPublicationStore` now exposes the sorted affected-chunk keys already
calculated once by each successful authoritative commit. The borrowed keys remain
valid only until the next commit and are explicitly shared publication facts, not
recipient dirty state.

`ChunkPublicationWorkScheduler` maintains a fixed-capacity, generation-fenced
recipient membership set and chunk-to-recipient reverse index. Accepted topology
changes create `Incoming` work, leaves remove membership and outstanding work,
and one observed dirty chunk coalesces one `Dirty` candidate per current
subscriber. Current scopes with no pending fact cause no publication query.
Cadence is copied from the accepted recipient subscription; transiently blocked
work can be requeued without creating another obligation. Source replacement and
a missed commit observation fail closed by making retained scopes eligible to
query current truth. The scheduler owns no encoded bytes, socket, connection,
application proof, or publication history.

Remote scheduling now accepts `SpatialChunkKey` rather than requiring a caller to
retain a sorted subscription-array index. It still enters the frozen N1.1-N1.3
encoder, delivery-ticket, transport, and application-proof paths.

The exact chunk-interest query also returns a conservative Manhattan movement
budget. It is derived from the current enter/leave circle-to-chunk-AABB thresholds
and capped by the Far hysteresis envelope. Cumulative movement within that budget
provably cannot change membership or tier, so the recipient can skip rebuilding
its roughly 500-row topology. Crossing the budget runs the unchanged exact query.
This is an exact acceleration, not a looser AOI approximation or client-trusted
position route.

Permanent gates prove:

- clean scopes create zero dirty work;
- one dirty chunk fans out exactly once to two subscribers and yields granular
  deltas;
- topology crossing, leave/re-entry, source replacement, missed-commit refresh,
  stale recipient generation, and scheduler capacity remain fenced;
- the scheduler's chunk-key work enters the accepted remote transport adapter;
- the stable-movement budget agrees with a fresh exact query and forces another
  exact query immediately beyond its bound;
- warmed work discovery allocates zero caller-thread bytes.

The first semantic-only observation at 256 recipients records 127,232 current
memberships (497 per recipient). A one-chunk dirty commit/query/apply fanout was
230.633 ns p95 per recipient (about 59 us aggregate), while a clean commit
observation was 1.025 ns per recipient after amortizing the one shared fact.
Small movement inside the exact stable budget no longer performs the prior full
query. A real chunk crossing still costs 62.691 us p95 per recipient and remains
an explicit transition-tail target.

The same observation also records the main pressure honestly: broad recipient
arrays plus the first reverse-index layout reserve 93,661,096 bytes at 256
recipients. The same-machine one-entity/256-recipient N0 control retains only one
relation per recipient and reserves 18,940,132 bytes. These are not equivalent
production workloads, but they are enough to prohibit an N1 adoption claim or a
4K extrapolation from the dirty-fanout timing alone. Subscription/cursor and
reverse-index compaction must be evaluated in the controlled N1.4 profile matrix.

Evidence:
[`network-n1.4-dirty-publication-scheduling-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.4-dirty-publication-scheduling-2026-08-22.json),
[`network-n1.4-n0-shared-one-dirty-256-control-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.4-n0-shared-one-dirty-256-control-2026-08-22.json).

This is an N1.4 implementation stop, not production adoption. Keep the single
client snapshot candidate until dense-entry evidence proves a larger bounded
window is worthwhile. Do not begin N1.5 or delete N0 before review and the
controlled matrix.

### N1.4 adoption-diagnostic and first-crossover stop — 2026-08-22

Checkpoint review identified two missing measurements: mixed-cadence pending
traversal and dirty-density/roaming distributions. The first mixed-cadence probe
confirmed repeated deferred-prefix rescans in `TryDequeueDue`. The scheduler
retains the same bounded flat pending array, but now uses one pass marker per
cadence step. Every pending membership is classified at most once in that pass;
another no-work call at the same step performs no repeated scan. A permanent gate
freezes that behavior, and all 65 N0/N1.0-N1.4 Game Replication tests pass.

At 32 recipients and the accepted 1/2/4 evidence cadence, full 497-chunk dirtiness
dequeues 15,904 obligations while examining 3.489 pending-array entries per work on
average. The measured drain is 307.497 us p95 and zero-allocation. This is the
smallest earned correction; no timing wheel, tier queue, or generic scheduler was
added.

The 256-recipient dirty-density sweep proves the intended workload dependence:
one shared dirty chunk fans out 256 obligations, while all 497 subscribed scopes
fan out 127,232 and approach recipient-times-chunk work again. A dispersed control
with one populated home scope per recipient proves that 256 independently dirty
chunks fan out only 256 obligations rather than inheriting the shared-anchor
shape.

The 4,096-step roaming census records a 44.41% stable fast-path rate and 55.59%
exact-query rate for sustained 100 mm cardinal travel. The exact-query movement
budget is 63 mm p50 / 499 mm p95. One-millimeter boundary and corner oscillations
use the fast path for 99.95% of steps after their initial exact reconciliation.
The shortcut is therefore useful but does not erase sustained-travel interest
cost.

The first semantic crossover reaches 512/768/1024 recipients. At 1,024, the
one-shared-dirty-chunk packet-free commit/query/apply path is 619.643 ns p95 per
recipient (about 635 us aggregate), while synchronized real crossings remain
97.106 us p95 per recipient. The broad 1,024-subscription recipient plus scheduler
reservation reaches 387,580,360 bytes. A capacity-sensitivity observation using
640 declared slots still retains all 503 observed subscriptions and reduces that
reservation to 242,183,400 bytes, but it remains materially above the 72,632,876 B
same-machine N0 narrow control. The 640 value is evidence, not a frozen production
maximum.

This is the next stop condition: do not extrapolate to 2,048/4,096 yet. First
attribute and compact the recipient subscription/cursor and scheduler reverse-index
layout without changing accepted interest, publication, wire, recovery, or proof
semantics. Then rerun the crossover before Dense-400, dense-town/snapshot,
boundary/corner, mixed-privacy, transport-replay, and sharded-client evidence.

Evidence:
[`network-n1.4-adoption-diagnostics-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.4-adoption-diagnostics-2026-08-22.json),
[`network-n1.4-semantic-crossover-512-1024-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.4-semantic-crossover-512-1024-2026-08-22.json),
[`network-n1.4-capacity-sensitivity-1024x640-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.4-capacity-sensitivity-1024x640-2026-08-22.json), and the matching
[`512`](../../_Benchmarks/Baselines/network-n1.4-n0-shared-one-dirty-512-control-2026-08-22.json),
[`768`](../../_Benchmarks/Baselines/network-n1.4-n0-shared-one-dirty-768-control-2026-08-22.json), and
[`1024`](../../_Benchmarks/Baselines/network-n1.4-n0-shared-one-dirty-1024-control-2026-08-22.json)
N0 controls. The concise interpretation is retained in
[`network-n1.4-adoption-diagnostics-2026-08-22.md`](../../_Benchmarks/Baselines/network-n1.4-adoption-diagnostics-2026-08-22.md).

### N1.4 storage-compaction stop — 2026-08-22

The first crossover proved capacity selection alone was insufficient. This pass
changes storage only and preserves every accepted N1.0-N1.4 interest, publication,
cursor/history, proof, recovery, cadence, and dirty-work semantic.

Recipient subscription banks now retain one 32-byte private row instead of the
64-byte public self-validating cursor value. The borrowed public view reconstructs
its cursor from the recipient-wide publication epoch, retained chunk key, and
applied revision. Known absence remains explicit knowledge with no applied
revision. At 1,024 declared slots, recipient reservation falls from 187,904 to
122,368 bytes.

Scheduler records remove duplicate recipient IDs, chunk keys, and derivable cadence
state. Exact fixed-capacity attribution records 16-byte recipient, 40-byte
membership, and 24-byte shared-chunk rows. The two pre-sized dictionaries remained
the largest removable lines, so they are replaced by bounded open-address indexes
containing only 4-byte scheduler-slot references. Backward-shift deletion prevents
tombstone accumulation without adding a generic index framework or assuming a
small globally shared chunk set.

A permanent 2,000-operation topology-churn gate covers recipient generation reuse,
random enter/leave transitions, lookup, reverse chunk membership, pending work,
and drains. All 66 N0/N1.0-N1.4 Game Replication tests pass.

The identical broad 512/768/1024 crossover now reports 221,798,400 combined bytes
at 1,024 recipients, 42.8% below the prior recorded 387,580,360-byte workload.
The 640-slot sensitivity case retains all 503 observed subscriptions at
147,873,792 bytes instead of 242,183,400 bytes. The new attribution counts every
fixed index slot and excludes only CLR object/array headers; historical dictionary
accounting was coarser, so these are workload-reservation observations rather than
process-RSS claims.

Compaction did not close the synchronized topology tail. The broad 1,024 case
reports 105.577 us p95 per recipient and the 640-slot case 99.573 us; separate
captures do not justify a formal microarchitectural ratio, but neither demonstrates
an improvement over the previous roughly 97 us observation.

Stop here for checkpoint review. Do not run 2,048/4,096 and do not begin N1.5.
If accepted, use this layout for the remaining Dense-400, dense-town/snapshot,
boundary/corner, mixed-private, transport-replay, and sharded-client adoption
matrix before deciding whether crossing needs its own optimization.

Evidence:
[`network-n1.4-storage-compaction-crossover-512-1024-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.4-storage-compaction-crossover-512-1024-2026-08-22.json),
[`network-n1.4-storage-compaction-capacity-sensitivity-1024x640-2026-08-22.json`](../../_Benchmarks/Baselines/network-n1.4-storage-compaction-capacity-sensitivity-1024x640-2026-08-22.json), and
[`network-n1.4-storage-compaction-2026-08-22.md`](../../_Benchmarks/Baselines/network-n1.4-storage-compaction-2026-08-22.md).

## N1.5 — shared semantic packing / chunk page reuse

Only after N1.4 proves chunk subscriptions improve the primary architecture,
measure whether publication can reduce repeated semantic copy/encode work.

Potential progression:

```text
ECS/public changes
    -> chunk SoA page updated once
    -> dirty query identifies rows once per required revision interval
    -> canonical packed semantic block
    -> recipient framing/encryption
```

Opportunities include:

- contiguous span copies from SoA/packed staging;
- chunk-local coordinate encoding;
- one snapshot encoding reused by many first-time subscribers when application
  protocol/security boundaries permit;
- shared immutable terrain/static payloads;
- scatter/gather or native-send batching only after byte ownership is clear.

Do not store peer-specific encryption state in chunk pages. Do not make chunks own
Network packet buffers indefinitely.

**Gate:** attribute semantic copy/encode separately and keep only changes that
improve complete server-role timing.

## N1.6 — client visibility / interior streaming policy integration

The server's public chunk subscription is deliberately coarse. The client may
render a smaller exact set.

Game/world policy may also expose separate chunk layers for building interiors or
decor. The current N1 chunk profile routes those authorized layer subscriptions
without rebuilding entity relations. Another game-owned profile may represent
that entitlement differently without changing Network.

Examples:

```text
outside cabin
    structural shell chunks/layers

enter cabin / near portal
    same surrounding public chunks
    + admitted interior/decor layer

leave
    retire interior presentation/cache according to client policy
```

Interactive/private facts continue through authoritative game paths.

**Gate:** no hidden/private state leaks through public chunk cache; entering/leaving
interiors does not churn unrelated dynamic public rows.

## N1.7 — continuous lifetime and recovery acceptance

After architecture/performance adoption, run a continuous roaming soak with
bounded telemetry rather than the multi-GiB raw-history benchmark pattern exposed
by N0.

The run must exercise:

- thousands of chunk crossings;
- revision-history wraps;
- repeated snapshots;
- slow/lossy recipients;
- client cache retirement/re-entry;
- entity cross-chunk migration;
- generation replacement;
- Event continuity;
- no unbounded cursor/history growth.

Memory evidence separates production live/reserved state from benchmark
observation storage.

**Gate:** bounded resident subscriptions/history, zero unexplained debt, exact
convergence, no increasing low-water across recovery cycles, and documented CPU
scope.

## N1 interaction with existing N0 mechanisms

Retain unless evidence explicitly replaces them:

- secure protocol/session ownership;
- Control/Input/Event/Data/Bulk lane semantics;
- handshake/manifest fingerprinting;
- connection epochs and authenticated Admission binding;
- revisioned owner state and input processing;
- retained application Events;
- baseline transaction principles;
- shared UDP listener and native send/receive batching;
- independent-unreliable application-proof semantics;
- opt-in unreliable multi-frame capability;
- deterministic impairment harnesses;
- fail-closed capacity/nonviable policies.

Chunk-native AOI is not a reason to redesign crypto, socket ownership, login, UI,
Input, or Events.

The N0 `GameInterestService` relation graph remains compiled/reference evidence
until N1 adoption. After adoption, remove it from production only when all current
protected tests have an equivalent N1 path or are explicitly historical
comparison projects.

## Capacity model

N1 explicitly bounds:

```text
maximum subscriptions/recipient
maximum subscription entries globally
maximum in-flight chunk snapshots/recipient
maximum snapshot rows/bytes/chunk
maximum delta rows/bytes/query
maximum application proofs pending
maximum chunk messages/datagram
maximum Bulk bytes/connection
maximum client resident public chunks
maximum client public rows/chunk
```

Chunk history capacity belongs to T0 world publication, not to Network recipient
count.

When capacity is insufficient, return a named status, snapshot, reject admission,
or mark the recipient nonviable according to the owning contract. Never allocate
an unbounded fallback collection in a packet or recipient hot path.

## Test matrix

Permanent N1 coverage includes:

- canonical chunk codec round trips and hostile input;
- first subscription snapshot;
- no-change cadence query;
- dirty coalescing;
- history-wrap snapshot fallback;
- chunk enter/leave/tier change;
- edge/corner subscription sets;
- cross-chunk migration and every important reorder permutation;
- destruction/reuse during migration;
- duplicate/stale application proof;
- loss/duplication/reorder/corruption;
- snapshot cancel/timeout/retry;
- generation/StateEpoch replacement;
- reconnect/cache reuse only when fingerprints/epochs match;
- public/private disclosure boundaries;
- retained Event continuity;
- Dense and sparse semantic parity;
- capacity exhaustion;
- warmed allocation gates;
- bounded long-lived roaming.

## Benchmark/evidence policy

The N0 evidence discipline remains unchanged:

- fixed authority cadence controls;
- exact scenario/configuration recorded;
- canonical semantic hashes before timing claims;
- server-role and integrated timing separated;
- p50/p95/p99 plus relevant long-tail samples;
- bytes/records alone never constitute a performance win;
- setup/cold allocation separated from warmed hot allocation;
- benchmark telemetry memory separated from production memory;
- matched controls and run-order sensitivity where host noise matters;
- immutable JSON for accepted/rejected checkpoints;
- negative experiments preserved rather than quietly removed.

N1.4 scale claims require three explicitly separated evidence levels:

```text
1. Server semantic replication
   no sockets and no fake-client CPU in the server-role number

2. Server transport replay
   real Network security/packetization with synthetic or replayed client traffic

3. Full end-to-end sharded clients
   real sockets plus client decode/apply outside the server process where scale
   would otherwise hit the same-process bot ceiling
```

The first level isolates Game replication architecture, the second attributes
transport cost, and the third proves actual integrated behavior. Same-process bot
population is useful correctness evidence but is not, by itself, a remote CCU
claim.

The practical CPU goal remains to leave enough authority budget for the game.
A workload fitting barely under one 60/62 Hz frame is feasibility, not the same as
an accepted 4-6 ms server-role target.

## Current UltraSim N1 definition of done

N1 is complete only when:

- T0 chunk publication remains the source of public spatial state;
- AOI remains explicit Game/Replication policy and the selected UltraSim profile
  maintains bounded chunk subscriptions/cursors;
- entity-relation and chunk-publication profiles both flatten through Game codecs
  into the same minimal Network delivery boundary;
- first interest and history misses recover through atomic snapshots;
- continued interest uses bounded coalesced dirty history;
- production discovery distinguishes Incoming, Current+clean, Current+dirty, and
  Leaving so unchanged subscriptions require zero replication work;
- tier changes affect cadence without rewriting member semantic type;
- cross-chunk migration cannot be corrupted by delayed/reordered data;
- public/private/Event streams remain distinct;
- Network kernel remains game/world independent;
- accepted N0 security/reliability/reconnect semantics remain green;
- the chunk-native path materially improves sparse server-role scaling on the
  accepted T0 world profile;
- Dense-400 does not materially regress;
- memory remains bounded over continuous roaming/history wrap;
- production adoption is supported by immutable evidence rather than counters
  alone.

If chunk-native replication fails the measured adoption gate, preserve T0's world
architecture anyway, keep the entity-relation game profile as production
reference, and investigate the next bottleneck from real gameplay profiles rather
than weakening semantics or creating separate Network stacks.

## Deliberate N1 decisions

Frozen for the current chunk profile through N1.3:

- canonical bounded dynamic snapshot/delta/absence/proof schemas;
- chunk revision/floor wire widths and schema fingerprint;
- MTU-safe ordinary-delta admission;
- reliable-Bulk snapshot transaction and atomic client publication;
- application proof, not transport submission, advances recipient knowledge.

Still require measured evidence before production adoption:

- whether terrain/static data uses Network messages or deployment cache/content
  distribution for normal clients;
- production subscription/scheduling layout and any compaction;
- whether same-revision semantic encoding can be reused across recipients;
- local transform quantization;
- compression thresholds;
- exact entity-relation profile retirement/retention point for this game;
- eventual server/world partition ownership boundaries.

None of these decisions may move authority into Network or make private data part
of the public chunk page.

## Current checkpoint boundary

T0 has accepted the chunk coordinate, packed publication, bounded dirty history,
local interest query, production client semantic cache, presentation window, and
launchable woods/cabin vertical. N1.0 connects those contracts to a bounded
packet-free recipient chunk-cursor layer. N1.1 adds canonical bounded chunk wire
semantics and application-proof fencing. N1.2 carries MTU-admissible ordinary
deltas and absences over real independent-unreliable transport with a bounded
server ticket ledger and atomic production-client application. N1.3 adds bounded
reliable-Bulk snapshot recovery with private staging and same-key ordering lineage.
N1.4 adds the shared dirty-work scheduler, exact stable-interest fast path, bounded
cadence traversal, and compact recipient/reverse-index storage. Its mechanisms are
implemented through the storage-compaction review stop; compaction acceptance and
the measured current-game AOI-profile adoption decision remain open.

Stop at the N1.4 storage-compaction checkpoint. The next work, after review, is the
remaining controlled adoption matrix—not N1.5 shared encoding, UDP redesign, N0
deletion, or a production scale claim.
