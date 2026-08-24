# P0.3G whole-flow Network attribution — 2026-08-24

## Result

The five Game replication profiles were carried through the same authenticated shared-UDP path:

```text
authority mutation / recipient movement
    -> concrete Game profile and canonical Game codec
    -> Network retained queues and lane selection
    -> frame encoding and AES-GCM
    -> real shared UDP listener (sendmmsg/recvmmsg when available)
    -> client Network admission and concrete semantic apply
    -> application proof through the reverse Network path
    -> server proof interpretation
```

All 30 workload/profile/frame-cap rows converged, had the expected family digest, and reported zero
transport capacity drops. The raw machine-readable result is
`network-p0.3g-whole-flow-attribution-2026-08-24.json`.

The main result is not a small ranking change: entity-granular delivery remains inside 16 ms for
both measured populations, while both chunk families exceed it even with eight-frame datagrams.
Unreliable frame aggregation substantially improves chunk transport, but it does not remove the
cost of recipient/chunk semantic work, client chunk application, and per-chunk proof multiplicity.

This does **not** replace the scaling matrix or establish a production CCU. The tick result includes
one process performing server work and every synthetic client's work. `ServerRole` and `ClientRole`
are reported separately for attribution.

## Provenance and workload

- Git: `d10d577694a353e069ff714a61e81eb815954472`
- JSON SHA-256: `d7ab6a091152ae0ee962e3dfb33d8715ff250ce83dceb06b596b37ff86f0b6ac`
- Runtime: .NET 8.0.29, Linux x64, 16 logical processors
- Warmup: 30 ticks
- Measured: 90 ticks; every tick is timed
- Dense: 400 recipients, 1,000 public entities, 20 spatially dirty entities/tick, stationary recipients
- Sparse: 512 recipients, 512 public entities, 179 spatially dirty entities/tick, moving recipients
- Fixed execution seed: `0x5037001`; all 30 cases were shuffled in one deterministic order
- Frame-cap A/B: 1, 4, and 8 unreliable frames/datagram; every other limit and workload input is unchanged
- Stage timing is opt-in and enabled in these rows. Its absolute results include instrumentation cost.
- Tick percentiles exclude bootstrap, semantic verification, and final convergence drain. Cumulative
  message/byte/stage counters include the bounded drain required to discharge work caused by the
  measured ticks.

The comparison manifest advertises eight-frame support. The production Game manifest still uses
cap one; this experiment does not silently change the production default.

## Complete tick p95 matrix

Milliseconds, lower is better.

### Dense-400

| Profile | Cap 1 | Cap 4 | Cap 8 |
|---|---:|---:|---:|
| MaintainedEntityRelation | 12.676 | 12.099 | 12.129 |
| ChunkPublication | 48.694 | 37.763 | 34.738 |
| DirtyEntitySpatialFanout | **10.927** | **11.063** | **11.553** |
| DirtyEntityChunkBatchFanout | 11.476 | 12.017 | 12.132 |
| DirtyChunkSpatialFanout | 50.633 | 44.709 | 43.828 |

### Sparse-512

| Profile | Cap 1 | Cap 4 | Cap 8 |
|---|---:|---:|---:|
| MaintainedEntityRelation | **13.689** | **14.347** | **13.994** |
| ChunkPublication | 87.638 | 63.101 | 57.097 |
| DirtyEntitySpatialFanout | 13.913 | 14.548 | 14.554 |
| DirtyEntityChunkBatchFanout | 15.036 | 14.664 | 14.479 |
| DirtyChunkSpatialFanout | 83.607 | 73.991 | 72.325 |

Cap 1 -> cap 8 reduced ChunkPublication tick p95 by 28.7% dense and 34.8% sparse. It reduced
DirtyChunk by 13.4% and 13.5%. The entity profiles had almost no packet-fill opportunity and were
flat or slightly slower because the aggregate selector performs extra bounded selection passes.

## Cap-eight timing attribution

All columns are per-tick p95 milliseconds. `Pump` is the observed combined protocol/socket/apply
pump wall; it is not the sum of the independently attributed server and client columns.

### Dense-400

| Profile | Tick | Server role | Client role | Game schedule/codec | Pump | Client decode/apply/proof | Server proof apply |
|---|---:|---:|---:|---:|---:|---:|---:|
| MaintainedEntityRelation | 12.129 | 5.151 | 5.453 | 1.021 | 9.587 | 0.972 | 0.326 |
| ChunkPublication | 34.738 | 8.610 | 23.559 | 4.243 | 30.453 | 17.398 | 0.861 |
| DirtyEntitySpatialFanout | **11.553** | **4.561** | **5.403** | 1.867 | 9.667 | 0.874 | 0.455 |
| DirtyEntityChunkBatchFanout | 12.132 | 5.090 | 5.506 | 2.335 | 9.799 | 1.080 | 0.556 |
| DirtyChunkSpatialFanout | 43.828 | 16.955 | 24.272 | 13.453 | 30.261 | 17.832 | 0.695 |

