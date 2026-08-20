# NETWORK V2 DESIGN

**Date:** 2026-04-19  
**Status:** Proposal  
**Scope:** Clean-slate Network 2.0 runtime spec with no backwards-compatibility constraints

---

## 1. Purpose

Network 1.x proved the gameplay, authority, AOI, prediction, correction, and live-load shape. It also exposed the main structural problem:

- dirty publication is relatively cheap
- dense local fanout is expensive
- replay/build/fanout cost lands too late
- the tick thread still pays too much for replication work that should be staged and bounded earlier

Network 2.0 replaces the current replay-driven, late-fanout model with a subscription-driven, connection-drained model.

The primary goal is not "more clever threading." The primary goal is to change the shape of the work:

- classify once
- route once
- subscribe incrementally
- drain incrementally
- budget before assembly
- isolate transport from world tick

---

## 2. Design Goals

1. Keep tick-thread network cost low and flat
2. Make dense hotspots degrade intentionally, not accidentally
3. Keep authority semantics unchanged
4. Eliminate global replay-range scans from the common hot path
5. Separate control, input, and replicated state cleanly
6. Make backlog, drop, coalesce, and recovery behavior explicit
7. Keep the design small enough to implement and reason about

## 3. Non-Goals

1. Preserving 1.x wire compatibility
2. Building a fully generic replication middleware product
3. Solving distributed server topology
4. Preserving every intermediate state update for observer traffic

---

## 4. Runtime Model

Network 2.0 has three planes:

- `Control`
  - login, session, UI, chat, character flow, admin/control
  - reliable, ordered
- `Input`
  - client movement/input commands
  - sequenced, low-latency, latest-wins oriented
- `State`
  - owner correction, observer state, spawn/despawn, replicated world state
  - mostly latest-state, with ordered event sub-channels only where needed

Network 2.0 has four stages:

1. `Publish`
2. `Schedule`
3. `Assemble`
4. `Send`

Only `Publish` is allowed to run on the world tick thread.

The `Input` plane is logically part of Network 2.0, but it does **not** use the stream/cursor/snapshot model defined for outbound state replication except where input acknowledgments or owner corrections cross into owner-critical lanes.

---

## 5. Core Invariants

These invariants must hold in every implementation:

1. No background worker reads live ECS world state
2. No connection is assembled by more than one assembly shard in a frame
3. No per-connection send reorder is allowed within a lane unless the lane explicitly allows it
4. No unbounded reliable backlog is allowed
5. No per-entity stream explosion is allowed
6. Streams are coarse partitions; entities are keys inside streams, not usually streams themselves
7. Tick-thread publish cost must remain proportional to classified changes, not dense recipient fanout
8. Under sustained overload, low-priority state degrades first
9. Snapshot recovery is preferred over replaying stale incremental state once a connection falls too far behind
10. Normal operation must not require per-tick heap growth on the hot path

---

## 6. Stream Model

### 6.1 Stream Families

The first implementation uses a small set of stream families:

- `OwnerState`
- `OwnerEvent`
- `SpatialState`
- `SpatialEvent`
- `GroupState`
- `GroupEvent`
- `Control`

Near/mid/far behavior is **policy**, not separate stream families.

### 6.2 Stream Cardinality Rules

Streams are partitioned by scope, not by individual entity, except in narrowly justified owner-critical cases.

Allowed coarse partitions:

- owner id
- chunk id / spatial cell id
- group id (team, party, faction, shard-local cohort)
- global/control scope

Disallowed by default:

- one stream per entity
- one stream per `(entity, connection)`
- one stream per packet type without a clear routing scope

### 6.3 Stream Types

Each stream family uses one of two backing types:

- `LatestStateStream`
- `OrderedEventStream`

`Control` uses a dedicated reliable ordered path and is not required to share stream backing with state/event traffic.

### 6.4 Family to Lane Mapping

Default mapping:

- `Control` -> control lane only
- `OwnerState` -> owner-critical lane by default
- `OwnerEvent` -> owner-critical or reliable event lane by policy
- `SpatialState` -> near/far observer lanes by policy
- `SpatialEvent` -> reliable or semi-reliable observer/event lane by policy
- `GroupState` -> group state lane by policy
- `GroupEvent` -> group event lane by policy

