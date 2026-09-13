# Network V3: current architecture and accepted replication policy

Status: **accepted / consumption-ready**, including physical Network capability packaging,
PF2.5 replication-ownership/observability reconciliation, PF3 authenticated performance policy,
PF4A quiet publication-work suppression, PF4B chunk-knowledge census, PF4C selected Game-message
bundling, the PF4C-R1 singleton MTU repair, PF4D unreliable Network-frame aggregation, PF4E
dense/distributed fanout plus remaining-cost attribution, PF4E-R1 rated scaling reconciliation,
PF4E-R2 direct ChunkPublication staging, and PF4F bounded selective message-129 compression,
as of
2026-09-13.

Network N0, Terrain/World T0, N1.0-N1.4, Hosted H0.0-H0.5, the five-profile
comparison program through P0.4, and the first real remote public-actor consumer
vertical are accepted. `ChunkPublication` is the selected **current-game dynamic
public** replication profile. `MaintainedEntityRelation` and
`DirtyEntitySpatialFanout` are retained alternatives;
`DirtyEntityChunkBatchFanout` and `DirtyChunkSpatialFanout` are retained
experimental/reference controls.

PF2.5 independently reconciles the historical benchmark conclusions with the real post-P0.4
product graph and retains **Option A** through Concept Demo freeze: all current mutable public state
continues through the one composed `ChunkPublication` owner. This is not a claim that it is the
fastest mechanism for every future semantic class. The exact routing, identities, alternative
readiness, and future fixed-owner/transfer requirements are recorded in
[PF2.5 replication reconciliation](PF2_5_REPLICATION_RECONCILIATION.md).

PF4A does not revise that ownership decision. It adds one bounded readiness scheduler to each
remote Game application so a clean recipient observes an empty authoritative publication commit
in O(1) and skips per-chunk scheduling. Dirty source commits, recipient-interest changes, retained
delivery/proof debt, transport/capacity deferral, cadence deadlines, history gaps, recovery, and
source/epoch replacement remain explicit wake conditions. This is Game replication scheduling;
the Network kernel and wire protocol are unchanged.

PF4B also leaves that architecture and every wire identity unchanged. Detailed, owner-local
telemetry now distinguishes message-120 delta production, proof-envelope occupancy, exact
application-knowledge advancement, outstanding delivery debt, and recovery-enabling overlap. The
client separately records proof generation/packing and applied/duplicate/superseded outcomes.
These counters are bounded diagnostics, not another knowledge source: only an exact authenticated
Applied proof may advance the recipient cursor. The ordinary production path still flushes proofs
at the next client progress opportunity and bases independently repairable deltas on proven applied
knowledge.

PF4C retains the selected owner and 1/2/4 cadence while using the existing message-129 Game
envelope for two or more physically beneficial, already-due per-recipient Delta-120/Absence-121
publications. PF4C-R1 additionally guarantees that considering that optional envelope cannot reject
a canonical standalone 120/121 message which fits the peer's actual application-message limit.

PF4D changes only the later Network representation. Reliable frames remain singleton. Up to two
already-ready unreliable frames for one secure peer may share one authenticated Data datagram,
including the measured OwnerState + WorldState case. There is no aggregation timer or added wait:
the first frame is the normal scheduler choice, following frames use the same bounded selection,
packing stops at a higher-priority reliable frame or the exact MTU, and endpoint refusal consumes
nothing. The Game manifest advances to version 28 and admits at most eight received unreliable
frames so cap 1/2/4/8 remain interoperable test policies; the selected production send policy is
cross-lane cap two. Transport wire revision 2 and all Game/AOI/cadence semantics remain unchanged.

PF4E adds bounded owner-local physical payload histograms and an exact inclusive
Network-plus-remote-replication rating clock; neither changes packet selection or protocol state.
The selected-profile sweep first exceeded the 4.000 ms subsystem p95 gate at dense 128 (the first
dense point tested) and distributed 256, with distributed 128 passing. These are rated replication
recipients, not CCU. Product Moving/Crowd attribution identifies message 129 as roughly 94.1% of
Game application egress, so a selective-compression experiment ranks above self-public suppression
or deeper header compaction. No compression is implemented or accepted by PF4E.

PF4E-R1 preserves that product policy and compares five current real-loopback-UDP curves on one
measurement basis. Current cap-one Dirty/Maintained controls reproduce the historical P0.3J class,
so the selected-profile gap is not general harness or clock drift. The prior PF4E Chunk dense
fixture passes the comparable legacy 4 ms gate at 128 and first fails at 192; an explicit
one-dirty-chunk/unbatchable shape passes 192 and fails 256; a production-shaped localized
eight-chunk shape passes 96 and narrowly fails 128. Recipient count alone is not a capacity claim.