### Sparse-512

| Profile | Tick | Server role | Client role | Game schedule/codec | Pump | Client decode/apply/proof | Server proof apply |
|---|---:|---:|---:|---:|---:|---:|---:|
| MaintainedEntityRelation | **13.994** | **5.870** | 6.214 | **0.747** | 11.279 | 0.499 | 0.285 |
| ChunkPublication | 57.097 | 19.862 | 37.542 | 12.825 | 48.289 | 27.347 | 1.322 |
| DirtyEntitySpatialFanout | 14.554 | 6.539 | 6.292 | 2.541 | 11.425 | 0.553 | 0.290 |
| DirtyEntityChunkBatchFanout | 14.479 | 6.672 | **6.197** | 2.568 | **11.206** | 0.549 | 0.302 |
| DirtyChunkSpatialFanout | 72.325 | 31.309 | 38.116 | 25.767 | 46.413 | 28.011 | 1.145 |

The chunk result is not only packetization. At cap eight, DirtyChunk spends 13.45 ms dense and
25.77 ms sparse in server Game scheduling/codec before the common transport has finished. Both
chunk families then spend about 17-28 ms/tick in aggregate client decode/cache application/proof.

## Cap-eight message, proof, and wire shape

Counts/bytes cover the measured trace plus its required convergence drain. `Forward` is successful
server Game delivery messages; each DirtyChunk bundle may carry several independently proven chunk
entries. MiB values are binary MiB.

### Dense-400

| Profile | Forward | Proof messages | Network frames | Data datagrams | Frames/datagram | Game MiB | Wire MiB |
|---|---:|---:|---:|---:|---:|---:|---:|
| MaintainedEntityRelation | 40,578 | 40,578 | 81,156 | 73,603 | 1.10 | 30.77 | 35.64 |
| ChunkPublication | 147,132 | 147,132 | 294,264 | 80,495 | 3.66 | 48.05 | 57.90 |
| DirtyEntitySpatialFanout | **36,722** | **36,722** | **73,444** | **72,938** | 1.01 | **30.59** | **35.26** |
| DirtyEntityChunkBatchFanout | 36,722 | 36,722 | 73,444 | 72,938 | 1.01 | 30.59 | 35.26 |
| DirtyChunkSpatialFanout | 39,273 | 147,132 | 186,405 | 75,673 | 2.46 | 45.76 | 53.04 |

### Sparse-512

| Profile | Forward | Proof messages | Network frames | Data datagrams | Frames/datagram | Game MiB | Wire MiB |
|---|---:|---:|---:|---:|---:|---:|---:|
| MaintainedEntityRelation | 47,046 | 47,046 | 94,092 | 94,092 | 1.00 | 18.64 | 24.65 |
| ChunkPublication | 272,617 | 272,617 | 545,214 | 107,900 | 5.06 | 53.78 | 70.29 |
| DirtyEntitySpatialFanout | **40,414** | **40,414** | **80,828** | **80,828** | 1.00 | **13.67** | **18.83** |
| DirtyEntityChunkBatchFanout | 40,414 | 40,414 | 80,828 | 80,828 | 1.00 | 13.67 | 18.83 |
| DirtyChunkSpatialFanout | 55,532 | **363,961** | 419,493 | 120,029 | 3.49 | 64.99 | 79.25 |

DirtyChunk's shared-page and multi-chunk-forward work is real:

- Dense: 663 shared page materializations, 149,342 reuse hits, 39,273 bundles carrying 147,132 entries.
- Sparse: 20,150 materializations, 352,454 reuse hits, 55,532 bundles carrying 363,961 entries.

Its remaining asymmetry is now explicit: forward entries are bundled, but the client emits one
application-proof Game message per independently meaningful chunk entry. Sparse DirtyChunk therefore
sends 55,532 forward bundles and 363,961 reverse proof messages. Datagram aggregation reduces AES and
syscall count, but it cannot remove the per-proof Game/frame/selection work.

Native socket batching was already effective: cap-eight rows averaged roughly 30-32 datagrams per
`sendmmsg`/`recvmmsg` call, with p50/p95 batch fill at 32. Syscalls are not the first unresolved layer.

## Protocol-stage census