Service class, cadence, and dense-area behavior remain policy-driven on top of this mapping.

---

## 7. Data Model

## 7.1 Connection Registry

`ConnectionRegistry`

Purpose:
- authoritative runtime table of active connections

Fields:
- `ConnectionId`
- `SessionId`
- `ConnectionState`
- `AssemblyShardId`
- `TransportShardId`
- `OwnershipEpoch`
- `SubscriptionSet`
- `CursorSet`
- `BudgetState`
- `OutboundState`
- `Metrics`

Rules:
- `AssemblyShardId` is stable across normal operation
- rebalancing increments `OwnershipEpoch`
- assembly workers must reject stale work items whose `OwnershipEpoch` no longer matches

## 7.2 Subscription Set

`ConnectionSubscriptionSet`

Purpose:
- durable routing topology for one connection

Fields:
- `OwnerScopeId`
- `SubscribedSpatialScopes[]`
- `SubscribedGroupScopes[]`
- `CenterChunk`
- `AoiRadius`
- `TierPolicyId`
- `DenseClass`
- `VisibilityGeneration`

Rules:
- updated incrementally on AOI changes
- never rebuilt from scratch during the hot assembly loop

## 7.3 Spatial Subscriber Index

`SpatialSubscriberIndex`

Purpose:
- reverse lookup from spatial scope to subscribed connections

Fields per scope:
- `ScopeId`
- `Subscribers[]`
- `DensityClass`
- `Version`

Notes:
- near/mid/far classification is derived by subscription policy, not by separate stream family
- if needed, tier-specific cohorts can exist as cached secondary views, not as primary stream identity

## 7.4 Replication Route Record

`ReplicationRouteRecord`

Purpose:
- routing metadata for one published change

Fields:
- `EntityId`
- `FieldGroupId`
- `Plane`
- `StreamFamily`
- `Lane`
- `RoutingScopeKind`
- `RoutingScopeId`
- `PolicyId`
- `Tick`

This object contains routing metadata only.

## 7.5 Replication Payload Handle

`ReplicationPayloadHandle`

Purpose:
- payload reference independent of routing metadata

Fields:
- `SerializerId`
- `EncodingVersion`
- `PayloadBufferId`
- `PayloadOffset`
- `PayloadLength`

This keeps routing logic separate from payload representation.

## 7.6 Latest State Stream

`LatestStateStream<TKey>`

Purpose:
- latest-value replication for hot mutable state

Fields:
- `StreamId`
- `CurrentEpoch`
- `EntriesByKey`
- `ChangedKeyLog`
- `ChangedKeyLogHead`
- `ChangedKeyLogTail`
- `SnapshotVersion`

Per-key entry:
- `Key`
- `LatestVersion`
- `LastChangedEpoch`
- `PayloadHandle`

Changed-key log entry:
- `Epoch`
- `Key`
- `Version`

Rules:
- each write updates the per-key latest entry
- each changed key is appended to the changed-key log with the stream epoch
- this is not a replay log of full historical payloads
- a stream keeps only bounded sparse change history

## 7.7 Ordered Event Stream

`OrderedEventStream`

Purpose:
- bounded ordered event history where intermediate ordering matters

Fields:
- `StreamId`
- `HeadSequence`
- `TailSequence`
- `EventRing`
- `RetentionLimitItems`
- `RetentionLimitBytes`

Per event:
- `Sequence`
- `EventType`
- `PayloadHandle`
- `Tick`

Rules:
- events append in order
- events are retained only up to bounded retention
- if a consumer falls behind past retention, recovery must switch to snapshot/baseline

## 7.8 Snapshot Descriptor

`SnapshotDescriptor`

Purpose:
- explicit baseline for initial sync, AOI enter, and recovery

Fields:
- `SnapshotId`
- `ScopeKind`
- `ScopeId`
- `Tier`
- `CreatedTick`
- `BaseEpoch`
- `BaseSequence`
- `PayloadHandle`
- `ExpiryTick`