The earlier inference that the PF4E fixture did not engage message 129 is superseded: at 128 it
bundles 98.20% of semantic publications. The production-shaped fixture bundles 97.47%. Neither
shape has an MTU-compatible PF4D cross-lane pair, so cap-two aggregation remains proven by PF4D's
own product workload rather than these chunk-only controls. At 128 the prior fixture's 4.85x Dirty
legacy p95 consists chiefly of 3.76x more useful publications plus a 1.17x mean cost/publication;
operation-count decomposition assigns about 81.2% of the mean gap to publication count and 18.8%
to the unit premium. This confirms a real selected-profile CPU cost without indicating a product
regression. See
[`concept-demo-pf4e-r1-rated-replication-scaling-reconciliation-2026-09-13.md`](../../_Benchmarks/Baselines/concept-demo-pf4e-r1-rated-replication-scaling-reconciliation-2026-09-13.md).

PF4E-R2 removes one measured server-internal representation cycle without changing this contract.
For a due Delta, the remote composition now materializes and validates the trusted Game record set
once. It writes canonical standalone 120 when selected, or writes those same validated records
directly into message 129 when bundling is beneficial and fits. It no longer encodes temporary 120
bytes and parses them back merely to stage a bundle. Untrusted readers retain full validation;
queue admission, per-entry delivery debt/proof, and cursor advancement remain transactional and
recipient-local. The production-shaped 128 inclusive Network-plus-replication p95 moves from 4.903
ms to 3.228 ms in the accepted A/B with identical semantic and wire work. Policy, protocol
manifest, transport revision, AOI, cadence, and participant bounds are unchanged.

PF4F changes only the later Game representation of an already-selected canonical message 129.
The server first builds the exact same bounded canonical bundle and then may encode message 148 as
one independently decodable LZ4 block. The selected attempt threshold is 384 canonical bytes and
the complete five-byte-wrapper representation must save at least 32 bytes. No compressed payload
may represent a canonical bundle which the unchanged bundler would not have admitted. Canonical
129 remains valid fallback, and Network admission still occurs once before delivery/proof state is
committed.

The client decompresses into fixed retained scratch, requires the exact declared canonical length,
and passes recovered bytes through the existing canonical message-129 reader. No cross-message
dictionary or loss/reorder dependency exists. The new Game representation advances the Game
manifest to v29; Network framing and transport revision 2 are unchanged. Matched product evidence
records 27.70% Moving-50 and 21.48% Crowd-50 downstream UDP-payload reduction while the rated
production-shaped 128 p95 remains 3.184 ms, below the 4.000 ms subsystem budget.

This document is the current Network/replication architecture contract. Historical
checkpoint detail remains in git and immutable `_Benchmarks/Baselines` artifacts.
A historical result may explain why a decision was made, but it does not override
the current contract below.

The separately scoped externally admitted listener/deployment lifecycle is recorded
in [Hosted Network H0](HOSTED_NETWORK.md). Game-owned profile details and conformance
live in [Game replication](../../Game/Replication/README.md). The dependency-clean
mechanism layer is documented in [Network kernel](../../Kernels/Network/README.md).

## Finalized principles

```text
AUTHORITY DOES NOT MOVE INTO NETWORK.
GAME SEMANTICS DO NOT MOVE INTO NETWORK.
AOI IS GAME / REPLICATION POLICY, NOT NETWORK POLICY.
PRESENTATION DOES NOT OWN GAMEPLAY TRUTH.

CONNECTION IDENTITY != GAME IDENTITY.
IP/PORT != CONNECTION IDENTITY.
RUNTIME ENTITY IDENTITY != WIRE IDENTITY.
WIRE / REPLICATION IDENTITY != PERSISTENCE IDENTITY.
CLIENT STORAGE IDENTITY != WIRE IDENTITY.

CAPTURE / PUBLICATION HAPPENS AFTER AUTHORITY COMMITS.
NETWORK MOVES BOUNDED SEMANTIC DATA; IT DOES NOT OWN WORLD TRUTH.
SENT != KNOWN.
TRANSPORT ACK != APPLICATION PROOF.
LATEST STATE COALESCES.
FACTS / EVENTS RETAIN OCCURRENCE.
LOSS MUST NOT SILENTLY CHANGE SEMANTICS.
BASELINES / SNAPSHOTS PREPARE, VALIDATE, THEN COMMIT.
MEMORY AND WORK ARE BOUNDED.

LANES DEFINE DELIVERY.
PACKETIZATION IS LAST.
SECURITY / TRANSPORT DO NOT DEFINE AOI.
TICK IS AUTHORITY-STEP IDENTITY.
DELTA IS ELAPSED SIMULATION TIME.
FREQUENCY / CADENCE ARE HOST AND DELIVERY POLICY.

THE CANONICAL ULTRASIM.NETWORK PRODUCT IS FULL.
ASSEMBLY IDENTITY != PHYSICAL CAPABILITY SET.
CAPABILITY STRIPPING MAY REMOVE A ROLE; IT MAY NOT CHANGE SHARED PROTOCOL BEHAVIOR.
TEST TOPOLOGY IS NOT PRODUCTION NETWORK CAPABILITY.
```

These rules apply equally to OfflineDirect evidence, deterministic ProtocolLoopback
through `UltraSim.Network.Testing`, connected UDP, the shared listener, Hosted H0,
and future transports.

## Ownership and dependency boundary

The intended production path is:

```text
AUTHORITATIVE GAME / WORLD
    ECS + game-owned committed state
            |
            v
Game-owned projection / publication
    authorization and disclosure
    public vs private vs fact routing
    AOI / interest policy
    replication source and recovery semantics
    semantic coalescing
    application-proof meaning
    canonical Game codec + retained semantic ticket
            |
            v
bounded delivery offer
    ConnectionId
    NetworkMessageDescriptor / lane
    bounded bytes
            |
            v
Kernels/Network
    connection/session machinery
    bounded queues and packet ownership
    lane scheduling
    reliability / unreliable delivery
    security / replay
    pacing
    fragmentation where the lane permits it
    datagram endpoint contract / UDP transport
            |
            v
Client/Network
    hostile decode boundary
    semantic application to Client Core
            |
            v
Client Core / Godot presentation
```

Deterministic endpoints and two-ended topology may satisfy that same datagram
contract from `UltraSim.Network.Testing`; they are verification inputs, not
production Network capabilities.

`Kernels/Network` must not reference ECS, `Game/World`, AOI types, inventory,
accounts, UI meaning, persistence, Godot, or game authority. Game adapters flatten
all concrete semantics to the same minimal delivery boundary.

### Canonical Full assembly and optional capability stripping

`UltraSim.Network` is one source tree, one public assembly identity, and one
canonical package. Its default build and normal `UltraSim.Network` package are
always **Full**:

```text
Full
    Core
    Hosted Client
    Hosted Server
```

The optional build property `UltraSimNetworkFlavor` admits exactly:

```text
Full         Core + Hosted Client + Hosted Server
ClientOnly   Core + Hosted Client
ServerOnly   Core + Hosted Server
```

`Full` is the canonical API and exact public-API union of the two stripped
capability sets. All outputs remain named `UltraSim.Network.dll`; the flavor is
recorded as assembly metadata and participates in isolated output/intermediate
paths. Shared public APIs may not mention flavor-specific types. All protocol
declarations, codecs, identities, lane contracts, cryptographic domains, limits,
and manifest inputs live in Core and compile identically in every capability set.

No product may infer physical capability presence from assembly identity alone.
The embedded `UltraSim.Network.Flavor` metadata and actual role-type inspection are
packaging evidence; compile-time product composition remains the capability owner.
A manually substituted Full DLL remains protocol/API compatible but means an
Online or Dedicated artifact no longer satisfies its current physical-stripping
policy.

Permanent gates compare protocol version/fingerprint and canonical packet bytes,
compare public type/member signatures, and inspect published/exported assemblies
for physically forbidden role types. The production matrix is currently:

```text
Single Player    no Network assembly
Online           ClientOnly
Dedicated        ServerOnly
Complete         Full
```

This matrix is packaging policy, not a semantic dependency. Online and Dedicated
may deliberately ship Full later without changing consumer code. Capability
stripping reduces binary size, accidental callable surface, and currently
distributed implementation; Network security never depends on server code being
absent from a client. The canonical NuGet/package remains Full. Specialist
ClientOnly/ServerOnly artifacts must not be combined in one process or published
under the canonical package as competing same-identity binaries.

Deterministic impairment, loopback handshakes, self-trusted two-ended UDP hosts,
and related test topologies are separately compiled into
`UltraSim.Network.Testing`. They remain reusable evidence machinery but are
absent from Full, ClientOnly, ServerOnly, and all production exports.

The accepted Release size, public-API, protocol, package, and actual product-export
evidence is recorded in
[`network-capability-packaging-2026-09-08.md`](../../_Benchmarks/Baselines/network-capability-packaging-2026-09-08.md).

Do not introduce a universal `IAoiThing<T...>`, generic cursor hierarchy, or a
single replication interface merely to hide real differences between profile
families. Concrete Game profiles may retain different interest, cursor, source,
recovery, and proof structures.

## Identity domains

Identity domains are explicit and non-interchangeable:

| Identity | Owner | Meaning |
| --- | --- | --- |
| endpoint/path | Hosted transport | currently observed IPv4/port path only; never connection or participant identity |
| `HostedCandidateId` | Hosted admission | generation-fenced pre-session candidate row |
| `ConnectionId` | Network | server-created generational transport connection identity |
| `SecurityEpoch` / packet number | Network | cryptographic/replay lifetime and datagram sequence |
| application session epoch | Game protocol/application binding | authenticated application-session lifetime |
| `StateEpoch` | Game projection | coherent canonical projection generation |
| `ReplicationEntityId` | entity-style Game projection | canonical entity-profile wire identity within its lineage |
| `WorldPublicEntityId` | chunk-publication Game world | dynamic public identity within chunk-publication lineage |
| `PersistentEntityKey` | persistence | durable world + entity identity |
| ECS `Entity` | authoritative runtime | generation-local runtime handle |
| client slot / semantic handle / render slot | Client | private implementation identities |

`WorldPublicEntityId` and `ReplicationEntityId` are deliberately not aliases.
There is currently no cross-profile logical-object identity/order domain.

## State, facts, and delivery

### Sent is not known