The following are cumulative milliseconds across the complete 90-tick trace and convergence drain,
combining server and client peer stage clocks. They are diagnostic counters, not independently
additive wall-clock totals. `Listener` combines send syscall, receive syscall, and routing clocks.

### Cap eight

| Workload/profile | Selection | Frame encode/admit | AES send/receive | Endpoint send/receive | Listener |
|---|---:|---:|---:|---:|---:|
| dense MaintainedEntityRelation | 206.1 | 15.2 | 102.3 | 237.9 | 99.7 |
| dense ChunkPublication | 265.8 | 40.7 | 131.8 | 255.6 | 122.0 |
| dense DirtyEntitySpatialFanout | 201.7 | 14.3 | 103.3 | 236.5 | 99.0 |
| dense DirtyEntityChunkBatchFanout | 201.9 | 14.6 | 107.5 | 237.4 | 101.4 |
| dense DirtyChunkSpatialFanout | 237.1 | 33.6 | 136.9 | 258.2 | 116.8 |
| sparse MaintainedEntityRelation | 258.3 | 17.9 | 132.6 | 304.9 | 130.7 |
| sparse ChunkPublication | 415.8 | 85.8 | 194.2 | 365.1 | 168.5 |
| sparse DirtyEntitySpatialFanout | 238.7 | 15.5 | 115.3 | 278.0 | 111.6 |
| sparse DirtyEntityChunkBatchFanout | 235.1 | 15.5 | 114.5 | 276.5 | 109.6 |
| sparse DirtyChunkSpatialFanout | 398.1 | 80.0 | 231.5 | 429.9 | 184.1 |

Aggregation does what was hypothesized for chunk traffic. Cap one -> cap eight changed:

| Workload/profile | Data datagrams | Wire MiB | AES cumulative ms | Endpoint cumulative ms | Tick p95 |
|---|---:|---:|---:|---:|---:|
| dense ChunkPublication | 294,264 -> 80,495 | 66.87 -> 57.90 | 373.7 -> 131.8 | 681.9 -> 255.6 | 48.694 -> 34.738 |
| dense DirtyChunk | 186,405 -> 75,673 | 57.68 -> 53.04 | 257.1 -> 136.9 | 552.9 -> 258.2 | 50.633 -> 43.828 |
| sparse ChunkPublication | 544,608 -> 107,900 | 88.54 -> 70.29 | 657.2 -> 194.2 | 1,245.0 -> 365.1 | 87.638 -> 57.097 |
| sparse DirtyChunk | 419,493 -> 120,029 | 91.83 -> 79.25 | 548.7 -> 231.5 | 1,228.0 -> 429.9 | 83.607 -> 72.325 |

The bounded linear selectors are also visible. At cap eight the entity profiles have essentially
nothing to aggregate, yet selection performs more work. For example, sparse DirtyEntity rises from
3.04M to 3.61M lane passes and from 97.3M/48.7M to 115.4M/57.7M reliable/independent slot probes;
combined selection time rises from 202.8 to 238.7 ms. This is a real mechanical target before making
eight-frame aggregation universal. A per-connection/profile-specific cap remains safer meanwhile.

## Reserved memory

Aggregate constructed reservation at cap four (MiB):

| Workload/profile | Game server | Game clients | Network server | Network clients |
|---|---:|---:|---:|---:|
| dense MaintainedEntityRelation | 166.5 | 118.8 | 151.5 | 115.1 |
| dense ChunkPublication | 97.2 | 2,150.6 | 151.5 | 115.1 |
| dense DirtyEntitySpatialFanout | 104.7 | 120.1 | 151.5 | 115.1 |
| dense DirtyEntityChunkBatchFanout | 104.7 | 120.1 | 151.5 | 115.1 |
| dense DirtyChunkSpatialFanout | 306.2 | 2,150.6 | 151.5 | 115.1 |
| sparse MaintainedEntityRelation | 67.3 | 19.6 | 193.9 | 147.3 |
| sparse ChunkPublication | 29.5 | 493.1 | 193.9 | 147.3 |
| sparse DirtyEntitySpatialFanout | 63.0 | 21.3 | 193.9 | 147.3 |
| sparse DirtyEntityChunkBatchFanout | 63.1 | 21.3 | 193.9 | 147.3 |
| sparse DirtyChunkSpatialFanout | 388.2 | 493.1 | 193.9 | 147.3 |

The client figures are aggregates across 400/512 complete in-process clients. Dense chunk semantic
cache reservation is about 5.38 MiB/client; sparse is about 0.96 MiB/client. Entity-client profile
state is about 0.30 MiB/client dense and 0.04 MiB/client sparse. Network construction is stable at
about 0.379 MiB server-side plus 0.288 MiB client-side per connection for this evidence profile.