Snapshot kinds:
- `OwnerBaseline`
- `SpatialBaseline`
- `GroupBaseline`

Rules:
- snapshots are distinct objects, not implied side effects of latest-state streams
- a new subscriber becomes consistent by receiving a baseline first, then switching to incremental drain

## 7.9 Cursor Set

`ConnectionCursorSet`

Purpose:
- committed read positions for all subscribed streams

For latest-state streams:
- `CommittedEpochByStream`
- `BaselineSnapshotIdByStream`
- `BaselinePendingByStream`

For ordered-event streams:
- `CommittedSequenceByStream`

Rules:
- cursor semantics differ by stream type
- latest-state cursors are epoch-based, not event-sequence-based

## 7.10 Budget State

`ConnectionBudgetState`

Fields:
- `BytesBudgetPerTick`
- `ItemsBudgetPerTick`
- `ServiceClass`
- `DebtScore`
- `StarvationTicks`
- `BacklogEstimateItems`
- `BacklogEstimateBytes`
- `LastServiceTick`

Service classes:
- `A` owner-critical
- `B` near observers
- `C` far observers
- `D` bulk/background

## 7.11 Backlog Policy

`BacklogPolicy`

Fields:
- `MaxPendingItems`
- `MaxPendingBytes`
- `CollapseToLatestThreshold`
- `SnapshotRecoveryThreshold`
- `StalenessLimitTicks`
- `DropTierFirst`
- `DisconnectOnReliableOverflow`

This is defined per lane / stream family combination.

## 7.12 Replication Work Item

`ReplicationWorkItem`

Purpose:
- bounded scheduler-owned assembly unit for one connection

Fields:
- `ConnectionId`
- `AssemblyShardId`
- `OwnershipEpoch`
- `AssignedBytesBudget`
- `AssignedItemBudget`
- `StreamsToDrain[]`
- `ServiceClassMask`
- `CreatedTick`

---

## 8. Stage Responsibilities

## 8.1 Publish Stage

Owner:
- world tick thread

Inputs:
- ECS mutations
- gameplay/system events
- session/control events
- AOI subscription deltas

Responsibilities:
- classify changed field groups
- create `ReplicationRouteRecord`s
- create `ReplicationPayloadHandle`s
- route updates into the correct stream family and scope
- collapse latest-state entries immediately where allowed
- append ordered events only for true event streams
- update subscription topology incrementally

Forbidden:
- no per-connection packet assembly
- no socket writes
- no global replay-range scanning

Cost target:
- proportional to changed field groups and AOI deltas

## 8.2 Schedule Stage

Owner:
- replication scheduler

Responsibilities:
- score connections by service class, starvation, debt, backlog, and density
- assign byte/item budgets before any heavy assembly work
- choose stream drain order for each connection
- emit bounded `ReplicationWorkItem`s

Drain priority default:
1. owner-critical
2. near spatial
3. group state/events
4. far spatial
5. bulk/background

## 8.3 Assemble Stage

Owner:
- assembly shard workers

Responsibilities:
- drain subscribed streams for one connection
- respect lane and backlog policy
- serialize selected items into outbound buffers
- update committed cursors only according to lane commit rules
- record coalesced/dropped/backlog metrics

Forbidden:
- no live ECS reads
- no AOI recomputation
- no ownership ambiguity for a connection

## 8.4 Send Stage

Owner:
- transport send path

Responsibilities:
- accept outbound buffers
- preserve per-lane connection ordering
- queue or flush to transport
- record transport metrics

---

## 9. Cursor Semantics

Cursor rules must be explicit.

## 9.1 Latest-State Cursor Rule

Latest-state streams use:
- stream epochs
- sparse changed-key log
- explicit baseline snapshots

A connection consuming a latest-state stream stores:
- `CommittedEpoch`
- `BaselinePending`
- `BaselineSnapshotId`

Rules:
1. A connection may not consume incremental latest-state entries until baseline is satisfied
2. At the start of a drain pass, the worker computes a bounded `DrainUpperEpoch`
3. Incremental drain scans changed-key log entries with `CommittedEpoch < Epoch <= DrainUpperEpoch`
4. If a key appears multiple times in that bounded epoch range, only the newest per-key version visible within that same bounded range is serialized
5. After a work item is assembled successfully and accepted by the lane commit rule, `CommittedEpoch = DrainUpperEpoch`
6. If the changed-key log no longer retains entries newer than `CommittedEpoch`, the connection must recover via snapshot, not partial replay

