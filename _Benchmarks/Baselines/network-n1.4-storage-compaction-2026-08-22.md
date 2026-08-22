# Network N1.4 storage-compaction stop

This checkpoint changes storage only. Accepted N1.0-N1.4 interest, publication,
cursor/history, application-proof, snapshot-recovery, cadence, and dirty-work
semantics remain unchanged. It does not begin N1.5 or make the current-game AOI
adoption decision.

## Retained recipient state

`OfflineDirectChunkSubscription` remains the 64-byte self-validating public read
value. The recipient no longer stores that value in both transactional banks.
Each private 32-byte retained row stores only:

```text
SpatialChunkKey
applied ChunkPublicationRevision
cadence phase
interest tier
has-knowledge flag
```

The recipient-wide `PublicationEpoch`, retained key, and applied revision
reconstruct the public `ChunkPublicationCursor` when a caller indexes the borrowed
subscription view. Known absence remains `HasKnowledge=true` with no applied
revision. At 1,024 declared slots, recipient reservation falls from 187,904 to
122,368 bytes (34.9%) without changing the 503 observed subscriptions.

## Scheduler storage

The scheduler membership row now retains a recipient slot plus its chunk slot
instead of duplicating `ChunkReplicationRecipientId`, `SpatialChunkKey`, cadence
period, and cadence phase. Current record widths are:

```text
RecipientState      16 B
MembershipState     40 B
ChunkState          24 B
```

Exact attribution showed that the two dictionaries remained the largest
individually removable storage lines after record compaction. They are replaced
by two fixed-capacity open-address indexes. Each bucket stores only a 4-byte
membership/chunk slot reference; canonical keys remain in the compact scheduler
records. Backward-shift deletion leaves no tombstones for churn to accumulate.
The tables remain bounded for the full declared membership capacity and do not
assume that recipients share one geographic window.

A permanent 2,000-operation deterministic churn gate covers recipient removal and
generation reuse, random enter/leave topology, membership lookup, shared-chunk
counts, pending work, and complete drains. The complete Game Replication suite now
contains 66 N0/N1.0-N1.4 tests.

## Controlled crossover rerun

The same broad 1,024-slot, 503-observed-subscription workload reports:

| Recipients | Recipient bytes | Scheduler bytes | Combined bytes | Crossing p95/recipient | One dirty p95/recipient |
|---:|---:|---:|---:|---:|---:|
| 512 | 62,652,416 | 48,246,784 | 110,899,200 | 104.780 us | 850.373 ns |
| 768 | 93,978,624 | 76,564,480 | 170,543,104 | 105.339 us | 258.839 ns |
| 1,024 | 125,304,832 | 96,493,568 | 221,798,400 | 105.577 us | 256.787 ns |

The recorded broad 1,024 total is 42.8% below the prior 387,580,360-byte artifact.
The new attribution counts every fixed index slot but, like prior `ReservedBytes`
evidence, excludes CLR object and array headers. Historical dictionary accounting
was coarser, so this is evidence of the workload reservation change rather than a
claim about exact process RSS.

The 640-slot sensitivity case retains the same 503 subscriptions and reports
147,873,792 combined bytes, down from the prior 242,183,400-byte observation.
The value 640 remains evidence rather than a production capacity commitment.

The crossing tail did not improve: the broad case is still about 100-106 us p95
per synchronized recipient, and the 640-slot case reports 99.573 us. Separate
captures are not controlled microarchitectural A/B trials, but there is no evidence
that storage compaction solved topology-transition cost. That gate remains open.

## Decision

Storage compaction is implemented and stopped for N1.4 checkpoint review;
current-game AOI adoption is still open. Stop before 2,048/4,096 and before N1.5.
If accepted, the next evidence should use this compact layout for Dense-400,
dense-town/snapshot recovery, boundary/corner, mixed-private, transport replay,
and sharded-client workloads. Only after those results should the profile be
adopted or the crossing path receive a separate optimization pass.

Machine-readable evidence:

- `network-n1.4-storage-compaction-crossover-512-1024-2026-08-22.json`
- `network-n1.4-storage-compaction-capacity-sensitivity-1024x640-2026-08-22.json`