## Correctness findings closed while obtaining the matrix

1. DirtyChunk's remote scheduler used a global bundle dequeue for a recipient-specific connection.
   A connection could therefore transmit another participant's bundle. The server now builds only
   for its bound participant, with a permanent cross-route regression.
2. The first whole-flow chunk client did not stage/retire production semantic-cache chunks as server
   subscriptions changed. The harness now uses a real bounded world, applies Enter/Leave topology,
   and compares every resident chunk against the exact publication-store snapshot.
3. Cap-one loss/proof lag exposed a production N1.2 issue. A client that locally applied B but whose
   server still knew A rejected a later complete coalesced A->C delta. Delta admission now rejects
   only a true gap (`local < base`); overlap is accepted and the semantic cache independently fences
   stale/conflicting rows. `N12CoalescedDeltaOverlapsLocallyAppliedUnprovedTruth` freezes the case.

The Game replication suite has 125 passing tests after these changes, including remote DirtyChunk
bundle transactionality/cross-route fencing and the coalesced-delta regression.

## H0.4/H0.5 owner-handoff control

The five-profile matrix deliberately uses the shared listener directly so every profile executes on
one deterministic comparison owner. H0.4 connection-owner handoff and external H0.1/H0.2 admission
are lower, profile-neutral mechanisms and are not silently approximated in those 30 rows.

They were rechecked separately with the H0.5 dedicated-process harness. That rerun exposed a harness
scheduling defect: `ServerApplication` retried unproved chunk state every 16 **owner-service passes**,
not authority ticks. Under cold `TieredCompilation=0`, `ReadyToRun=0` startup, proof delay could fill
all 128 delivery tickets and 128 independent Network slots, starving the Event lane. The H0.5 sample
now permits at most four overlapping state attempts and waits 256 owner passes between retries.

After that correction, the exact cold 8-client/4-owner, 24-tick, one-reconnect gate passed five
consecutive times. The retained run is
`network-hosted-h0.5-whole-flow-control-2026-08-24.json`:

- H0.5 harness Git: `4811f9fa669feed2308f001d1c36693d5e1d8b62`;
- H0.5 JSON SHA-256: `990e962d8ba9e53fd085eb4c22acd03a41e8eb0adb36cfad1d1182433dea305e`;

- 16/16 application phases completed through external admission and four owner threads;
- 16 path rebinds, 384 owner states, 384 Events, 384 Event proofs;
- zero disclosure, handoff, packet-capacity, destination-full, or listener-capacity drops;
- zero packet leases at shutdown;
- retained observation: 330 ms server process CPU, 175.8 ms Game semantic, 202.5 ms listener pump,
  144.1 ms owner protocol, 346.5 ms combined server transport, 19.088 ms client end-to-end p95.

These are cold independent-process localhost observations, not additive to the five-profile matrix
and not a production latency target.

## Verification

- 125/125 `UltraSim.Game.Replication.Tests` passed.
- 11 N0.0 groups plus N0.2/N0.3/N0.5/N0.10.1/H0.1-H0.5 `UltraSim.Network.Tests` passed.
- 69/69 `UltraSim.Network.Application.Tests` passed.
- 22/22 `UltraSim.Client.Tests` passed.
- All 30 whole-flow rows converged with zero capacity drops.
- Entity-family digests match within each workload across all three entity profiles and caps.
- Chunk-family digests match within each workload across both chunk profiles and caps.

## Decision implications

1. Do not select a profile from Game-only timing. The real reverse proof and client semantic paths
   materially widen the current chunk/entity difference.
2. DirtyEntitySpatialFanout is the strongest current whole-flow result for dense mobile public state;
   MaintainedEntityRelation is narrowly best in sparse moving-recipient p95. The adaptive entity/chunk
   discovery profile remains close but does not win either measured whole-flow row.
3. ChunkPublication remains much stronger than DirtyChunk on the current server Game path. DirtyChunk
   proves shared materialization and forward bundling, but its dirty-time discovery plus unbundled
   reverse proofs remain expensive.
4. Keep chunk semantics for state naturally owned/published as chunks; this evidence argues against
   forcing all mobile dynamic public entities through chunk delivery merely because terrain/statics
   are chunk-native.
5. Before enabling multi-frame unreliable delivery generally, either choose the cap per traffic
   shape or replace the repeated bounded linear selection scans with measured ready-state indexing.
6. The next earned chunk experiment is a bounded proof envelope carrying independently meaningful
   chunk tickets. It should reduce Game messages/frames in the reverse direction without changing
   `SENT != KNOWN` or making a bundle proof atomic.