This avoids drifting back to replay-window scanning while keeping cursor state compact.

## 9.2 Ordered-Event Cursor Rule

Ordered-event streams use:
- committed sequence per stream

Rules:
1. A connection drains events with `Sequence > CommittedSequence`
2. `CommittedSequence` advances only according to that lane's commit rule
3. If `CommittedSequence < TailSequence - RetainedDepth`, the connection is no longer recoverable from incremental history and must use snapshot/baseline recovery

---

## 10. Snapshot and Catchup Model

Snapshots are first-class.

## 10.1 When Snapshots Are Used

Snapshots are required for:
- initial join
- AOI enter
- reconnect
- lane recovery after falling behind
- dense snapshot mode for lower-tier observers

Snapshots are produced from stream-owned replicated state or prebuilt snapshot caches, never by reading live ECS state from assembly workers.

## 10.2 Snapshot Flow

For a new spatial subscription:
1. create or fetch the current `SpatialBaseline` snapshot
2. mark `BaselinePending = true`
3. send baseline payload
4. once baseline reaches lane commit eligibility, mark:
   - `BaselinePending = false`
   - `CommittedEpoch = Snapshot.BaseEpoch`
5. future drains are incremental

## 10.3 Snapshot Recovery

When a connection falls behind beyond latest-state log retention or ordered-event retention:
- stop incremental drain for the affected scope
- emit a fresh snapshot baseline
- fast-forward cursor to that baseline upon commit

The system must prefer snapshot recovery over replaying stale backlog.

## 10.4 Dense Snapshot Mode

For extreme dense areas, lower-priority spatial observers may switch from per-entity incremental state to `SpatialBaseline` refreshes plus lighter incremental deltas.

This is an explicit mode, not an accidental fallback.

Spatial snapshots for dense scopes may be cached and reused across many connections if the snapshot cache is derived from stream-owned replicated state.

## 10.5 Cross-Scope Consistency

Network 2.0 guarantees:

- per-lane ordering
- per-stream ordering where the stream type requires it
- baseline-then-incremental consistency within a scope

Network 2.0 does **not** guarantee a globally atomic cross-scope snapshot across all subscribed scopes in a tick.

---

## 11. Commit, Queueing, and Delivery Semantics

The implementation must distinguish:

- `Built`
- `Queued`
- `TransportAccepted`
- `Acked` where relevant

## 11.1 Latest-State Commit Rule

For latest-state lanes that tolerate loss:
- cursor advance is eligible at `TransportAccepted`
- once a newer value supersedes an older pending value, the older one may be dropped immediately
- missed state is healed by later latest-state delivery or snapshot recovery

## 11.2 Ordered-Event Commit Rule

For ordered event lanes:
- cursor advances when the event enters the reliable send window owned by the transport/lane
- events remain pending until the reliable lane guarantees eventual delivery semantics
- if reliable backlog exceeds policy, the connection must either:
  - recover via snapshot when semantically allowed, or
  - disconnect for control-plane overflow

## 11.3 Control Commit Rule

Control-plane messages are reliable ordered.

Rules:
- no silent drop
- overflow is a protocol/runtime fault, not normal degradation
- persistent overflow may disconnect the connection

---

## 12. Overload and Recovery Semantics

Overload behavior must be explicit and lane-specific.

## 12.1 Service Classes

- `A` Owner-Critical
  - guaranteed minimum service
  - near-zero drop tolerance
- `B` Near Observer
  - high priority
  - heavy coalescing allowed
- `C` Far Observer
  - opportunistic
  - whole ticks may be skipped
- `D` Bulk/Background
  - only served when spare budget exists

## 12.2 Default Degradation Order

When overloaded:
1. coalesce latest-state harder
2. reduce far observer cadence
3. drop far observer service
4. reduce mid observer cadence
5. switch lower-tier spatial scopes to snapshot mode
6. preserve owner-critical and control