Submitting bytes, receiving a transport ACK, or successfully decrypting a datagram
does not prove that the owning application semantic committed.

The application owner advances recipient knowledge only after the corresponding
semantic apply succeeds and a matching authenticated application proof returns.
Lost proof leaves server knowledge unchanged and is repaired by reconstructible
retry/current-truth delivery. Duplicate proof is idempotent. Stale proof may not
advance a newer lineage.

### Latest state versus facts

Latest state may supersede intermediate values when the semantic contract says the
newest complete value is sufficient. Examples include owner latest-state and public
world latest-state.

Facts whose occurrence matters do not collapse merely because Network is under
pressure. Chat messages, purchase/trade results, quest facts, and other retained
application events use ordered/idempotent fact semantics. Overflow/recovery must be
explicit.

ECS lifecycle events are not automatically Event-lane application facts. They are
source evidence from which Game replication derives recoverable projection state.

## Lane contracts

| Lane | Delivery / order | Meaning |
| --- | --- | --- |
| Control | reliable ordered | protocol/session/control state and small ordered application control |
| Input | unreliable sequenced | expiring complete authoritative input samples |
| OwnerState | unreliable sequenced | latest owner-private state |
| WorldState | unreliable unordered / independent | reconstructible latest public state containers; Game owns semantic debt and proof |
| Event | reliable ordered | non-coalescible application facts |
| Bulk | reliable ordered, fragmentable | bounded definitions, baselines, atomic snapshot bodies, large semantic UI operations |
| Disposable | unreliable unordered | explicitly disposable presentation-only data |

Network does not infer lane from object type. Game/application protocol declares the
message descriptor and therefore the delivery contract before packetization.

## Authoritative cadence and Network cadence

The frozen Runtime contract is:

```text
Tick       = authoritative step / ordering identity
Delta      = elapsed simulation time represented by the step
Frequency  = host execution policy that determines Delta
```

Fixed 20/30/40/50/60 Hz authority, live cadence changes, and cross-rate save/load
have permanent correctness coverage. Time-relative behavior uses authoritative
world time / delta rather than pretending a tick count is duration.

Network execution is not required to run one-for-one with authority. The
architecture must remain correct when authority runs faster or slower than Network:

```text
authority faster than delivery
    latest-state debt coalesces to current truth
    non-coalescible facts remain retained/bounded according to their contract

Network faster than authority
    empty/maintenance delivery opportunities are allowed
    no new authoritative truth is invented
```

A future host may choose matched or unmatched server/Network rates. Do not create a
hidden one-tick-one-pump correctness dependency.

### P0.3K server-step / AOI-cadence conclusion

The final P0.3K matrix independently varied 16/33 ms server steps and AOI skipped-
tick policies `0/1/2` versus `1/1/2`. It used the authenticated whole-flow fixture:
Game codec, Network queues/lanes, AES-GCM, real shared localhost UDP, client apply,
reverse proof, and server proof interpretation.

| Server step | AOI | Dense last integrated pass | Sparse last integrated pass |
| --- | --- | ---: | ---: |
| 16 ms | 0/1/2 | 704 @ 15.244 ms | 640 @ 12.911 ms |
| 16 ms | 1/1/2 | 704 @ 15.251 ms | 768 @ 15.816 ms |
| 33 ms | 0/1/2 | 1,344 @ 32.907 ms | 1,280 @ 30.405 ms |
| 33 ms | 1/1/2 | 1,280 @ 31.215 ms | 1,280 @ 30.156 ms |

Those are integrated-fixture deadline brackets, **not dedicated-server CCU ratings**.
The integrated `Tick` includes server plus all synthetic clients in one process.
Server-role p95 is reported separately in the evidence.

At matched population, 33 ms does not make replication work cheaper per tick.
Bytes/tick and CPU/tick remain approximately the same; the host gets roughly twice
the deadline and runs roughly half as many ticks per second. Wire rate per simulated
second therefore falls by about half. The cost is freshness: the same tick-period
cadence corresponds to larger wall-clock spacing.

Changing only Immediate from `0/1/2` to `1/1/2` changed traffic by less than 0.4% in
the measured workloads. It is a semantic freshness policy, not a supported CCU
optimization claim.

UltraSim continues to tune/build against a 16 ms stress target because it is the
harder engineering envelope. Production cadence remains configurable (for example
20/30/40/50 Hz where supported by Runtime policy) and is a gameplay/product choice,
not a Network correctness assumption.

Evidence:
[`network-p0.3k-server-tick-aoi-cadence-matrix-2026-08-24.md`](../../_Benchmarks/Baselines/network-p0.3k-server-tick-aoi-cadence-matrix-2026-08-24.md).

## Public, private, static, and fact domains

Do not collapse these domains because they happen to share a connection:

```text
terrain / immutable base
    WorldPack / deployment content identity
    snapshot/cache by content revision

frozen structural/static content
    WorldPack/static layer + explicit rare mutable overlay

interior/decor content
    independently admitted/retired by Game/world policy

current-game dynamic public state
    selected ChunkPublication profile

owner/private latest state
    separately authorized stream / UI projection

application facts
    Event semantics

control/input
    authenticated command intent into Game authority

account/session/UI
    separately authorized application semantics
```

A shared `SpatialChunkKey` may index several world layers without forcing them into
one message, lane, revision domain, or replication profile.

Inventory is a concrete proof of this boundary: owner-private inventory state uses
the generic authenticated UI semantic path and never enters public AOI merely
because it is remotely visible to its owner.

## Current dynamic-public replication profiles

`GamePublicReplicationProfiles` is a finite Game-owned catalog. Every catalog entry
covers **dynamic public state only**.

| Profile | Source / topology | P0.4 disposition |
| --- | --- | --- |
| `ChunkPublication` | recipient/chunk subscriptions + chunk revision/history | **selected current-game dynamic-public profile** |
| `MaintainedEntityRelation` | maintained recipient/entity relations + entity latest state | retained alternative |
| `DirtyEntitySpatialFanout` | dirty-time entity recipient discovery + bounded semantic knowledge/debt | retained alternative |
| `DirtyEntityChunkBatchFanout` | same entity semantics/proof; dirty-time entrant discovery batched by current chunk | experimental/reference |
| `DirtyChunkSpatialFanout` | dirty-chunk recipient discovery + chunk history; no reverse subscriber graph | experimental/reference |

All preserve the common semantic contract:

```text
authoritative source truth
recipient-scoped disclosure
stable profile-local identity/lifetime
sent != known application proof
bounded impairment recovery
bounded memory/work
eventual semantic convergence
```

They do **not** share one universal internal API.

### P0.4 selection meaning

P0.4 deliberately did not convert benchmark winners into a per-object routing table.
Whole-flow evidence found `DirtyEntitySpatialFanout` strongest in the measured
Dense/mobile dynamic workload, while `MaintainedEntityRelation` was narrowly strongest
in the measured sparse moving-recipient workload. Those observations are retained.
They do not prove that all actors should use one entity profile or that profile
ownership should switch at runtime.

`ChunkPublication` remains selected for the current game's dynamic-public architecture
because that is the accepted concrete composition. This is not a claim that it is the
best representation for every future dynamic semantic class.

PF2.5 confirms why that decision remains correct after the real actor, item, interaction, metadata,
content, multiplayer, exported-client, and bot consumers landed. The two entity profiles still use
their own `ReplicationEntityId` lineage and old entity-state client, while the product uses
`WorldPublicEntityId`, chunk proof/order, and `ClientWorldSemanticCache`. A fixed per-class split is
not logically forbidden by `RuntimeOwnershipTransitionsSupported == false`, but it is not ready
until a shared public identity, explicit one-owner table, merged client lifetime/order, proof, and
local-parity seam exist. Runtime transfer additionally requires a transactional ownership epoch.

### Runtime cross-profile transfer is unsupported

One logical object must not be simultaneously published through multiple public
mechanisms. An old-profile remove plus a new-profile present is not a handoff
protocol: the current identity domains and clients cannot prove ordering/deduplication
across them.

If a future gameplay feature earns activation/freezing/dormancy/static
consolidation across ownership domains, it must first define:

```text
stable Game public identity across the relevant domains
explicit ownership epoch / lineage
client-side cross-domain ordering and deduplication
transactional old-owner -> new-owner handoff
```

A dynamic object becoming true static content additionally crosses into
WorldPack/static or mutable-overlay ownership; it is not merely a replication
profile switch. None of this meaning belongs in Network.

P0.4 commit: `df7d2ca2`.

## Selected `ChunkPublication` semantics

The selected current-game public profile consumes T0's accepted world contracts:

```text
SpatialChunkKey / normalized WorldPosition
ChunkInterestQuery
ChunkPublicationStore
ChunkPublicationCursor
ChunkPublicationRevision / HistoryFloor
ClientWorldSemanticCache
```

`ChunkPublicationStore` is **dynamic public** storage. It is not immutable terrain,
frozen static, or structural WorldPack storage.

### Recipient state

A recipient maintains bounded chunk subscriptions, cadence tier/phase, an applied
cursor per key, and bounded in-flight proof/recovery lineage. Current+clean scopes
create zero replication work. Dirty work is discovered through the shared
`ChunkPublicationWorkScheduler` reverse subscriber index.

The production remote application owns one single-recipient scheduler and observes the source's
committed affected-key set once per authority progression. `PendingWorkCount == 0` is an O(1)
clean gate: no subscription scan, encode attempt, or cadence-deferral traversal occurs. A dirty
commit fans only to current subscribers; an interest change enqueues only the affected membership;
retained delivery debt is requeued until proof/application converges. Commit gaps and source
replacement conservatively refresh current truth rather than sleeping uncertain state.

First interest, invalid knowledge, and history-floor loss recover complete current
truth. Leaving retires scope membership and invalidates outstanding lineage.

### Snapshot / delta / absence

Ordinary MTU-admissible deltas and absence use independent-unreliable WorldState.
A recipient cursor advances only after successful semantic apply and matching
application proof.

Complete exceptional recovery uses one bounded atomic reliable-Bulk snapshot
transaction:

```text
ChunkSnapshotBegin
ChunkSnapshotSegment...
ChunkSnapshotCommit
    -> private validation/staging
    -> one ClientWorldSemanticCache snapshot publication
    -> ChunkApplied proof

ChunkSnapshotCancel / timeout
    -> retire only the matching candidate
```

Partial snapshot rows never become live. Snapshot/history capture does not pin
shared chunk history for one slow recipient.

### Cross-chunk migration

Migration publishes remove in the old chunk and present in the new chunk under the
same chunk-public identity/lifetime. Messages may reorder.

The client therefore uses canonical identity/version lineage, not chunk-local row
position, to reject stale old presents/removes. A snapshot establishes complete
membership for its chunk only; it does not by itself prove global absence from every
other chunk.

Permanent coverage includes both migration orders, stale old data, destruction,
slot/generation reuse, snapshot/delta overlap, leave/re-entry, and source generation
replacement.

## Real public-actor consumer vertical

Commit `815706b2` composes a real authoritative ECS woodland actor through the
selected production seams:

```text
ECS / Game authority
    -> GameWorldState
    -> ChunkPublicationStore
    -> ChunkPublicationWorkScheduler
    -> RemoteChunkWorldStateServer
    -> secure canonical Network protocol
    -> RemoteChunkWorldStateClient
    -> ClientWorldSemanticCache
    -> GodotWorldRenderer
```

The native acceptance proves initial spawn, movement/state replacement, interest
leave/re-entry, fresh-session reconnect/resync, authoritative destruction, and
save/load publication-generation replacement.

That vertical exposed a real decoder bug: a remote remove delta must reconstruct
`WorldPublicDeltaRecord.Current` as canonical `default`. Identity and public version
live in the delta fields; inventing a partial `Current` record caused valid removals
to be rejected. The focused regression is permanent. The fix changes no Network,
profile, proof, or ownership semantics.

This consumer vertical is the point at which public replication is treated as
infrastructure to consume rather than a benchmark project to continue extending.

## Network ready selection and proof packing

P0.3H replaced repeated bounded ready-table scans with per-lane reliable FIFO heads,
IndependentState ready bitsets/counts, and a priority-ready mask. It preserved
reliability/loss semantics while reducing selector work substantially. The bounded
cost was 268 bytes per measured peer.

P0.3I added an MTU-safe envelope carrying up to 40 independently meaningful chunk
application proofs. Envelope packing does not collapse proof granularity: each entry
retains its delivery ID, chunk, revision, kind, and application result, and the server
processes entries independently.

These are Network mechanism improvements and are retained regardless of which Game
public profile is selected.

PF4D activates the existing multi-frame Data path for already-ready unreliable work only. The
default is `MaximumUnreliableFramesPerDatagram=2` with `CrossLane` scope. `SameLane`, scalar cap one,
and cross-lane caps four/eight remain explicit measurement controls. Every inner frame retains its
own lane/message/version/sequence/length/payload and independently applies normal unreliable receive
semantics after the entire datagram structure has validated. Corruption or invalid framing rejects
the whole datagram before any inner delivery; ordinary unreliable loss deliberately couples only
the physical fate of frames which shared that datagram. Detailed telemetry supplies the bounded
cap/scope opportunity census; ordinary aggregation counters remain owner-local primitive values.

## Performance evidence: what may and may not be claimed

Three different envelopes must not be conflated.

### 1. Strict server-role rating

P0.3J repeated the rated authenticated whole-flow sweep with diagnostic stage timing
disabled. Under the formal `server-role p95 <= 4 ms` criterion:

- Dense `DirtyEntitySpatialFanout`: 640 at 3.969 ms; 704 crossed at 4.900 ms.
- Sparse `MaintainedEntityRelation`: 512 at 3.032 ms; 640 crossed at 4.081 ms.

Dense 640 is a narrow observed rating, not a comfortable production target. These
are localhost fixture measurements, not public-WAN CCU claims.

Evidence:
[`network-p0.3j-rated-server-role-probes-off-2026-08-24.md`](../../_Benchmarks/Baselines/network-p0.3j-rated-server-role-probes-off-2026-08-24.md).

### 2. Integrated tick-deadline envelope

P0.3K's 16/33 ms rows measure the complete in-process server + synthetic-client tick
deadline. They are stress/scaling evidence and intentionally allow server-role work
above 4 ms. Do not call the 704/1,280/1,344 brackets the strict Network rating.

### 3. Architecture scaling / stress

P0.3F and related Game-core matrices prove how the concrete profile algorithms scale
before full transport/client cost. For example, P0.3F reached the 4,096 sparse safety
cap for `ChunkPublication` in its Game-core fixture and showed different dense/sparse
crossovers among entity profiles. Those results are architecture evidence, not remote
CCU.

### Whole-flow profile conclusion

The five-profile whole-flow attribution is especially important because it corrected
a misleading Game-core intuition. Under the measured dynamic workloads:

- Dirty Entity was strongest for Dense/mobile dynamic public state.
- Maintained Entity was narrowly strongest for sparse moving recipients.
- Chunk families paid materially more server Game scheduling/codec, client semantic
  cache apply, and proof work in that dynamic workload.