## 12.3 Backlog Rules

For latest-state:
- backlog is bounded by latest-value collapse and changed-key log retention
- if `StalenessLimitTicks` exceeded, use snapshot recovery

For ordered events:
- backlog is bounded by stream retention and reliable window
- if retention is exceeded, use snapshot recovery if semantics allow; otherwise disconnect

For control:
- backlog is bounded and must not silently degrade

---

## 13. Shard Ownership and Handoff

Shard ownership must be stable and explicit.

## 13.1 Ownership

Each connection has:
- `AssemblyShardId`
- `TransportShardId`
- `OwnershipEpoch`

Rules:
- assembly ownership is stable during normal operation
- work items carry `OwnershipEpoch`
- stale work items are discarded, never applied

## 13.2 Rebalance

Rebalance is explicit and rare:
1. pause new scheduling for the connection
2. bump `OwnershipEpoch`
3. move connection registry ownership
4. resume scheduling on the new shard

No implicit cross-shard simultaneous assembly is allowed.

---

## 14. Memory and Growth Limits

The design must define hard limits.

## 14.1 Stream Limits

- max active latest-state streams
- max active event streams
- max changed-key log bytes per stream
- max event retention bytes/items per stream

## 14.2 Connection Limits

- max subscribed spatial scopes per connection
- max subscribed group scopes per connection
- max outbound queued bytes per lane
- max pending snapshot count per connection

## 14.3 Recovery Rule

When limits are exceeded:
- spatial/state traffic should collapse, snapshot-recover, or drop lower tiers
- control-plane overflow may disconnect

Exceeding a configured hard limit must produce one of:

- coalescing
- tier drop
- snapshot recovery
- scheduling suppression
- disconnect

Silent unbounded growth is a correctness bug.

No structure should grow without limit under normal load.

---

## 15. Dense Spatial Policy

`DenseSpatialPolicy`

Fields:
- `NearCadenceTicks`
- `MidCadenceTicks`
- `FarCadenceTicks`
- `NearScopeServiceCap`
- `MidScopeServiceCap`
- `FarScopeServiceCap`
- `SnapshotSwitchThreshold`

Dense policy applies to spatial observer traffic only.

Owner-critical and control traffic must remain outside this degradation path.

---

## 16. Metrics

Metrics are mandatory.

Required metrics:
- publish ms
- schedule ms
- assemble ms by shard
- send/flush ms
- bytes out total
- bytes out per CCU
- bytes in per CCU
- latest-state coalesced count
- latest-state dropped count
- event backlog count
- snapshot recovery count
- per-connection backlog max
- dense-scope count
- per-service-class service rate
- per-lane queue depth

The system is not complete without these.

---

## 17. Recommended Implementation Shape

Module layout:

- `UltraSim/Network2/Core`
- `UltraSim/Network2/Transport`
- `UltraSim/Network2/Subscriptions`
- `UltraSim/Network2/Streams`
- `UltraSim/Network2/Publish`
- `UltraSim/Network2/Scheduler`
- `UltraSim/Network2/Assembly`
- `UltraSim/Network2/Metrics`

Host/framework glue stays outside these modules.

---

## 18. Definition of Done

Network 2.0 is done enough when:

1. tick thread only performs publish-stage work
2. subscriptions are durable and incrementally maintained
3. latest-state and ordered-event paths are separate in code and semantics
4. snapshots are first-class and used for join/catchup/recovery
5. cursor semantics are implemented exactly as specified
6. overload behavior matches explicit lane/backlog policy
7. assembly ownership per connection is stable and enforced
8. no hot path depends on replay-range scanning of global deltas
9. metrics expose cost and backlog clearly

---

## 19. Bottom Line

Network 2.0 is a subscription-driven replication fabric:

- world mutation classifies once
- routing scopes are explicit
- streams are coarse partitions
- connections drain incrementally under budget
- hot mutable state collapses aggressively
- events preserve order only where necessary
- snapshots handle baseline and recovery
- dense areas degrade by policy
- tick-thread cost remains bounded

That is the clean replacement for the current replay-driven, late-fanout model.