- packet aggregation helped chunk traffic but did not erase those semantic costs.

That evidence motivated the conservative P0.4 selection: retain the current concrete
`ChunkPublication` architecture without inventing unsupported per-object composition.

Evidence:
[`network-p0.3g-whole-flow-attribution-2026-08-24.md`](../../_Benchmarks/Baselines/network-p0.3g-whole-flow-attribution-2026-08-24.md).

## Capacity and failure policy

Every hot or remotely influenceable structure has explicit capacity. Examples include:

```text
connections / candidates / path attempts
packet slots and owner handoff queues
reliable flights and reassembly
independent-state slots
recipient subscriptions and scheduler membership
pending application proofs
snapshot candidates and bytes
public rows/chunk and resident client chunks
retained application events
UI semantic payloads and application operations
```

Capacity exhaustion must return a named status, reject admission, trigger bounded
recovery, or mark the owning session nonviable according to its contract. It must not
allocate an unbounded fallback collection or silently drop semantic obligations.

Expected rejection is transactional. Unexpected failure after trusted authoritative
mutation begins follows the owning authority's terminal/fault policy rather than being
converted into a domain rejection.

## Security / transport relationship

Network security establishes and protects a transport/application path. It does not
authenticate an account or grant Game authority.

The accepted lower mechanisms include:

- stateless cookie-before-allocation handshake gating;
- pinned deployment server identity;
- P-256 ECDH + signed transcript + HKDF;
- AES-256-GCM established datagrams;
- independent authenticated replay and reliable-delivery acknowledgement state;
- lane-scoped reliable ordering;
- bounded fragmentation/reassembly;
- deterministic impairment evidence supplied by `UltraSim.Network.Testing`;
- connected UDP and one shared listener with Linux native batch paths where enabled;
- Hosted H0 authenticated external admission, IPv4 rebinding, and connection-owner
  packet handoff.

Transport wire revision 2 adds the acknowledged opposite-direction `SecurityEpoch` to every
authenticated ACK. Delayed genuine ACKs from an older epoch therefore cannot retire reused packet
numbers or alter current RTT/congestion state. This role-neutral wire revision participates in
every protocol-manifest fingerprint independently of the Game message-manifest version.

The 2026-09-09 defensive correction also binds packet-buffer tokens to their issuing pool,
unifies scalar/sharded Hosted application-retirement and idle/drain cleanup, bounds sharded owner
ingress work per pass, makes Direct Connect observe automatic closure, and makes Hosted shutdown
finish safe independent cleanup before reporting worker/application failures. These are repairs to
existing ownership/lifetime contracts, not a new replication profile or authority layer.

Hosted/deployment claims and exclusions live in `HOSTED_NETWORK.md` and may not be
inferred from replication benchmarks.

## Protocol versioning policy

Generated Game/Network source is the authority for the current protocol/manifest
version and fingerprint. Historical N1/P0 evidence records the exact manifest and
fingerprint used at that checkpoint. Those historical values are **not current
configuration constants** and should not be copied forward into architecture prose.

As of this document refresh, `GeneratedUltraSimGameProtocol` is protocol version 13
and `UltraSimGameProtocol` constructs manifest version 28. Version 20 added the bounded cold
public-presentation metadata/application-proof pair above existing hot chunk state. Version 21
adds the authoritative `Primary` result to the playable owner-state schema for I0.12. Version 22
widens the existing cold presentation occurrence with an independently bounded optional item-detail
string and admits the `GroundItem` state bit in the unchanged hot chunk-record shape. Item details
remain absent from hot records. Version 23 supersedes that temporary formatted detail string with a
canonical set of at most 16 stable typed Inspection Property facts. The decoder requires ascending
unique property identity, a finite value grammar, bounded tokens, exact kind/value validity, and no
trailing bytes before Client Core publication. Network still owns neither item state, presentation
order/labels, nor action meaning. Version 24 advances Chat submit/occurrence to message version 2,
adding only the finite `Global` submission mode and explicit routed-Global/System occurrence kinds.
System has no forged player identity or public anchor; audience and broadcast authority remain Game
semantics. Version 25 advances only the retained player-command message to v2 for the finite
read-only Server Monitor request. The sanitized model remains Game/UI-owned, its content-backed
definition uses the existing UI protocol, HUD ping reads the existing peer snapshot, and test
markers reuse the unchanged System occurrence path. No diagnostic payload, endpoint, key, or
administrative command enters the Network kernel. Version 26 advances the cold public-presentation
metadata message to v4 for the bounded public Main Hand prefab, finite Game-owned vertical phase,
and latest accepted Interact/Primary sequence used by D0.7a actor presentation; hot movement and
private item rolls remain unchanged. Version 27 adds the bounded authenticated Server Performance
Profile advertisement and exact application-session proof used by PF3 before content/application
progression; it changes policy negotiation and enforcement without changing movement, replication,
or Network transport cadence. Version 28 raises the manifest's finite unreliable-Data receive
capability from one to eight for PF4D. It changes the manifest fingerprint but not outer transport
revision 2; the selected sender policy is independently bounded to cross-lane cap two. Future schema
changes must advance the appropriate generated/
manifest identity and remain bounded/hostile-input transactional.

## Evidence policy

Accepted Network evidence follows these rules:

- semantic correctness before timing;
- exact workload/configuration recorded;
- authority, topology, clock, and capacity scope stated;
- server-role and integrated tick timing separated;
- p50/p95/p99 plus useful tail context;
- setup/cold work separated from warmed hot work;
- managed allocation claims scoped precisely;
- production memory separated from benchmark/client-observation memory;
- matched controls for relative claims;
- run-order/host-noise caveats when relevant;
- immutable evidence artifacts for accepted/rejected checkpoints;
- negative results preserved rather than rewritten into success;
- scaling/stress != strict rating != production deployment claim.

Same-process synthetic clients are useful correctness and conservative integrated
fixture evidence. They are not, by themselves, remote-client CCU evidence.

## Accepted checkpoint index

This index is intentionally compact. Git history and `_Benchmarks/Baselines` retain
the full measurements and intermediate hypotheses.

### N0 foundation

N0 established the dependency-clean Network kernel, security, reliable/unreliable
lane semantics, application binding, controlled input/owner state, maintained public
WorldState, retained Events, atomic baseline recovery, shared UDP listener/native
batching, long-lived identity-ledger recovery, and the final exact-AOI negative
experiment. The maintained implementation is retained as the concrete
`MaintainedEntityRelation` Game profile.

N0.11's important negative result remains architectural evidence: reducing exact
recipient/entity relations and WorldState rows did not improve the complete path
because maintaining precise moving topology cost more. Do not reopen open-ended N0
packet/AOI tuning from that result.

### N1 chunk-publication path

- N1.0: bounded packet-free recipient chunk subscriptions/cursors.
- N1.1: canonical chunk snapshot/delta/absence/proof semantics.
- N1.2: ordinary independent-unreliable remote delta/absence lifecycle.
- N1.3: atomic reliable-Bulk snapshot recovery.
- N1.4: dirty-driven work discovery, exact stable-interest acceleration, bounded
  cadence traversal, recipient/reverse-index compaction, mixed/shared-socket evidence.
- Hosted H0.5: dedicated-server/independent-client process gate.

N1.0-N1.4 remain the selected profile's semantic implementation. N1.5 shared
semantic packing has **not** been authorized.

### P0 profile comparison / closure

- P0.0-P0.2: explicit finite profile catalog and dirty entity/chunk alternatives.
- P0.3-P0.3F: corrected semantics, broadphase/scheduler floors, dense/sparse Game-core
  scale evidence.
- P0.3G: Dirty Chunk equal-lineage page reuse + recipient packing; separate
  five-profile whole-flow attribution.
- P0.3H: Network ready-state selection floor.
- P0.3I: bounded chunk proof envelopes.
- P0.3J: probes-off strict server-role rating closure.
- P0.3K: independent server-step/AOI-cadence matrix.
- P0.4: conservative current-game dynamic-public selection; no runtime composition.

## Current stop / forward policy

Network/replication is now **consumption-ready infrastructure**.

Do not begin any of the following merely because the benchmark infrastructure exists:

- N1.5 shared encoding/page reuse in the selected profile;
- another profile tournament;
- 2K/4K transport/sharded scaling work for its own sake;
- a generic runtime replication-profile interface;
- per-object workload-adaptive profile switching;
- a cross-profile identity/transition framework;
- public-WAN or production-CCU claims;
- IPv6 or multi-host connection migration hidden inside unrelated Game work.

New Network work should be earned by a real gameplay/client/deployment consumer that
exposes a correctness, capacity, freshness, or measured performance problem.

The first such consumer vertical has already validated public actors, and I0.8 has
validated a very different owner-private UI/Inventory path over the same lower
Network machinery. Future gameplay should continue to consume these seams before
reopening Network architecture.

M0.0 now closes the first major multiplayer consumer after Client F0.5: one shared Game/World
authority serves three independent authenticated application/participant/control sessions in both
dedicated and Complete-hosted shapes. It composes the accepted listener, Hosted handoff,
per-recipient replication, Chat, UI, and session-detach paths without a new wire profile. A paced
snapshot may no longer be replaced while its transaction/proof is outstanding; this prevents
normal multi-recipient scheduling from repeatedly cancelling a healthy transfer.

M0.0 remains Game/server composition evidence, not an implicit N1.5 or another Network-profile
campaign. Reopen lower Network design only if later crowd/distributed/churn acceptance exposes a
correctness, boundedness, or measured capacity failure.

C0.2 consumes the unchanged reliable Event plus Control-proof machinery for one first routed
Global channel and authority-originated System broadcasts. Spatial hearing, routed audience, and
system-origin authority remain in Game/Chat. The Network kernel sees only the versioned bounded
message descriptors and bytes; no new transport lane, replication profile, or session authority
was added.

The current exclusions and deployment gates for externally admitted UDP remain in
[HOSTED_NETWORK.md](HOSTED_NETWORK.md).
